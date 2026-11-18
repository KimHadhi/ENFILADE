using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// What a held card does, said beside its badge while the pointer is on it (the user, 2026-11-18: "Tab 메뉴에서 보유
/// 카드에 마우스 오버 시 능력 설명 툴팁 표시"): the card's name in its deck's colour (ClassLook.CardColour), whose card
/// it is and its level, then the same effect and why lines the pick screen shows, with the numbers of the level held.
/// One panel per canvas, made the first time a badge on that canvas is pointed at and moved from badge to badge; it
/// takes no pointer itself, so it can never come between the pointer and the badge that called it. PerkBadge puts the
/// hover on a badge (PerkBadge.Make with hover) and nothing else knows of this.
/// </summary>
[DisallowMultipleComponent]
public sealed class PerkTooltip : MonoBehaviour
{
    // Room inside the panel, between its lines, and between it and the badge (px); the type's sizes.
    private const float Pad = 14f, Gap = 6f, Away = 10f, Edge = 12f, NameSize = 21f, SmallSize = 14f, TextSize = 17f;

    private RectTransform root, canvas;
    private Image bar;
    private TextMeshProUGUI title, whose, level, effect, why;
    private RectTransform shownFor;

    /// <summary>Shows what <paramref name="perk"/> does at <paramref name="held"/> levels, beside <paramref name="badge"/>.</summary>
    public static void Show(RectTransform badge, PlayerPerks.Perk perk, int held)
    {
        if (badge == null || perk == null) return;
        var host = badge.GetComponentInParent<Canvas>();
        if (host == null) return;
        var top = host.rootCanvas;
        var tip = top.GetComponentInChildren<PerkTooltip>(true);
        if (tip == null) tip = Build((RectTransform)top.transform);
        tip.Fill(badge, perk, held);
    }

    /// <summary>The pointer left <paramref name="badge"/>: its panel goes, unless another badge has taken it since.</summary>
    public static void Hide(RectTransform badge)
    {
        if (badge == null) return;
        var host = badge.GetComponentInParent<Canvas>();
        var tip = host != null ? host.rootCanvas.GetComponentInChildren<PerkTooltip>(true) : null;
        if (tip != null && tip.shownFor == badge) tip.gameObject.SetActive(false);
    }

    /// <summary>
    /// Takes down whatever panel the canvas above <paramref name="anywhere"/> shows: for a screen that puts its pointer
    /// away while it stays up (the Tab board), where no "pointer left" comes for the badge it was on.
    /// </summary>
    public static void HideOn(Transform anywhere)
    {
        var host = anywhere != null ? anywhere.GetComponentInParent<Canvas>() : null;
        var tip = host != null ? host.rootCanvas.GetComponentInChildren<PerkTooltip>(true) : null;
        if (tip != null) tip.gameObject.SetActive(false);
    }

    private static PerkTooltip Build(RectTransform canvas)
    {
        var rect = FeelUI.Rect("Card Tip", canvas);
        rect.gameObject.layer = canvas.gameObject.layer;
        var tip = rect.gameObject.AddComponent<PerkTooltip>();
        tip.root = rect;
        tip.canvas = canvas;
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        KitStyle.Cut(KitStyle.Stretch("Plate", rect), KitStyle.With(KitStyle.Night, .97f));
        tip.bar = KitStyle.Bar("Bar", rect, Vector2.zero, new Vector2(4f, 10f), KitStyle.Text);
        tip.title = Line(rect, NameSize, TextAlignmentOptions.TopLeft, false);
        tip.level = Line(rect, SmallSize, TextAlignmentOptions.TopRight, false);
        tip.whose = Line(rect, SmallSize, TextAlignmentOptions.TopLeft, false);
        tip.effect = Line(rect, TextSize, TextAlignmentOptions.TopLeft, true);
        tip.effect.fontStyle = FontStyles.Normal;
        tip.why = Line(rect, SmallSize, TextAlignmentOptions.TopLeft, true);
        tip.why.fontStyle = FontStyles.Italic;
        tip.level.color = tip.whose.color = tip.why.color = KitStyle.Soft;
        return tip;
    }

