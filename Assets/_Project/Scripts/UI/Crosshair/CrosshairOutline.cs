using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An even border for crosshair bars and discs: each quad is copied, grown by the width on every side and drawn
/// underneath in the outline colour. Unity's Outline shifts four copies diagonally instead, which at a few pixels splits
/// into shadows beside a thin line and runs past its ends. <see cref="DynamicCrosshairView"/> keeps Unity's Outline for
/// sprites with a shape of their own, where growing the quad would only scale the drawing.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrosshairOutline : BaseMeshEffect
{
    private static readonly List<UIVertex> mesh = new List<UIVertex>(), border = new List<UIVertex>();
    private float width;
    private Color color;

    public void Set(float width, Color color)
    {
        if (Mathf.Approximately(this.width, width) && this.color == color) return;
        this.width = width;
        this.color = color;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || width <= 0f) return;
        mesh.Clear();
        border.Clear();
        vh.GetUIVertexStream(mesh);
        // Two triangles per quad; each corner moves away from the quad's centre.
        for (int quad = 0; quad + 6 <= mesh.Count; quad += 6)
        {
            Vector3 min = mesh[quad].position, max = min;
            for (int i = 1; i < 6; i++)
            {
                min = Vector3.Min(min, mesh[quad + i].position);
                max = Vector3.Max(max, mesh[quad + i].position);
            }
            Vector3 centre = (min + max) * .5f;
            for (int i = 0; i < 6; i++)
            {
                var vertex = mesh[quad + i];
                vertex.position.x += (vertex.position.x < centre.x ? -1f : 1f) * width;
                vertex.position.y += (vertex.position.y < centre.y ? -1f : 1f) * width;
                var tint = color;
                tint.a *= vertex.color.a / 255f; // fades with the crosshair's own opacity
                vertex.color = tint;
                border.Add(vertex);
            }
        }
        border.AddRange(mesh); // the border goes first, so it draws underneath
        vh.Clear();
        vh.AddUIVertexTriangleStream(border);
    }
}
