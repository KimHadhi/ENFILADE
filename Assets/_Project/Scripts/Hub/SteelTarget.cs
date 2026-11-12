using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A hub range target's body: a self-resetting steel target, as on a real steel range (the user, 2026-11-12: the old
/// dummy's head read as a balloon and its body as wood, and each hit burst like flesh; "ㄹㅇ 쇠나 철로 된 타겟 때리는
/// 사운드"). A heavy base on the ground holds a hinge; on it stands a post with a painted torso plate on a bracket in front
/// and a round head plate hung on an axle from an arm over the top (rows Zombie: a silhouette, Plate: a thick rectangle).
/// <para>
/// Each bullet that strikes a plate, whoever fired it (IBulletStruck, so a friend's shots ring on this PC too), sparks
/// off it (the surface SteelTarget: Metal's sparks and no hole, Surfaces.csv and ImpactEffects.csv), leaves a grey lead
/// splash on the paint that rides the plate and fades, rings (a high ting from the head plate, a lower clang from the
/// torso: the Bullets bank's Steel/Head and Steel/Body, heard after the time the sound takes to come back from the plate)
/// and pushes it, by the gun's prop push: the head plate swings on its axle, the whole target rocks back on its hinge.
/// A shotgun's pellets on one plate ring once, louder. A knockdown (RangeTarget's kill) lays it on its back with a clank
/// (Steel/Down), and its spring stands it up again with a wobble. The stand stops a bullet and sparks but isn't the
/// target: a hit on it is a miss (SteelStand). The fists and a shove ring and push it too (RangeTarget passes them on).
/// </para>
/// Shapes, swings, splashes and the ring's reach are Tables/RangeTargets. It is all built at play from code, so the scene
/// keeps only where each target stands, and an older hub's wooden dummy parts are cleared away.
/// </summary>
[DisallowMultipleComponent]
public sealed class SteelTarget : MonoBehaviour, IBulletStruck
{
    public const string TableName = "RangeTargets";
    /// <summary>What its parts are made of (Surfaces.csv): metal's sparks, no bullet holes, no round gets through.</summary>
    public const string SurfaceId = "SteelTarget";
    private const string BankId = "Bullets", HeadKey = "Steel/Head", BodyKey = "Steel/Body", DownKey = "Steel/Down";
    // The hinge block's top, where the post stands and tips about (m above the ground).
    private const float Hinge = .05f, PostSize = .05f, ArmSize = .04f;
    private const int DiscSides = 28;
    // A rise ends with the plate's own ring, quieter than a shot's.
    private const float StoodRing = .35f;

    /// <summary>One row of Tables/RangeTargets: a kind of target's shape and how it answers a hit.</summary>
    public sealed class Shape
    {
        public float TorsoWidth, TorsoHeight, TorsoBottom, ShoulderCut, FootCut, Thickness, HeadRadius, HeadDrop, PostTop, PostBack;
        public Color Paint, HeadPaint, Steel;
        public float RockHz, RockSeconds, RockKick, RockMost, RiseKick;
        public float HeadHz, HeadSeconds, HeadKick, HeadBack, HeadFront, HeadBounce;
        public float SplashSize, SplashSeconds, SplashFade;
        public int SplashMost;
        public float RingReach, SoundSpeed, RingGap, MeleePush, ForwardMost;

