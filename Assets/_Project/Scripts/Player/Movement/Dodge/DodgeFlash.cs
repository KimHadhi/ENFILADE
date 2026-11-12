using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dodge's one beat as this PC's own player sees it from inside the body, the first-person half of DodgeDash (user,
/// 2026-11-12, memo 9 and 16: the reference, a Roblox "NEW DASH VFX" short; then "회피가 레퍼런스 영상처럼 걍 딱 굵게 한
/// 번만 나오면 될 것 같은데"; and their rule "내가 보는 화면이 진실": what the player sees is what friends see of them):
/// - the world's beat (the kick-off puff, the one afterimage, the one streak: DodgeDash) is drawn for this PC too, and a
///   step back sees all of it ahead, so the screen adds nothing;
/// - a step aside or in leaves that streak beside, behind or under the camera, so the same streak is drawn on the screen
///   instead: three bold white kinked strokes, all at once, across the low screen for a side step or out round the
///   middle for a step in, gone with the world's;
/// - an evade (a blow let through, step or Q/E) turns that one streak red: the world's on every PC (DodgeDash.Succeed),
///   and on this screen the strokes under way, or for a Q/E slip red strokes across the way it slipped. The slowed
///   counter window after it keeps its deep red rim (PerfectDefenseVignette).
/// Seen from outside the body (third person) the world's beat is all in view and nothing is drawn here. Real time, so the
/// evade's slow motion doesn't hold it. Made at spawn for this PC's own player (PlayerPerfectDefense.Initialize).
/// Numbers: Tables/Player.csv, row DodgeView.
/// </summary>
[DisallowMultipleComponent]
public sealed class DodgeFlash : MonoBehaviour
{
    [SerializeField, Min(0), Tooltip("Strokes of the streak on the screen (the world's streak has as many lines).")]
    private int strokes = 3;
    [SerializeField, Min(.05f), Tooltip("How long they last (real s): the world streak's time.")]
    private float strokeSeconds = .22f;
    [SerializeField, Min(.5f), Tooltip("How thick (px at 1080p).")]
    private float strokeWidth = 7f;
    [SerializeField, Min(0f), Tooltip("How far their kinks jump off the line (px at 1080p).")]
    private float strokeKink = 26f;
    [SerializeField, Range(0f, 1f), Tooltip("How solid.")]
    private float strokeAlpha = .9f;

    private static readonly Color Air = new Color(1f, 1f, 1f);
    private static readonly Color Blood = new Color(1f, .08f, .1f);

    private PlayerMovement player;
    private DodgeController dodge;
    private GameObject root;
    private SpeedLinesGraphic lines;
    private Camera view;
    private bool pendingStep;

    /// <summary>The player's dodge look, made on first use.</summary>
    public static DodgeFlash For(PlayerMovement player)
    {
        if (player == null || !Application.isPlaying) return null;
        return player.TryGetComponent(out DodgeFlash flash) ? flash : player.gameObject.AddComponent<DodgeFlash>();
    }

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        GameTables.Bind(PlayerMovement.TableName, "DodgeView", this);
        root = new GameObject("Dodge flash", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 39;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 1f;
        lines = new GameObject("Lines", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpeedLinesGraphic)).GetComponent<SpeedLinesGraphic>();
        var rect = lines.rectTransform;
        rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        lines.raycastTarget = false;
        var additive = FeelAssets.UiAdditive;
        if (additive != null) lines.material = additive;
        // The bash's emitters too, at spawn rather than at the first shove that lands.
        BashImpact.Prepare();
    }

    private void Update()
    {
        // The dodge exists once the movement has set up its abilities.
        if (dodge != null || player == null || player.Abilities == null) return;
        dodge = player.Abilities.Dodge;
        dodge.Started += OnStarted;
        dodge.Succeeded += OnSucceeded;
    }

    // The way the body goes is set just after the dodge starts (MovementAbilities), so the streak is read at the end of the frame.
    private void OnStarted(DodgeKind kind, int side)
    {
        if (kind == DodgeKind.Step) pendingStep = true;
    }

    private void OnSucceeded(DodgeKind kind, int side)
    {
        // This very dodge's strokes, if its start was still waiting for the end of the frame.
        if (pendingStep) { pendingStep = false; StepAir(); }
        // The one streak goes red: in the world, on every PC.
        if (TryGetComponent(out DodgeDash dash)) dash.Succeed(kind == DodgeKind.Micro ? side : 0);
        if (!InBody()) return;
        var red = KitStyle.With(Blood, strokeAlpha);
        // On the screen: the strokes under way, held long enough to be seen red; a Q/E slip has none, so red strokes go
        // by the way it slipped. A step back's red streak is ahead, in the world.
        if (lines.Showing) lines.Flare(red, strokeSeconds * .8f);
        else if (kind == DodgeKind.Micro && side != 0) Strokes(SpeedLinesGraphic.Flow.Across, side > 0 ? -1f : 1f, red);
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;
        if (pendingStep) { pendingStep = false; StepAir(); }
        lines.Advance(Mathf.Min(.05f, Time.unscaledDeltaTime));
    }

    // The streak seen from inside: across the low screen for a side step, out round the middle for a step in. A step
    // back shows none here: its afterimage and streak are in the world ahead, in view.
    private void StepAir()
    {
        if (strokes <= 0 || !InBody()) return;
        if (view == null) view = GetComponentInChildren<Camera>();
        Transform eyes = view != null ? view.transform : transform;
        Vector3 moving = player.Abilities.ActionDirection;
        float across = Vector3.Dot(moving, Vector3.ProjectOnPlane(eyes.right, Vector3.up).normalized);
        float ahead = Vector3.Dot(moving, Vector3.ProjectOnPlane(eyes.forward, Vector3.up).normalized);
        var white = KitStyle.With(Air, strokeAlpha);
        if (Mathf.Abs(across) >= Mathf.Abs(ahead) * .7f) Strokes(SpeedLinesGraphic.Flow.Across, across > 0f ? -1f : 1f, white);
        else if (ahead > 0f) Strokes(SpeedLinesGraphic.Flow.Out, 1f, white);
    }

    // way: which way an Across flow goes (the world slides the other way past a side step).
    private void Strokes(SpeedLinesGraphic.Flow flow, float way, Color color)
        => lines.Burst(flow, new Vector2(way, 0f), strokes, strokeSeconds, color, strokeWidth, strokeKink, true);

    // Seen from its own eyes. From outside the body (third person) the world's beat is all in view and the screen needs none.
    private bool InBody()
    {
        var camera = Camera.main;
        Transform eyes = PlayerView.EyesOf(player);
        return camera == null || eyes == null || (camera.transform.position - eyes.position).sqrMagnitude < .36f;
    }

    private void OnDestroy()
    {
        if (dodge != null) { dodge.Started -= OnStarted; dodge.Succeeded -= OnSucceeded; }
        if (root != null) Destroy(root);
    }
}

