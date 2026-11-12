using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How the objective bar looks (Hud.csv's Mission row); MissionHUD decides what it says. The sections stand on one line
/// and rise to their own heights: the ! tab tallest, the count box next, the objective text lowest. That line is the
/// progress, one segment per target while they fit, and the segment that just filled flashes. The count box carries the strip's dark on past its end, edged in the tab's
/// orange, its number big and the total small beside it, and the number punches when it moves. One palette: the tab's
/// orange, the strip's dark, white.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionBarLook : MonoBehaviour, ITableBound
{
    private const string LineName = "Progress Line";
    private const float TitlePad = 12f, EdgeWidth = 3f, Tail = 18f, SegmentGap = 2f;
    private const float FillSpeed = 1.8f, FlashSeconds = .45f, PunchSeconds = .28f;

    [Tooltip("The ! tab, the tallest section, rising from the bar's bottom line (px at 1080p).")]
    [SerializeField] private Vector2 badgeSize = new Vector2(50f, 54f);
    [Tooltip("Height of the objective text's strip, the lowest section (px).")]
    [SerializeField, Min(16)] private float barHeight = 36f;
    [Tooltip("Height of the count box, between the other two (px).")]
    [SerializeField, Min(16)] private float counterHeight = 43f;
    [SerializeField, Min(6)] private float symbolSize = 38f, titleSize = 19f, counterSize = 24f;
    [Tooltip("The bar's one colour: the ! tab, the count box's edge and the done part of the line.")]
    [SerializeField] private Color accent = new Color(.937f, .522f, .259f);
    [Tooltip("The line's undone part: the accent darkened to this share.")]
    [SerializeField, Range(0f, 1f)] private float trackShade = .32f;
    [Tooltip("The line's thickness, and the gap between it and the sections above (px).")]
    [SerializeField, Min(1)] private float lineHeight = 3f;
    [SerializeField, Min(0)] private float lineGap = 2f;
    [Tooltip("Targets up to this many get a segment each on the line; more draw it plain.")]
    [SerializeField, Min(0)] private int mostSegments = 30;
    [Tooltip("How much the count grows for a moment when it moves (share of its size).")]
    [SerializeField, Min(0)] private float punch = .22f;

    private RectTransform bar, badge, counterRect;
    private Image strip, counterBox, edge;
    private Graphic tab;
    private TMP_Text title, counter, symbol;
    private GameObject counterRoot;
    private MissionLineGraphic line;
    private AssaultBar assault;
    private string shownId;
    private int shownCurrent, segments;
    private float fill, targetFill, flash, punchClock = -1f, canvasWidth = 1920f;
    private bool numeric;

    /// <summary>
    /// How far under the bar's foot its own rows reach now (px): the progress line, or an assault's time row while one
    /// runs. MissionHUD keeps the key cap and hint under the bar clear of it.
    /// </summary>
    public float Below => Mathf.Max(lineGap + lineHeight, assault != null ? assault.Below : 0f);

    /// <summary>How far right of the bar's left edge it draws (px): the strip, the count box and the line's tail.</summary>
    public float Reach { get; private set; }

    /// <summary>Takes over the bar's parts. MissionHUD calls it once, before anything is shown.</summary>
    public void Init(TMP_Text title, TMP_Text counter, GameObject counterRoot, Image oldProgress)
    {
        bar = (RectTransform)transform;
        this.title = title; this.counter = counter; this.counterRoot = counterRoot;
        strip = GetComponent<Image>();
        badge = bar.Find("ObjectiveBadge") as RectTransform;
        if (badge != null) { tab = badge.GetComponent<Graphic>(); symbol = badge.GetComponentInChildren<TMP_Text>(true); }
        counterRect = (RectTransform)counterRoot.transform;
        counterBox = counterRoot.GetComponent<Image>();
        var divider = counterRect.Find("Divider");
        edge = divider != null ? divider.GetComponent<Image>() : null;
        // The thin track that sat inside the strip gives way to the line under the whole bar.
        if (oldProgress != null) oldProgress.transform.parent.gameObject.SetActive(false);
        line = Part<MissionLineGraphic>(LineName);
        var font = GunFeelSettings.Instance.scoreFont;
        if (font != null) counter.font = font;
        // The prefab clips the count with "…". TMP once could not find it here, warned and cut the count plainly (user,
        // 2026-11-11); ClippedOverflow keeps "…" wherever TMP will find it for this font and style.
        counter.overflowMode = CombatUIAssets.ClippedOverflow(counter);
        // The count's big number and small total are tags; the prefab's text had them switched off.
        counter.richText = true;
        Anchor();
        // A chapter's assault turns the bar into its own tape (D-3, memo 25).
        if (!TryGetComponent(out assault)) assault = gameObject.AddComponent<AssaultBar>();
        GameTables.Bind(HudCamera.TableName, "Mission", this);
    }

    public void OnTableApplied()
    {
        if (Application.isPlaying && bar != null) LayOut();
    }

    /// <summary>Shows an objective, or the wait for one (null), at the HUD's canvas width.</summary>
    public void Show(MissionObjective objective, float canvasWidth)
    {
        this.canvasWidth = canvasWidth;
        numeric = objective != null && objective.Display != MissionProgressDisplay.None;
        bool same = objective != null && objective.Id == shownId;
        if (!same) fill = 0f;
        else if (numeric && objective.Current > shownCurrent) { punchClock = 0f; flash = 1f; }
        shownId = objective != null ? objective.Id : null;
        shownCurrent = objective != null ? objective.Current : 0;
        targetFill = numeric ? objective.Progress : 0f;
        segments = numeric && objective.Display == MissionProgressDisplay.Count && objective.Target <= mostSegments ? objective.Target : 0;
        counterRoot.SetActive(numeric);
        counter.text = numeric ? CountText(objective) : "";
        LayOut();
    }

    /// <summary>Editor (ENFILADE > HUD): the bar with its sample texts and the count showing, the way Play draws it.</summary>
    public void LayOutForEditor(float canvasWidth)
    {
        this.canvasWidth = canvasWidth;
        numeric = true;
        counterRoot.SetActive(true);
        LayOut();
    }

    private void Update()
    {
        if (line == null) return;
        float dt = Time.unscaledDeltaTime;
        fill = Mathf.MoveTowards(fill, targetFill, dt * FillSpeed);
        flash = Mathf.MoveTowards(flash, 0f, dt / FlashSeconds);
        if (punchClock >= 0f)
        {
            punchClock += dt;
            float t = Mathf.Clamp01(punchClock / PunchSeconds);
            counter.rectTransform.localScale = Vector3.one * (1f + punch * (1f - t) * (1f - t));
            if (t >= 1f) punchClock = -1f;
        }
        DrawLine();
    }

    // The count big, the total small and dim beside it, so "5 / 17" reads as 5 first.
    private static string CountText(MissionObjective objective) => objective.Display == MissionProgressDisplay.Percent
        ? $"{Mathf.FloorToInt(objective.Progress * 100f)}<size=68%><alpha=#8C>%</size>"
        : $"{objective.Current}<size=68%><alpha=#8C> / {objective.Target}</size>";

    // Keeps the authored left and bottom edges: titles grow the bar only to the right, and the bottom edge is the line
    // the sections stand on, whatever height the table gives the strip.
    private void Anchor()
    {
        bar.anchoredPosition -= Vector2.Scale(bar.rect.size, bar.pivot);
        bar.pivot = Vector2.zero;
    }

    private void LayOut()
    {
        Color dark = strip != null ? strip.color : new Color(0f, 0f, 0f, .73f);
        if (badge != null)
        {
            badge.anchorMin = badge.anchorMax = badge.pivot = Vector2.zero;
            badge.anchoredPosition = Vector2.zero;
            badge.sizeDelta = badgeSize;
            if (tab != null) tab.color = accent;
            if (symbol != null) symbol.fontSize = symbolSize;
        }

        // The strip, as long as its title needs.
        title.fontSize = titleSize;
        float titleLeft = badgeSize.x + TitlePad;
        float longest = Mathf.Min(560f, Mathf.Max(260f, canvasWidth * .3f));
        float width = Mathf.Clamp(title.GetPreferredValues(title.text).x + titleLeft + TitlePad + 4f, 260f, longest);
        bar.sizeDelta = new Vector2(width, barHeight);
        title.rectTransform.offsetMin = new Vector2(titleLeft, 4f);
        title.rectTransform.offsetMax = new Vector2(-TitlePad, -4f);

        // The count box hangs past the strip's end on the same bottom line, in the strip's dark, edged in the accent.
        float counterWidth = 0f;
        if (numeric)
        {
            counter.fontSize = counterSize;
            counter.color = Color.white;
            counter.alignment = TextAlignmentOptions.Center;
            counterWidth = Mathf.Max(84f, counter.GetPreferredValues(counter.text).x + EdgeWidth + 28f);
            counterRect.anchorMin = counterRect.anchorMax = Vector2.right;
            counterRect.pivot = Vector2.zero;
            counterRect.anchoredPosition = Vector2.zero;
            counterRect.sizeDelta = new Vector2(counterWidth, counterHeight);
            counter.rectTransform.offsetMin = new Vector2(EdgeWidth + 4f, 0f);
            counter.rectTransform.offsetMax = new Vector2(-4f, 0f);
            if (counterBox != null) counterBox.color = dark;
            if (edge != null)
            {
                var rect = edge.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.up; rect.pivot = new Vector2(0f, .5f);
                rect.offsetMin = Vector2.zero; rect.offsetMax = new Vector2(EdgeWidth, 0f);
                edge.color = accent;
            }
        }

        // The line under the strip and the count box, with its tail past them.
        var lineRect = line.rectTransform;
        lineRect.anchorMin = Vector2.zero; lineRect.anchorMax = Vector2.right; lineRect.pivot = Vector2.up;
        lineRect.offsetMin = new Vector2(0f, -lineGap - lineHeight);
        lineRect.offsetMax = new Vector2(counterWidth + Tail, -lineGap);
        Reach = width + counterWidth + Tail;
        DrawLine();
    }

    private void DrawLine()
    {
        var track = new Color(accent.r * trackShade, accent.g * trackShade, accent.b * trackShade, .85f);
        line.Set(segments, fill, flash, Tail, SegmentGap, accent, track);
    }

    private T Part<T>(string name) where T : Component
    {
        var found = bar.Find(name);
        var rect = found != null ? (RectTransform)found : FeelUI.Rect(name, bar);
        rect.gameObject.layer = bar.gameObject.layer;
        return rect.TryGetComponent(out T part) ? part : rect.gameObject.AddComponent<T>();
    }
}
