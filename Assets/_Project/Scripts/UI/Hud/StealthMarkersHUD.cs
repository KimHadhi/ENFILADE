using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The 2026-11-11 stealth round: who is noticing this PC's player, and how sure they are. Over the head of each zombie
/// that is (ZombieMind.Watching), a small leaning diamond fills from the bottom as its suspicion grows, white turning
/// pink; once it stops to look (Alert) or comes looking (Searching) a "?" stands in it; the moment it wakes on them a
/// "!" bursts out of it with a sting and is gone within a second. One off the screen or behind shows as a wedge on a
/// ring round the crosshair instead, pointing its way and filling the same, so being made out from behind is never a
/// surprise (the user: scattered calm zombies "사방을 보고 있고"). After Advanced Stealth's suspicion marker over the
/// guard who sees you (F:\Addons), with the detection arrows shooters put round the crosshair. On the local player,
/// added by PlayerMovement; built on first use. Numbers: Stealth.csv Markers.
/// </summary>
[DisallowMultipleComponent]
public sealed class StealthMarkersHUD : MonoBehaviour, ITableBound
{
    public const string TableName = "Stealth", TableRow = "Markers";
    // From this suspicion on, a zombie that stopped to look is unsure of the player: pink rim, and a tick.
    private const float SuspectFrom = .2f;

