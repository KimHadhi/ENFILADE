using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The flame a shot throws out of the muzzle, made after CS2's first-person rifle flash measured frame by frame
/// (2026-11-06; the user picked it as mockup A). Four to six cloud puffs overlap along the barrel and stay bright for
/// three frames (50 ms) while they swell and drift forward, cooling from white-yellow through cream to apricot; about
/// half the shots leave a faint salmon afterglow a frame longer, and zero to two sparks fly out. Seen from further than
/// an arm's length (the F5 view, a teammate) it is drawn up to 1.6 times bigger with a thin wisp of smoke, as CS2's
/// third-person flash outgrows its first-person one. Every shot rolls its own shape.
/// That is the measured flame, and the prefab holds it (Prefabs/Resources/Combat/MuzzleFlame.prefab). What a given gun's
/// shot makes of it (a pistol's ball, a sniper's long tongue, a shotgun's wide burst, a suppressor's dim puff) is the
/// shot's look, a row of Tables/MuzzleFlash whose numbers are shares of this one (MuzzleFlashLooks): how many puffs, how
/// likely an afterglow and sparks are, how long, wide, hot and solid it is.
/// One mesh of quads drawn by Hidden/ENFILADE/MuzzleFlame; MuzzleFlash fires it, times it and carries it on the muzzle.
/// </summary>
[DisallowMultipleComponent]
public sealed class MuzzleFlame : MonoBehaviour
{
    private const string PrefabPath = "Combat/MuzzleFlame";
    private const int MostShots = 8, MostPuffs = 8, MostSparks = 2, MostQuads = 64;
    private const float Puff = 0f, Spark = 1f, Smoke = 2f, SparkLife = .09f;
    private const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds
        | MeshUpdateFlags.DontResetBoneBounds;
    private static readonly VertexAttributeDescriptor[] Layout =
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
    };
    private static readonly Vector2[] Corners = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };

    [SerializeField, Tooltip("Hidden/ENFILADE/MuzzleFlame.")] private Material material;

    [Header("Shape")]
    [SerializeField, Min(0f), Tooltip("Size of a look of size 1 (Tables/MuzzleFlash), as the shooter sees it.")]
    private float size = 1.35f;
    [SerializeField, Range(0f, .5f), Tooltip("Each shot is up to this share smaller or larger.")]
    private float sizeJitter = .21f;

    [Header("Timing (seconds of the shooter's time)")]
    [SerializeField, Min(.005f), Tooltip("How long the puffs stay bright while they swell and cool. CS2: three frames at 60 fps.")]
    private float brightTime = .05f;
    [SerializeField, Min(.005f), Tooltip("When a shot without an afterglow is gone.")]
    private float plainEnd = .052f;
    [SerializeField, Min(.005f), Tooltip("When the faint salmon afterglow is gone, in the shots that leave one.")]
    private float afterglowEnd = .068f;

    [Header("Seen from afar (F5 view, teammates)")]
    [SerializeField, Min(0f)] private float farScale = 1.6f;
    [SerializeField, Min(0f), Tooltip("Closer than this to the camera (m) the flame keeps its own size...")]
    private float nearDistance = .9f;
    [SerializeField, Min(0f), Tooltip("...and from this far (m) it is Far Scale times bigger, with smoke.")]
    private float farDistance = 1.8f;
    [SerializeField, Range(0f, 1f)] private float smokeAlpha = .08f;
    [SerializeField, Min(.05f)] private float smokeLife = .45f;

    private struct Shot
    {
        public uint seed;
        public float age, scale;
        public bool far, live;
        // What this shot's flame is like (MuzzleFlashLooks): its gun's row, or the suppressed one.
        public MuzzleFlashLook look;
    }

    private struct Element
    {
        public Vector3 center, axis;
        public float halfWidth, halfLength, turn, seed, heat, alpha, kind, boil, rough, occlusion, depth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 center;
        public Vector4 corner, axis, look, extra;
    }

    // The same shot rolls the same numbers every frame, so its shape only grows and cools.
    private struct Dice
    {
        private uint state;
        public Dice(uint seed) { state = seed; }
        public float Next()
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return ((t ^ (t >> 14)) >> 8) * (1f / 16777216f);
            }
        }
        public float Range(float from, float to) => from + (to - from) * Next();
    }

    private static bool prepared, warned;
    private readonly Shot[] shots = new Shot[MostShots];
    private readonly Element[] elements = new Element[MostQuads];
    private readonly int[] order = new int[MostQuads];
    private readonly Vertex[] vertices = new Vertex[MostQuads * 4];
    private Mesh mesh;
    private MeshRenderer view;
    private Camera sortCamera;
    private int nextShot;
    private uint fired;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => prepared = warned = false;

    /// <summary>A new flame from the prefab, or null (with one warning) when the prefab is missing.</summary>
    public static MuzzleFlame Create(string name)
    {
        var prefab = Resources.Load<MuzzleFlame>(PrefabPath);
        if (prefab == null) { Warn("Resources/" + PrefabPath + ".prefab is missing: shots show no muzzle flame."); return null; }
        var flame = Instantiate(prefab);
        flame.name = name;
        return flame;
    }

    /// <summary>
    /// Draws the flame's shader once, invisibly, in front of the player's camera as soon as there is one, so the first shot
    /// doesn't stop to prepare it. Once a run; MuzzleFlash calls it while the map loads.
    /// </summary>
    public static void Prepare()
    {
        if (prepared || !Application.isPlaying) return;
        prepared = true;
        var flame = Create("Muzzle Flame Warmup");
        if (flame != null) flame.StartCoroutine(flame.WarmUp());
    }

    private IEnumerator WarmUp()
    {
        for (float waited = 0f; Camera.main == null; waited += Time.unscaledDeltaTime)
        {
            if (waited > 30f) { Destroy(gameObject); yield break; }
            yield return null;
        }
        Transform eye = Camera.main.transform;
        transform.SetPositionAndRotation(eye.position + eye.forward * .6f, eye.rotation);
        // One puff that draws nothing: the same program a real shot uses.
        elements[0] = new Element { halfWidth = .05f, rough = .6f, occlusion = .5f };
        for (int frame = 0; frame < 2; frame++)
        {
            Upload(1);
            yield return null;
        }
        Destroy(gameObject);
    }

    private void Awake() => Build();

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    /// <summary>One shot. look: the flame it throws (MuzzleFlashLooks); viewDistance: how far the muzzle is from the camera (m).</summary>
    public void Fire(in MuzzleFlashLook look, float viewDistance)
    {
        Build();
        float far = Mathf.InverseLerp(nearDistance, Mathf.Max(nearDistance + .01f, farDistance), viewDistance);
        ref Shot shot = ref shots[nextShot];
        nextShot = (nextShot + 1) % MostShots;
        unchecked { shot.seed = (++fired * 2654435761u) ^ (uint)GetInstanceID() * 2246822519u; }
        shot.age = 0f;
        shot.scale = look.Size * Mathf.Lerp(1f, farScale, far);
        shot.far = far >= .5f;
        shot.look = look;
        shot.live = true;
    }

    /// <summary>Draws every live shot where it is now, then moves them on by dt (the shooter's time, so slow motion slows it).</summary>
    public void Advance(float dt)
    {
        if (mesh == null) return;
        int count = 0;
        for (int i = 0; i < MostShots; i++)
        {
            ref Shot shot = ref shots[i];
            if (!shot.live) continue;
            if (shot.age >= Lifetime(shot)) { shot.live = false; continue; }
            count = Add(ref shot, count);
            shot.age += Mathf.Max(0f, dt);
        }
        Upload(count);
    }

    /// <summary>Puts every flame out at once (the gun was put away or the shooter left).</summary>
    public void Clear()
    {
        for (int i = 0; i < MostShots; i++) shots[i].live = false;
        if (view != null) view.enabled = false;
    }

    private float Lifetime(in Shot shot)
        => Mathf.Max(Mathf.Max(plainEnd, afterglowEnd) * shot.look.Bright, Mathf.Max(SparkLife, Smokes(shot) ? smokeLife : 0f));

    // Its wisp of smoke shows: from further off, and at the shooter's own eye for a look that says so (a suppressor's puff).
    private bool Smokes(in Shot shot) => (shot.far || shot.look.NearSmoke) && smokeAlpha * shot.look.Smoke > 0f;

    // Mockup A, shot by shot: everything is rolled in the same order every frame, and only then is what is over left out.
    private int Add(ref Shot shot, int count)
    {
        var dice = new Dice(shot.seed);
        var look = shot.look;
        float scale = shot.scale, age = shot.age;
        float burst = scale * size * dice.Range(1f - sizeJitter, 1f + sizeJitter);
        bool afterglow = dice.Next() < look.Afterglow;
        // The look's own pace: a sniper's flame hangs a little, a suppressor's puff is gone sooner.
        float bright = brightTime * look.Bright;
        float end = (afterglow ? afterglowEnd : plainEnd) * look.Bright, swell = Mathf.Clamp01(age / bright);
        int least = Mathf.Min(look.FewestPuffs, MostPuffs), most = Mathf.Clamp(look.MostPuffs, least, MostPuffs);
        int puffs = Mathf.Min(most, least + (int)(dice.Next() * (most - least + 1)));
        for (int i = 0; i < MostPuffs; i++)
        {
            float along = i / (float)Mathf.Max(1, puffs - 1), clamped = Mathf.Min(along, 1f);
            float start = (.02f + .15f * Mathf.Pow(clamped, .9f)) * burst * look.Length * dice.Range(.85f, 1.15f);
            float drift = (.7f + 1.3f * along) * scale * size * look.Length;
            float radius = (.036f + .03f * Mathf.Sin(Mathf.PI * (clamped * .8f + .2f))) * burst * look.Width * dice.Range(.8f, 1.2f);
            float x = dice.Range(-1f, 1f) * .013f * burst * look.Width, y = dice.Range(-1f, 1f) * .013f * burst * look.Width;
            float heat0 = (.96f - .26f * along) * look.Heat * dice.Range(.9f, 1.08f), seed = dice.Next() * 97f, turn = dice.Next() * 6.283f;
            if (i >= puffs || age >= end) continue;
            float heat = heat0 * (1f - .45f * swell), alpha = look.Alpha;
            if (age > bright)
            {
                float fading = (age - bright) / Mathf.Max(.001f, end - bright);
                heat = heat0 * .4f;
                alpha = .42f * look.Alpha * (1f - fading);
            }
            count = Push(count, new Element
            {
                center = new Vector3(x, y, start + drift * age), halfWidth = radius * (.9f + .42f * swell), turn = turn, seed = seed,
                heat = heat, alpha = alpha, kind = Puff, boil = age * 9f, rough = .62f, occlusion = .5f
            });
        }
        // The white-hot heart right at the muzzle, the first two frames.
        float coreSeed = dice.Next() * 97f, coreTime = bright * .68f;
        if (age < coreTime)
            count = Push(count, new Element
            {
                center = new Vector3(0f, 0f, .012f * burst), halfWidth = .024f * burst * look.Width, seed = coreSeed, heat = 1.25f * look.Heat,
                alpha = (1f - age / coreTime * .5f) * look.Alpha, kind = Puff, rough = .3f, occlusion = .3f
            });
        int sparks = dice.Next() < look.Sparks ? (dice.Next() < .6f ? 1 : 2) : 0;
        for (int i = 0; i < MostSparks; i++)
        {
            Vector3 heading = Cone(ref dice, .35f);
            float speed = dice.Range(9f, 15f) * Mathf.Sqrt(scale), life = dice.Range(.04f, SparkLife), seed = dice.Next() * 97f;
            if (i >= sparks || age >= life) continue;
            count = Push(count, new Element
            {
                center = new Vector3(0f, 0f, .015f * scale) + heading * (speed * age), axis = heading,
                halfWidth = .0022f * scale, halfLength = Mathf.Max(.008f, speed * .012f) * .5f, seed = seed,
                heat = 1.05f * (1f - .35f * age / life), alpha = 1f - age / life, kind = Spark
            });
        }
        float smokeSeed = dice.Next() * 97f;
        if (Smokes(shot) && age < smokeLife)
            count = Push(count, new Element
            {
                center = new Vector3(0f, .12f * age, .06f * scale + .28f * age), halfWidth = (.045f + .3f * age) * scale, seed = smokeSeed,
                alpha = Mathf.Clamp01(smokeAlpha * look.Smoke) * (1f - age / smokeLife), kind = Smoke, boil = age * 2f
            });
        return count;
    }

    private int Push(int count, in Element element)
    {
        if (count >= MostQuads) return count;
        elements[count] = element;
        return count + 1;
    }

    // A direction within spread (radians) of straight out of the barrel.
    private static Vector3 Cone(ref Dice dice, float spread)
    {
        float around = dice.Next() * Mathf.PI * 2f, off = Mathf.Sqrt(dice.Next()) * spread, sin = Mathf.Sin(off);
        return new Vector3(sin * Mathf.Cos(around), sin * Mathf.Sin(around), Mathf.Cos(off));
    }

    private void Build()
    {
        if (mesh != null) return;
        mesh = new Mesh { name = "Muzzle Flame", hideFlags = HideFlags.DontSave };
        mesh.MarkDynamic();
        mesh.SetVertexBufferParams(MostQuads * 4, Layout);
        var indices = new ushort[MostQuads * 6];
        for (int q = 0; q < MostQuads; q++)
        {
            int v = q * 4, i = q * 6;
            indices[i] = (ushort)v; indices[i + 1] = (ushort)(v + 1); indices[i + 2] = (ushort)(v + 2);
            indices[i + 3] = (ushort)v; indices[i + 4] = (ushort)(v + 2); indices[i + 5] = (ushort)(v + 3);
        }
        mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
        mesh.SetIndexBufferData(indices, 0, 0, indices.Length, Quiet);
        mesh.subMeshCount = 1;
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, 6), Quiet);
        // The quads open up in the shader, so the bounds hold the biggest flame a look may ask for: far scale, twice the
        // measured flame's size and length, the longest drift.
        mesh.bounds = new Bounds(new Vector3(0f, 0f, 1.2f), new Vector3(3f, 3f, 4.4f));
        var filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        view = GetComponent<MeshRenderer>();
        if (view == null) view = gameObject.AddComponent<MeshRenderer>();
        if (material != null) view.sharedMaterial = material;
        view.shadowCastingMode = ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = LightProbeUsage.Off;
        view.reflectionProbeUsage = ReflectionProbeUsage.Off;
        view.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        view.allowOcclusionWhenDynamic = false;
        view.enabled = false;
        if (material == null) Warn("MuzzleFlame.prefab has no material: shots show no muzzle flame.");
    }

    // A setup problem, said once a run.
    private static void Warn(string message)
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning("[MuzzleFlame] " + message);
    }

    // Far to near from the camera, so each puff dims only what lies behind it; then one quad per element.
    private void Upload(int count)
    {
        if (mesh == null || view == null) return;
        if (count <= 0) { view.enabled = false; return; }
        if (sortCamera == null || !sortCamera.isActiveAndEnabled) sortCamera = Camera.main;
        if (sortCamera != null)
        {
            Vector3 eye = sortCamera.transform.position, forward = sortCamera.transform.forward;
            for (int i = 0; i < count; i++) elements[i].depth = Vector3.Dot(transform.TransformPoint(elements[i].center) - eye, forward);
        }
        for (int i = 0; i < count; i++)
        {
            int index = i;
            while (index > 0 && elements[order[index - 1]].depth < elements[i].depth) { order[index] = order[index - 1]; index--; }
            order[index] = i;
        }
        for (int i = 0; i < count; i++)
        {
            ref Element e = ref elements[order[i]];
            var axis = new Vector4(e.axis.x, e.axis.y, e.axis.z, e.turn);
            var look = new Vector4(e.seed, e.heat, e.alpha, e.kind);
            var extra = new Vector4(e.boil, e.rough, e.occlusion, 0f);
            for (int c = 0; c < 4; c++)
                vertices[i * 4 + c] = new Vertex
                {
                    center = e.center, corner = new Vector4(Corners[c].x, Corners[c].y, e.halfWidth, e.halfLength),
                    axis = axis, look = look, extra = extra
                };
        }
        mesh.SetVertexBufferData(vertices, 0, 0, count * 4, 0, Quiet);
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, count * 6), Quiet);
        view.enabled = true;
    }
}
