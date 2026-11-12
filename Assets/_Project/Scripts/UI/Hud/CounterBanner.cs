using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shove counter's COUNTER (ShoveCounter, G&amp;B-3) in the HUD's own look: near-black, near-white and the one pink
/// (KitStyle). The user's pick of five mockups (2026-11-09, draft D "negative slash", exactly as drawn: "딱 D 그대로
/// 깔끔하게"): the world flashes negative for three frames, a pink slash with a white core cuts across the whole screen,
/// thick then thin, with a pink shard at its end and a smaller one past it (the HUD band's accent), and COUNTER, cut
/// along it, slams together from two halves in black under a near-white rim. As it leaves, the halves part again and
/// fade. Short and loud, never lingering; runs on real time, since the counter slows time. Built on first use, on this
/// player.
/// That is grade 0. A stronger counter shows more on top of it, never something else (user, 2026-11-10, of the second
/// drafts: "카운터 이펙트가 꼭 하나여야 할까? 지금도 맘에 들고 A도 B도 C도 맘에 드는데 … 카운터가 강화되면 더 이펙트가
/// 화려해져도"): grade 1 lays a black slanted plate under the word, hazard stripes running at its ends and a
/// rangefinder's ticks along its top, the word white on it and tightening from wide (draft A); grade 2 adds two thin
/// cuts either side of the slash, a pink afterimage of the word and nested diamonds thrown along the cut (draft B);
/// grade 3 adds focus lines rushing in from the screen's edge, a pink frame round the plate with a diamond at each end
/// and the whole of it slammed down from large, with a thin ring thrown out by the blow (draft C). ShoveCounter says
/// which. Sizes are the drafts' own, doubled from their 960-wide canvas.
/// Another word in another colour may borrow the whole of it. Since the special-moment picks of 2026-11-12 only one does:
/// the Knight's COVER (KnightCover; the user kept it as it is). The ultimate, the bounty, the duel and the reversal left
/// it for their own restrained looks (MomentHUD, MomentFx), the user having found one banner over every moment "다 똑같은
/// 화면 가리고 눈 아픈 이펙트만 무지성으로 반복"; so COUNTER is its own again.
/// </summary>
[DisallowMultipleComponent]
public sealed class CounterBanner : MonoBehaviour
{
    // The slash's lean (° counter-clockwise), how far it reaches each way, and its thickness from first to last (px at 1080p).
    private const float Lean = 16f, Reach = 1300f, ThickFirst = 46f, ThickLast = 5f;
    // Negative frames, the pink wash after them, the word meeting, leaving (real s).
    private const float InvertSeconds = .05f, TintSeconds = .25f, WordFrom = .03f, Meet = .14f, OutSeconds = .15f;
    private const float TintAlpha = .14f, WordSize = 220f, ShardAt = 760f;
    // How far apart the halves start and how far they part as they go (px), along and across the slash.
    private const float GapIn = 26f, GapOut = 40f;
    /// <summary>How many grades there are: 0, the plain one, to Grades - 1.</summary>
    public const int Grades = 4;
    // Grade 1's plate (px at 1080p): its size, how far its top edge leans right of its bottom one (the word's italic
    // lean), its hazard ends, the ticks over it and how wide the word's letters start apart.
    // WideSpacing is TMP's character spacing, hundredths of the type's size: 27 at 220 is about 60 px a letter.
    private const float PlateWidth = 1340f, PlateHeight = 250f, PlateSlant = 54f, CapWidth = 62f, TickHeight = 26f, WideSpacing = 27f;
    // Grade 2: how far the two thin cuts lie from the slash (clear of the plate and its ticks), and the diamonds thrown along it.
    private const float CutApart = 196f;
    private const int Diamonds = 8;
    // Grade 3: how large the plate and its word start; how far the frame stands out past the plate's ends and over its
    // edges; the diamonds at its ends; the ring's radius from first to last, how flat it lies and its line.
    private const float SlamFrom = 2.2f, FrameGrow = 24f, FrameRise = 20f, EndDiamond = 60f, RingFirst = 660f, RingLast = 1000f, RingFlat = .34f,
        RingLine = 10f;