/// <summary>
/// Lines kinked twice like a bolt (the dodge reference's streak), fading in from their tails: across the screen, in from
/// its edges, or out from round its middle. Together (the dodge's one beat) they go as one stroke, evenly spread, across
/// the low screen; otherwise each comes and goes on its own within the burst, off the screen's middle band. Drawn by DodgeFlash.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpeedLinesGraphic : MaskableGraphic
{
    public enum Flow { Across, In, Out }
    private struct Line { public float place, length, width, delay, speed, kinkA, kinkB; }
    private const int Most = 40;
    private readonly Line[] lines = new Line[Most];
    private int count;
    private float time = 1f, seconds = 1f, thickness = 2f, kink = 16f;
    private Flow flow;
    private float across = 1f;

    /// <summary>A burst is on the screen.</summary>
    public bool Showing => time < 1f && count > 0;

    /// <summary>
    /// A burst of count lines over seconds (real), of this colour and thickness (px), kinks jumping up to kinkPx off the
    /// line; direction.x is the way an Across flow goes. together: one stroke of evenly spread lines, all at once.
    /// </summary>
    public void Burst(Flow flow, Vector2 direction, int count, float seconds, Color color, float thickness, float kinkPx, bool together = false)
    {
        this.flow = flow;
        across = direction.x < 0f ? -1f : 1f;
        this.count = Mathf.Clamp(count, 0, Most);
        this.seconds = Mathf.Max(.05f, seconds);
        this.thickness = Mathf.Max(.5f, thickness);
        kink = Mathf.Max(0f, kinkPx);
        this.color = color;
        time = 0f;
        float turn = Random.value * 360f;
        for (int i = 0; i < this.count; i++)
        {
            float even = this.count > 1 ? i / (this.count - 1f) : .5f;
            float place = together
                ? flow == Flow.Across ? Mathf.Lerp(.1f, .36f, even) + Random.Range(-.02f, .02f) : turn + i * 360f / this.count + Random.Range(-14f, 14f)
                : flow == Flow.Across ? OffMiddle() : Random.value * 360f;
            lines[i] = new Line
            {
                place = place,
                length = together ? Random.Range(.32f, .4f) : Random.Range(.18f, .42f),
                width = together ? Random.Range(.9f, 1.1f) : Random.Range(.7f, 1.3f),
                delay = together ? 0f : Random.Range(0f, .3f),
                speed = together ? Random.Range(.96f, 1.04f) : Random.Range(.85f, 1.3f),
                kinkA = Random.Range(-1f, 1f), kinkB = Random.Range(-1f, 1f)
            };
        }
        SetVerticesDirty();
    }

    /// <summary>The burst under way turns this colour, its rest lasting at least atLeast real seconds so it is seen.</summary>
    public void Flare(Color color, float atLeast)
    {
        if (!Showing) return;
        this.color = color;
        float rest = (1f - time) * seconds;
        if (rest < atLeast) seconds = atLeast / Mathf.Max(.01f, 1f - time);
        SetVerticesDirty();
    }

    // A height on the screen (0-1) away from the middle band, where the crosshair is.
    private static float OffMiddle()
    {
        float off = Random.Range(.07f, .46f);
        return Random.value < .5f ? .5f - off : .5f + off;
    }

    public void Advance(float dt)
    {
        if (time >= 1f) return;
        time = Mathf.Min(1f, time + dt / seconds);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (time >= 1f || count == 0) return;
        Rect rect = GetPixelAdjustedRect();
        Vector2 middle = rect.center;
        float reach = rect.size.magnitude * .5f;
        // Kinks and widths are given at 1080p.
        float scale = rect.height / 1080f;
        for (int i = 0; i < count; i++)
        {
            var line = lines[i];
            float own = Mathf.Clamp01((time - line.delay) / Mathf.Max(.05f, 1f - line.delay));
            if (own <= 0f || own >= 1f) continue;
            float go = Mathf.Clamp01(own * line.speed);
            Vector2 head, tail;
            if (flow == Flow.Across)
            {
                float along = Mathf.Lerp(-line.length, 1f + line.length, go);
                if (across < 0f) along = 1f - along;
                float y = rect.yMin + rect.height * line.place, x = rect.xMin + rect.width * along;
                head = new Vector2(x, y);
                tail = new Vector2(x - across * rect.width * line.length, y);
            }
            else
            {
                float angle = line.place * Mathf.Deg2Rad;
                var outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float r = flow == Flow.Out ? Mathf.Lerp(.28f, 1.1f, go) : Mathf.Lerp(1.1f, .4f, go);
                head = middle + outward * (r * reach);
                float back = line.length * .6f * (flow == Flow.Out ? -1f : 1f);
                tail = middle + outward * (Mathf.Max(.12f, r + back) * reach);
            }
            var tint = color;
            tint.a *= Mathf.Sin(own * Mathf.PI);
            Bolt(mesh, tail, head, thickness * line.width * scale, kink * scale, line.kinkA, line.kinkB, tint);
        }
    }

    // Tail to head in three straight runs, the two joints jumping either side of the line; faint at the tail.
    private static void Bolt(VertexHelper mesh, Vector2 tail, Vector2 head, float width, float kink, float kinkA, float kinkB, Color tint)
    {
        Vector2 along = head - tail;
        if (along.sqrMagnitude < 4f) return;
        Vector2 across = new Vector2(-along.y, along.x).normalized;
        Vector2 b = Vector2.Lerp(tail, head, .36f) + across * (kinkA * kink);
        Vector2 c = Vector2.Lerp(tail, head, .68f) - across * (kinkB * kink);
        Segment(mesh, tail, b, width, 0f, .8f, tint);
        Segment(mesh, b, c, width, .8f, 1f, tint);
        Segment(mesh, c, head, width, 1f, 1f, tint);
    }

    private static void Segment(VertexHelper mesh, Vector2 from, Vector2 to, float width, float fromAlpha, float toAlpha, Color tint)
    {
        Vector2 along = to - from;
        if (along.sqrMagnitude < 1f) return;
        Vector2 side = new Vector2(-along.y, along.x).normalized * (width * .5f);
        Color start = tint, end = tint;
        start.a *= fromAlpha; end.a *= toAlpha;
        int first = mesh.currentVertCount;
        mesh.AddVert(from + side, start, Vector4.zero);
        mesh.AddVert(from - side, start, Vector4.zero);
        mesh.AddVert(to - side, end, Vector4.zero);
        mesh.AddVert(to + side, end, Vector4.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }
}
