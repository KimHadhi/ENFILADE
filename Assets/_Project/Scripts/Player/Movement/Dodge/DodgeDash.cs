using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The step dodge seen in the world, on every PC, as one bold beat (user, 2026-11-12, memo 9: the reference, a Roblox
/// "NEW DASH VFX" short, "이대로 참 멋있는 듯"; then "회피가 레퍼런스 영상처럼 걍 딱 굵게 한 번만 나오면 될 것 같은데.
/// 레퍼런스대로 해 달라니까..."). Read frame by frame, each dash in it is one burst that comes all at once and is gone in
/// about a third of a second, and this draws the same three things, once, at the kick-off:
/// - a bright white puff where the foot kicks off;
/// - one afterimage left where the dash began: the character itself, faintly see-through ("내 캐릭터가 잔상으로");
/// - one streak: three bold glowing white lines with sharp kinks, like lightning, from just behind where it began to the
///   body, at the feet, knees and waist, thinning away together once it lands.
/// Nothing follows it (no ghost halfway or over the arrived body, no smoke on landing, no curls of air): the dash itself
/// lasts 0.14 s, so the streak's heads riding on the body that long still land as one stroke.
/// An evade (a blow let through, PlayerPerfectDefense) turns the streak red on every PC: Succeed here on the evader's PC,
/// ShowEvaded on the others' copies of them (PlayerNetworkCombat), so friends see what the evader sees (the user's rule,
/// "내가 보는 화면이 진실"). A Q/E slip has no dash: it gets a short red streak to the body from the side it slipped from.
/// The afterimage is the body's skinned meshes baked at that moment and drawn with one shared shader (DodgeGhost) handed
/// each renderer's own main texture and colour per draw, so any outfit or modded character needs nothing of its own.
/// This PC's own afterimage fades in over a few hundredths of a second so the camera, still in its head when it is baked,
/// never sees it from inside, and in first person the streak keeps below the eyes. The first-person view of the same beat
/// is DodgeFlash's. Real time throughout. Numbers: Tables/Player.csv, row DodgeLook.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth + 1)]
public sealed class DodgeDash : MonoBehaviour, ITableBound
{
    [Header("The afterimage")]
    [SerializeField, Range(0f, 1f), Tooltip("How solid it is when fresh (the character itself, see-through; 1 would be solid).")]
    private float ghostAlpha = .5f;
    [SerializeField, Min(.05f), Tooltip("How long it takes to go (real s).")]
    private float ghostSeconds = .3f;
    [SerializeField, Min(0f), Tooltip("This PC's own rises over this long (real s), so its camera is out of it first.")]
    private float ghostRise = .04f;
    [SerializeField, Min(1000), Tooltip("Most vertices baked into it: the biggest meshes of a heavy outfit are left out past it.")]
    private int ghostVertexBudget = 150000;
    [Header("The streak")]
    [SerializeField, Min(0), Tooltip("Its lines, from the feet up (one at the eyes would be left out in first person).")]
    private int lineCount = 3;
    [SerializeField, Tooltip("Their heights off the floor (m), one per line; the last repeats.")]
    private float[] lineHeights = { .2f, .55f, .9f };
    [SerializeField, Min(.001f), Tooltip("How thick (m).")]
    private float lineWidth = .06f;
    [SerializeField, Min(0f), Tooltip("How far the kinks jump off the line (m).")]
    private float lineKink = .12f;
    [SerializeField, Min(0f), Tooltip("How far it trails behind where the dodge began (m).")]
    private float lineTail = .4f;
    [SerializeField, Min(.05f), Tooltip("How long it takes to thin away once the body lands (real s).")]
    private float lineSeconds = .22f;
    [SerializeField, Tooltip("Its colour.")]
    private Color lineColor = new Color(.95f, .98f, 1f, 1f);
    [SerializeField, Min(0f), Tooltip("How bright it burns (above 1 glows).")]
    private float lineGlow = 3f;
    [Header("The kick-off puff")]
    [SerializeField, Min(0f), Tooltip("Its bright core (m across).")]
    private float popSize = .8f;
    [SerializeField, Min(0), Tooltip("Soft puffs of smoke thrown back round it.")]
    private int smokePuffs = 3;
    [SerializeField, Min(.05f), Tooltip("How long a puff lasts (real s).")]
    private float smokeSeconds = .35f;
    [Header("An evade (a blow let through)")]
    [SerializeField, Tooltip("The streak goes this colour.")]
    private Color successLine = new Color(1f, .14f, .12f, 1f);
    [SerializeField, Min(.1f), Tooltip("A Q/E slip has no dash: its red streak runs this far (m) to the body from the side it slipped from.")]
    private float slipLength = .9f;

    // The ghost shader's properties (Resources/Combat/Shaders/DodgeGhost), set per draw from each renderer's own look.
    private const string GhostShader = "Combat/Shaders/DodgeGhost";
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), MainTexStId = Shader.PropertyToID("_MainTex_ST"),
        ColorId = Shader.PropertyToID("_Color"), CutId = Shader.PropertyToID("_Cut"), AlphaId = Shader.PropertyToID("_Alpha");
    // The longest a dodge is followed before it is taken as over (real s), and how long a copy holds word of an evade
    // for a dash that hasn't begun on it yet.
    private const float MostDash = .5f, RedWait = .35f;
    // First person, a line keeps at least this far below the eyes (m), or it would pass through the camera.
    private const float EyeClear = .4f;
    private const int LinePoints = 5;

    // One piece of a baked mesh's look: the texture, its tiling, the colour its own material draws it with, and 1 when
    // the texture's alpha cuts it (cut-out and see-through materials).
    private struct Look { public Texture texture; public Vector4 st; public Color color; public float cut; }
    // A shader's main texture and colour property ids (MainOf).
    private struct Main { public int texture, color; public bool hasTexture, hasColor; }
    private static readonly Dictionary<Shader, Main> mains = new Dictionary<Shader, Main>();

    private sealed class Ghost
    {
        public readonly List<Mesh> meshes = new List<Mesh>();
        public readonly List<Matrix4x4> places = new List<Matrix4x4>();
        // Each mesh's sub-mesh looks start at its entry here in looks.
        public readonly List<int> firstLook = new List<int>();
        public readonly List<Look> looks = new List<Look>();
        public int used;
        public float age, seconds, rise;
        public bool live;
    }

    // One line of a streak, from where it began (from, going way) to the head it is placed at.
    private sealed class Line
    {
        public LineRenderer renderer;
        public readonly Vector3[] points = new Vector3[LinePoints];
        public Vector3 from, way;
        public float height, side, kinkA, kinkB, tail, age, seconds;
        public bool live, following;
    }

    private static Material ghostMaterial;
    private static Texture2D lineTexture;
    private static ParticleSystem pop, smoke;
    // This player's own copy of the additive line material, its colour pushed past 1 by lineGlow so the lines bloom
    // (a line's own colours are 8-bit and can't).
    private Material lineMaterial;

    private PlayerMovement player;
    // One afterimage a dodge; two, for a dodge chained on a confirmed one while the first's is still fading.
    private readonly Ghost[] ghosts = { new Ghost(), new Ghost() };
    private readonly List<Line> lines = new List<Line>();
    private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
    private readonly List<Material> materials = new List<Material>();
    // Made on the first draw: Unity refuses one made in a field initializer (the component's constructor).
    private MaterialPropertyBlock block;
    private Transform lineHolder;
    private Vector3 direction;
    private float dashAge, redUntil;
    private int lastActivation;
    private bool wasDodging, dashing;

    /// <summary>
    /// This PC's own player let a blow through a dodge (side: the lean side of a Q/E slip, 0 for a step):
    /// PlayerNetworkCombat tells the other PCs, whose copies show it (ShowEvaded).
    /// </summary>
    public event System.Action<int> Evaded;

    private bool Mine => player != null && player.IsLocal && !player.IsNetworkDriven;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        GameTables.Bind(PlayerMovement.TableName, "DodgeLook", this);
    }

    void ITableBound.OnTableApplied() { if (lineHeights == null || lineHeights.Length == 0) lineHeights = new[] { .5f }; }

    /// <summary>
    /// This PC's own player let a blow through a dodge (DodgeFlash): the streak goes red here, and on every other PC
    /// through Evaded. side: the lean side of a Q/E slip, 0 for a step.
    /// </summary>
    public void Succeed(int side = 0)
    {
        if (Flare(side) && Mine) Evaded?.Invoke(side);
    }

    /// <summary>Another PC's player let a blow through a dodge (EvadedRpc): their copy's streak goes red as it did there.</summary>
    public void ShowEvaded(int side) => Flare(side);

    // The streak under way goes red, the part already let go held a moment longer; with none (a Q/E slip, or word of it
    // come after the streak is gone) a short red streak runs to the body from the side the blow went by.
    private bool Flare(int side)
    {
        if (!Application.isPlaying || player == null) return false;
        bool any = false;
        foreach (var line in lines)
        {
            if (!line.live) continue;
            Tint(line, successLine);
            if (!line.following) line.age = 0f;
            any = true;
        }
        if (any) return true;
        // A copy can hear of a step's evade before its own dash has begun (the word and the movement come apart): that
        // dash begins red.
        if (side == 0 && !Mine) { redUntil = Time.unscaledTime + RedWait; return true; }
        Vector3 way = side != 0 ? transform.right * side : direction.sqrMagnitude > .5f ? direction : -transform.forward;
        way = Vector3.ProjectOnPlane(way, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = -Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        way.Normalize();
        Vector3 at = transform.position;
        Streak(at - way * slipLength, way, at, false, successLine);
        return true;
    }

    // This PC's own player seen from its own eyes (first person): a line through the head would cross the camera.
    private bool ViewInBody()
    {
        if (!Mine) return false;
        var view = Camera.main;
        Transform eyes = PlayerView.EyesOf(player);
        return view == null || eyes == null || (view.transform.position - eyes.position).sqrMagnitude < .36f;
    }

    private void LateUpdate()
    {
        if (player == null || Time.timeScale <= 0f) return;
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        bool dodging = player.CurrentState == MovementState.StepDodge;
        // A step chained on a confirmed one restarts in the same state: this PC's own dodge counts each start.
        int activation = Mine && player.Abilities != null ? player.Abilities.Dodge.Activation : 0;
        if (dodging && (!wasDodging || activation != lastActivation)) Begin();
        else if (dashing && (!dodging || dashAge > MostDash)) Land();
        wasDodging = dodging; lastActivation = activation;
        if (dashing) Follow(dt);
        TickGhosts(dt);
        TickLines(dt);
    }

    // The beat, all at once: the puff at the foot, the one afterimage where it began, the streak from there.
    private void Begin()
    {
        if (dashing) Land();
        dashing = true; dashAge = 0f;
        Vector3 start = transform.position;
        direction = player.Abilities != null ? Vector3.ProjectOnPlane(player.Abilities.ActionDirection, Vector3.up) : Vector3.zero;
        if (direction.sqrMagnitude < 1e-4f) direction = -Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        direction.Normalize();
        Pop(start + Vector3.up * .08f);
        Puffs(start, smokePuffs, new Color(.95f, .97f, 1f, .5f));
        Bake(Free(), ghostSeconds, Mine ? ghostRise : 0f);
        Streak(start, direction, start, true, Time.unscaledTime < redUntil ? successLine : lineColor);
        redUntil = 0f;
    }

    // The dash lasts 0.14 s: the streak keeps its heads on the body as it goes, so it lands as one stroke.
    private void Follow(float dt)
    {
        dashAge += dt;
        Vector3 at = transform.position;
        foreach (var line in lines) if (line.live && line.following) Place(line, at);
    }

    // Landed: the streak lets go of the body and thins away. Nothing new comes: one beat.
    private void Land()
    {
        dashing = false;
        foreach (var line in lines) if (line.live && line.following) { line.following = false; line.age = 0f; }
    }

    // One streak: lineCount kinked lines from just behind from to head, at their heights, all going together.
    // following: their heads ride on the body until it lands.
    private void Streak(Vector3 from, Vector3 way, Vector3 head, bool following, Color color)
    {
        // In first person the lines keep below the eyes, crouched or standing, or they would run through the camera.
        Transform eyes = ViewInBody() ? PlayerView.EyesOf(player) : null;
        float clear = eyes != null ? eyes.position.y - from.y - EyeClear : float.PositiveInfinity;
        for (int i = 0; i < lineCount; i++)
        {
            float height = lineHeights[Mathf.Min(i, lineHeights.Length - 1)];
            if (height > clear) continue;
            var line = Take();
            line.from = from; line.way = way;
            line.height = height; line.side = Random.Range(-.16f, .16f);
            line.kinkA = Random.Range(-1f, 1f) * lineKink; line.kinkB = Random.Range(-1f, 1f) * lineKink;
            line.tail = lineTail * Random.Range(.6f, 1f);
            line.following = following; line.age = 0f; line.seconds = lineSeconds;
            Tint(line, color);
            Place(line, head);
        }
    }

    // A line from its tail behind where it began to the head, kinked twice on the way like a bolt.
    private static void Place(Line line, Vector3 head)
    {
        Vector3 side = Vector3.Cross(Vector3.up, line.way);
        Vector3 up = Vector3.up * line.height, across = side * line.side;
        Vector3 from = line.from + up + across, to = head + up + across;
        var points = line.points;
        points[0] = from - line.way * line.tail;
        points[1] = from;
        points[2] = Vector3.Lerp(from, to, .38f) + side * line.kinkA + Vector3.up * (line.kinkA * .4f);
        points[3] = Vector3.Lerp(from, to, .68f) - side * line.kinkB - Vector3.up * (line.kinkB * .4f);
        points[4] = to;
        line.renderer.SetPositions(points);
    }

    private void TickLines(float dt)
    {
        foreach (var line in lines)
        {
            if (!line.live) continue;
            if (line.following) { line.renderer.widthMultiplier = lineWidth; continue; }
            line.age += dt;
            float left = 1f - line.age / line.seconds;
            if (left <= 0f) { line.live = false; line.renderer.enabled = false; continue; }
            // Bold nearly to the end, then gone: the beat doesn't trail off.
            line.renderer.widthMultiplier = lineWidth * Mathf.Sqrt(left);
        }
    }

    private void TickGhosts(float dt)
    {
        var material = GhostMaterial();
        if (material == null || ghosts == null) return;
        foreach (var ghost in ghosts)
        {
            if (ghost == null || !ghost.live) continue;
            if (ghost.used > ghost.meshes.Count) { ghost.live = false; continue; }
            ghost.age += dt;
            float rise = ghost.rise > 0f ? Mathf.Clamp01(ghost.age / ghost.rise) : 1f;
            float left = 1f - Mathf.Max(0f, ghost.age - ghost.rise) / ghost.seconds;
            if (left <= 0f) { ghost.live = false; continue; }
            // One block for every draw: DrawMesh copies it as it is when called.
            block ??= new MaterialPropertyBlock();
            block.SetFloat(AlphaId, ghostAlpha * rise * left * left);
            for (int i = 0; i < ghost.used; i++)
            {
                var mesh = ghost.meshes[i];
                int first = i < ghost.firstLook.Count ? ghost.firstLook[i] : 0;
                for (int part = 0; part < mesh.subMeshCount; part++)
                {
                    int at = Mathf.Min(first + part, ghost.looks.Count - 1);
                    var look = at >= 0 ? ghost.looks[at] : new Look { st = new Vector4(1f, 1f, 0f, 0f), color = Color.white };
                    block.SetTexture(MainTexId, look.texture != null ? look.texture : Texture2D.whiteTexture);
                    block.SetVector(MainTexStId, look.st);
                    block.SetColor(ColorId, look.color);
                    block.SetFloat(CutId, look.cut);
                    Graphics.DrawMesh(mesh, ghost.places[i], material, 0, null, part, block, ShadowCastingMode.Off, false);
                }
            }
        }
    }

    // The body as it is this moment, every skinned mesh of it that shows, up to the vertex budget, with each of its
    // sub-meshes' own texture, tiling and colour.
    private void Bake(Ghost ghost, float seconds, float rise)
    {
        if (ghost == null || GhostMaterial() == null) return;
        skins.Clear();
        GetComponentsInChildren(false, skins);
        int budget = ghostVertexBudget;
        ghost.used = 0;
        ghost.firstLook.Clear(); ghost.looks.Clear();
        foreach (var skin in skins)
        {
            var shared = skin.sharedMesh;
            if (!skin.enabled || shared == null || !skin.gameObject.activeInHierarchy) continue;
            int vertices = shared.vertexCount;
            if (vertices > budget) continue;
            budget -= vertices;
            if (ghost.used == ghost.meshes.Count)
            {
                var made = new Mesh { name = "Dodge ghost" };
                made.MarkDynamic();
                ghost.meshes.Add(made); ghost.places.Add(Matrix4x4.identity);
            }
            var mesh = ghost.meshes[ghost.used];
            // Scaled as the renderer is, so it is drawn without its scale again.
            skin.BakeMesh(mesh, true);
            Transform place = skin.transform;
            ghost.places[ghost.used] = Matrix4x4.TRS(place.position, place.rotation, Vector3.one);
            ghost.firstLook.Add(ghost.looks.Count);
            materials.Clear();
            skin.GetSharedMaterials(materials);
            for (int part = 0; part < mesh.subMeshCount; part++)
                ghost.looks.Add(LookOf(materials.Count > 0 ? materials[Mathf.Min(part, materials.Count - 1)] : null));
            ghost.used++;
        }
        ghost.age = 0f; ghost.seconds = seconds; ghost.rise = rise;
        ghost.live = ghost.used > 0;
    }

    // What a material draws as its own surface: its shader's main texture and colour (MainOf), the texture's tiling,
    // and whether the texture's alpha cuts it. Only cut-out and see-through materials are cut: an opaque one ignores
    // its texture's alpha, and many keep something else there. Anything without a main texture draws white.
    private static Look LookOf(Material material)
    {
        var look = new Look { st = new Vector4(1f, 1f, 0f, 0f), color = Color.white };
        var shader = material != null ? material.shader : null;
        if (shader == null) return look;
        var main = MainOf(shader);
        if (main.hasTexture)
        {
            look.texture = material.GetTexture(main.texture);
            Vector2 scale = material.GetTextureScale(main.texture), offset = material.GetTextureOffset(main.texture);
            look.st = new Vector4(scale.x, scale.y, offset.x, offset.y);
        }
        if (main.hasColor) look.color = material.GetColor(main.color);
        look.cut = material.renderQueue >= (int)RenderQueue.AlphaTest ? 1f : 0f;
        return look;
    }

    // A shader's main texture and colour, found once per shader: the ones it marks [MainTexture] and [MainColor]
    // (URP Lit: _BaseMap, _BaseColor; lilToon: _MainTex, _Color — lilToon also keeps a hidden, unused _BaseMap and
    // _BaseColor for fallbacks, so going by the names alone would draw its outfits white), else the names Unity takes
    // as main (_MainTex, _Color: MToon, Poiyomi, the built-in shaders), else URP's.
    private static Main MainOf(Shader shader)
    {
        if (mains.TryGetValue(shader, out var main)) return main;
        int texture = -1, color = -1;
        for (int i = 0, count = shader.GetPropertyCount(); i < count; i++)
        {
            var flags = shader.GetPropertyFlags(i);
            var type = shader.GetPropertyType(i);
            if (texture < 0 && (flags & ShaderPropertyFlags.MainTexture) != 0 && type == ShaderPropertyType.Texture) texture = i;
            if (color < 0 && (flags & ShaderPropertyFlags.MainColor) != 0 && type == ShaderPropertyType.Color) color = i;
        }
        if (texture < 0) texture = shader.FindPropertyIndex("_MainTex");
        if (texture < 0) texture = shader.FindPropertyIndex("_BaseMap");
        if (color < 0) color = shader.FindPropertyIndex("_Color");
        if (color < 0) color = shader.FindPropertyIndex("_BaseColor");
        main = new Main
        {
            hasTexture = texture >= 0, texture = texture >= 0 ? shader.GetPropertyNameId(texture) : 0,
            hasColor = color >= 0, color = color >= 0 ? shader.GetPropertyNameId(color) : 0,
        };
        mains[shader] = main;
        return main;
    }

    // The older ghost if both are still showing.
    private Ghost Free()
    {
        Ghost oldest = ghosts[0];
        foreach (var ghost in ghosts)
        {
            if (!ghost.live) return ghost;
            if (ghost.age > oldest.age) oldest = ghost;
        }
        return oldest;
    }

    private Line Take()
    {
        Line line = null;
        foreach (var free in lines) if (!free.live) { line = free; break; }
        if (line == null)
        {
            if (lineHolder == null) lineHolder = FeelAssets.CreateRuntimeRoot("Dodge lines " + name).transform;
            var host = new GameObject("Dodge streak line");
            host.transform.SetParent(lineHolder, false);
            var renderer = host.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.alignment = LineAlignment.View;
            renderer.textureMode = LineTextureMode.Stretch;
            renderer.numCapVertices = 0; renderer.numCornerVertices = 0;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sharedMaterial = LineMaterial();
            renderer.positionCount = LinePoints;
            line = new Line { renderer = renderer };
            lines.Add(line);
        }
        line.live = true;
        line.renderer.enabled = true;
        line.renderer.widthMultiplier = lineWidth;
        return line;
    }

    // Faint at the tail, full along the body's way, fading off at the head.
    private static void Tint(Line line, Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .22f), new GradientAlphaKey(1f, .8f), new GradientAlphaKey(0f, 1f) });
        line.renderer.colorGradient = gradient;
    }

    private static Material GhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;
        var shader = Resources.Load<Shader>(GhostShader);
        if (shader == null) return null;
        return ghostMaterial = new Material(shader) { name = "Dodge Ghost (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    // Soft across its width, even along its length: the ends are faded by the line's own colours.
    private Material LineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        if (lineTexture == null)
        {
            lineTexture = new Texture2D(4, 32, TextureFormat.RGBA32, false) { name = "Dodge Line", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 32; y++)
            {
                float across = (y / 31f - .5f) / .22f;
                float a = Mathf.Exp(-across * across);
                for (int x = 0; x < 4; x++) lineTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            lineTexture.Apply(false, false);
        }
        lineMaterial = new Material(FeelAssets.Particle(lineTexture, true)) { name = "Dodge Line (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", new Color(lineGlow, lineGlow, lineGlow, 1f));
        return lineMaterial;
    }

    // The white puff at the foot and its smoke: one pair of emitters for every player's dodges.
    private static void Emitters()
    {
        if (pop != null && smoke != null) return;
        var root = FeelAssets.CreateRuntimeRoot("Dodge Dash VFX").transform;
        // Its own copy of the additive dot, pushed past 1 so the puff blooms as the reference's does (particle colours are 8-bit).
        var hot = new Material(FeelAssets.Particle(FeelAssets.SoftDot, true)) { name = "Dodge Pop (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        if (hot.HasProperty("_Color")) hot.SetColor("_Color", new Color(2.4f, 2.4f, 2.4f, 1f));
        pop = FeelAssets.CreateEmitter("Dodge pop", root, hot, 24, 0f);
        var main = pop.main; main.useUnscaledTime = true;
        var popFade = pop.colorOverLifetime; popFade.enabled = true; popFade.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
        var popGrow = pop.sizeOverLifetime; popGrow.enabled = true; popGrow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .7f, 1f, 1.25f));
        smoke = FeelAssets.CreateEmitter("Dodge smoke", root, FeelAssets.Particle(FeelAssets.SoftDot, false), 160, -.02f);
        main = smoke.main; main.useUnscaledTime = true;
        var smokeFade = smoke.colorOverLifetime; smokeFade.enabled = true; smokeFade.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
        var smokeGrow = smoke.sizeOverLifetime; smokeGrow.enabled = true; smokeGrow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 1.5f));
        var drag = smoke.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 3f; drag.multiplyDragByParticleSize = false;
    }

    private void Pop(Vector3 at)
    {
        if (popSize <= 0f) return;
        Emitters();
        pop.Emit(new ParticleSystem.EmitParams
        {
            position = at, velocity = Vector3.zero, startSize = popSize, startLifetime = .14f,
            startColor = new Color(1f, 1f, 1f, .95f)
        }, 1);
    }

    private void Puffs(Vector3 at, int count, Color color)
    {
        if (count <= 0) return;
        Emitters();
        var puff = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector2 flat = Random.insideUnitCircle;
            puff.position = at + new Vector3(flat.x * .2f, .06f + Random.value * .1f, flat.y * .2f);
            // Thrown back off the kick and spreading low over the floor.
            puff.velocity = -direction * Random.Range(.6f, 1.6f) + new Vector3(flat.x, Random.Range(.15f, .5f), flat.y) * .5f;
            puff.startSize = Random.Range(.3f, .48f);
            puff.startLifetime = smokeSeconds * Random.Range(.85f, 1.1f);
            puff.startColor = color;
            smoke.Emit(puff, 1);
        }
    }

    private void OnDisable()
    {
        dashing = wasDodging = false;
        foreach (var ghost in ghosts) ghost.live = false;
        foreach (var line in lines) { line.live = false; if (line.renderer != null) line.renderer.enabled = false; }
    }

    private void OnDestroy()
    {
        foreach (var ghost in ghosts) foreach (var mesh in ghost.meshes) if (mesh != null) Destroy(mesh);
        if (lineHolder != null) Destroy(lineHolder.gameObject);
        if (lineMaterial != null) Destroy(lineMaterial);
    }
}