    private GameObject root;
    private CanvasGroup group, wordGroup, plateGroup;
    private Image invert, tint, line, core, plate, frame, underline, cutA, cutB;
    private RectTransform topWord, bottomWord, body;
    private TextMeshProUGUI topText, bottomText, ghost;
    private HazardGraphic capLeft, capRight;
    private TickRowGraphic ticks;
    private FocusLinesGraphic focus;
    private CrosshairRing ring;
    private DiamondGraphic endLeft, endRight;
    private readonly DiamondGraphic[] diamonds = new DiamondGraphic[Diamonds];
    private UITriangle shard, chip;
    private Material invertMaterial, rim, plain;
    private float age = -1f, length = .85f;
    private int grade;
    // The word and the colour that stands for the pink this time (COUNTER in the HUD's pink unless Play says otherwise).
    private string word = "COUNTER";
    private Color accent = KitStyle.Accent;

    /// <summary>
    /// COUNTER now, for this long (real s), at this grade (0 the plain one; see the class's summary); or another word,
    /// in another colour where the pink would be.
    /// </summary>
    public void Play(float seconds, int grade = 0, string word = null, Color? accent = null)
    {
        Build();
        this.grade = Mathf.Clamp(grade, 0, Grades - 1);
        this.word = string.IsNullOrEmpty(word) ? "COUNTER" : word;
        this.accent = accent ?? KitStyle.Accent;
        length = Mathf.Max(.3f, seconds);
        age = 0f;
        Dress();
        root.SetActive(true);
        Pose(0f);
    }

    // What this grade wears. The word is black under a near-white rim over the world, and near-white on the plate.
    private void Dress()
    {
        bool plated = grade >= 1, doubled = grade >= 2, slammed = grade >= 3;
        var face = plated || rim == null ? plain : rim;
        topText.text = bottomText.text = ghost.text = word;
        // The pink of every part, or the colour asked for in its place (the wash keeps its own fading alpha).
        var wash = accent;
        wash.a = tint.color.a;
        tint.color = wash;
        focus.Accent = accent;
        line.color = cutA.color = frame.color = underline.color = accent;
        capLeft.color = capRight.color = accent;
        shard.color = chip.color = accent;
        foreach (var text in new[] { topText, bottomText })
        {
            text.color = plated ? KitStyle.Ice : KitStyle.Ink;
            if (face != null) text.fontSharedMaterial = face;
            text.characterSpacing = -4f;
        }
        plateGroup.gameObject.SetActive(plated);
        frame.enabled = endLeft.enabled = endRight.enabled = slammed;
        // The ticks stand on the plate's top edge, or on the frame's when it has one.
        ticks.rectTransform.anchoredPosition = new Vector2(PlateSlant * .5f + 6f, PlateHeight * .5f + 10f + (slammed ? FrameRise : 0f));
        cutA.enabled = cutB.enabled = doubled;
        ghost.gameObject.SetActive(doubled);
        foreach (var diamond in diamonds) diamond.gameObject.SetActive(doubled);
        focus.gameObject.SetActive(slammed);
        ring.gameObject.SetActive(slammed);
        body.localScale = Vector3.one;
    }

    private void LateUpdate()
    {
        if (age < 0f || root == null) return;
        age += Time.unscaledDeltaTime;
        if (age >= length) { age = -1f; root.SetActive(false); return; }
        Pose(age);
    }

