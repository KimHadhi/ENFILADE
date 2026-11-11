using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dodge's look on this PC's screen (user, 2026-11-11 #9 and #16, after For Honor's dodge: "a plain dodge white, a
/// success red"):
/// - a step dodge (Alt) streaks the screen with white air for a moment, the lines going past the way the body moves:
///   sideways across the screen for a side step, in from the edges for a step back, out from the middle for a step in,
///   with a faint white rim;
/// - a dodge that lets a blow go by (a perfect evade, step or Q/E) bursts red: red lines flying out round the crosshair,
///   the rim and both side edges flaring red and the picture drained to a red cast for a blink. The slowed counter window
///   that follows keeps a deep red rim (PerfectDefenseVignette).
/// Short and bright, gone within a third of a second (the user's rule for hit effects). Real time, so the evade's slow
/// motion doesn't hold it. Made at spawn for this PC's own player (PlayerPerfectDefense.Initialize). A YouTube reference
/// the user means to send may change the look; the parts are kept apart so each can be retuned.
/// </summary>
[DisallowMultipleComponent]
public sealed class DodgeFlash : MonoBehaviour
{
    // The plain dodge's air and the success's burst: how many lines, how long (real s), how bright, how thick (px).
    private const int AirLines = 18, RedLines = 30;
    private const float AirSeconds = .22f, RedSeconds = .3f, AirAlpha = .55f, RedAlpha = .95f, AirWidth = 1.6f, RedWidth = 3.2f;
    // The rim's strength for each, and how long it takes to go (real s).
    private const float AirRim = .1f, RedRim = .38f, RimSeconds = .22f;
    private static readonly Color Air = new Color(1f, 1f, 1f);
    private static readonly Color Blood = new Color(1f, .08f, .1f);

    private PlayerMovement player;
    private DodgeController dodge;
    private GameObject root;
    private SpeedLinesGraphic lines;
    private RawImage rim;
    private Texture2D rimTexture;
    private Camera view;
    private float rimTime = 1f, rimStrength;
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
        rimTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float u = x / 63f * 2f - 1f, v = y / 63f * 2f - 1f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1.25f, Mathf.Sqrt(u * u + v * v)));
            rimTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        rimTexture.Apply(false, true);
        var additive = FeelAssets.UiAdditive;
        rim = new GameObject("Rim", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        Stretch(rim.rectTransform);
        rim.texture = rimTexture; rim.raycastTarget = false; rim.enabled = false;
        if (additive != null) rim.material = additive;
        lines = new GameObject("Lines", typeof(RectTransform), typeof(CanvasRenderer), typeof(SpeedLinesGraphic)).GetComponent<SpeedLinesGraphic>();
        Stretch(lines.rectTransform);
        lines.raycastTarget = false;
        if (additive != null) lines.material = additive;
        // The bash's emitters too, at spawn rather than at the first shove that lands.
        BashImpact.Prepare();
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

    // The way the body goes is set just after the dodge starts (MovementAbilities), so the air is read at the end of the frame.
    private void OnStarted(DodgeKind kind, int side)
    {
        if (kind == DodgeKind.Step) pendingStep = true;
    }

    private void OnSucceeded(DodgeKind kind, int side)
    {
        pendingStep = false;
        lines.Burst(SpeedLinesGraphic.Flow.Out, Vector2.zero, RedLines, RedSeconds, KitStyle.With(Blood, RedAlpha), RedWidth);
        Rim(Blood, RedRim);
        var glint = CombatEdgeGlint.For(this);
        glint?.Show(-1, Blood, .8f, .3f);
        glint?.Show(1, Blood, .8f, .3f);
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
        if (rimTime >= 1f) return;
        rimTime = Mathf.Min(1f, rimTime + dt / RimSeconds);
        float fade = 1f - rimTime;
        rim.color = KitStyle.With(rimColor, rimStrength * fade * fade);
        rim.enabled = rimTime < 1f;
    }

    // The air goes past the way the body goes: sideways for a side step, in from the edges for a step back.
    private void StepAir()
    {
        if (view == null) view = GetComponentInChildren<Camera>();
        Transform eyes = view != null ? view.transform : transform;
        Vector3 moving = player.Abilities.ActionDirection;
        float across = Vector3.Dot(moving, Vector3.ProjectOnPlane(eyes.right, Vector3.up).normalized);
        float ahead = Vector3.Dot(moving, Vector3.ProjectOnPlane(eyes.forward, Vector3.up).normalized);
        var color = KitStyle.With(Air, AirAlpha);
        if (Mathf.Abs(across) >= Mathf.Abs(ahead) * .7f)
            lines.Burst(SpeedLinesGraphic.Flow.Across, new Vector2(across > 0f ? -1f : 1f, 0f), AirLines, AirSeconds, color, AirWidth);
        else lines.Burst(ahead < 0f ? SpeedLinesGraphic.Flow.In : SpeedLinesGraphic.Flow.Out, Vector2.zero, AirLines, AirSeconds, color, AirWidth);
        Rim(Air, AirRim);
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
    }
}

