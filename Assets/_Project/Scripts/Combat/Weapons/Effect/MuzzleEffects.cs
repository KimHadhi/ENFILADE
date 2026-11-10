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
/// BulletTracer), tapering to nothing at its tail, and it never thins below minAngle on screen, so a far one still
/// reads. Reaching the hit it shrinks into it and is gone: nothing lingers. A shotgun's pellets fly shorter and dimmer.
/// Friends' shots draw theirs too (WeaponController.ShowShot raises ShotFired). Without the shader the old line plays.
/// Pooled: no tracer is made or destroyed while firing.
/// </summary>
[DisallowMultipleComponent]
public sealed class MuzzleEffects : MonoBehaviour, ITableBound
{
    public const string TableName = "Tracer", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/BulletTracer";
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor"), GlowColorId = Shader.PropertyToID("_GlowColor"),
        CoreId = Shader.PropertyToID("_Core"), TailId = Shader.PropertyToID("_Tail"), HeadId = Shader.PropertyToID("_Head");

    [Header("Bullet Tracer (old line, when the tracer shader is missing)")]
    [SerializeField] private LineRenderer tracerPrefab;
    [SerializeField, Min(0.01f)] private float tracerDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float tracerTailLength = 0.35f;

    [Header("Tracer (Tracer.csv)")]
    [SerializeField, Min(1f), Tooltip("How fast a tracer flies (m/s).")]
    private float speed = 600f;
    [SerializeField, Min(.1f), Tooltip("The streak's length (m).")]
    private float length = 5.5f;
    [SerializeField, Min(0f), Tooltip("It appears this far out of the muzzle (m), so the first frame already shows a streak.")]
    private float startAhead = .4f;
    [SerializeField, Min(.001f), Tooltip("Width of the glow (m); the core is `core` of it.")]
    private float width = .05f;
    [SerializeField, Min(0f), Tooltip("Never thinner on screen than this angle (radians), so a far tracer still reads.")]
    private float minAngle = .004f;
    [SerializeField, Tooltip("The core's hue; coreBrightness makes it white-hot.")]
    private Color coreColor = new Color(1f, .8f, .53f);
    [SerializeField, Min(0f), Tooltip("The core's brightness: above 1 it blooms.")]
    private float coreBrightness = 6f;
    [SerializeField, Tooltip("The glow's hue round the core.")]
    private Color glowColor = new Color(1f, .4f, .1f);
    [SerializeField, Min(0f), Tooltip("The glow's brightness.")]
    private float glowBrightness = 2.4f;
    [SerializeField, Range(.02f, 1f), Tooltip("The core's share of the width.")]
    private float core = .24f;
    [SerializeField, Min(.1f), Tooltip("How fast it fades toward its tail (higher: a shorter bright part).")]
    private float tail = 1.6f;
    [SerializeField, Range(0f, 1f), Tooltip("The hot head's share of the streak.")]
    private float head = .16f;
    [SerializeField, Range(0f, 1f), Tooltip("A shotgun pellet's tracer: this share of the brightness.")]
    private float pelletBrightness = .55f;
    [SerializeField, Range(.1f, 1f), Tooltip("A shotgun pellet's tracer: this share of the length.")]
    private float pelletLength = .6f;
    [SerializeField, Min(1), Tooltip("Tracers in flight at once from one gun; the oldest gives way.")]
    private int mostAtOnce = 48;

    private WeaponController weapon;
    private Material material;
    private bool fallback;

    private sealed class Flying
    {
        public LineRenderer line;
        public Vector3 from, way;
        public float distance, flown, length, brightness;
        // The old line's numbers, when it plays instead.
        public Vector3 end;
        public float elapsed;
        public Color startColor, endColor;
    }

    private readonly List<Flying> flying = new();
    private readonly Stack<LineRenderer> spare = new();

    public void OnTableApplied() => Paint();

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
        material.SetFloat(TailId, tail);
        material.SetFloat(HeadId, head);
    }

    private void Play(WeaponShot shot)
    {
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
        var tracer = new Flying
        {
            line = line, from = from, way = path / distance, distance = distance,
            flown = delay > 0f ? -delay : Mathf.Min(startAhead, distance), length = length * (pellet ? pelletLength : 1f),
            brightness = pellet ? pelletBrightness : 1f
        };
        flying.Add(tracer);
        Place(tracer, Camera.main);
    }

    private void Update()
    {
        if (flying.Count == 0) return;
        var view = Camera.main;
        float dt = Time.deltaTime;
        for (int i = flying.Count - 1; i >= 0; i--)
        {
            var tracer = flying[i];
            if (tracer.line == null) { flying.RemoveAt(i); continue; }
            if (fallback) { if (OldTick(tracer, dt)) Land(i); continue; }
            tracer.flown += speed * dt;
            // Gone once its tail has reached the hit too: it shrinks into it.
            if (tracer.flown - tracer.length >= tracer.distance) { Land(i); continue; }
            Place(tracer, view);
        }
    }

    // The streak where it is now: its head ahead along the path, its tail a length behind, both kept on the path.
    private void Place(Flying tracer, Camera view)
    {
        var line = tracer.line;
        // A later stretch the bullet hasn't reached yet.
        bool started = tracer.flown > 0f;
        if (line.enabled != started) line.enabled = started;
        if (!started) return;
        float headAt = Mathf.Min(tracer.flown, tracer.distance);
        float tailAt = Mathf.Clamp(tracer.flown - tracer.length, 0f, headAt);
        Vector3 tailPoint = tracer.from + tracer.way * tailAt, headPoint = tracer.from + tracer.way * headAt;
        line.SetPosition(0, tailPoint);
        line.SetPosition(1, headPoint);
        line.startWidth = Wide(tailPoint, view);
        line.endWidth = Wide(headPoint, view);
        // Shrinking into the hit, it dims with what is left of it.
        float left = tracer.length > 0f ? Mathf.Clamp01((headAt - tailAt) / tracer.length) : 1f;
        bool arrived = tracer.flown >= tracer.distance;
        var tint = new Color(1f, 1f, 1f, tracer.brightness * (arrived ? left : 1f));
        line.startColor = line.endColor = tint;
    }

    private float Wide(Vector3 point, Camera view)
        => view != null ? Mathf.Max(width, Vector3.Distance(view.transform.position, point) * minAngle) : width;

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
