using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The 2026-11-11 stealth round's view of the world, for the local player, painted on the ground
/// (Hidden/ENFILADE/StealthDecal):
/// - Sight cones: what each nearby zombie that isn't awake can see, run out along rays from its eyes so walls cut it
///   off where its view ends, the way a light on its head would cast it (the user's reference: a top-down view hidden
///   behind walls by one camera on the character's head). Brightest across its full view, fainter toward the edge of
///   its sight; white while it notices nothing, pink as it grows sure or stops to look, gone once it wakes. They come in
///   while the player crouches to sneak (Stealth.csv View.coneMode) and turn with the zombie as it looks round.
/// - Sound rings: each sound of the player's own that a calm zombie could hear runs out over the ground as a ring to
///   how far it carries, pink when one that isn't awake does hear it (Mark of the Ninja's noise circles), so it is
///   learnt what is loud. Only near zombies that aren't awake; a crouched step makes none (ZombieMind.CalmReach).
/// Drawn with Graphics.DrawMesh on the player's camera only. On the local player, added by PlayerMovement.
/// </summary>
[DisallowMultipleComponent]
public sealed class StealthView : MonoBehaviour, ITableBound
{
    public const string TableName = "Stealth", TableRow = "View";
    private const string ShaderPath = "Combat/Shaders/StealthDecal";
    private const int Reaches = 24, MostRings = 4;

    [SerializeField, Range(0, 2), Tooltip("When sight cones show: 0 never, 1 while crouched, 2 always.")]
    private int coneMode = 1;
    [SerializeField, Min(0f), Tooltip("Zombies farther than this from the player (m) show no cone.")]
    private float coneNear = 26f;
    [SerializeField, Range(1, 12), Tooltip("Most cones at once, the nearest zombies'.")]
    private int mostCones = 6;
    [SerializeField, Range(0f, 1f), Tooltip("How strong a cone is across its full view.")]
    private float coneStrength = .17f;
    [SerializeField, Range(0f, 1f), Tooltip("Toward the edge of its sight, this share of that.")]
    private float coneEdge = .35f;
    [SerializeField, Range(0f, 2f), Tooltip("How bright its rim is.")]
    private float coneRim = .55f;
    [SerializeField, Min(.01f), Tooltip("How long cones take to come in and go (s).")]
    private float coneFade = .25f;
    [SerializeField, Min(.1f), Tooltip("How far above or below its feet a cone still paints the ground (m).")]
    private float coneHeight = 1.1f;
    [SerializeField, Min(0f), Tooltip("Sounds carrying less far than this to a calm zombie (m) make no ring.")]
    private float ringFrom = 2.5f;
    [SerializeField, Min(.1f), Tooltip("How long a ring lasts (s).")]
    private float ringSeconds = .9f;
    [SerializeField, Min(1f), Tooltip("A ring shows only with a zombie that isn't awake within this many times its reach.")]
    private float ringNear = 2.5f;
    [SerializeField, Range(0f, 1f), Tooltip("How strong a ring is.")]
    private float ringStrength = .55f;
    [SerializeField, Min(.01f), Tooltip("A ring's width (m).")]
    private float ringWidth = .14f;

    private static readonly int TintId = Shader.PropertyToID("_Tint"), OriginId = Shader.PropertyToID("_Origin"),
        FacingId = Shader.PropertyToID("_Facing"), ShapeId = Shader.PropertyToID("_Shape"), LookId = Shader.PropertyToID("_Look"),
        ReachesId = Shader.PropertyToID("_Reaches");
    private static int worldMask;
    // The world: what stops a zombie's view (not players, corpses or what lies on the floor, not other zombies).
    private static int WorldMask => worldMask != 0 ? worldMask : worldMask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");

    private sealed class Cone
    {
        public ZombieMind mind;
        public readonly float[] reaches = new float[Reaches];
        public readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        public float alpha;
        public bool live, cast;
    }

    private sealed class Ring
    {
        public Vector3 at;
        public float reach, age = -1f;
        public bool heard;
        public readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    }

    private readonly List<Cone> cones = new List<Cone>();
    private readonly Stack<Cone> spare = new Stack<Cone>();
    private readonly Ring[] rings = new Ring[MostRings];
    private readonly List<ZombieMind> near = new List<ZombieMind>();
    private readonly Dictionary<ZombieMind, float> nearness = new Dictionary<ZombieMind, float>();
    private PlayerMovement movement;
    private Material material;
    private Mesh cube;
    private float show, pickLeft, lastRing = -9f;
    // The stone's aim (PlayerThrow, held T): where it would land, how far it carries, the frame it was last asked for,
    // whether a stone is ready, and which zombies would hear it (worked out a few times a second).
    private readonly HashSet<ZombieMind> wouldHear = new HashSet<ZombieMind>();
    // Made in Awake, not here: Unity won't make a MaterialPropertyBlock while a MonoBehaviour is being constructed (field
    // initializers run then), and the throw left this view half made each time PlayerMovement added it.
    private MaterialPropertyBlock aimBlock, aimDot;
    private Vector3 aimAt, aimHeardAt;
    private float aimNoise, aimReach, aimCheckAt;
    private int aimFrame = -9;
    private bool aimReady, aimHeard;