    // A line hung from the panel's top left, flat type; wrapping ones take the panel's width.
    private static TextMeshProUGUI Line(RectTransform parent, float size, TextAlignmentOptions alignment, bool wraps)
    {
        var box = KitStyle.TopLeft("Line", parent, Vector2.zero, Vector2.zero);
        var label = KitStyle.Flat(box, "", size, alignment);
        label.textWrappingMode = wraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return label;
    }

    private void Fill(RectTransform badge, PlayerPerks.Perk perk, int held)
    {
        shownFor = badge;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        float width = Mathf.Max(200f, GameTables.Number(HudCamera.TableName, "Board", "tipWidth", 400f)), inner = width - Pad * 2f - 4f;
        Color colour = ClassLook.CardColour(perk.Class);
        bar.color = colour;
        title.text = perk.Name;
        title.color = colour;
        level.text = Texts.Format("perk.tooltip.level", held, Mathf.Max(1, perk.Levels));
        // Whose card: the class's name, or the word for everyone's.
        whose.text = string.IsNullOrEmpty(perk.Class) ? Texts.Get("perk.tooltip.common") : ClassLook.Title(perk.Class);
        effect.text = perk.Describe(held);
        string reason = perk.Reason;
        why.text = reason;
        why.gameObject.SetActive(!string.IsNullOrEmpty(reason));

        // Stacked from the top by what each line really takes at this width.
        float x = Pad + 4f, y = Pad;
        y = Place(title, x, y, inner, NameSize * 1.25f) + 2f;
        Place(level, x, Pad + 4f, inner, SmallSize * 1.3f);
        y = Place(whose, x, y, inner, SmallSize * 1.3f) + Gap;
        y = Place(effect, x, y, inner, effect.GetPreferredValues(effect.text, inner, 0f).y) + Gap;
        if (why.gameObject.activeSelf) y = Place(why, x, y, inner, why.GetPreferredValues(why.text, inner, 0f).y) + Gap;
        float height = y - Gap + Pad;
        root.sizeDelta = new Vector2(width, height);
        bar.rectTransform.sizeDelta = new Vector2(4f, height);

        // Over the badge, or under it where there is no room above; kept inside the screen's sides.
        var corners = new Vector3[4];
        badge.GetWorldCorners(corners);
        Vector2 low = canvas.InverseTransformPoint(corners[0]), high = canvas.InverseTransformPoint(corners[2]);
        Rect screen = canvas.rect;
        float middle = Mathf.Clamp((low.x + high.x) * .5f, screen.xMin + width * .5f + Edge, screen.xMax - width * .5f - Edge);
        bool above = high.y + Away + height <= screen.yMax - Edge;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, above ? 0f : 1f);
        root.anchoredPosition = new Vector2(middle - screen.center.x, (above ? high.y + Away : low.y - Away) - screen.center.y);
    }

    // Puts a line at (x, y from the top) with this box; returns where the next line may start.
    private static float Place(TextMeshProUGUI label, float x, float y, float width, float height)
    {
        var box = (RectTransform)label.rectTransform.parent;
        box.anchoredPosition = new Vector2(x, -y);
        box.sizeDelta = new Vector2(width, height);
        return y + height;
    }

    /// <summary>
    /// On a badge: the pointer coming onto it shows its card, leaving it hides it. The badge's drawn plate is what takes
    /// the pointer (its slant included: KitSkew), and a badge switched off or destroyed under the pointer takes its panel
    /// with it.
    /// </summary>
    public sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public PlayerPerks.Perk Perk;
        public int Level;

        public void OnPointerEnter(PointerEventData eventData) => Show((RectTransform)transform, Perk, Level);
        public void OnPointerExit(PointerEventData eventData) => Hide((RectTransform)transform);
        private void OnDisable() => Hide((RectTransform)transform);
    }
}
