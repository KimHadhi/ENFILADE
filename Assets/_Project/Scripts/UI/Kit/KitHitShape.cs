using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lets a graphic take the pointer only where it is drawn (KitHit.Allows): KitStyle.Shape puts it on the kit's cut-corner
/// images, whose cut corners took clicks like the square they were cut from. KitSkew does the same for a leaned graphic by
/// itself. It works while its own graphic is a raycast target. A raycast asks the filters of the graphic it tries and of
/// each parent, so what lies under the graphic is held to the same outline inside the graphic's own square; a child
/// reaching out past that square is left alone (KitHit.Allows).
/// </summary>
[DisallowMultipleComponent]
public sealed class KitHitShape : MonoBehaviour, ICanvasRaycastFilter
{
    private Graphic graphic;

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (!isActiveAndEnabled) return true;
        if (graphic == null && !TryGetComponent(out graphic)) return true;
        return !graphic.raycastTarget || KitHit.Allows(graphic, screenPoint, eventCamera);
    }
}
