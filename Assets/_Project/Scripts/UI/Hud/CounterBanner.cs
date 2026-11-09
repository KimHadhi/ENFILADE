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

    private GameObject root;
    private CanvasGroup group, wordGroup;
    private Image invert, tint, line, core;
    private RectTransform topWord, bottomWord;
    private UITriangle shard, chip;
    private Material invertMaterial;
    private float age = -1f, length = .85f;

    /// <summary>COUNTER now, for this long (real s).</summary>
    public void Play(float seconds)
    {
        Build();
        length = Mathf.Max(.3f, seconds);
        age = 0f;
        root.SetActive(true);
        Pose(0f);
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

        var slash = Rect("Slash", root.transform);
        slash.localRotation = Quaternion.Euler(0f, 0f, Lean);
        line = Panel("Line", slash, KitStyle.Accent);
        var white = KitStyle.Ice; white.a = .95f;
        core = Panel("Core", slash, white);

        // The word: the same text twice, each masked to its side of the slash.
        var font = GunFeelSettings.Instance.scoreFont != null ? GunFeelSettings.Instance.scoreFont : CombatUIAssets.ResolveFont(null);
        var words = Rect("Word", slash);
        wordGroup = words.gameObject.AddComponent<CanvasGroup>();
        wordGroup.blocksRaycasts = wordGroup.interactable = false;
        topWord = Half("Top half", words, font, true);
        bottomWord = Half("Bottom half", words, font, false);

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

        var text = new GameObject("COUNTER", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(side, false);
        text.raycastTarget = false;
        if (font != null) text.font = font;
        text.text = "COUNTER";
        text.fontSize = WordSize;
        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        text.characterSpacing = -4f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = KitStyle.Ink;
        var rim = CombatUIAssets.Outlined(text.font, KitStyle.Ice, .2f);
        if (rim != null) text.fontSharedMaterial = rim;
        var rect = text.rectTransform;
        // Both halves measure from the slash: the bottom of the top mask, the top of the bottom one.
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, top ? 0f : 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(1700f, 320f);
        return rect;
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