    [SerializeField, Range(0f, 1f), Tooltip("Least suspicion that shows a mark.")]
    private float showFrom = .03f;
    [SerializeField, Min(0f), Tooltip("How far over its head the mark stands (m).")]
    private float above = .42f;
    [SerializeField, Min(8f), Tooltip("The diamond's height (px at 1080p).")]
    private float size = 34f;
    [SerializeField, Min(40f), Tooltip("How far from the crosshair the wedges of zombies off the screen stand (px).")]
    private float ringRadius = 165f;
    [SerializeField, Min(8f), Tooltip("A wedge's length (px).")]
    private float wedgeSize = 30f;
    [SerializeField, Min(0f), Tooltip("Zombies farther than this (m) show no mark.")]
    private float farthest = 45f;
    [SerializeField, Min(0f), Tooltip("How far in from the screen's edges a head must be to show its mark there instead of a wedge (px).")]
    private float edgeMargin = 60f;
    [SerializeField, Min(.1f), Tooltip("How long the '!' of a zombie that woke on the player stays (s).")]
    private float spottedSeconds = .9f;
    [SerializeField, Range(0f, 1f), Tooltip("Volume of the tick when one stops to look this way.")]
    private float suspectVolume = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("Volume of the sting when one wakes on the player.")]
    private float spottedVolume = .85f;
    [SerializeField, Min(0f), Tooltip("How far below the crosshair the light meter stands while crouched (px; StealthLight).")]
    private float lightBelow = 92f;
    [SerializeField, Min(8f), Tooltip("The light meter's diamond (px).")]
    private float lightSize = 22f;

    // The light meter (StealthLight): a small diamond under the crosshair, filled as lit as the player stands, with a word.
    private RectTransform lightRoot;
    private CanvasGroup lightGroup;
    private StealthGlyph lightGlyph;
    private TextMeshProUGUI lightWord;
    private float lightAlpha, lightShown = 1f;

    private sealed class Mark
    {
        public RectTransform root, wedgeRoot;
        public CanvasGroup group;
        public StealthGlyph glyph, wedge;
        public TextMeshProUGUI sign;
        public float fill, alpha, burst = -1f;
        public bool looked, used;
    }

    private readonly Dictionary<ZombieMind, Mark> marks = new Dictionary<ZombieMind, Mark>();
    private readonly Dictionary<ZombieMind, ZombieMood> moods = new Dictionary<ZombieMind, ZombieMood>();
    private readonly Stack<Mark> spare = new Stack<Mark>();
    private readonly List<ZombieMind> gone = new List<ZombieMind>();
    private PlayerMovement movement;
    private RectTransform canvas;
    private float lastSuspect = -9f, lastSpotted = -9f;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        GameTables.Bind(TableName, TableRow, this);
    }

    void ITableBound.OnTableApplied() { }

    private void LateUpdate()
    {
        var view = Camera.main;
        float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
        foreach (var pair in marks) pair.Value.used = false;
        if (view != null && movement != null && movement.IsLocal)
            foreach (var mind in ZombieMind.All)
                if (mind != null && mind.isActiveAndEnabled) Track(mind, view, dt, now);
        // Marks of zombies gone or no longer about this player fade and go back to the pool.
        gone.Clear();
        foreach (var pair in marks)
        {
            var mark = pair.Value;
            if (pair.Key != null && mark.used) continue;
            mark.alpha = Mathf.MoveTowards(mark.alpha, 0f, dt * 6f);
            if (mark.group != null) mark.group.alpha = mark.alpha;
            if (pair.Key == null || mark.alpha <= 0f) gone.Add(pair.Key);
        }
        foreach (var mind in gone)
        {
            if (!marks.TryGetValue(mind, out var mark)) continue;
            marks.Remove(mind);
            Release(mark);
        }
        // Destroyed zombies' entries: swept out whole.
        if (gone.Contains(null)) Sweep();
        if (Time.frameCount % 120 == 0) Prune();
        LightMeter(dt);
    }

    // Crouched or lying, how lit they stand (StealthLight): the darker, the less the zombies see them. A diamond under the
    // crosshair fills from dim to white as the light grows, with 어둠 / 그늘 / 밝음 beside it, as Thief's light gem tells
    // its thief; the memo's item 47 ("어두운 곳일수록 덜 보이게").
    private void LightMeter(float dt)
    {
        bool wanted = movement != null && movement.IsLocal && !movement.IsDead && StealthLight.Enabled
            && (movement.IsCrouching || movement.IsProning);
        lightAlpha = Mathf.MoveTowards(lightAlpha, wanted ? 1f : 0f, dt * 5f);
        if (lightRoot == null)
        {
            if (lightAlpha <= 0f) return;
            BuildLight();
        }
        lightGroup.alpha = lightAlpha;
        if (lightAlpha <= 0f) return;
        lightShown = Mathf.MoveTowards(lightShown, StealthLight.Level(movement), dt * 2.5f);
        Color fill = Color.Lerp(KitStyle.With(KitStyle.Ice, .3f), Color.white, lightShown);
        lightGlyph.Paint(KitStyle.With(KitStyle.Ink, .82f), KitStyle.Ice, fill, Mathf.Max(.08f, lightShown));
        lightWord.text = Texts.Get(lightShown < .3f ? "hud.stealth.dark" : lightShown < .7f ? "hud.stealth.dim" : "hud.stealth.lit");
        lightWord.color = KitStyle.With(KitStyle.Ice, Mathf.Lerp(.55f, 1f, lightShown));
    }

    private void BuildLight()
    {
        Build();
        lightRoot = new GameObject("Light Meter", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        lightRoot.SetParent(canvas, false);
        lightRoot.anchorMin = lightRoot.anchorMax = lightRoot.pivot = new Vector2(.5f, .5f);
        lightRoot.anchoredPosition = new Vector2(0f, -lightBelow);
        lightRoot.sizeDelta = new Vector2(lightSize, lightSize);
        lightGroup = lightRoot.GetComponent<CanvasGroup>();
        lightGroup.interactable = lightGroup.blocksRaycasts = false;
        lightGlyph = HudShapes.Make<StealthGlyph>("Diamond", lightRoot, Color.white);
        lightGlyph.rectTransform.sizeDelta = new Vector2(lightSize * .78f, lightSize);
        lightGlyph.Form(false, lightSize * .16f);
        lightWord = new GameObject("Word", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        lightWord.rectTransform.SetParent(lightRoot, false);
        lightWord.rectTransform.anchorMin = lightWord.rectTransform.anchorMax = new Vector2(1f, .5f);
        lightWord.rectTransform.pivot = new Vector2(0f, .5f);
        lightWord.rectTransform.anchoredPosition = new Vector2(lightSize * .35f, 0f);
        lightWord.rectTransform.sizeDelta = new Vector2(120f, lightSize);
        // The prompts' font: it has the Korean letters.
        lightWord.font = CombatUIAssets.ResolveFont(null);
        lightWord.fontSize = lightSize * .62f;
        lightWord.fontStyle = FontStyles.Bold;
        lightWord.alignment = TextAlignmentOptions.MidlineLeft;
        lightWord.raycastTarget = false;
        lightWord.textWrappingMode = TextWrappingModes.NoWrap;
        lightWord.overflowMode = TextOverflowModes.Overflow;
    }

    private void Track(ZombieMind mind, Camera view, float dt, float now)
    {
        bool mine = mind.Watching == movement;
        var mood = mind.Mood;
        bool asleep = !mind.IsAwake;
        // Every zombie's last mood is kept, marked or not: one that wakes straight from nothing (seen right before its
        // eyes, or called by another) still shows its '!'.
        bool wasAsleep = moods.TryGetValue(mind, out var before) && (before == ZombieMood.Calm || before == ZombieMood.Alert
            || before == ZombieMood.Searching);
        moods[mind] = mood;
        marks.TryGetValue(mind, out var mark);
        float suspicion = mind.ShownSuspicion;
        Vector3 head = mind.EyePoint + Vector3.up * above;
        float distance = Vector3.Distance(head, view.transform.position);
        bool looking = mood == ZombieMood.Alert || mood == ZombieMood.Searching;
        // It woke on this player just now: the '!'.
        if (mine && !asleep && wasAsleep && distance <= farthest)
        {
            if (mark == null) { mark = Take(); marks[mind] = mark; mark.fill = 1f; }
            mark.burst = 0f;
            if (now - lastSpotted > .3f) { KitSound.Play(KitSound.Cue.Spotted, spottedVolume); lastSpotted = now; }
        }
        bool bursting = mark != null && mark.burst >= 0f && mark.burst < spottedSeconds;
        bool wanted = mine && asleep && distance <= farthest && (suspicion >= showFrom || looking);
        // Not wanted, it is left unused and fades out in LateUpdate.
        if (!wanted && !bursting) return;
        if (mark == null)
        {
            mark = Take();
            marks[mind] = mark;
            mark.fill = suspicion;
        }
        mark.used = true;
        // It stopped to look this way, unsure of them (a fresh mark counts as having been calm). One only turned to a
        // sound or off to see a stone is no news of being seen: no tick.
        if (mine && looking && !mark.looked && suspicion >= SuspectFrom && now - lastSuspect > .25f)
        {
            KitSound.Play(KitSound.Cue.Suspect, suspectVolume);
            lastSuspect = now;
        }
        mark.looked = looking;
        if (asleep && !bursting) mark.burst = -1f;
        if (mark.burst >= 0f) mark.burst += dt;
        mark.fill = Mathf.MoveTowards(mark.fill, bursting ? 1f : suspicion, dt * 3f);
        Draw(mark, mind, view, head, looking, bursting);
    }

    // Forgets zombies that are gone.
    private void Prune()
    {
        gone.Clear();
        foreach (var pair in moods) if (pair.Key == null) gone.Add(pair.Key);
        if (gone.Count == 0) return;
        var keep = new Dictionary<ZombieMind, ZombieMood>();
        foreach (var pair in moods) if (pair.Key != null) keep[pair.Key] = pair.Value;
        moods.Clear();
        foreach (var pair in keep) moods[pair.Key] = pair.Value;
    }

    private void Draw(Mark mark, ZombieMind mind, Camera view, Vector3 head, bool looking, bool bursting)
    {
        float t = bursting ? mark.burst : 0f;
        float fade = bursting ? 1f - Mathf.Clamp01((t - spottedSeconds * .55f) / (spottedSeconds * .45f)) : 1f;
        mark.alpha = Mathf.MoveTowards(mark.alpha, fade, Time.unscaledDeltaTime * 10f);
        mark.group.alpha = bursting ? fade : mark.alpha;
        Color pink = KitStyle.Accent, ice = KitStyle.Ice;
        // Pink is for being made out: one off to look at a stone (no suspicion of anyone) keeps a white rim and an empty
        // diamond under its '?'.
        Color line = bursting ? Color.Lerp(Color.white, pink, Mathf.Clamp01(t / .12f)) : looking && mark.fill >= SuspectFrom ? pink : ice;
        Color fill = bursting ? pink : Color.Lerp(ice, pink, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, .8f, mark.fill)));
        Color plate = bursting ? new Color(.16f, .02f, .06f, .92f) : KitStyle.With(KitStyle.Ink, .82f);
        string sign = bursting ? "!" : looking ? "?" : "";

        Vector3 screen = view.WorldToScreenPoint(head);
        bool onScreen = screen.z > 0f && screen.x >= edgeMargin && screen.x <= Screen.width - edgeMargin
            && screen.y >= edgeMargin && screen.y <= Screen.height - edgeMargin;
        mark.glyph.gameObject.SetActive(onScreen);
        mark.sign.gameObject.SetActive(onScreen && sign.Length > 0);
        mark.wedgeRoot.gameObject.SetActive(!onScreen);
        if (onScreen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
            // The '!' bursts out big and shakes a moment.
            float pop = bursting ? Mathf.Lerp(1.75f, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(t / .14f), 3f)) : 1f;
            Vector2 shake = bursting && t < .28f ? new Vector2(Mathf.Sin(t * 90f), Mathf.Cos(t * 77f)) * 3f * (1f - t / .28f) : Vector2.zero;
            mark.root.anchoredPosition = local + shake;
            mark.root.localScale = Vector3.one * pop;
            mark.glyph.Paint(plate, line, fill, mark.fill);
            mark.sign.text = sign;
            mark.sign.color = bursting ? Color.white : ice;
            mark.sign.fontSize = bursting ? size * .9f : size * .62f;
            return;
        }
        // Off the screen: a wedge on the ring, pointing the way it is from the camera, seen from above.
        Vector3 local3 = view.transform.InverseTransformPoint(head);
        Vector2 way = new Vector2(local3.x, local3.z);
        if (way.sqrMagnitude < 1e-4f) way = Vector2.down;
        float angle = Mathf.Atan2(way.x, way.y);
        mark.root.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * ringRadius;
        mark.root.localScale = Vector3.one * (bursting ? Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(t / .14f)) : 1f);
        mark.wedgeRoot.localEulerAngles = new Vector3(0f, 0f, -angle * Mathf.Rad2Deg);
        mark.wedge.Paint(plate, line, fill, mark.fill);
    }

    private Mark Take()
    {
        Build();
        Mark mark = spare.Count > 0 ? spare.Pop() : Make();
        mark.root.gameObject.SetActive(true);
        mark.alpha = 0f;
        mark.burst = -1f;
        mark.looked = false;
        mark.group.alpha = 0f;
        return mark;
    }

    private void Release(Mark mark)
    {
        if (mark.root == null) return;
        mark.root.gameObject.SetActive(false);
        spare.Push(mark);
    }

    // Drops entries of destroyed zombies (Unity's null keys can't be found by lookup).
    private void Sweep()
    {
        var keep = new Dictionary<ZombieMind, Mark>();
        foreach (var pair in marks)
        {
            if (pair.Key != null) keep[pair.Key] = pair.Value;
            else Release(pair.Value);
        }
        marks.Clear();
        foreach (var pair in keep) marks[pair.Key] = pair.Value;
    }

    private Mark Make()
    {
        var mark = new Mark();
        mark.root = new GameObject("Suspicion", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        mark.root.SetParent(canvas, false);
        mark.root.anchorMin = mark.root.anchorMax = mark.root.pivot = new Vector2(.5f, .5f);
        mark.root.sizeDelta = new Vector2(size, size);
        mark.group = mark.root.GetComponent<CanvasGroup>();
        mark.group.interactable = mark.group.blocksRaycasts = false;
        mark.glyph = HudShapes.Make<StealthGlyph>("Diamond", mark.root, Color.white);
        mark.glyph.rectTransform.sizeDelta = new Vector2(size * .78f, size);
        mark.glyph.Form(false, size * .16f);
        mark.sign = new GameObject("Sign", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        mark.sign.rectTransform.SetParent(mark.glyph.rectTransform, false);
        mark.sign.rectTransform.anchorMin = Vector2.zero;
        mark.sign.rectTransform.anchorMax = Vector2.one;
        mark.sign.rectTransform.offsetMin = mark.sign.rectTransform.offsetMax = Vector2.zero;
        var font = GunFeelSettings.Instance != null ? GunFeelSettings.Instance.scoreFont : null;
        mark.sign.font = font != null ? font : CombatUIAssets.ResolveFont(null);
        mark.sign.fontStyle = FontStyles.Bold;
        mark.sign.alignment = TextAlignmentOptions.Center;
        mark.sign.raycastTarget = false;
        mark.sign.textWrappingMode = TextWrappingModes.NoWrap;
        mark.sign.overflowMode = TextOverflowModes.Overflow;
        mark.wedgeRoot = new GameObject("Wedge", typeof(RectTransform)).GetComponent<RectTransform>();
        mark.wedgeRoot.SetParent(mark.root, false);
        mark.wedgeRoot.anchorMin = mark.wedgeRoot.anchorMax = mark.wedgeRoot.pivot = new Vector2(.5f, .5f);
        mark.wedge = HudShapes.Make<StealthGlyph>("Wedge", mark.wedgeRoot, Color.white);
        mark.wedge.rectTransform.sizeDelta = new Vector2(wedgeSize * .85f, wedgeSize);
        mark.wedge.Form(true, 0f);
        return mark;
    }

    private void Build()
    {
        if (canvas != null) return;
        var host = new GameObject("Stealth Markers HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.hideFlags = HideFlags.DontSave;
        host.transform.SetParent(transform, false);
        var made = host.GetComponent<Canvas>();
        made.renderMode = RenderMode.ScreenSpaceOverlay;
        made.sortingOrder = 77;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas = host.GetComponent<RectTransform>();
    }

    private void OnDisable()
    {
        foreach (var pair in marks) Release(pair.Value);
        marks.Clear();
        moods.Clear();
    }

    private void OnDestroy()
    {
        if (canvas == null) return;
        if (Application.isPlaying) Destroy(canvas.gameObject); else DestroyImmediate(canvas.gameObject);
    }
}

/// <summary>
/// The stealth HUD's mark (StealthMarkersHUD): a diamond leaning to the right as the HUD's pieces lean, or a wedge
/// pointing up, each a dark plate in a bright rim, filled from the bottom to Level. Meshes, so they stay sharp at any
/// size and need no sprite.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class StealthGlyph : MaskableGraphic
{
    private const float Rim = .17f;
    [SerializeField] private bool wedge;
    [SerializeField] private float lean;
    private Color plate = new Color(0f, 0f, 0f, .8f), rim = Color.white, inside = Color.white;
    private float level;

    private static readonly List<Vector2> outer = new List<Vector2>(4), inner = new List<Vector2>(4), cut = new List<Vector2>(8);

    /// <summary>Wedge or diamond, and how far the diamond's top leans right of its bottom (px).</summary>
    public void Form(bool asWedge, float leanBy)
    {
        wedge = asWedge;
        lean = leanBy;
        SetVerticesDirty();
    }

    /// <summary>Its colours and how full it is (0 to 1).</summary>
    public void Paint(Color plateColor, Color rimColor, Color fillColor, float fill)
    {
        fill = Mathf.Clamp01(fill);
        if (plate == plateColor && rim == rimColor && inside == fillColor && Mathf.Abs(level - fill) < .002f) return;
        plate = plateColor; rim = rimColor; inside = fillColor; level = fill;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Outline(rect, 1f, outer);
        Outline(rect, 1f - Rim, inner);
        Fan(mesh, outer, rim * color);
        Fan(mesh, inner, plate * color);
        if (level <= 0f) return;
        // The fill rises inside the rim from the bottom.
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var point in inner) { bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y); }
        Below(inner, cut, Mathf.Lerp(bottom, top, level));
        Fan(mesh, cut, inside * color);
    }

    // Its corners at this share of its size about the middle.
    private void Outline(Rect rect, float share, List<Vector2> into)
    {
        into.Clear();
        Vector2 middle = rect.center;
        float halfWidth = rect.width * .5f * share, halfHeight = rect.height * .5f * share, slant = lean * .5f * share;
        if (wedge)
        {
            into.Add(middle + new Vector2(0f, halfHeight));
            into.Add(middle + new Vector2(halfWidth, -halfHeight));
            into.Add(middle + new Vector2(-halfWidth, -halfHeight));
            return;
        }
        into.Add(middle + new Vector2(slant, halfHeight));
        into.Add(middle + new Vector2(halfWidth, 0f));
        into.Add(middle + new Vector2(-slant, -halfHeight));
        into.Add(middle + new Vector2(-halfWidth, 0f));
    }

    // What of a convex shape lies at or below y.
    private static void Below(List<Vector2> shape, List<Vector2> kept, float y)
    {
        kept.Clear();
        for (int i = 0; i < shape.Count; i++)
        {
            Vector2 a = shape[i], b = shape[(i + 1) % shape.Count];
            bool aIn = a.y <= y, bIn = b.y <= y;
            if (aIn) kept.Add(a);
            if (aIn != bIn) kept.Add(Vector2.Lerp(a, b, (y - a.y) / (b.y - a.y)));
        }
    }

    private static void Fan(VertexHelper mesh, List<Vector2> shape, Color tint)
    {
        if (shape.Count < 3) return;
        int first = mesh.currentVertCount;
        Color32 color = tint;
        foreach (var point in shape) mesh.AddVert(point, color, Vector4.zero);
        for (int i = 1; i < shape.Count - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
    }
}
