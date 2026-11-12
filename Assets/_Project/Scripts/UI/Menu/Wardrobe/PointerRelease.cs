using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tells the wardrobe when the pointer is let go after pressing this object: a body slider (MainMenuWardrobe's body page)
/// is saved then, not on every step of its drag. It answers the release only, so the wheel and drags still reach the
/// slider and the list around it (an EventTrigger would take the wheel and the list would stop scrolling over a slider).
/// </summary>
[DisallowMultipleComponent]
public sealed class PointerRelease : MonoBehaviour, IPointerUpHandler
{
    private Action released;

    public static void On(GameObject target, Action released)
    {
        if (!target.TryGetComponent(out PointerRelease release)) release = target.AddComponent<PointerRelease>();
        release.released = released;
    }

    public void OnPointerUp(PointerEventData eventData) => released?.Invoke();
}