        /// <summary>The row for a zombie's target (its health is a zombie's) or for a thick plate.</summary>
        public static Shape Of(bool zombie)
        {
            GameTables.TryGetRow(TableName, zombie ? "Zombie" : "Plate", out var row);
            return new Shape
            {
                TorsoWidth = N(row, "torsoWidth", zombie ? .46f : .6f), TorsoHeight = N(row, "torsoHeight", zombie ? .62f : .9f),
                TorsoBottom = N(row, "torsoBottom", zombie ? .85f : 1f), ShoulderCut = N(row, "shoulderCut", zombie ? .1f : .03f),
                FootCut = N(row, "footCut", .04f), Thickness = Mathf.Max(.004f, N(row, "thickness", zombie ? .012f : .02f)),
                HeadRadius = N(row, "headRadius", zombie ? .115f : .15f), HeadDrop = N(row, "headDrop", zombie ? .17f : .21f),
                PostTop = N(row, "postTop", zombie ? 1.9f : 2.42f), PostBack = Mathf.Max(.08f, N(row, "postBack", zombie ? .24f : .26f)),
                Paint = C(row, "paint", new Color(.93f, .92f, .88f)), HeadPaint = C(row, "headPaint", KitStyle.Accent),
                Steel = C(row, "steel", new Color(.16f, .17f, .18f)),
                RockHz = N(row, "rockHz", 1.6f), RockSeconds = N(row, "rockSeconds", .45f), RockKick = N(row, "rockKick", zombie ? 9f : 4f),
                RockMost = N(row, "rockMost", zombie ? 22f : 12f), RiseKick = N(row, "riseKick", 70f),
                HeadHz = N(row, "headHz", 1.15f), HeadSeconds = N(row, "headSeconds", 1.3f), HeadKick = N(row, "headKick", zombie ? 75f : 45f),
                HeadBack = N(row, "headBack", zombie ? 50f : 40f), HeadFront = N(row, "headFront", 35f), HeadBounce = N(row, "headBounce", .35f),
                SplashSize = N(row, "splashSize", .1f), SplashSeconds = N(row, "splashSeconds", 7f), SplashFade = N(row, "splashFade", 1.5f),
                SplashMost = Mathf.Clamp(Mathf.RoundToInt(N(row, "splashMost", 24f)), 1, 128),
                RingReach = N(row, "ringReach", 160f), SoundSpeed = N(row, "soundSpeed", 343f), RingGap = N(row, "ringGap", .03f),
                MeleePush = N(row, "meleePush", 4f), ForwardMost = N(row, "forwardMost", 70f),
            };
        }

        private static float N(IReadOnlyDictionary<string, string> row, string key, float fallback)
            => row != null && row.TryGetValue(key, out string text)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

        private static Color C(IReadOnlyDictionary<string, string> row, string key, Color fallback)
            => row != null && row.TryGetValue(key, out string text) && ColorUtility.TryParseHtmlString(text.Trim(), out Color color) ? color : fallback;
    }

    private sealed class Splash
    {
        public Transform Transform;
        public MeshRenderer Renderer;
        public float Age;
    }

    private sealed class Plate
    {
        public Transform Face;
        public Collider Collider;
        public bool Head;
        public readonly List<Splash> Splashes = new List<Splash>();
        public int Next;
    }

    // A sound on its way: it leaves the plate as it is hit and reaches the ear a moment later.
    private struct Ring
    {
        public string Key;
        public Vector3 Point;
        public float Due, Queued, Volume;
    }

    private static readonly Dictionary<(Color, float), Material> paints = new Dictionary<(Color, float), Material>();
    private static Texture2D splashTexture;
    private static SoundBank bank;
    private static AudioListener listener;
    private static MaterialPropertyBlock block;

