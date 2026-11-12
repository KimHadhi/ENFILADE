using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A cover over a kill feed row in its plate's own shape (KillRowPlate: both ends slanted by the same shear), from one
/// point along it to another: the takedown flair's veil, which lifts off the row its right end first (KillFlairFx). It
/// lies over the names, which the plate itself can't.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KillRowCover : MaskableGraphic
{
    // KillRowPlate's Shear: how far the plate's top runs right of its bottom at each end (px).
    private const float Shear = 9f;
    private float from, to = 1f;

    /// <summary>Covers the row from from to to (0-1 along it, left to right); to at or under from covers nothing.</summary>
    public void Cover(float from, float to)
    {
        from = Mathf.Clamp01(from); to = Mathf.Clamp01(to);
        if (Mathf.Abs(from - this.from) < .002f && Mathf.Abs(to - this.to) < .002f) return;
        this.from = from; this.to = to;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (to <= from || rect.width <= Shear || rect.height <= 0f) return;
        float span = rect.width - Shear, x0 = rect.xMin + from * span, x1 = rect.xMin + to * span;
        Color32 c = color;
        mesh.AddVert(new Vector2(x0, rect.yMin), c, Vector2.zero);
        mesh.AddVert(new Vector2(x0 + Shear, rect.yMax), c, Vector2.zero);
        mesh.AddVert(new Vector2(x1 + Shear, rect.yMax), c, Vector2.zero);
        mesh.AddVert(new Vector2(x1, rect.yMin), c, Vector2.zero);
        mesh.AddTriangle(0, 1, 2);
        mesh.AddTriangle(2, 3, 0);
    }
}
