using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where one target's damage numbers stand on screen: just outside its body's outline at the layout's height, on the
/// side away from the crosshair (the right while the target sits in the middle). The side flips only once the body has
/// clearly crossed to the other half, or when the numbers would leave the screen. After a finishing blow, or once the
/// target is gone, the body stays where it last stood, so the number does not ride the falling corpse.
/// </summary>
public sealed class DamageNumberAnchor
{
    private const float ScreenEdge = 16f, FlipBand = 24f;

    private Transform follow;
    private readonly List<Collider> colliders = new List<Collider>();
    private Bounds body, held;
    private bool frozen;

    /// <summary>+1 while the numbers stand right of the body, -1 left of it, 0 before the first placement.</summary>
    public int Side { get; private set; }

    public void Begin(Component target)
    {
        follow = target.transform;
        target.GetComponentsInChildren(colliders);
        frozen = false;
        Side = 0;
        body = Body();
    }

    public void Release()
    {
        follow = null;
        colliders.Clear();
    }

    /// <summary>Stops following: the body stays where it stands now.</summary>
    public void Freeze()
    {
        if (frozen) return;
        held = follow != null ? Body() : body;
        frozen = true;
    }

    /// <summary>Follows the target again if it still exists (a practice target that came back).</summary>
    public void Thaw()
    {
        if (follow != null) frozen = false;
    }

    /// <summary>
    /// The spot a number's near edge stands on (canvas px from the centre, which is the crosshair) and the distance to
    /// it. room is the widest number's width, to keep it on screen. False while the target is behind the camera.
    /// </summary>
    public bool Place(Camera view, RectTransform canvas, DamageNumberLayout layout, float room, out Vector2 spot, out float distance)
    {
        spot = default;
        distance = 0f;
        if (view == null) return false;
        if (!frozen && follow == null) Freeze(); // destroyed by the blow: finish where it stood
        Bounds shape = frozen ? held : (body = Body());

        // The body's outline on screen: its bounds' corners, those in front of the camera.
        Vector3 min = shape.min, max = shape.max;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            if (!ToCanvas(view, canvas, corner, out Vector2 local)) continue;
            left = Mathf.Min(left, local.x);
            right = Mathf.Max(right, local.x);
        }
        var stand = new Vector3(shape.center.x, shape.min.y + shape.size.y * layout.height, shape.center.z);
        if (left > right || !ToCanvas(view, canvas, stand, out Vector2 centre)) return false;

        // Away from the crosshair, keeping the current side until the body is clearly over on the other half.
        float middle = (left + right) * .5f, band = Mathf.Max(FlipBand, (right - left) * .25f);
        if (Side == 0) Side = middle < -band ? -1 : 1;
        else if (Side > 0 && middle < -band) Side = -1;
        else if (Side < 0 && middle > band) Side = 1;
        // A side that would push the numbers off the screen gives way to the other one when that one fits.
        float half = canvas.rect.width * .5f;
        bool rightFits = right + layout.gap + room <= half - ScreenEdge, leftFits = left - layout.gap - room >= ScreenEdge - half;
        if (Side > 0 && !rightFits && leftFits) Side = -1;
        else if (Side < 0 && !leftFits && rightFits) Side = 1;

        float x = Side > 0 ? Mathf.Min(right + layout.gap, half - ScreenEdge - room) : Mathf.Max(left - layout.gap, ScreenEdge - half + room);
        spot = new Vector2(x, centre.y);
        distance = Vector3.Distance(view.transform.position, stand);
        return true;
    }

    private Bounds Body()
    {
        bool found = false;
        Bounds bounds = default;
        foreach (var collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        if (found) return bounds;
        return follow != null ? new Bounds(follow.position + Vector3.up, new Vector3(1f, 2f, 1f)) : body;
    }

    private static bool ToCanvas(Camera view, RectTransform canvas, Vector3 world, out Vector2 local)
    {
        local = default;
        Vector3 screen = view.WorldToScreenPoint(world);
        return screen.z > view.nearClipPlane && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out local);
    }
}
