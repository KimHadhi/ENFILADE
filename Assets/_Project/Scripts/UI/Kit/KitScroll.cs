using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A list that scrolls, for the kit's pages whose rows are laid by hand from the top down (KitStyle.TopLeft into Rows): the
/// title screen's weapons, wardrobe, classes and records pages. It fills the area it is given (stretch it between a page's
/// heading and its foot and it fits any screen shape: 16:9, 16:10, 21:9, 4:3), widened at both sides by Gutter so slanted,
/// leaning and hovered rows are never cut at the sides; rows that go past its top or foot fade out over Fade and are cut
/// there, and only what shows takes clicks (RectMask2D filters the raycast, and each plate's own KitSkew shape still
/// decides within it, KitHit). The wheel, a drag and a thin bar at its right move it; the bar shows only when the rows
/// outgrow it. Offset keeps the place across a page rebuilt, and Show scrolls the least that brings a row wholly into view
/// (the row picked). The user's GM, 2026-11-13: the weapons page's plates ran down over the hint bar at the screen's foot
/// once the guns grew from 11 to 28 ("무기 쪽에 UI 이거 아래랑 겹치는데").
/// </summary>
[DisallowMultipleComponent]
public sealed class KitScroll : MonoBehaviour
{
    /// <summary>Room left of and right of the list area inside the cut (px): a slant, a lean and a hover's sweep fit in it.</summary>
    public const float Gutter = 28f;
    /// <summary>Room above the first row and below the last (px), and how far rows fade out at the cut edges.</summary>
    public const float Pad = 14f, Fade = 14f;
    // A wheel notch (px), as the wardrobe's lists have it, and the bar's width.
    private const float Step = 40f, BarWidth = 4f;

    /// <summary>Where the rows go: their own top left is its top left, x and y measured from there as on the page.</summary>
    public RectTransform Rows { get; private set; }
    public RectTransform Viewport { get; private set; }
    public RectTransform Content { get; private set; }
    private ScrollRect scroll;

    /// <summary>
    /// A scrolled list on <paramref name="parent"/> between the given anchors and offsets (the list area, before the Gutter
    /// is added at its sides). Fill Rows, then Fit with their height.
    /// </summary>
    public static KitScroll Make(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var root = FeelUI.Rect(name, parent);
        root.anchorMin = anchorMin;
        root.anchorMax = anchorMax;
        root.pivot = new Vector2(0f, 1f);
        root.offsetMin = new Vector2(offsetMin.x - Gutter, offsetMin.y);
        root.offsetMax = new Vector2(offsetMax.x + Gutter, offsetMax.y);
        var list = root.gameObject.AddComponent<KitScroll>();

        var viewport = KitStyle.Stretch("Viewport", root);
        var cut = viewport.gameObject.AddComponent<RectMask2D>();
        cut.softness = new Vector2Int(0, Mathf.RoundToInt(Fade));
        // The wheel and a drag work between the rows too.
        KitStyle.Fill(viewport, Color.clear).raycastTarget = true;
        var content = FeelUI.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var rows = FeelUI.Rect("Rows", content);
        rows.anchorMin = new Vector2(0f, 1f);
        rows.anchorMax = Vector2.one;
        rows.pivot = new Vector2(0f, 1f);
        rows.anchoredPosition = new Vector2(Gutter, -Pad);
        rows.sizeDelta = new Vector2(-2f * Gutter, 0f);

        // The bar: a faint track in the right gutter and a white thumb, both drawn straight (it is a readout, not a plate).
        var bar = FeelUI.Rect("Bar", root);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(.5f, .5f);
        bar.sizeDelta = new Vector2(BarWidth, -2f * Pad);
        bar.anchoredPosition = new Vector2(-Gutter * .5f, 0f);
        var track = KitStyle.Fill(bar, KitStyle.Faint);
        track.raycastTarget = true;
        var slide = KitStyle.Stretch("Sliding Area", bar);
        var thumb = FeelUI.Rect("Handle", slide);
        thumb.sizeDelta = Vector2.zero;
        var handle = KitStyle.Fill(thumb, KitStyle.With(KitStyle.Ice, .7f));
        handle.raycastTarget = true;
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = thumb;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.transition = Selectable.Transition.None;

        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = .06f;
        scroll.scrollSensitivity = Step;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        list.Viewport = viewport;
        list.Content = content;
        list.Rows = rows;
        list.scroll = scroll;
        return list;
    }

    /// <summary>The rows' height once they are in (from Rows' top to the last row's foot): the list can scroll that far.</summary>
    public void Fit(float rowsHeight)
    {
        Content.sizeDelta = new Vector2(0f, Mathf.Max(0f, rowsHeight) + 2f * Pad);
        Rows.sizeDelta = new Vector2(-2f * Gutter, Mathf.Max(0f, rowsHeight));
        Offset = Offset;
    }

    /// <summary>How far it is scrolled down (px, 0 at the top): kept across a rebuild of its page.</summary>
    public float Offset
    {
        get => Content != null ? Content.anchoredPosition.y : 0f;
        set
        {
            if (Content == null || Viewport == null) return;
            float most = Mathf.Max(0f, Content.rect.height - Viewport.rect.height);
            Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, Mathf.Clamp(value, 0f, most));
            if (scroll != null) scroll.StopMovement();
        }
    }

    /// <summary>
    /// Scrolls the least that brings <paramref name="row"/> (one of the rows, or anything under them) wholly into view, with
    /// <paramref name="margin"/> round it: the gun or class picked stays in sight.
    /// </summary>
    public void Show(RectTransform row, float margin = 12f)
    {
        if (row == null || Content == null || Viewport == null) return;
        var corners = new Vector3[4];
        row.GetWorldCorners(corners);
        float top = float.NegativeInfinity, foot = float.PositiveInfinity;
        foreach (var corner in corners)
        {
            float y = Content.InverseTransformPoint(corner).y;
            top = Mathf.Max(top, y);
            foot = Mathf.Min(foot, y);
        }
        // The content hangs from its top: below it is negative.
        float from = -top, to = -foot, view = Viewport.rect.height, at = Offset;
        if (from - margin < at) at = from - margin;
        else if (to + margin > at + view) at = to + margin - view;
        Offset = at;
    }
}
