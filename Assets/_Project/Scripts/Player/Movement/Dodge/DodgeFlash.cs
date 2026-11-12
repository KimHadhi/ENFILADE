using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dodge's look on this PC's own screen, the first-person half of DodgeDash (user's memo 9 and 16; 2026-11-12 the
/// user sent the reference: a Roblox "NEW DASH VFX" short, "이대로 참 멋있는 듯"; a plain dodge white, a success red):
/// - a step dodge (Alt) sends thin glowing white wind lines with sharp kinks, like the reference's bolts, streaming past
///   the way the body goes: across the screen for a side step, in from the edges for a step back, out round the middle
///   for a step in; a soft white pop of air blooms up from the bottom edge where the foot kicks off, and the rim lightens
///   a touch. The world half (afterimages, the same lines along the floor, the pop and the smoke) is DodgeDash's, which
///   friends see too, and which this PC sees most on a step back, its ghost left ahead;
/// - a dodge that lets a blow go by (a perfect evade, step or Q/E) bursts red: red kinked lines flying out round the
///   crosshair, the rim and both side edges flaring red, the picture drained to a red cast for a blink, and the dodge's
///   own ghosts and lines in the world turning red (DodgeDash.Succeed). The slowed counter window after it keeps a deep
///   red rim (PerfectDefenseVignette).
/// Short and bright, gone within a third of a second (the user's rule for hit effects). Real time, so the evade's slow
/// motion doesn't hold it. Made at spawn for this PC's own player (PlayerPerfectDefense.Initialize).
/// </summary>
[DisallowMultipleComponent]
public sealed class DodgeFlash : MonoBehaviour
{
    // The plain dodge's wind and the success's burst: how many lines, how long (real s), how bright, how thick and how
    // far their kinks jump (px at 1080p).
    private const int AirLines = 9, RedLines = 24;
    private const float AirSeconds = .2f, RedSeconds = .28f, AirAlpha = .85f, RedAlpha = .95f;
    private const float AirWidth = 2.2f, RedWidth = 3f, AirKink = 16f, RedKink = 22f;
    // The rim's strength for each, how long it takes to go (real s), and the pop of air from the bottom edge.
    private const float AirRim = .08f, RedRim = .38f, RimSeconds = .22f, PopAlpha = .32f, PopSeconds = .2f;
    private static readonly Color Air = new Color(1f, 1f, 1f);
    private static readonly Color Blood = new Color(1f, .08f, .1f);

    private PlayerMovement player;
    private DodgeController dodge;
    private GameObject root;
    private SpeedLinesGraphic lines;
    private RawImage rim, pop;
    private Texture2D rimTexture, popTexture;
    private Camera view;
    private float rimTime = 1f, rimStrength, popTime = 1f;
    private Color rimColor;
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
        root = new GameObject("Dodge flash", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 39;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 1f;
        // Heavy at the edges and clear in the middle, so the crosshair and the fight stay readable through it.
        rimTexture = Soft("Dodge rim", 64, 64, (u, v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1.25f, Mathf.Sqrt(u * u + v * v))));
        // A soft mound of air standing on the bottom edge.
        popTexture = Soft("Dodge pop", 64, 32, (u, v) =>
        {
            float up = (v + 1f) * .5f, d = Mathf.Sqrt(u * u * .8f + up * up * 1.6f);
            return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(d));
        });
        var additive = FeelAssets.UiAdditive;
        rim = Image("Rim", rimTexture, additive);
        Stretch(rim.rectTransform);
        pop = Image("Pop", popTexture, additive);
        var popRect = pop.rectTransform;
        popRect.SetParent(root.transform, false);
        popRect.anchorMin = popRect.anchorMax = popRect.pivot = new Vector2(.5f, 0f);
        popRect.sizeDelta = new Vector2(980f, 360f); popRect.anchoredPosition = Vector2.zero;
        lines = new GameObject("Lines", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpeedLinesGraphic)).GetComponent<SpeedLinesGraphic>();
        Stretch(lines.rectTransform);
        lines.raycastTarget = false;
        if (additive != null) lines.material = additive;
        // The bash's emitters too, at spawn rather than at the first shove that lands.
        BashImpact.Prepare();
    }

    private static Texture2D Soft(string name, int width, int height, System.Func<float, float, float> alpha)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(x / (width - 1f) * 2f - 1f, y / (height - 1f) * 2f - 1f)));
        texture.Apply(false, true);
        return texture;
    }

    private RawImage Image(string name, Texture texture, Material additive)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.texture = texture; image.raycastTarget = false; image.enabled = false;
        if (additive != null) image.material = additive;
        return image;
    }

    private void Stretch(RectTransform rect)
    {
        rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void Update()
    {
        // The dodge exists once the movement has set up its abilities.
        if (dodge != null || player == null || player.Abilities == null) return;
        dodge = player.Abilities.Dodge;
        dodge.Started += OnStarted;
        dodge.Succeeded += OnSucceeded;
    }

    // The way the body goes is set just after the dodge starts (MovementAbilities), so the wind is read at the end of the frame.
    private void OnStarted(DodgeKind kind, int side)
    {
        if (kind == DodgeKind.Step) pendingStep = true;
    }

    private void OnSucceeded(DodgeKind kind, int side)
    {
        pendingStep = false;
        lines.Burst(SpeedLinesGraphic.Flow.Out, Vector2.zero, RedLines, RedSeconds, KitStyle.With(Blood, RedAlpha), RedWidth, RedKink);
        Rim(Blood, RedRim);
        var glint = CombatEdgeGlint.For(this);
        glint?.Show(-1, Blood, .8f, .3f);
        glint?.Show(1, Blood, .8f, .3f);
        // The dodge's own afterimages and wind lines in the world go red with it.
        GetComponent<DodgeDash>()?.Succeed();
        // A blink of red over the whole picture, gone with the lines.
        KillConfirmEffect.Parry(new ParryScreenLook
        {
            Flash = .2f, Chromatic = .8f, Lens = -.18f, Saturation = -35f, Contrast = 18f, Bloom = 1.2f, Vignette = .4f,
            Hold = .08f, Fade = .28f, Filter = new Color(1f, .8f, .8f), VignetteColor = new Color(.45f, .02f, .04f)
        });
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f) return;
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        if (pendingStep) { pendingStep = false; StepAir(); }
        lines.Advance(dt);
        if (popTime < 1f)
        {
            popTime = Mathf.Min(1f, popTime + dt / PopSeconds);
            // Up fast from the edge, then gone.
            float swell = 1f - (1f - popTime) * (1f - popTime);
            pop.rectTransform.localScale = new Vector3(1f, Mathf.Lerp(.55f, 1.15f, swell), 1f);
            pop.color = KitStyle.With(Air, PopAlpha * (1f - popTime));
            pop.enabled = popTime < 1f;
        }
        if (rimTime >= 1f) return;
        rimTime = Mathf.Min(1f, rimTime + dt / RimSeconds);
        float fade = 1f - rimTime;
        rim.color = KitStyle.With(rimColor, rimStrength * fade * fade);
        rim.enabled = rimTime < 1f;
    }

    // The wind goes past the way the body goes: sideways for a side step, in from the edges for a step back.
    private void StepAir()
    {
        if (view == null) view = GetComponentInChildren<Camera>();
        Transform eyes = view != null ? view.transform : transform;
        Vector3 moving = player.Abilities.ActionDirection;
        float across = Vector3.Dot(moving, Vector3.ProjectOnPlane(eyes.right, Vector3.up).normalized);
        float ahead = Vector3.Dot(moving, Vector3.ProjectOnPlane(eyes.forward, Vector3.up).normalized);
        var color = KitStyle.With(Air, AirAlpha);
        if (Mathf.Abs(across) >= Mathf.Abs(ahead) * .7f)
            lines.Burst(SpeedLinesGraphic.Flow.Across, new Vector2(across > 0f ? -1f : 1f, 0f), AirLines, AirSeconds, color, AirWidth, AirKink);
        else lines.Burst(ahead < 0f ? SpeedLinesGraphic.Flow.In : SpeedLinesGraphic.Flow.Out, Vector2.zero, AirLines, AirSeconds, color, AirWidth, AirKink);
        Rim(Air, AirRim);
        popTime = 0f; pop.enabled = true;
    }

    private void Rim(Color color, float strength)
    {
        rimColor = color; rimStrength = strength; rimTime = 0f;
        rim.enabled = true;
        rim.color = KitStyle.With(color, strength);
    }

    private void OnDestroy()
    {
        if (dodge != null) { dodge.Started -= OnStarted; dodge.Succeeded -= OnSucceeded; }
        if (root != null) Destroy(root);
        if (rimTexture != null) Destroy(rimTexture);
        if (popTexture != null) Destroy(popTexture);
    }
}

