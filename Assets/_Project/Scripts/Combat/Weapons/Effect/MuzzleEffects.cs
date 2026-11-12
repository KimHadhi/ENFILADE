using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bullet's tracer for the parent WeaponController: a hot streak that flies from the muzzle to the hit point, and on
/// along each stretch the bullet went on through or off something. Lives on the muzzle point, so it follows the gun model
/// and hides with it. The flame is MuzzleFlash's; the particle flash this used to play (the MuzzleFlash object under the
/// muzzle in Player.prefab) no longer plays.
/// The user reads their grouping off the tracers and wanted them handsomer (2026-11-11 playtest: "총알 날라가는 거 보고
/// 탄착군 잡긴 함 ... 멋스럽게 다듬어야 할 듯"). The old one was a flat yellow line that slid along the whole path in a fixed
/// 0.12 s, so a near shot crawled and a far one was a 35 m bar. Now every tracer flies at one speed with one streak
/// length (Tracer.csv), a white-hot core with a hot head inside an orange glow that bloom spreads (Hidden/ENFILADE/
/// BulletTracer), tapering to nothing at its tail, and it never thins below minAngle or minPixels on screen, so a far
/// one still reads. Reaching the hit it shrinks into it and is gone: nothing lingers. A shotgun's pellets fly shorter
/// and dimmer. Friends' shots draw theirs too (WeaponController.ShowShot raises ShotFired), the same way frame by frame.
/// Without the shader the old line plays. Pooled: no tracer is made or destroyed while firing.
/// Every frame's streak reaches back to where the last frame's head was, and that stretch keeps a least brightness
/// (swept), so the frames join into one line however far a frame carries the bullet. A frame used to show only the
/// streak's own length behind the head: at 60 fps a 600 m/s bullet flies 10 m a frame against a 5.5 m streak, the first
/// frame showed 0.4 m at the muzzle and the next began 4.9 m out, and since the first metres after the muzzle cross most
/// of the screen between the gun and the crosshair, over half of a hip shot's path on screen was never drawn (a third
/// at 90 fps). An open gun's flame hid the gap; a suppressor throws none (user, 2026-11-12: "소음기 총알 궤적은 너무
/// 중간에 끊기는 것처럼 보임").
/// </summary>
[DisallowMultipleComponent]
public sealed class MuzzleEffects : MonoBehaviour, ITableBound
{
    public const string TableName = "Tracer", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/BulletTracer";
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor"), GlowColorId = Shader.PropertyToID("_GlowColor"),
        CoreId = Shader.PropertyToID("_Core"), TailId = Shader.PropertyToID("_Tail"), HeadId = Shader.PropertyToID("_Head"),
        CorePixelsId = Shader.PropertyToID("_CorePixels"), SweptId = Shader.PropertyToID("_Swept"), CoverId = Shader.PropertyToID("_Cover"),
        CometId = Shader.PropertyToID("_Comet"), StepId = Shader.PropertyToID("_Step");

    [Header("Bullet Tracer (old line, when the tracer shader is missing)")]
    [SerializeField] private LineRenderer tracerPrefab;
    [SerializeField, Min(0.01f)] private float tracerDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float tracerTailLength = 0.35f;

