using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The empty screen behind the title menu's buttons: dragging across it turns the character (MainMenuPreview.Turn). The
/// hub's mirror uses it the same way to turn the player's own body (HubDressing), so it turns whichever wardrobe stage it
/// is given. Sits on a see-through image that fills the screen under every button, so the buttons still take their own
/// clicks.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public IWardrobeStage Preview;
    /// <summary>The first drag (the menu's hint fades once it has been tried).</summary>
    public event Action Dragged;

    public void OnBeginDrag(PointerEventData eventData) => Dragged?.Invoke();

    public void OnDrag(PointerEventData eventData)
    {
        // In 1080p pixels, so the same drag turns it as far at any resolution.
        Preview?.Turn(eventData.delta.x * 1080f / Mathf.Max(1, Screen.height), Time.unscaledDeltaTime);
    }

    public void OnEndDrag(PointerEventData eventData) => Preview?.Release();
}
