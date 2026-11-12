using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The lines and see-through fills the hit-shape view (console /hits, KitHitView) draws over the screen, in this graphic's
/// own space. Filled again every frame while the view is up.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KitHitLines : MaskableGraphic
{
    private readonly List<(Vector2 a, Vector2 b, Color color, float width)> lines = new List<(Vector2, Vector2, Color, float)>();
    private readonly List<(int first, int count, Color color)> fills = new List<(int, int, Color)>();
    private readonly List<Vector2> corners = new List<Vector2>();

    /// <summary>Forgets what was drawn (call Show after the new lines).</summary>
    public void Clear()
    {
        lines.Clear();
        fills.Clear();
        corners.Clear();
    }

    public void Line(Vector2 a, Vector2 b, Color color, float width) => lines.Add((a, b, color, width));

    /// <summary>A see-through fill over a convex outline, its corners in order round it.</summary>
    public void Fill(List<Vector2> outline, Color color)
    {
        if (outline.Count < 3) return;
        fills.Add((corners.Count, outline.Count, color));
        corners.AddRange(outline);
    }

    /// <summary>Draws what was added since Clear.</summary>
    public void Show() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        foreach (var (first, count, color) in fills)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i < count; i++) vh.AddVert(corners[first + i], color, Vector4.zero);
            for (int i = 1; i < count - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
        }
        foreach (var (a, b, color, width) in lines)
        {
            var along = b - a;
            float length = along.magnitude;
            if (length < .01f) continue;
            var side = new Vector2(-along.y, along.x) * (width * .5f / length);
            int start = vh.currentVertCount;
            vh.AddVert(a - side, color, Vector4.zero);
            vh.AddVert(a + side, color, Vector4.zero);
            vh.AddVert(b + side, color, Vector4.zero);
            vh.AddVert(b - side, color, Vector4.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