    /// <summary>
    /// The held stone's landing (PlayerThrow), for this frame: the ring of how far its clack carries there, pink when a
    /// zombie that isn't awake will hear it, those zombies' cones pink, and every cone shown while aiming.
    /// </summary>
    internal void ShowAim(Vector3 at, float noise, bool ready)
    {
        aimAt = at;
        aimNoise = noise;
        aimReady = ready;
        aimFrame = Time.frameCount;
    }

    private bool Aiming => Time.frameCount - aimFrame <= 1;

    // Which zombies would hear the stone land there, and how far its clack carries: again when the aim moves or anyway
    // a few times a second (each is a ray).
    private void Hearers()
    {
        if (Time.time < aimCheckAt && (aimAt - aimHeardAt).sqrMagnitude < .09f) return;
        aimCheckAt = Time.time + .15f;
        aimHeardAt = aimAt;
        var clack = new NoiseEvent(aimAt + Vector3.up * .15f, aimNoise, null, true);
        aimReach = ZombieMind.CalmReach(clack, null, out aimHeard);
        wouldHear.Clear();
        foreach (var cone in cones) if (cone.mind != null && cone.mind.Hears(clack)) wouldHear.Add(cone.mind);
    }

    private void DrawAim(Camera view)
    {
        Hearers();
        if (aimReach <= 0f) return;
        float strength = aimReady ? 1f : .45f;
        Color tint = (aimHeard ? KitStyle.Accent : KitStyle.Ice).linear;
        // The reach, standing still, faintly filled.
        aimBlock.SetVector(TintId, new Vector4(tint.r, tint.g, tint.b, ringStrength * .7f * strength));
        aimBlock.SetVector(OriginId, new Vector4(aimAt.x, aimAt.y, aimAt.z, aimReach));
        aimBlock.SetVector(FacingId, new Vector4(0f, 1f, 0f, 0f));
        aimBlock.SetVector(ShapeId, new Vector4(1f, aimReach, ringWidth, coneHeight));
        aimBlock.SetVector(LookId, new Vector4(0f, 0f, 0f, .05f));
        var box = Matrix4x4.TRS(aimAt, Quaternion.identity, new Vector3(aimReach * 2f + 1f, coneHeight * 2f + .5f, aimReach * 2f + 1f));
        Graphics.DrawMesh(cube, box, material, 0, view, 0, aimBlock, ShadowCastingMode.Off, false);
        // And a small beating ring where it lands.
        float beat = .3f + .06f * Mathf.Sin(Time.unscaledTime * 9f);
        Color mark = (aimReady ? KitStyle.Accent : KitStyle.Ice).linear;
        aimDot.SetVector(TintId, new Vector4(mark.r, mark.g, mark.b, .9f * strength));
        aimDot.SetVector(OriginId, new Vector4(aimAt.x, aimAt.y, aimAt.z, .6f));
        aimDot.SetVector(FacingId, new Vector4(0f, 1f, 0f, 0f));
        aimDot.SetVector(ShapeId, new Vector4(1f, beat, .07f, coneHeight));
        aimDot.SetVector(LookId, new Vector4(0f, 0f, 0f, .25f));
        Graphics.DrawMesh(cube, Matrix4x4.TRS(aimAt, Quaternion.identity, new Vector3(2.2f, coneHeight * 2f + .5f, 2.2f)), material, 0, view, 0, aimDot,
            ShadowCastingMode.Off, false);
    }
    private int turn;
    private bool broken;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        // The knock key (PlayerKnock) goes with the stealth view: alone there is no PlayerNetworkCombat to add it.
        if (GetComponent<PlayerKnock>() == null) gameObject.AddComponent<PlayerKnock>();
        for (int i = 0; i < rings.Length; i++) rings[i] = new Ring();
        aimBlock = new MaterialPropertyBlock();
        aimDot = new MaterialPropertyBlock();
        GameTables.Bind(TableName, TableRow, this);
    }

    void ITableBound.OnTableApplied() { }

    private void OnEnable() => Noise.Made += OnNoise;
    private void OnDisable() => Noise.Made -= OnNoise;

    private void LateUpdate()
    {
        if (movement == null || !movement.IsLocal || !Ready()) return;
        var view = Camera.main;
        if (view == null) return;
        float dt = Time.deltaTime;
        bool alive = !movement.IsDead;
        bool aiming = alive && Aiming;
        // Aiming a stone shows them too, whatever the posture: whom it would turn is the point.
        bool wanted = alive && (coneMode == 2 || coneMode == 1 && (movement.IsCrouching || movement.IsProning) || aiming && coneMode > 0);
        show = Mathf.MoveTowards(show, wanted ? 1f : 0f, dt / coneFade);
        if ((pickLeft -= dt) <= 0f) { pickLeft = .25f; Pick(); }
        if (show > 0f) CastSome();
        for (int i = cones.Count - 1; i >= 0; i--)
        {
            var cone = cones[i];
            bool on = cone.live && cone.mind != null && cone.mind.Living && !cone.mind.IsAwake;
            cone.alpha = Mathf.MoveTowards(cone.alpha, on ? 1f : 0f, dt / coneFade);
            if (cone.alpha <= 0f && !on) { cones.RemoveAt(i); cone.mind = null; spare.Push(cone); continue; }
            if (show > 0f && cone.cast && cone.mind != null) DrawCone(cone, view);
        }
        if (aiming) DrawAim(view);
        foreach (var ring in rings)
        {
            if (ring.age < 0f) continue;
            ring.age += dt;
            if (ring.age >= ringSeconds || !alive) { ring.age = -1f; continue; }
            DrawRing(ring, view);
        }
    }

    // The nearest zombies that aren't awake get the cones, the rest let theirs go.
    private void Pick()
    {
        near.Clear();
        nearness.Clear();
        Vector3 me = movement.transform.position;
        float most = coneNear * coneNear;
        foreach (var mind in ZombieMind.All)
        {
            if (mind == null || !mind.isActiveAndEnabled || mind.IsAwake || !mind.Living) continue;
            float far = (mind.transform.position - me).sqrMagnitude;
            if (far > most) continue;
            near.Add(mind);
            nearness[mind] = far;
        }
        near.Sort((a, b) => nearness[a].CompareTo(nearness[b]));
        if (near.Count > mostCones) near.RemoveRange(mostCones, near.Count - mostCones);
        foreach (var cone in cones) cone.live = cone.mind != null && near.Contains(cone.mind);
        foreach (var mind in near)
        {
            bool has = false;
            foreach (var cone in cones) if (cone.mind == mind) { has = true; break; }
            if (has) continue;
            var made = spare.Count > 0 ? spare.Pop() : new Cone();
            made.mind = mind;
            made.alpha = 0f;
            made.live = true;
            made.cast = false;
            cones.Add(made);
        }
    }

    // Rays for the cones: every frame for one turned to look (it turns fast), two of the rest a frame in turn.
    private void CastSome()
    {
        if (cones.Count == 0) return;
        foreach (var cone in cones)
            if (cone.mind != null && (cone.mind.Mood == ZombieMood.Alert || cone.mind.Mood == ZombieMood.Searching || !cone.cast)) Cast(cone);
        for (int k = 0; k < 2; k++)
        {
            turn = (turn + 1) % cones.Count;
            Cast(cones[turn]);
        }
    }

    private void Cast(Cone cone)
    {
        var mind = cone.mind;
        if (mind == null) return;
        Vector3 eye = mind.EyePoint;
        Vector2 forward = Flat(mind.transform.forward);
        float edge = mind.EdgeView * .5f * Mathf.Deg2Rad, range = mind.ViewRange;
        for (int i = 0; i < Reaches; i++)
        {
            // Turned from its forward on the ground by the same sense the shader reads them in.
            float angle = -edge + 2f * edge * i / (Reaches - 1), c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            var way = new Vector3(forward.x * c - forward.y * s, 0f, forward.x * s + forward.y * c);
            cone.reaches[i] = Physics.Raycast(eye, way, out RaycastHit hit, range, WorldMask, QueryTriggerInteraction.Ignore) ? hit.distance : range;
        }
        cone.block.SetFloatArray(ReachesId, cone.reaches);
        cone.cast = true;
    }

    private void DrawCone(Cone cone, Camera view)
    {
        var mind = cone.mind;
        Vector3 feet = mind.transform.position;
        Vector2 forward = Flat(mind.transform.forward);
        // How far it would make this player out where they stand now: shorter in the dark (StealthLight), so the cone
        // shows what hiding in the shadows buys.
        float range = mind.ViewRange * StealthLight.RangeShare(StealthLight.Level(movement));
        var mood = mind.Mood;
        bool looking = mood == ZombieMood.Alert || mood == ZombieMood.Searching;
        float sure = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mind.ShownSuspicion / .6f));
        // One the aimed stone would turn shows pink and beats.
        bool turned = Aiming && wouldHear.Contains(mind);
        Color tint = Color.Lerp(KitStyle.Ice, KitStyle.Accent, looking || turned ? 1f : sure).linear;
        float strength = coneStrength * cone.alpha * show * (looking ? 1.35f : 1f + .3f * sure)
            * (turned ? 1.4f + .25f * Mathf.Sin(Time.unscaledTime * 8f) : 1f);
        cone.block.SetVector(TintId, new Vector4(tint.r, tint.g, tint.b, strength));
        cone.block.SetVector(OriginId, new Vector4(feet.x, feet.y, feet.z, range));
        cone.block.SetVector(FacingId, new Vector4(forward.x, forward.y, mind.FrontView * .5f * Mathf.Deg2Rad, mind.EdgeView * .5f * Mathf.Deg2Rad));
        cone.block.SetVector(ShapeId, new Vector4(0f, 0f, 0f, coneHeight));
        cone.block.SetVector(LookId, new Vector4(coneEdge, Mathf.Repeat(Time.time * (looking ? .9f : .3f), 1f), coneRim, 0f));
        var box = Matrix4x4.TRS(feet, Quaternion.identity, new Vector3(range * 2f + 1f, coneHeight * 2f + .5f, range * 2f + 1f));
        Graphics.DrawMesh(cube, box, material, 0, view, 0, cone.block, ShadowCastingMode.Off, false);
    }

    private void DrawRing(Ring ring, Camera view)
    {
        float t = Mathf.Clamp01(ring.age / ringSeconds);
        float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / .45f), 3f);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, (t - .5f) / .5f);
        Color tint = (ring.heard ? KitStyle.Accent : KitStyle.Ice).linear;
        float reach = Mathf.Max(.1f, ring.reach);
        ring.block.SetVector(TintId, new Vector4(tint.r, tint.g, tint.b, ringStrength * fade));
        ring.block.SetVector(OriginId, new Vector4(ring.at.x, ring.at.y, ring.at.z, reach));
        ring.block.SetVector(FacingId, new Vector4(0f, 1f, 0f, 0f));
        ring.block.SetVector(ShapeId, new Vector4(1f, reach * grow, ringWidth, coneHeight));
        ring.block.SetVector(LookId, new Vector4(0f, 0f, 0f, .12f));
        var box = Matrix4x4.TRS(ring.at, Quaternion.identity, new Vector3(reach * 2f + 1f, coneHeight * 2f + .5f, reach * 2f + 1f));
        Graphics.DrawMesh(cube, box, material, 0, view, 0, ring.block, ShadowCastingMode.Off, false);
    }

    // One of this player's own sounds: a ring for it, when a zombie that isn't awake is about to hear such things.
    private void OnNoise(NoiseEvent noise)
    {
        if (movement == null || !movement.IsLocal || movement.IsDead) return;
        var source = noise.Source != null ? noise.Source.GetComponentInParent<PlayerMovement>() : null;
        if (source != movement) return;
        float reach = ZombieMind.CalmReach(noise, source, out bool heard);
        if (reach < ringFrom || !AnyNear(noise.Point, reach * ringNear)) return;
        // Footsteps come fast: a new ring only once the last has run out a while, unless this sound is the bigger one.
        float now = Time.time;
        Ring newest = null, free = null, oldest = null;
        foreach (var ring in rings)
        {
            if (ring.age < 0f) { if (free == null) free = ring; continue; }
            if (newest == null || ring.age < newest.age) newest = ring;
            if (oldest == null || ring.age > oldest.age) oldest = ring;
        }
        if (newest != null && now - lastRing < .3f && reach <= newest.reach * 1.1f && !noise.Lure) return;
        // A free ring, or else the one furthest gone.
        var into = free ?? oldest ?? rings[0];
        into.at = noise.Point;
        into.reach = reach;
        into.heard = heard;
        into.age = 0f;
        lastRing = now;
    }

    private static bool AnyNear(Vector3 at, float within)
    {
        float most = within * within;
        foreach (var mind in ZombieMind.All)
            if (mind != null && mind.isActiveAndEnabled && !mind.IsAwake && mind.Living && (mind.transform.position - at).sqrMagnitude <= most)
                return true;
        return false;
    }

    private static Vector2 Flat(Vector3 forward)
    {
        var flat = new Vector2(forward.x, forward.z);
        return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.up;
    }

    private bool Ready()
    {
        if (material != null && cube != null) return true;
        if (broken) return false;
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported)
        {
            broken = true;
            Debug.LogWarning("StealthView: Resources/" + ShaderPath + " is missing or not supported; no sight cones or sound rings.", this);
            return false;
        }
        material = new Material(shader) { hideFlags = HideFlags.DontSave, name = "Stealth Decal" };
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (cube == null)
        {
            var made = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = made.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(made);
        }
        return cube != null;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
