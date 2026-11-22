using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>A box of the HUD's menus that brightens under the pointer (HudMenu: rows, buttons, options). A button that
/// can't be pressed stays as it is.</summary>
[DisallowMultipleComponent]
public sealed class HudHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Graphic graphic;
    private Selectable selectable;
    private Color rest, over;
    private bool pointed;

    public static HudHover On(Graphic graphic, Color rest, Color over)
    {
        var hover = graphic.gameObject.TryGetComponent(out HudHover found) ? found : graphic.gameObject.AddComponent<HudHover>();
        hover.graphic = graphic;
        hover.Set(rest, over);
        return hover;
    }

    /// <summary>The colours at rest and under the pointer from now on (an option that became the chosen one).</summary>
    public void Set(Color atRest, Color underPointer)
    {
        rest = atRest;
        over = underPointer;
        Paint();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointed = true;
        Paint();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointed = false;
        Paint();
    }

    private void OnDisable()
    {
        pointed = false;
        Paint();
    }

    private void Paint()
    {
        if (graphic == null) return;
        if (selectable == null) graphic.TryGetComponent(out selectable);
        graphic.color = pointed && (selectable == null || selectable.interactable) ? over : rest;
    }
}
