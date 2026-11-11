using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tells the wardrobe (MainMenuWardrobe) when the pointer comes onto one of its tiles or choices and leaves it, so the
/// character can show that slot's pieces or try the choice on. Going away under the pointer (the page rebuilt after a
/// pick) counts as leaving. KitHover keeps the look of the hover; this only reports it.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardrobeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Action entered, left;
    private bool over;

    public static void On(GameObject target, Action entered, Action left)
    {
        if (!target.TryGetComponent(out WardrobeHover hover)) hover = target.AddComponent<WardrobeHover>();
        hover.entered = entered;
        hover.left = left;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        over = true;
        entered?.Invoke();
    }

    public void OnPointerExit(PointerEventData eventData) => Leave();

    private void OnDisable() => Leave();

    private void Leave()
    {
        if (!over) return;
        over = false;
        left?.Invoke();
    }
}
