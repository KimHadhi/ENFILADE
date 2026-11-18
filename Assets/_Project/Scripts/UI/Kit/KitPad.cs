using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// A rect that says where in it the pointer is pressed and dragged: x and y, each 0 at its left and bottom edge to 1 at
/// its right and top (a colour board's square, a strip, a bar; ColourPanel). The pointer is laid onto the rect as it is
/// drawn, in the rect's own plane the way a raycast takes it, so a scaled or turned panel maps the same (KitSlider does
/// the same for a track). A drag that leaves the rect keeps giving its nearest edge, as a slider's knob stays at its end.
/// It needs a raycast target on its own object (the picture it is drawn with). A pad is a plain rectangle, never leaned
/// or cut, so the square a raycast tries is exactly the shape drawn (KitHit; the console's /hits shows it).
/// </summary>
[DisallowMultipleComponent]
public sealed class KitPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    /// <summary>The pointer is pressed or dragged here.</summary>
    public event Action<Vector2> Moved;
    /// <summary>The pointer that pressed it let go.</summary>
    public event Action Released;

    private RectTransform rect;

    /// <summary>The pointer is held on it now.</summary>
    public bool Held { get; private set; }

    // A press that moves a pixel is already a drag: no dead distance before the first move counts.
    public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        Held = true;
        Tell(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (Held && eventData.button == PointerEventData.InputButton.Left) Tell(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!Held || eventData.button != PointerEventData.InputButton.Left) return;
        Held = false;
        Released?.Invoke();
    }

    private void Tell(PointerEventData eventData)
    {
        if (rect == null) rect = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out var local)) return;
        var box = rect.rect;
        if (box.width <= 0f || box.height <= 0f) return;
        Moved?.Invoke(new Vector2(Mathf.Clamp01((local.x - box.xMin) / box.width), Mathf.Clamp01((local.y - box.yMin) / box.height)));
    }
}
