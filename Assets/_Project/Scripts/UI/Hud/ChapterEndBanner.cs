using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The moment a chapter is escaped, before its closing card (user, 2026-11-10, of the chapter's end: "연출이 없어서
/// 아쉽고"; the card used to come straight up over the game). In the HUD's own look: the screen blinks white, black
/// bars close in from the top and bottom, a pink band sweeps across at the HUD's lean with 탈출 on it in near-black,
/// the letters wide and then tight, ticks along its foot and a diamond at each end, and under it a dark plate with the
/// chapter's name and who made it. Held a moment, then it lets go as the card comes up over it (CampaignRun shows the
/// card once this is nearly done). Every PC plays its own. Built on first use, on real time.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChapterEndBanner : MonoBehaviour
{
    // How long it takes to come in and to go (real s), and the white blink.
    private const float InSeconds = .3f, OutSeconds = .45f, BlinkSeconds = .22f, BlinkMost = .55f;
    // The band (px at 1080p): its lean (degrees), height and full reach either way; the bars; the word.
    private const float Lean = 7f, BandHeight = 170f, Reach = 1300f, BarHeight = 118f, WordSize = 150f, WideSpacing = 60f;
    private static ChapterEndBanner instance;

    private CanvasGroup group;
    private Image blink, top, bottom, band, plate;
    private TextMeshProUGUI word, title, line;
    private TickRowGraphic ticks;
    private DiamondGraphic left, right;
    private HazardGraphic capLeft, capRight;
    private float age = -1f, length = 2.6f;

    /// <summary>The chapter is escaped: its name, a line under it (who made it), and how long the banner stays (real s).</summary>
    public static void Play(string chapter, string under, float seconds)
    {
        if (!Application.isPlaying) return;
        if (instance == null)
        {
            var host = new GameObject("Chapter End", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<ChapterEndBanner>();
        }
        instance.Begin(chapter, under, seconds);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        // Under the closing card (85), which fades in over it.
        FeelUI.SetupOverlay(gameObject, 84);
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        blink = Fill("Blink", transform, Color.white);
        top = Bar("Top bar", true);
        bottom = Bar("Bottom bar", false);

        var slant = FeelUI.Rect("Slant", transform);
        slant.localRotation = Quaternion.Euler(0f, 0f, Lean);
        band = Panel("Band", slant, KitStyle.Accent);
        capLeft = HudShapes.Make<HazardGraphic>("Hazard left", slant, KitStyle.Ink);
        capRight = HudShapes.Make<HazardGraphic>("Hazard right", slant, KitStyle.Ink);
        capLeft.Stripe = capRight.Stripe = 18f;
        ticks = HudShapes.Make<TickRowGraphic>("Ticks", slant, KitStyle.Ink);
        ticks.rectTransform.pivot = new Vector2(.5f, 0f);
        ticks.rectTransform.anchoredPosition = new Vector2(0f, -BandHeight * .5f + 8f);
        ticks.Shape(41, 5, 3f, .5f);
        left = HudShapes.Make<DiamondGraphic>("Diamond left", slant, KitStyle.Ice);
        right = HudShapes.Make<DiamondGraphic>("Diamond right", slant, KitStyle.Ice);
        left.rectTransform.sizeDelta = right.rectTransform.sizeDelta = Vector2.one * 54f;
        var font = GunFeelSettings.Instance.scoreFont != null ? GunFeelSettings.Instance.scoreFont : CombatUIAssets.ResolveFont(null);
        word = Text("Word", slant, font, WordSize, KitStyle.Ink);
        word.text = Texts.Get("hud.chapter_end.word");
        word.fontStyle = FontStyles.Bold | FontStyles.Italic;
        word.rectTransform.sizeDelta = new Vector2(1600f, BandHeight);
        word.rectTransform.anchoredPosition = new Vector2(0f, 6f);

        plate = Panel("Plate", slant, new Color(.024f, .024f, .031f, .92f));
        plate.rectTransform.sizeDelta = new Vector2(900f, 96f);
        plate.rectTransform.anchoredPosition = new Vector2(40f, -BandHeight * .5f - 62f);
        KitSkew.On(plate, 22f);
        title = Text("Chapter", plate.transform, font, 34f, KitStyle.Ice);
        title.rectTransform.sizeDelta = new Vector2(840f, 44f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        line = Text("Who", plate.transform, font, 24f, KitStyle.With(KitStyle.Ice, .75f));
        line.rectTransform.sizeDelta = new Vector2(840f, 34f);
        line.rectTransform.anchoredPosition = new Vector2(0f, -22f);
        gameObject.SetActive(false);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Begin(string chapter, string under, float seconds)
    {
        title.text = chapter ?? "";
        line.text = under ?? "";
        length = Mathf.Max(InSeconds + OutSeconds + .4f, seconds);
        age = 0f;
        gameObject.SetActive(true);
        Pose(0f);
        KitSound.Play(KitSound.Cue.Escape);
    }

    private void LateUpdate()
    {
        if (age < 0f) return;
        age += Time.unscaledDeltaTime;
        if (age >= length) { age = -1f; gameObject.SetActive(false); return; }
        Pose(age);
    }

    private static float Out(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    private void Pose(float t)
    {
        float arrive = Out(t / InSeconds), leave = Mathf.Clamp01((t - (length - OutSeconds)) / OutSeconds);
        group.alpha = 1f - leave;
        blink.color = new Color(1f, 1f, 1f, BlinkMost * (1f - Mathf.Clamp01(t / BlinkSeconds)));
        top.rectTransform.sizeDelta = bottom.rectTransform.sizeDelta = new Vector2(0f, BarHeight * arrive);

        // The band opens along its lean from the middle; its ends, ticks and diamonds follow it out.
        float open = Out((t - .08f) / .26f), width = Reach * 2f * open;
        band.rectTransform.sizeDelta = new Vector2(width, BandHeight);
        bool ends = width > 400f;
        capLeft.enabled = capRight.enabled = left.enabled = right.enabled = ends;
        float at = Mathf.Min(width * .5f, 820f);
        capLeft.rectTransform.sizeDelta = capRight.rectTransform.sizeDelta = new Vector2(70f, BandHeight);
        capLeft.rectTransform.anchoredPosition = new Vector2(-at, 0f);
        capRight.rectTransform.anchoredPosition = new Vector2(at, 0f);
        capLeft.Offset = -t * 300f;
        capRight.Offset = t * 300f;
        left.rectTransform.anchoredPosition = new Vector2(-at - 80f, 0f);
        right.rectTransform.anchoredPosition = new Vector2(at + 80f, 0f);
        ticks.rectTransform.sizeDelta = new Vector2(Mathf.Min(width, 1500f), 22f);

        // The word tightens from wide as the band opens; the plate under it follows a beat later.
        float set = Out((t - .14f) / .3f);
        word.characterSpacing = Mathf.Lerp(WideSpacing, 6f, set);
        word.alpha = Mathf.Clamp01((t - .1f) / .12f);
        float under = Out((t - .3f) / .3f);
        plate.rectTransform.localScale = new Vector3(under, 1f, 1f);
        title.alpha = line.alpha = Mathf.Clamp01((t - .42f) / .2f);
    }

    private Image Bar(string name, bool up)
    {
        var bar = Panel(name, transform, KitStyle.Ink);
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, up ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, up ? 1f : 0f);
        rect.pivot = new Vector2(.5f, up ? 1f : 0f);
        rect.anchoredPosition = Vector2.zero;
        return bar;
    }

    private static Image Fill(string name, Transform parent, Color color)
    {
        var image = Panel(name, parent, color);
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        return image;
    }

    private static Image Panel(string name, Transform parent, Color color)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color)
    {
        var text = FeelUI.Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        if (font != null) text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.color = color;
        return text;
    }
}