    private static float Out(float x, float power) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), power);
    private static float In(float x, float power) => Mathf.Pow(Mathf.Clamp01(x), power);

    private void Pose(float t)
    {
        float leave = Mathf.Clamp01((t - (length - OutSeconds)) / OutSeconds);
        invert.enabled = invertMaterial != null && t < InvertSeconds;
        var wash = tint.color;
        wash.a = t < InvertSeconds ? 0f : TintAlpha * (1f - Mathf.Clamp01((t - InvertSeconds) / TintSeconds));
        tint.color = wash;

        // The slash: out from the middle at once, thick, thinning to a line.
        float reach = Reach * Out(t / .06f, 3f), thick = Mathf.Lerp(ThickFirst, ThickLast, Out(t / .3f, 2f)) * (1f - In(leave, 2f));
        line.rectTransform.sizeDelta = new Vector2(reach * 2f, thick);
        core.rectTransform.sizeDelta = new Vector2(reach * 2f, thick * .24f);

        // The word, two halves meeting along the slash, then parting as it goes.
        bool shown = t >= WordFrom;
        float meet = 1f - Out((t - WordFrom) / Meet, 3f), part = In(leave, 2f), gap = GapIn * meet + GapOut * part;
        wordGroup.alpha = shown ? 1f - part : 0f;
        topWord.anchoredPosition = new Vector2(-gap * 1.6f, gap * .6f - 6f);
        bottomWord.anchoredPosition = new Vector2(gap * 1.6f, -gap * .6f - 6f);
        shard.enabled = chip.enabled = shown;
        var shardColor = shard.color;
        shardColor.a = 1f - part;
        shard.color = chip.color = shardColor;
        group.alpha = 1f;
        if (grade >= 1) PosePlate(t, leave);
        if (grade >= 2) PoseCuts(t, leave, shown);
        if (grade >= 3) PoseSlam(t, leave);
    }

    // Grade 1: the plate opens along the slash from the middle, its stripes running outward, and the word tightens.
    private void PosePlate(float t, float leave)
    {
        float open = Out((t - .02f) / .1f, 3f), width = PlateWidth * open * (1f - .2f * In(leave, 2f));
        plate.rectTransform.sizeDelta = new Vector2(width, PlateHeight);
        frame.rectTransform.sizeDelta = new Vector2(width + FrameGrow * 2f, PlateHeight + FrameRise * 2f);
        float end = width * .5f + FrameGrow + EndDiamond * .6f;
        endLeft.rectTransform.anchoredPosition = new Vector2(-end, 0f);
        endRight.rectTransform.anchoredPosition = new Vector2(end, 0f);
        // The ends only once there is room for them, so they don't cross as it opens.
        bool ends = width > CapWidth * 3f;
        float at = Mathf.Max(0f, (width - CapWidth) * .5f);
        capLeft.enabled = capRight.enabled = ends;
        capLeft.rectTransform.anchoredPosition = new Vector2(-at, 0f);
        capRight.rectTransform.anchoredPosition = new Vector2(at, 0f);
        capLeft.Offset = -t * 520f;
        capRight.Offset = t * 520f;
        ticks.rectTransform.sizeDelta = new Vector2(width * .84f, TickHeight);
        underline.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, width - 260f), 8f);
        // Nothing of it before it has opened a little: its frame and ends have a size even at no width.
        plateGroup.alpha = Mathf.Clamp01(open * 4f) * (1f - In(leave, 2f));
        float spacing = Mathf.Lerp(WideSpacing, -4f, Out((t - .02f) / .14f, 3f));
        topText.characterSpacing = bottomText.characterSpacing = ghost.characterSpacing = spacing;
    }

    // Grade 2: a thin cut either side of the slash, one after the other; the word's pink afterimage closing on it and
    // going; diamonds thrown out both ways along the cut.
    private void PoseCuts(float t, float leave, bool shown)
    {
        float thin = 5f * (1f - In(leave, 2f));
        cutA.rectTransform.sizeDelta = new Vector2(Reach * 2f * Out((t - .02f) / .07f, 3f), thin);
        cutB.rectTransform.sizeDelta = new Vector2(Reach * 2f * Out((t - .05f) / .07f, 3f), thin);
        // Up and to the right of the word, closing on it as it fades.
        ghost.rectTransform.anchoredPosition = Vector2.Lerp(new Vector2(60f, 28f), new Vector2(36f, 16f), Out((t - WordFrom) / .2f, 2f));
        var afterimage = accent;
        afterimage.a = shown ? .6f * (1f - Mathf.Clamp01((t - .12f) / .32f)) : 0f;
        ghost.color = afterimage;
        float u = Mathf.Clamp01((t - .03f) / .5f);
        for (int i = 0; i < Diamonds; i++)
        {
            // The same throw every time: no randomness, so two counters in a row look like one move.
            float a = Mathf.Repeat(Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f, 1f), b = Mathf.Repeat(a * 7.31f, 1f);
            float along = (i % 2 == 0 ? 1f : -1f) * (430f + Out(u, 2f) * 860f * (.5f + b)), across = (a - .5f) * 300f * (.3f + u);
            var rect = diamonds[i].rectTransform;
            rect.anchoredPosition = new Vector2(along, across);
            rect.sizeDelta = Vector2.one * Mathf.Lerp(44f, 14f, u);
            var shade = i % 3 == 0 ? accent : KitStyle.Ice;
            shade.a = shown ? 1f - u : 0f;
            diamonds[i].color = shade;
        }
    }

    // Grade 3: focus lines rush in from the edges, the plate and its word come down from large, and the blow throws a ring.
    private void PoseSlam(float t, float leave)
    {
        focus.Inner = Mathf.Lerp(1.2f, .62f, Out(t / .2f, 3f));
        var lines = KitStyle.Ice;
        lines.a = .7f * (1f - In(leave, 2f));
        focus.color = lines;
        body.localScale = Vector3.one * Mathf.Lerp(SlamFrom, 1f, Out(t / .1f, 3f));
        float r = Mathf.Clamp01((t - .07f) / .33f);
        ring.Set(Mathf.Lerp(RingFirst, RingLast, Out(r, 2f)), Mathf.Lerp(RingLine, 2f, r), KitStyle.With(KitStyle.Ice, t < .07f ? 0f : 1f - r), 0f, Color.clear);
    }

    private void Build()
    {
        if (root != null) return;
        root = new GameObject("Counter banner", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 86;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;

        var shader = Resources.Load<Shader>("Combat/Shaders/UIInvert");
        if (shader != null) invertMaterial = new Material(shader) { name = "UI Invert (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        invert = Fill("Negative", root.transform, Color.white);
        invert.material = invertMaterial;
        invert.enabled = false;
        var wash = KitStyle.Accent; wash.a = 0f;
        tint = Fill("Pink wash", root.transform, wash);

        // Grade 3's focus lines, over the whole screen and under the slash.
        focus = HudShapes.Make<FocusLinesGraphic>("Focus lines", root.transform, KitStyle.Ice);
        focus.rectTransform.anchorMin = Vector2.zero; focus.rectTransform.anchorMax = Vector2.one;
        focus.rectTransform.offsetMin = focus.rectTransform.offsetMax = Vector2.zero;
        focus.Accent = KitStyle.Accent;

        var slash = Rect("Slash", root.transform);
        slash.localRotation = Quaternion.Euler(0f, 0f, Lean);
        line = Panel("Line", slash, KitStyle.Accent);
        var white = KitStyle.Ice; white.a = .95f;
        core = Panel("Core", slash, white);
        // Grade 2's thin cuts, one pink above the slash and one white below it.
        cutA = Panel("Cut above", slash, KitStyle.Accent);
        cutA.rectTransform.anchoredPosition = new Vector2(0f, CutApart);
        cutB = Panel("Cut below", slash, white);
        cutB.rectTransform.anchoredPosition = new Vector2(0f, -CutApart);
        // Grade 3's ring, flattened to lie along the slash: the crosshair's ring mesh, whose line stays thin at any size.
        ring = CrosshairRing.Create(slash);
        ring.rectTransform.localScale = new Vector3(1f, RingFlat, 1f);

        // What comes down as one at grade 3: the plate and everything on it.
        body = Rect("Body", slash);
        var plates = Rect("Plate", body);
        plateGroup = plates.gameObject.AddComponent<CanvasGroup>();
        plateGroup.blocksRaycasts = plateGroup.interactable = false;
        frame = Panel("Frame", plates, KitStyle.Accent);
        KitSkew.On(frame, PlateSlant * (PlateHeight + FrameRise * 2f) / PlateHeight);
        endLeft = HudShapes.Make<DiamondGraphic>("End left", plates, KitStyle.Ice);
        endRight = HudShapes.Make<DiamondGraphic>("End right", plates, KitStyle.Ice);
        endLeft.rectTransform.sizeDelta = endRight.rectTransform.sizeDelta = Vector2.one * EndDiamond;
        plate = Panel("Plate", plates, new Color(.024f, .024f, .031f, .95f));
        KitSkew.On(plate, PlateSlant);
        capLeft = HudShapes.Make<HazardGraphic>("Hazard left", plates, KitStyle.Accent);
        capRight = HudShapes.Make<HazardGraphic>("Hazard right", plates, KitStyle.Accent);
        foreach (var cap in new[] { capLeft, capRight })
        {
            cap.rectTransform.sizeDelta = new Vector2(CapWidth, PlateHeight);
            cap.Stripe = 16f;
            cap.Slant = PlateSlant;
        }
        // The ticks stand on the plate's top edge and the line lies in its lower part, each carried sideways as the
        // plate's lean carries that height.
        ticks = HudShapes.Make<TickRowGraphic>("Ticks", plates, KitStyle.Ice);
        ticks.rectTransform.pivot = new Vector2(.5f, 0f);
        ticks.Shape(27, 5, 3f, .5f);
        underline = Panel("Underline", plates, KitStyle.Accent);
        underline.rectTransform.anchoredPosition = new Vector2(-PlateSlant * .38f, -PlateHeight * .38f);

        // The word: the same text twice, each masked to its side of the slash. Under it, grade 2's afterimage, whole.
        var font = GunFeelSettings.Instance.scoreFont != null ? GunFeelSettings.Instance.scoreFont : CombatUIAssets.ResolveFont(null);
        ghost = Word("Afterimage", body, font);
        ghost.rectTransform.sizeDelta = new Vector2(1700f, 320f);
        var words = Rect("Word", body);
        wordGroup = words.gameObject.AddComponent<CanvasGroup>();
        wordGroup.blocksRaycasts = wordGroup.interactable = false;
        topWord = Half("Top half", words, font, true);
        bottomWord = Half("Bottom half", words, font, false);
        topText = topWord.GetComponent<TextMeshProUGUI>();
        bottomText = bottomWord.GetComponent<TextMeshProUGUI>();
        plain = topText.font != null ? topText.font.material : null;
        rim = CombatUIAssets.Outlined(topText.font, KitStyle.Ice, .2f);
        for (int i = 0; i < Diamonds; i++) diamonds[i] = HudShapes.Make<DiamondGraphic>("Diamond " + i, slash, KitStyle.Ice);

        // The pink shard at the slash's end and a small one past it, the HUD band's accent.
        shard = Triangle("Shard", slash, new Vector2(ShardAt + 32f, 0f), new Vector2(64f, 60f));
        shard.SetCorners(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f));
        chip = Triangle("Chip", slash, new Vector2(ShardAt + 97f, 29f), new Vector2(26f, 18f));
        chip.SetCorners(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.31f, 0f));
        root.SetActive(false);
    }

    private RectTransform Half(string name, RectTransform parent, TMP_FontAsset font, bool top)
    {
        // A mask on one side of the line (stencil, so it holds at the slash's lean).
        var side = Rect(name, parent);
        side.pivot = new Vector2(.5f, top ? 0f : 1f);
        side.sizeDelta = new Vector2(2800f, 420f);
        var shape = side.gameObject.AddComponent<Image>();
        shape.raycastTarget = false;
        side.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var text = Word("COUNTER", side, font);
        var rect = text.rectTransform;
        // Both halves measure from the slash: the bottom of the top mask, the top of the bottom one.
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, top ? 0f : 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(1700f, 320f);
        return rect;
    }

    // The word as it is set: Dress gives it its colour and rim for the grade.
    private static TextMeshProUGUI Word(string name, Transform parent, TMP_FontAsset font)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.raycastTarget = false;
        if (font != null) text.font = font;
        text.text = "COUNTER";
        text.fontSize = WordSize;
        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        text.characterSpacing = -4f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = KitStyle.Ink;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private static Image Panel(string name, Transform parent, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.raycastTarget = false;
        image.color = color;
        return image;
    }

    private static Image Fill(string name, Transform parent, Color color)
    {
        var image = Panel(name, parent, color);
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return image;
    }

    private static UITriangle Triangle(string name, Transform parent, Vector2 at, Vector2 size)
    {
        var triangle = new GameObject(name, typeof(RectTransform), typeof(UITriangle)).GetComponent<UITriangle>();
        triangle.transform.SetParent(parent, false);
        triangle.raycastTarget = false;
        triangle.color = KitStyle.Accent;
        triangle.rectTransform.anchoredPosition = at;
        triangle.rectTransform.sizeDelta = size;
        return triangle;
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root);
        if (invertMaterial != null) Destroy(invertMaterial);
    }
}
