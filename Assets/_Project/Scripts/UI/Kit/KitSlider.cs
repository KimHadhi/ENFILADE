using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The menus' slider (SettingsWidgets.Slider: the settings rows, the wardrobe's body page). The value comes from the
/// pointer laid onto the track as it is drawn, so the knob stays under the cursor (user, 2026-11-12: "옷장이나 설정창
/// 슬라이더나 마우스 커서대로 선택이 잘 안 됨"). Pressing the track jumps the knob there; pressing the knob keeps the spot it
/// was taken by, so it doesn't twitch, and a thin knob gets a few px of grace either side. The knob leans (KitSkew), so at
/// the pointer's height its middle sits off its centre line by part of that lean: allowed for, so the part of the knob
/// at the cursor's height is what lands under it. Where the track lies is the handle's own range (its area), and the
/// track and its fill are drawn exactly that wide (SettingsWidgets), so the fill ends at the knob's middle at every value.
/// Taken into the track's own plane the way a raycast takes it, so a turned or tilted panel maps the same. A number typed
/// in the row's box still sets the value as before.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitSlider : Slider
{
    // Room either side of a thin knob that still counts as taking it (px).
    private const float Grace = 4f;
    // True while Slider's own press runs: its jump to the pointer is replaced by Follow.
    private bool pressing;
    // How far along the track the pointer held the knob from its middle at the pointer's height (px); 0 off the knob.
    private float held;

    private bool Horizontal => direction == Direction.LeftToRight || direction == Direction.RightToLeft;
    private bool Reversed => direction == Direction.RightToLeft || direction == Direction.TopToBottom;
    // The rect the value spans: the handle's area, else the fill's, as Slider itself takes it.
    private RectTransform Track => handleRect != null ? handleRect.parent as RectTransform : fillRect != null ? fillRect.parent as RectTransform : null;

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (!MayDrag(eventData)) return;
        pressing = true;
        try { base.OnPointerDown(eventData); } // selection and the pressed look; its own value change is dropped (Set)
        finally { pressing = false; }
        held = 0f;
        var camera = eventData.pressEventCamera;
        if (OnKnob(eventData.position, camera) && Along(eventData.position, camera, out float along, out float length))
            held = along - (Reversed ? 1f - normalizedValue : normalizedValue) * length;
        Follow(eventData.position, camera);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        if (MayDrag(eventData)) Follow(eventData.position, eventData.pressEventCamera);
    }

    protected override void Set(float input, bool sendCallback = true)
    {
        if (pressing) return;
        base.Set(input, sendCallback);
    }

    private bool MayDrag(PointerEventData eventData) => IsActive() && IsInteractable() && eventData.button == PointerEventData.InputButton.Left;

    // The value under the pointer: where it lies along the track, less where it took the knob.
    private void Follow(Vector2 screenPoint, Camera camera)
    {
        if (!Along(screenPoint, camera, out float along, out float length)) return;
        float t = Mathf.Clamp01((along - held) / length);
        normalizedValue = Reversed ? 1f - t : t;
    }

    // Where along the track (px from its start) a knob drawn under the pointer would have its middle, and the track's
    // length. False when the pointer can't be taken into the track's plane or the track has no length.
    private bool Along(Vector2 screenPoint, Camera camera, out float along, out float length)
    {
        along = length = 0f;
        var track = Track;
        if (track == null) return false;
        var box = track.rect;
        length = Horizontal ? box.width : box.height;
        if (length <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(track, screenPoint, camera, out var local)) return false;
        along = Horizontal ? local.x - Lean(track, local) - box.xMin : local.y - box.yMin;
        return true;
    }

    // How far right of its middle the leaned knob is drawn at this height (a point in the track's space): KitSkew moves
    // each of its points by k * slant, k from -0.5 at its bottom to +0.5 at its top.
    private float Lean(RectTransform track, Vector2 local)
    {
        if (handleRect == null || !handleRect.TryGetComponent(out Graphic knob)) return 0f;
        float slant = KitHit.SlantOf(knob);
        float height = handleRect.rect.height;
        if (slant == 0f || height <= 0f) return 0f;
        Vector2 middle = track.InverseTransformPoint(handleRect.TransformPoint(handleRect.rect.center));
        return Mathf.Clamp((local.y - middle.y) / height, -.5f, .5f) * slant;
    }

    // Whether the pointer is on the knob as drawn, give or take Grace across.
    private bool OnKnob(Vector2 screenPoint, Camera camera)
    {
        if (handleRect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(handleRect, screenPoint, camera, out var local))
            return false;
        var box = handleRect.rect;
        box.xMin -= Grace;
        box.xMax += Grace;
        float slant = handleRect.TryGetComponent(out Graphic knob) ? KitHit.SlantOf(knob) : 0f;
        return KitHit.Inside(local, box, slant, Vector2.zero);
    }
}