    private readonly List<Plate> plates = new List<Plate>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Ring> rings = new List<Ring>();
    private Shape shape;
    private Transform tipper, headPivot;
    private Vector3 tipAxis = Vector3.right;
    private float fall, rock, rockSpeed, swing, swingSpeed;
    private bool down;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        paints.Clear();
        splashTexture = null;
        bank = null;
        listener = null;
        block = null;
    }

    /// <summary>
    /// Builds the target on this object, which stands where its base goes, facing the firing line along its +Z; a target
    /// tips about <paramref name="axis"/> (its own axes). Whatever was under it (an older hub's dummy) is cleared away.
    /// </summary>
    public void Build(bool zombie, Vector3 axis)
    {
        shape = Shape.Of(zombie);
        tipAxis = axis.sqrMagnitude > .0001f ? axis.normalized : Vector3.right;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var old = transform.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        if (!TryGetComponent(out SurfaceType surface)) surface = gameObject.AddComponent<SurfaceType>();
        surface.Surface = SurfaceId;

        Material steel = Paint(shape.Steel, .75f, .45f), paint = Paint(shape.Paint, 0f, .3f), headPaint = Paint(shape.HeadPaint, 0f, .3f);
        float back = shape.PostBack, t = shape.Thickness;

        // The base stays on the ground; the rest stands on its hinge at the post's foot.
        Box("받침", transform, new Vector3(0f, .02f, -back), new Vector3(shape.TorsoWidth + .12f, .04f, .5f), steel);
        Box("경첩", transform, new Vector3(0f, Hinge * .5f + .02f, -back), new Vector3(.18f, Hinge, .08f), steel);
        tipper = new GameObject("서는 부분").transform;
        tipper.SetParent(transform, false);
        tipper.localPosition = new Vector3(0f, Hinge, -back);

        float postHeight = shape.PostTop - Hinge;
        Box("기둥", tipper, new Vector3(0f, postHeight * .5f, 0f), new Vector3(PostSize, postHeight, PostSize), steel);
        // The torso plate's front is the target's face (z 0); its bracket reaches it from the post.
        float bracketY = shape.TorsoBottom + shape.TorsoHeight * .62f - Hinge, bracket = back - t - PostSize * .5f;
        Box("몸통 받침쇠", tipper, new Vector3(0f, bracketY, PostSize * .5f + bracket * .5f), new Vector3(.07f, .05f, bracket), steel);
        var torso = PlateObject("몸통 판", tipper, new Vector3(0f, shape.TorsoBottom - Hinge, back), Torso(), paint);
        plates.Add(new Plate { Face = torso.transform, Collider = torso.GetComponent<Collider>() });

        // The head plate hangs on an axle under the arm's front end, through the plates' middle.
        float axleY = shape.PostTop - ArmSize - Hinge, axleZ = back - t * .5f;
        Box("머리 팔", tipper, new Vector3(0f, shape.PostTop - ArmSize * .5f - Hinge, axleZ * .5f), new Vector3(ArmSize, ArmSize, axleZ + ArmSize * .5f), steel);
        headPivot = new GameObject("머리 축").transform;
        headPivot.SetParent(tipper, false);
        headPivot.localPosition = new Vector3(0f, axleY, axleZ);
        float strap = Mathf.Max(.02f, shape.HeadDrop - shape.HeadRadius);
        Box("머리 고리", headPivot, new Vector3(0f, -strap * .5f, 0f), new Vector3(.03f, strap + .01f, t * .6f), steel);
        var head = PlateObject("머리 판", headPivot, new Vector3(0f, -shape.HeadDrop, t * .5f), Disc(), headPaint);
        head.AddComponent<WeaponHitRegion>().Kind = WeaponHitRegion.Region.Head;
        plates.Add(new Plate { Face = head.transform, Collider = head.GetComponent<Collider>(), Head = true });
        Pose();
    }

    private void OnDestroy()
    {
        foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
    }

    // ---- Hits ----

    /// <summary>A bullet struck one of its parts (IBulletStruck, from any gun on this PC, its own or a friend's copy).</summary>
    public void Struck(Collider part, Vector3 point, Vector3 normal, Vector3 incoming, float push)
    {
        var plate = PlateOf(part);
        // The stand sparks (its surface) and takes the round, but isn't the target: nothing rings or swings.
        if (plate == null || shape == null) return;
        Answer(plate, point, normal, incoming, push);
    }

    /// <summary>A hit that wasn't a bullet (the fists, a shove: RangeTarget passes them on): the nearest plate answers it.</summary>
    public void Struck(Vector3 point, Vector3 direction, bool shove)
    {
        if (shape == null || plates.Count == 0) return;
        if (point == Vector3.zero) point = plates[0].Face.position + Vector3.up * shape.TorsoHeight * .5f;
        Plate nearest = plates[0];
        float best = float.PositiveInfinity;
        foreach (var plate in plates)
        {
            if (plate.Collider == null) continue;
            float far = (plate.Collider.ClosestPoint(point) - point).sqrMagnitude;
            if (far < best) { best = far; nearest = plate; }
        }
        Vector3 along = direction.sqrMagnitude > .0001f ? direction.normalized : -transform.forward;
        Answer(nearest, point, -along, along, shape.MeleePush * (shove ? 1.5f : 1f));
    }

    private void Answer(Plate plate, Vector3 point, Vector3 normal, Vector3 incoming, float push)
    {
        push = Mathf.Max(0f, push);
        // Where the push sends its top: a turn about the tip axis carries the top along Cross(axis, up).
        Vector3 axis = transform.TransformDirection(tipAxis), forward = Vector3.Cross(axis, Vector3.up);
        float back = Vector3.Dot(forward, incoming) <= 0f ? 1f : -1f;
        if (plate.Head) swingSpeed += back * shape.HeadKick * push;
        if (!down) rockSpeed -= back * shape.RockKick * push * (plate.Head ? .35f : 1f);
        Stain(plate, point, normal);
        Sound(plate.Head ? HeadKey : BodyKey, point, Mathf.Clamp(push / 3f, .5f, 1.2f));
    }

    // ---- The knockdown (RangeTarget keeps the time) ----

    /// <summary>How far it lies over (degrees, signed: positive carries its top along Cross(tip axis, up)); 0 standing.</summary>
    public void Tip(float angle)
    {
        if (shape == null) return;
        if (!down && Mathf.Abs(angle) > .01f) { down = true; rock = rockSpeed = 0f; }
        fall = angle >= 0f ? Mathf.Min(angle, shape.ForwardMost) : angle;
    }

    /// <summary>It hit the ground on its back.</summary>
    public void Landed() => Sound(DownKey, tipper != null ? tipper.position + tipper.up * .5f : transform.position, 1f);

    /// <summary>Its spring stood it up: it overshoots and wobbles to rest, and the plate rings against its stop.</summary>
    public void Stood(float sign)
    {
        if (shape == null) return;
        down = false;
        fall = 0f;
        rockSpeed = -sign * shape.RiseKick;
        if (plates.Count > 0) Sound(BodyKey, plates[0].Face.position, StoodRing);
    }

    // ---- Moving ----

    private void LateUpdate()
    {
        if (shape == null) return;
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / .01f), 1, 8);
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Spring(ref swing, ref swingSpeed, shape.HeadHz, shape.HeadSeconds, step);
                Stop(ref swing, ref swingSpeed, -shape.HeadFront, shape.HeadBack, shape.HeadBounce);
                if (down) continue;
                Spring(ref rock, ref rockSpeed, shape.RockHz, shape.RockSeconds, step);
                Stop(ref rock, ref rockSpeed, -shape.RockMost, shape.RockMost, .3f);
            }
        }
        Pose();
        Fade(dt);
        Play();
    }

    private void Pose()
    {
        if (tipper != null) tipper.localRotation = Quaternion.AngleAxis(fall + rock, tipAxis);
        if (headPivot != null) headPivot.localRotation = Quaternion.AngleAxis(swing, Vector3.right);
    }

    // A damped spring back to 0: the head plate's hanging, the stand's spring under the hinge.
    private static void Spring(ref float angle, ref float speed, float hz, float seconds, float dt)
    {
        float w = 2f * Mathf.PI * Mathf.Max(.05f, hz);
        speed += (-w * w * angle - 2f / Mathf.Max(.02f, seconds) * speed) * dt;
        angle += speed * dt;
    }

    // At a stop it knocks back with part of its speed.
    private static void Stop(ref float angle, ref float speed, float least, float most, float bounce)
    {
        if (angle > most) { angle = most; if (speed > 0f) speed = -speed * bounce; }
        else if (angle < least) { angle = least; if (speed < 0f) speed = -speed * bounce; }
    }

    // ---- Lead splashes ----

    // A splash where the round flattened on the paint, on the face it hit, riding the plate.
    private void Stain(Plate plate, Vector3 point, Vector3 normal)
    {
        if (plate.Face == null || point == Vector3.zero) return;
        Vector3 local = plate.Face.InverseTransformPoint(point);
        bool front = plate.Face.InverseTransformDirection(normal).z >= -.01f;
        Splash splash;
        if (plate.Splashes.Count < shape.SplashMost)
        {
            var host = new GameObject("납 자국");
            host.transform.SetParent(plate.Face, false);
            host.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(SplashTexture, false);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            splash = new Splash { Transform = host.transform, Renderer = renderer };
            plate.Splashes.Add(splash);
        }
        else
        {
            splash = plate.Splashes[plate.Next % plate.Splashes.Count];
            plate.Next = (plate.Next + 1) % plate.Splashes.Count;
        }
        // A hair off the paint, a little more for each later one, so overlapping splashes don't flicker.
        int slot = plate.Splashes.IndexOf(splash);
        float lift = .0015f + slot * .00006f;
        local.z = front ? lift : -shape.Thickness - lift;
        splash.Transform.localPosition = local;
        splash.Transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        splash.Transform.localScale = Vector3.one * shape.SplashSize * Random.Range(.75f, 1.3f);
        splash.Age = 0f;
        splash.Transform.gameObject.SetActive(true);
        Tint(splash, 1f);
    }

    private void Fade(float dt)
    {
        foreach (var plate in plates)
            foreach (var splash in plate.Splashes)
            {
                if (!splash.Transform.gameObject.activeSelf) continue;
                splash.Age += dt;
                if (splash.Age <= shape.SplashSeconds) continue;
                float left = 1f - (splash.Age - shape.SplashSeconds) / Mathf.Max(.05f, shape.SplashFade);
                if (left <= 0f) { splash.Transform.gameObject.SetActive(false); continue; }
                Tint(splash, left);
            }
    }

    private static void Tint(Splash splash, float alpha)
    {
        block ??= new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
        splash.Renderer.SetPropertyBlock(block);
    }

    // Lead flattened on painted steel: a darker core with a lighter rim and thin rays thrown out along the face, a few
    // droplets past their ends. Grey, as lead is; the paint shows between the rays.
    private static Texture2D SplashTexture
    {
        get
        {
            if (splashTexture != null) return splashTexture;
            const int size = 128, rays = 16;
            var random = new System.Random(4207);
            float[] angle = new float[rays], reach = new float[rays], width = new float[rays];
            for (int i = 0; i < rays; i++)
            {
                angle[i] = (i + (float)random.NextDouble() * .7f) * Mathf.PI * 2f / rays;
                reach[i] = .45f + (float)random.NextDouble() * .5f;
                width[i] = .035f + (float)random.NextDouble() * .04f;
            }
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                float edge = .2f + .03f * Mathf.Sin(a * 5f + 1.1f) + .02f * Mathf.Sin(a * 9f + .3f);
                float core = Mathf.Clamp01((edge - r) * 40f), cover = core, grey = Mathf.Lerp(.6f, .33f, Mathf.Clamp01((edge - r) / edge * 1.6f));
                for (int i = 0; i < rays; i++)
                {
                    float off = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, angle[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                    float along = r / reach[i];
                    if (along < 1f && r > edge * .6f)
                    {
                        float ray = Mathf.Clamp01((width[i] * (1f - along * .85f) - off) * 90f) * (1f - along * along * .5f);
                        if (ray > cover) { cover = ray; grey = .55f; }
                    }
                    float dx = u - Mathf.Cos(angle[i]) * (reach[i] + .06f), dy = v - Mathf.Sin(angle[i]) * (reach[i] + .06f);
                    float drop = Mathf.Clamp01((.03f - Mathf.Sqrt(dx * dx + dy * dy)) * 70f);
                    if (drop > cover) { cover = drop; grey = .5f; }
                }
                byte g = (byte)Mathf.RoundToInt(grey * 255f);
                pixels[y * size + x] = new Color32(g, g, (byte)Mathf.Min(255, g + 6), (byte)Mathf.RoundToInt(cover * 235f));
            }
            splashTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
                { name = "Steel Lead Splash", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            splashTexture.SetPixels32(pixels);
            splashTexture.Apply(true, true);
            return splashTexture;
        }
    }

    // ---- The ring ----

    // Heard where the plate is, after the time sound takes to come back from it (a far plate's ting follows the shot,
    // as it does on a real range); a shotgun's pellets on one plate within ringGap ring once, louder.
    private void Sound(string key, Vector3 point, float volume)
    {
        float now = Time.time;
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var queued = rings[i];
            if (queued.Key != key || key == DownKey || now - queued.Queued > shape.RingGap) continue;
            queued.Volume = Mathf.Min(1.6f, queued.Volume + volume * .35f);
            rings[i] = queued;
            return;
        }
        float delay = shape.SoundSpeed > 1f ? Vector3.Distance(Ear(), point) / shape.SoundSpeed : 0f;
        rings.Add(new Ring { Key = key, Point = point, Queued = now, Due = now + delay, Volume = volume });
    }

    // The sounds whose time has come.
    private void Play()
    {
        if (rings.Count == 0) return;
        float now = Time.time;
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var ring = rings[i];
            if (ring.Due > now) continue;
            rings.RemoveAt(i);
            bank ??= SoundBank.Load(BankId);
            var clip = bank != null ? bank.Pick(ring.Key) : null;
            if (clip == null) continue;
            var tuning = bank.SettingsOf(ring.Key);
            FeelAudio.PlayClipAt(clip, ring.Point, tuning.Volume * ring.Volume, tuning.RandomPitch(), shape.RingReach);
        }
    }

    private static Vector3 Ear()
    {
        if (listener == null || !listener.isActiveAndEnabled) listener = FindAnyObjectByType<AudioListener>();
        if (listener != null) return listener.transform.position;
        return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
    }

    // ---- Building ----

    private Plate PlateOf(Collider part)
    {
        foreach (var plate in plates) if (plate.Collider == part) return plate;
        return null;
    }

    // A part of the stand: solid, steel, and a miss when hit (SteelStand).
    private static void Box(string name, Transform parent, Vector3 at, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
        part.AddComponent<SteelStand>();
    }

    // A plate: its outline drawn in its own X/Y, its front face at its local z 0, looking along +Z.
    private GameObject PlateObject(string name, Transform parent, Vector3 at, Mesh mesh, Material material)
    {
        var plate = new GameObject(name);
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = at;
        plate.AddComponent<MeshFilter>().sharedMesh = mesh;
        plate.AddComponent<MeshRenderer>().sharedMaterial = material;
        var collider = plate.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMesh = mesh;
        return plate;
    }

    // The torso's outline: its shoulders and feet cut off at the corners, a silhouette on a range.
    private Mesh Torso()
    {
        float w = shape.TorsoWidth * .5f, h = shape.TorsoHeight;
        float s = Mathf.Clamp(shape.ShoulderCut, 0f, w * .9f), f = Mathf.Clamp(shape.FootCut, 0f, w * .9f);
        var outline = new List<Vector2>
        {
            new Vector2(-w + f, 0f), new Vector2(w - f, 0f), new Vector2(w, f), new Vector2(w, h - s),
            new Vector2(w - s, h), new Vector2(-w + s, h), new Vector2(-w, h - s), new Vector2(-w, f)
        };
        return Extrude("Steel Torso", outline);
    }

    private Mesh Disc()
    {
        var outline = new List<Vector2>(DiscSides);
        for (int i = 0; i < DiscSides; i++)
        {
            float a = i * Mathf.PI * 2f / DiscSides;
            outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * shape.HeadRadius);
        }
        return Extrude("Steel Head", outline);
    }

    // A convex outline (counter-clockwise in X/Y) made into a plate: the front at z 0 facing +Z, the back at -thickness,
    // the edges between. Each face has its own corners, so it shades flat as cut steel does.
    private Mesh Extrude(string name, List<Vector2> outline)
    {
        float depth = shape.Thickness;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        int n = outline.Count;
        for (int side = 0; side < 2; side++)
        {
            int first = vertices.Count;
            Vector3 facing = side == 0 ? Vector3.forward : Vector3.back;
            foreach (var point in outline)
            {
                vertices.Add(new Vector3(point.x, point.y, side == 0 ? 0f : -depth));
                normals.Add(facing);
            }
            for (int i = 1; i < n - 1; i++) Triangle(vertices, triangles, first, first + i, first + i + 1, facing);
        }
        for (int i = 0; i < n; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % n];
            Vector3 outward = new Vector3(b.y - a.y, a.x - b.x, 0f).normalized;
            int first = vertices.Count;
            vertices.Add(new Vector3(a.x, a.y, 0f)); vertices.Add(new Vector3(b.x, b.y, 0f));
            vertices.Add(new Vector3(b.x, b.y, -depth)); vertices.Add(new Vector3(a.x, a.y, -depth));
            for (int k = 0; k < 4; k++) normals.Add(outward);
            Triangle(vertices, triangles, first, first + 1, first + 2, outward);
            Triangle(vertices, triangles, first, first + 2, first + 3, outward);
        }
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    // Wound so it faces `facing` (Unity draws the side whose corners run clockwise to the eye: Cross(b - a, c - a) faces it).
    private static void Triangle(List<Vector3> vertices, List<int> triangles, int a, int b, int c, Vector3 facing)
    {
        Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Vector3.Dot(face, facing) >= 0f) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        else { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
    }

    // Painted steel or the bare stand, one shared material per colour and finish.
    private static Material Paint(Color color, float metallic, float smoothness)
    {
        if (paints.TryGetValue((color, metallic), out var known) && known != null) return known;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = "Steel Target " + ColorUtility.ToHtmlStringRGB(color), hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        paints[(color, metallic)] = material;
        return material;
    }

    // ---- In the editor: where its plates go, before Play builds them ----

    /// <summary>The outline of a target's plates for the scene view (RangeTarget's gizmo), on <paramref name="root"/>.</summary>
    public static void DrawGizmo(Transform root, bool zombie)
    {
        var shape = Shape.Of(zombie);
        Gizmos.matrix = root.localToWorldMatrix;
        Gizmos.color = new Color(.93f, .92f, .88f, .9f);
        Gizmos.DrawWireCube(new Vector3(0f, shape.TorsoBottom + shape.TorsoHeight * .5f, -shape.Thickness * .5f),
            new Vector3(shape.TorsoWidth, shape.TorsoHeight, shape.Thickness));
        Gizmos.color = KitStyle.Accent;
        float headY = shape.PostTop - ArmSize - shape.HeadDrop;
        Gizmos.DrawWireCube(new Vector3(0f, headY, -shape.Thickness * .5f), new Vector3(shape.HeadRadius * 2f, shape.HeadRadius * 2f, shape.Thickness));
        Gizmos.color = new Color(.4f, .42f, .45f, .9f);
        Gizmos.DrawWireCube(new Vector3(0f, shape.PostTop * .5f, -shape.PostBack), new Vector3(PostSize, shape.PostTop, PostSize));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