    [Header("Tracer (Tracer.csv)")]
    [SerializeField, Min(1f), Tooltip("How fast a tracer flies (m/s).")]
    private float speed = 600f;
    [SerializeField, Min(.1f), Tooltip("The streak's length (m).")]
    private float length = 7f;
    [SerializeField, Min(0f), Tooltip("It appears this far out of the muzzle (m), so the first frame already shows a streak.")]
    private float startAhead = .4f;
    [SerializeField, Min(.001f), Tooltip("Width of the glow (m); the core is `core` of it.")]
    private float width = .05f;
    [SerializeField, Min(0f), Tooltip("Never thinner on screen than this angle (radians), so a far tracer still reads.")]
    private float minAngle = .005f;
    [SerializeField, Min(0f), Tooltip("Nor thinner than this many pixels, at any screen size.")]
    private float minPixels = 5f;
    [SerializeField, Min(0f), Tooltip("Never wider on screen than this angle (radians) near the eye: a line leaving the muzzle, "
        + "not a blob. 0: no limit.")]
    private float maxAngle = .016f;
    [SerializeField, Tooltip("The core's hue; coreBrightness makes it white-hot.")]
    private Color coreColor = new Color(1f, .8f, .53f);
    [SerializeField, Min(0f), Tooltip("The core's brightness: above 1 it blooms.")]
    private float coreBrightness = 8f;
    [SerializeField, Tooltip("The glow's hue round the core.")]
    private Color glowColor = new Color(1f, .4f, .1f);
    [SerializeField, Min(0f), Tooltip("The glow's brightness.")]
    private float glowBrightness = 3f;
    [SerializeField, Range(.02f, 1f), Tooltip("The core's share of the width.")]
    private float core = .24f;
    [SerializeField, Min(0f), Tooltip("The core is never thinner on screen than this many pixels.")]
    private float corePixels = 1.6f;
    [SerializeField, Min(.1f), Tooltip("How fast it fades toward its tail (higher: a shorter bright part).")]
    private float tail = 1.6f;
    [SerializeField, Range(0f, 1f), Tooltip("The hot head's share of the streak.")]
    private float head = .16f;
    [SerializeField, Range(0f, 1f), Tooltip("The way the bullet flew since the last frame is never darker than this share of the "
        + "head, so the frames join into one line.")]
    private float swept = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of what is behind the bright part it covers: over a bright sky the hot orange "
        + "still shows. Over the dark it changes nothing.")]
    private float cover = .6f;
    [SerializeField, Range(0f, 1f), Tooltip("A shotgun pellet's tracer: this share of the brightness.")]
    private float pelletBrightness = .55f;
    [SerializeField, Range(.1f, 1f), Tooltip("A shotgun pellet's tracer: this share of the length.")]
    private float pelletLength = .6f;
    [SerializeField, Min(1), Tooltip("Tracers in flight at once from one gun; the oldest gives way.")]
    private int mostAtOnce = 48;

    private WeaponController weapon;
    private Material material;
    private MaterialPropertyBlock perTracer;
    private bool fallback;

    private sealed class Flying
    {
        public LineRenderer line;
        public Vector3 from, way;
        public float distance, flown, length, brightness;
        // Its gun's own look (a Tracer.csv row named after the gun, else Default's): the glow's width, core and glow colours.
        public float width;
        public Vector4 core, glow;
        // Where its head is drawn now and where it was drawn the frame before (m along the way), and the frame it was
        // launched in.
        public float head, before;
        public int launched;
        // The old line's numbers, when it plays instead.
        public Vector3 end;
        public float elapsed;
        public Color startColor, endColor;
    }

    private readonly List<Flying> flying = new();
    private readonly Stack<LineRenderer> spare = new();

    public void OnTableApplied()
    {
        looks.Clear();
        Paint();
    }

    // A gun with a row of its own in Tracer.csv (the row's id is the gun's id, as Desperado's) streaks in that row's length,
    // width, colours and brightness; a blank cell keeps Default's. Read once per gun, again when the table is reloaded.
    private sealed class GunLook
    {
        public float length, width;
        public Vector4 core, glow;
    }

    private readonly Dictionary<WeaponData, GunLook> looks = new();

    private GunLook LookOf(WeaponData gun)
    {
        if (gun == null) return null;
        if (looks.TryGetValue(gun, out var look)) return look;
        look = null;
        if (GameTables.TryGetRow(TableName, gun.name, out var row))
            look = new GunLook
            {
                length = Number(row, "length", length), width = Number(row, "width", width),
                core = Hue(row, "coreColor", coreColor).linear * Number(row, "coreBrightness", coreBrightness),
                glow = Hue(row, "glowColor", glowColor).linear * Number(row, "glowBrightness", glowBrightness),
            };
        looks[gun] = look;
        return look;
    }

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : fallback;

    private static Color Hue(IReadOnlyDictionary<string, string> row, string key, Color fallback)
        => row.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text) && ColorUtility.TryParseHtmlString(text.Trim(), out var value)
            ? value : fallback;

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += Play;
        if (material == null && !fallback)
        {
            GameTables.Bind(TableName, TableRow, this);
            var shader = Resources.Load<Shader>(ShaderPath);
            if (shader != null) material = new Material(shader) { name = "Bullet Tracer (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            else fallback = true;
            Paint();
        }
    }

    private void Paint()
    {
        if (material == null) return;
        // A table colour is plain RGB; the brightness takes it past white for bloom. Sent as linear values: the project
        // renders in linear space, and SetColor would raise an HDR colour to the power 2.2 (the 2026-11-11 review: the
        // core came out about 8 times hotter and the orange glow deep red).
        material.SetVector(CoreColorId, coreColor.linear * coreBrightness);
        material.SetVector(GlowColorId, glowColor.linear * glowBrightness);
        material.SetFloat(CoreId, core);
        material.SetFloat(CorePixelsId, corePixels);
        material.SetFloat(TailId, tail);
        material.SetFloat(HeadId, head);
        material.SetFloat(SweptId, swept);
        material.SetFloat(CoverId, cover);
    }

    private void Play(WeaponShot shot)
    {
        // A gun whose shots wear a bought pack's look flies the pack's round instead of this tracer, not both
        // (PackShots: the Desperado's Realistic Gunslinger round drew over its own tracer, 2026-11-13).
        if (weapon != null && PackShots.Covers(weapon.EquippedData)) return;
        bool pellet = weapon != null && weapon.EquippedData != null && weapon.EquippedData.Pellets > 1;
        Vector3 first = EndOf(shot.Trace);
        Launch(shot.Origin, first, pellet, 0f);
        // A bullet that went on through or off something (AttackTrace.Bullet) streaks on from there, once it gets there:
        // each stretch waits for the path flown before it.
        if (shot.Later == null) return;
        float before = Vector3.Distance(shot.Origin, first);
        foreach (var leg in shot.Later)
        {
            Vector3 end = EndOf(leg.Trace);
            Launch(leg.Onward.From, end, pellet, before);
            before += Vector3.Distance(leg.Onward.From, end);
        }
    }

    private static Vector3 EndOf(in AttackTraceResult trace)
        => trace.HasHit && trace.Hit.collider != null ? trace.Hit.point : trace.End;

    // delay: how far the bullet flies before this stretch starts (m); the stretch stays hidden until then.
    private void Launch(Vector3 from, Vector3 to, bool pellet, float delay)
    {
        Vector3 path = to - from;
        float distance = path.magnitude;
        if (distance < .01f) return;
        if (fallback) { Old(from, to); return; }
        if (material == null) return;
        if (flying.Count >= Mathf.Max(1, mostAtOnce)) Land(0);
        var line = Take();
        var own = LookOf(weapon != null ? weapon.EquippedData : null);
        var tracer = new Flying
        {
            line = line, from = from, way = path / distance, distance = distance,
            flown = delay > 0f ? -delay : Mathf.Min(startAhead, distance),
            length = (own != null ? own.length : length) * (pellet ? pelletLength : 1f),
            brightness = pellet ? pelletBrightness : 1f, launched = Time.frameCount,
            width = own != null ? own.width : width,
            core = own != null ? own.core : (Vector4)(coreColor.linear * coreBrightness),
            glow = own != null ? own.glow : (Vector4)(glowColor.linear * glowBrightness),
        };
        flying.Add(tracer);
        Place(tracer, Camera.main);
    }

    private void Update()
    {
        if (flying.Count == 0) return;
        var view = Camera.main;
        float dt = Time.deltaTime;
        int frame = Time.frameCount;
        for (int i = flying.Count - 1; i >= 0; i--)
        {
            var tracer = flying[i];
            if (tracer.line == null) { flying.RemoveAt(i); continue; }
            if (fallback) { if (OldTick(tracer, dt)) Land(i); continue; }
            // Launch placed it this frame, where it leaves the muzzle. A friend's shot arrives before this Update (the
            // network runs first) and was moved on a whole frame before it was ever seen, starting 4.9 m out at 60 fps;
            // one's own is fired after it (WeaponController, 300). Both now show the muzzle first.
            if (tracer.launched == frame) continue;
            tracer.before = tracer.head;
            tracer.flown += speed * dt;
            // Gone once its tail has reached the hit too: it shrinks into it. The tail never passes where the head was
            // last frame, so the frame that brings the head to the hit is always drawn; the tail alone used to pass the
            // hit at once on a near shot (under 5 m at 60 fps), and the streak never got there.
            if (TailAt(tracer) >= tracer.distance) { Land(i); continue; }
            Place(tracer, view);
        }
    }

    // How far along the way its tail is: a length behind the head, but never past where the head was last frame.
    private static float TailAt(Flying tracer) => Mathf.Min(tracer.flown - tracer.length, tracer.before);

    // The streak where it is now: its head ahead along the path, its tail a length behind or back to where the head was
    // last frame, both kept on the path.
    private void Place(Flying tracer, Camera view)
    {
        var line = tracer.line;
        tracer.head = Mathf.Clamp(tracer.flown, 0f, tracer.distance);
        // A later stretch the bullet hasn't reached yet.
        bool started = tracer.flown > 0f;
        if (line.enabled != started) line.enabled = started;
        if (!started) return;
        float headAt = tracer.head;
        float tailAt = Mathf.Clamp(TailAt(tracer), 0f, headAt);
        Vector3 tailPoint = tracer.from + tracer.way * tailAt, headPoint = tracer.from + tracer.way * headAt;
        line.SetPosition(0, tailPoint);
        line.SetPosition(1, headPoint);
        float pixel = PixelAngle(view);
        line.startWidth = Wide(tailPoint, view, pixel, tracer.width);
        line.endWidth = Wide(headPoint, view, pixel, tracer.width);
        // The comet keeps its own length at the front, and what the frame flew beyond it its least brightness
        // (BulletTracer _Comet, _Step).
        float drawn = headAt - tailAt;
        perTracer ??= new MaterialPropertyBlock();
        perTracer.SetFloat(CometId, drawn > tracer.length ? tracer.length / drawn : 1f);
        perTracer.SetVector(CoreColorId, tracer.core);
        perTracer.SetVector(GlowColorId, tracer.glow);
        perTracer.SetFloat(StepId, drawn > 0f ? Mathf.Clamp01((headAt - tracer.before) / drawn) : 0f);
        line.SetPropertyBlock(perTracer);
        // Shrinking into the hit after the head got there, it dims with what is left of it.
        float left = tracer.length > 0f ? Mathf.Clamp01(drawn / tracer.length) : 1f;
        bool arrived = tracer.before >= tracer.distance;
        var tint = new Color(1f, 1f, 1f, tracer.brightness * (arrived ? left : 1f));
        line.startColor = line.endColor = tint;
    }

    // How wide one pixel is in the middle of the screen (radians).
    private static float PixelAngle(Camera view)
        => view != null ? 2f * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Max(1, view.pixelHeight) : 0f;

    // The glow's width at a point (m): `width` across, no wider on screen than maxAngle near the eye, and no thinner than
    // minAngle or minPixels however far. The line blends its two ends' widths, and the distance from the eye along a
    // straight line never rises above the blend of its ends' distances, so the floor holds all along. The cap holds along
    // a line heading away from the eye, as one's own shots do; one crossing right past it is wider in the middle.
    private float Wide(Vector3 point, Camera view, float pixel, float width)
    {
        if (view == null) return width;
        float distance = Vector3.Distance(view.transform.position, point);
        float near = maxAngle > 0f ? Mathf.Min(width, distance * maxAngle) : width;
        return Mathf.Max(near, distance * Mathf.Max(minAngle, minPixels * pixel));
    }

    private LineRenderer Take()
    {
        while (spare.Count > 0)
        {
            var reused = spare.Pop();
            if (reused == null) continue;
            reused.gameObject.SetActive(true);
            return reused;
        }
        var line = new GameObject("Bullet Tracer", typeof(LineRenderer)).GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 0;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        return line;
    }

    private void Land(int at)
    {
        var tracer = flying[at];
        flying.RemoveAt(at);
        if (tracer.line == null) return;
        if (fallback) { Destroy(tracer.line.gameObject); return; }
        tracer.line.gameObject.SetActive(false);
        spare.Push(tracer.line);
    }

    // ---- The old line, when the tracer shader can't be found ----

    private void Old(Vector3 from, Vector3 to)
    {
        if (tracerPrefab == null) return;
        var line = Instantiate(tracerPrefab);
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        flying.Add(new Flying { line = line, from = from, end = to, startColor = line.startColor, endColor = line.endColor });
    }

    // True once it has run its course.
    private bool OldTick(Flying tracer, float dt)
    {
        tracer.elapsed += dt;
        float progress = Mathf.Clamp01(tracer.elapsed / Mathf.Max(.01f, tracerDuration));
        tracer.line.SetPosition(0, Vector3.Lerp(tracer.from, tracer.end, progress));
        tracer.line.SetPosition(1, Vector3.Lerp(tracer.from, tracer.end, Mathf.Min(1f, progress + tracerTailLength)));
        Color start = tracer.startColor, end = tracer.endColor;
        start.a *= 1f - progress;
        end.a *= 1f - progress;
        tracer.line.startColor = start;
        tracer.line.endColor = end;
        return progress >= 1f;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= Play;
        Clear();
    }

    private void OnDestroy()
    {
        Clear();
        while (spare.Count > 0)
        {
            var line = spare.Pop();
            if (line != null) Destroy(line.gameObject);
        }
        if (material != null) Destroy(material);
    }

    private void Clear()
    {
        for (int i = flying.Count - 1; i >= 0; i--) Land(i);
    }
}