/// <summary>
/// Thin streaks that taper to nothing behind them: across the screen at heights kept off its middle band, in from its
/// edges, or out from round its middle. Each comes and goes on its own within the burst. Drawn by DodgeFlash.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpeedLinesGraphic : MaskableGraphic
{
    public enum Flow { Across, In, Out }
    private struct Line { public float place, length, width, delay, speed; }
    private const int Most = 40;
    private readonly Line[] lines = new Line[Most];
    private int count;
    private float time = 1f, seconds = 1f, thickness = 2f;
    private Flow flow;
    private float across = 1f;

    /// <summary>A burst of count lines over seconds (real), of this colour and thickness (px); direction.x is the way an Across flow goes.</summary>
    public void Burst(Flow flow, Vector2 direction, int count, float seconds, Color color, float thickness)
    {
        this.flow = flow;
        across = direction.x < 0f ? -1f : 1f;
        this.count = Mathf.Clamp(count, 0, Most);
        this.seconds = Mathf.Max(.05f, seconds);
        this.thickness = Mathf.Max(.5f, thickness);
        this.color = color;
        time = 0f;
        for (int i = 0; i < this.count; i++)
        {
            float place = flow == Flow.Across ? OffMiddle() : Random.value * 360f;
            lines[i] = new Line
            {
                place = place, length = Random.Range(.16f, .4f), width = Random.Range(.6f, 1.4f),
                delay = Random.Range(0f, .3f), speed = Random.Range(.85f, 1.3f)
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
                var out1 = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float r = flow == Flow.Out ? Mathf.Lerp(.28f, 1.1f, go) : Mathf.Lerp(1.1f, .4f, go);
                head = middle + out1 * (r * reach);
                float back = line.length * .6f * (flow == Flow.Out ? -1f : 1f);
                tail = middle + out1 * (Mathf.Max(.12f, r + back) * reach);
            }
            var tint = color;
            tint.a *= Mathf.Sin(own * Mathf.PI);
            Taper(mesh, tail, head, thickness * line.width, tint);
        }
    }

    // A sliver from a point at the tail to the full width at the head.
    private static void Taper(VertexHelper mesh, Vector2 tail, Vector2 head, float width, Color tint)
    {
        Vector2 along = head - tail;
        if (along.sqrMagnitude < 1f) return;
        Vector2 side = new Vector2(-along.y, along.x).normalized * (width * .5f);
        Color32 full = tint, clear = tint;
        clear.a = 0;
        int first = mesh.currentVertCount;
        mesh.AddVert(tail, clear, Vector4.zero);
        mesh.AddVert(head + side, full, Vector4.zero);
        mesh.AddVert(head - side, full, Vector4.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
    }
}
