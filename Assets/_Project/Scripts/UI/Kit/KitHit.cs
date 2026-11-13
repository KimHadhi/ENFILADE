using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Where a menu piece takes the pointer: exactly where it is drawn. A uGUI raycast tests each target's square rect, but
/// the kit draws slanted plates (KitSkew moves a graphic's vertices into a parallelogram), cut corners (KitShapes.Chamfer)
/// and hovered pieces that slide aside (KitHover), so near a slanted edge the pointer picked the neighbour or missed
/// what it sat on, and a hovered piece slid out from under a pointer at its left edge (user, 2026-11-12: "옷장이나 설정창
/// 슬라이더나 마우스 커서대로 선택이 잘 안 됨").
/// The kit's raycast filters (KitSkew itself, KitHitShape on cut-corner images) ask <see cref="Allows"/>: the point is
/// taken into the graphic's own plane the way the raycast itself takes it (its rotation, the lobby wings' 3D turn) and
/// tested against the drawn outline. A raycast only asks a filter once the point is inside the target's square, widened by
/// its raycastPadding, so <see cref="Pad"/> widens that square over whatever the outline reaches past it: a lean's
/// overhangs and, while its piece leans on a hover, the place it rests. The kit owns raycastPadding on what it pads.
/// The debug view (console /hits, KitHitView) draws the same outlines (<see cref="Outline(Graphic, List{Vector2})"/>).
/// </summary>
public static class KitHit
{
    /// <summary>
    /// Whether <paramref name="screenPoint"/> may hit <paramref name="graphic"/> or what lies under it: yes on the graphic
    /// as it is drawn, or where its piece rests while a hover leans it (the place it left keeps the pointer, so the hover
    /// never slides out from under it); no in the parts of its own raycast square the drawing leaves out (a lean's cut-off
    /// corners, a cut corner). Past that square the point was found on a child reaching out of it, which is not this
    /// graphic's to refuse.
    /// </summary>
    public static bool Allows(Graphic graphic, Vector2 screenPoint, Camera eventCamera)
    {
        if (graphic == null) return true;
        var rect = graphic.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out var local)) return true;
        var box = rect.rect;
        float slant = SlantOf(graphic);
        var cut = CutOf(graphic, box);
        if (Inside(local, box, slant, cut)) return true;
        var lean = LeanOf(graphic);
        if (lean != Vector2.zero && Inside(local + lean, box, slant, cut)) return true;
        var pad = graphic.raycastPadding;
        return local.x < box.xMin + pad.x || local.x > box.xMax - pad.z || local.y < box.yMin + pad.y || local.y > box.yMax - pad.w;
    }

    /// <summary>
    /// Whether a point in a graphic's own space lies inside its drawn outline: its rect, its top-right and bottom-left
    /// corners cut by <paramref name="cut"/> (along the width, along the height; zero for none), then leaned the way
    /// KitSkew leans it, the top edge <paramref name="slant"/> px right of the bottom one.
    /// </summary>
    public static bool Inside(Vector2 point, Rect box, float slant, Vector2 cut)
    {
        if (box.width <= 0f || box.height <= 0f) return false;
        // KitSkew moved each vertex right by k * slant, k running from -0.5 at the bottom edge to +0.5 at the top: undone.
        float k = (point.y - box.center.y) / box.height;
        if (k < -.5f || k > .5f) return false;
        float x = point.x - k * slant;
        if (x < box.xMin || x > box.xMax) return false;
        if (cut.x <= 0f || cut.y <= 0f) return true;
        // Past the diagonal from (xMax - cut.x, yMax) to (xMax, yMax - cut.y) is the cut top-right corner; the bottom-left
        // one mirrors it.
        return (box.xMax - x) / cut.x + (box.yMax - point.y) / cut.y >= 1f
            && (x - box.xMin) / cut.x + (point.y - box.yMin) / cut.y >= 1f;
    }

    /// <summary>An outline as <see cref="Inside"/> tests it, moved by <paramref name="offset"/>, corners in order round it.</summary>
    public static void Outline(Rect box, float slant, Vector2 cut, Vector2 offset, List<Vector2> into)
    {
        into.Clear();
        if (cut.x > 0f && cut.y > 0f)
        {
            Corner(box.xMin + cut.x, box.yMin);
            Corner(box.xMax, box.yMin);
            Corner(box.xMax, box.yMax - cut.y);
            Corner(box.xMax - cut.x, box.yMax);
            Corner(box.xMin, box.yMax);
            Corner(box.xMin, box.yMin + cut.y);
        }
        else
        {
            Corner(box.xMin, box.yMin);
            Corner(box.xMax, box.yMin);
            Corner(box.xMax, box.yMax);
            Corner(box.xMin, box.yMax);
        }

        void Corner(float x, float y)
        {
            float k = box.height > 0f ? (y - box.center.y) / box.height : 0f;
            into.Add(new Vector2(x + k * slant, y) + offset);
        }
    }

    /// <summary>How far <paramref name="graphic"/>'s KitSkew leans it (px), unless that is <paramref name="without"/>; 0 without one.</summary>
    public static float SlantOf(Graphic graphic, KitSkew without = null) =>
        graphic != null && graphic.TryGetComponent(out KitSkew skew) && skew != without && skew.isActiveAndEnabled ? skew.Slant : 0f;

    /// <summary>
    /// How much of the corners a cut-corner image (KitShapes.Chamfer, sliced) cuts in its own space, along its width and
    /// its height; zero for anything else. A rect too small for the sprite's border squeezes the border, and the cut with
    /// it, as Image's sliced mesh does.
    /// </summary>
    public static Vector2 CutOf(Graphic graphic, Rect box)
    {
        if (!(graphic is Image image) || image.type != Image.Type.Sliced || image.sprite == null || image.sprite != KitShapes.Chamfer)
            return Vector2.zero;
        float perUnit = image.pixelsPerUnit * image.pixelsPerUnitMultiplier;
        if (perUnit <= 0f) return Vector2.zero;
        var border = image.sprite.border / perUnit;
        float cut = KitShapes.ChamferCut / perUnit;
        float across = border.x + border.z, up = border.y + border.w;
        float squeezeX = across > 0f && box.width < across ? box.width / across : 1f;
        float squeezeY = up > 0f && box.height < up ? box.height / up : 1f;
        return new Vector2(cut * squeezeX, cut * squeezeY);
    }

    /// <summary>
    /// How far the hover over <paramref name="graphic"/>'s piece has moved it from where it rests, in the graphic's own
    /// space (zero at rest): a point plus this lies on the piece as it rests.
    /// </summary>
    public static Vector2 LeanOf(Graphic graphic)
    {
        if (graphic == null) return Vector2.zero;
        var hover = graphic.GetComponentInParent<KitHover>();
        if (hover == null || hover.Shift == Vector2.zero) return Vector2.zero;
        var parent = hover.transform.parent;
        Vector3 moved = parent != null ? parent.TransformVector(hover.Shift) : (Vector3)hover.Shift;
        return graphic.rectTransform.InverseTransformVector(moved);
    }

    /// <summary>
    /// Widens the square a raycast tries first on <paramref name="graphic"/> over all its outline covers: half its lean
    /// on either side (the parallelogram's overhangs) and, while a hover leans its piece, back over where the piece rests.
    /// <paramref name="without"/> is a KitSkew going away (its own OnDisable), left out.
    /// </summary>
    public static void Pad(Graphic graphic, KitSkew without = null)
    {
        if (graphic == null) return;
        float side = Mathf.Abs(SlantOf(graphic, without)) * .5f;
        var lean = LeanOf(graphic);
        // raycastPadding is left, bottom, right, top; positive shrinks the square, negative widens it. The place a piece
        // rests lies opposite the way it leaned.
        graphic.raycastPadding = new Vector4(-(side + Mathf.Max(0f, lean.x)), -Mathf.Max(0f, lean.y),
            -(side + Mathf.Max(0f, -lean.x)), -Mathf.Max(0f, -lean.y));
    }
}