/// <summary>
/// Thin lines kinked twice like a bolt (the dodge reference's wind lines), fading in from their tails: across the screen
/// at heights kept off its middle band, in from its edges, or out from round its middle. Each comes and goes on its own
/// within the burst. Drawn by DodgeFlash.
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

    /// <summary>
    /// A burst of count lines over seconds (real), of this colour and thickness (px), kinks jumping up to kinkPx off the
    /// line; direction.x is the way an Across flow goes.
    /// </summary>
    public void Burst(Flow flow, Vector2 direction, int count, float seconds, Color color, float thickness, float kinkPx)
    {
        this.flow = flow;
        across = direction.x < 0f ? -1f : 1f;
        this.count = Mathf.Clamp(count, 0, Most);
        this.seconds = Mathf.Max(.05f, seconds);
        this.thickness = Mathf.Max(.5f, thickness);
        kink = Mathf.Max(0f, kinkPx);
        this.color = color;
        time = 0f;
        for (int i = 0; i < this.count; i++)
        {
            float place = flow == Flow.Across ? OffMiddle() : Random.value * 360f;
            lines[i] = new Line
            {
                place = place, length = Random.Range(.18f, .42f), width = Random.Range(.7f, 1.3f),
                delay = Random.Range(0f, .3f), speed = Random.Range(.85f, 1.3f),
                kinkA = Random.Range(-1f, 1f), kinkB = Random.Range(-1f, 1f)
            };
        }
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
