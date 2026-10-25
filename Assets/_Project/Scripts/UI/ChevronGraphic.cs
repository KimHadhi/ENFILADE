using UnityEngine;
using UnityEngine.UI;

/// <summary>Filled '&lt;' or '&gt;' stroke spanning its rect. No sprite or shader required.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChevronGraphic : MaskableGraphic
{
    [SerializeField] private bool pointsRight;
    [SerializeField, Min(.5f)] private float thickness = 2.5f;

    public void Configure(bool right, float stroke)
    {
        pointsRight = right; thickness = stroke; raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float t = Mathf.Clamp(thickness, .5f, rect.width);
        float cy = rect.center.y;
        // '<' as two parallelogram arms meeting at the tip. Mirrored for '>'.
        Vector2 tip = P(rect, rect.xMin, cy), innerTip = P(rect, rect.xMin + t, cy);
        Vector2 topOuter = P(rect, rect.xMax, rect.yMax), topInner = P(rect, rect.xMax - t, rect.yMax);
        Vector2 bottomOuter = P(rect, rect.xMax, rect.yMin), bottomInner = P(rect, rect.xMax - t, rect.yMin);
        Color32 tint = color;
        int i = mesh.currentVertCount;
        mesh.AddVert(tip, tint, Vector2.zero);
        mesh.AddVert(topInner, tint, Vector2.zero);
        mesh.AddVert(topOuter, tint, Vector2.zero);
        mesh.AddVert(innerTip, tint, Vector2.zero);
        mesh.AddVert(bottomOuter, tint, Vector2.zero);
        mesh.AddVert(bottomInner, tint, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i + 2, i + 3, i);
        mesh.AddTriangle(i, i + 3, i + 4); mesh.AddTriangle(i + 4, i + 5, i);
    }

    private Vector2 P(Rect rect, float x, float y) => new Vector2(pointsRight ? rect.xMin + rect.xMax - x : x, y);
}
