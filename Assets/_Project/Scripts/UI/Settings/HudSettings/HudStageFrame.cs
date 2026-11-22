using HudAddons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// One part of the HUD as the HUD settings' stage lets it be touched (HudSettingsView): a box exactly where the part drew
/// itself (HudStage.TryRect), unseen until pointed at or chosen, then a thin white frame with the part's name over it.
/// A press chooses the part; a drag moves it, when it has a place to move. It takes no look of its own onto the HUD, so
/// what is seen is the HUD as the game draws it.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudStageFrame : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IBeginDragHandler, IDragHandler,
    IEndDragHandler
{
    private const float Room = 4f, Thick = 2f, Corner = 8f, TagHeight = 26f;

    private HudSettingsView view;
    private RectTransform rect, tag;
    private CanvasGroup look;
    private GameObject corners;
    private Canvas canvas;
    private bool pointed, dragged;

    public HudPart Part { get; private set; }

    public static HudStageFrame Make(Transform layer, HudSettingsView view, HudPart part, string name)
    {
        var rect = W.Rect("Part " + part, layer);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        W.Fill(rect, Color.clear);
        var frame = rect.gameObject.AddComponent<HudStageFrame>();
        frame.view = view;
        frame.rect = rect;
        frame.Part = part;
        var shown = W.Rect("Look", rect);
        KitStyle.Stretch(shown);
        frame.look = shown.gameObject.AddComponent<CanvasGroup>();
        frame.look.blocksRaycasts = false;
        Edge(shown, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, Thick));
        Edge(shown, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, Thick));
        Edge(shown, Vector2.zero, new Vector2(0f, 1f), new Vector2(Thick, 0f));
        Edge(shown, new Vector2(1f, 0f), Vector2.one, new Vector2(Thick, 0f));
        var handles = W.Rect("Corners", shown);
        KitStyle.Stretch(handles);
        foreach (var at in new[] { Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 1f), Vector2.one })
        {
            var corner = W.Rect("Corner", handles);
            corner.anchorMin = corner.anchorMax = at;
            corner.sizeDelta = new Vector2(Corner, Corner);
            W.Fill(corner, Color.white).raycastTarget = false;
        }
        frame.corners = handles.gameObject;
        // The name over the frame's left end, under it where there is no room above (Place).
        var tag = frame.tag = W.Rect("Name", shown);
        tag.anchorMin = tag.anchorMax = new Vector2(0f, 1f);
        tag.pivot = new Vector2(0f, 0f);
        HudMenu.Box(tag, Color.white, false, 4f);
        var label = W.Label(tag, name, 16f, HudMenu.Ink, TextAlignmentOptions.Center);
        KitStyle.Stretch(label.rectTransform);
        label.fontStyle = FontStyles.Bold;
        tag.sizeDelta = new Vector2(label.preferredWidth + 18f, TagHeight);
        return frame;
    }

    private static void Edge(RectTransform parent, Vector2 min, Vector2 max, Vector2 size)
    {
        var edge = W.Rect("Edge", parent);
        edge.anchorMin = min;
        edge.anchorMax = max;
        edge.pivot = new Vector2(min.x == max.x ? min.x : .5f, min.y == max.y ? min.y : .5f);
        edge.sizeDelta = size;
        edge.anchoredPosition = Vector2.zero;
        W.Fill(edge, Color.white).raycastTarget = false;
    }

    /// <summary>One frame: over its part, or gone while the part isn't drawn.</summary>
    public void Place(bool chosen)
    {
        bool drawn = HudStage.TryRect(Part, out Rect at);
        if (gameObject.activeSelf != drawn) gameObject.SetActive(drawn);
        if (!drawn) return;
        rect.anchoredPosition = new Vector2(at.x - Room, -(at.y - Room));
        rect.sizeDelta = new Vector2(at.width + Room * 2f, at.height + Room * 2f);
        look.alpha = chosen ? 1f : pointed ? .7f : 0f;
        if (corners.activeSelf != chosen) corners.SetActive(chosen);
        bool under = at.y < HudSettingsView.BarHeight + TagHeight + 8f;
        tag.anchorMin = tag.anchorMax = new Vector2(0f, under ? 0f : 1f);
        tag.pivot = new Vector2(0f, under ? 1f : 0f);
        tag.anchoredPosition = new Vector2(0f, under ? -4f : 4f);
    }

    public void OnPointerEnter(PointerEventData eventData) => pointed = true;

    public void OnPointerExit(PointerEventData eventData) => pointed = false;

    private void OnDisable() => pointed = dragged = false;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        view.Choose(Part);
    }

    public void OnBeginDrag(PointerEventData eventData) => dragged = eventData.button == PointerEventData.InputButton.Left;

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragged) return;
        if (canvas == null) canvas = GetComponentInParent<Canvas>().rootCanvas;
        // Down is down: the pointer's y runs up, a place's down.
        view.Move(new Vector2(eventData.delta.x, -eventData.delta.y) / Mathf.Max(1e-4f, canvas.scaleFactor), false);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragged) return;
        dragged = false;
        view.Move(Vector2.zero, true);
    }
}
