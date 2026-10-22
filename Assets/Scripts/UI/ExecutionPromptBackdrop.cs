using UnityEngine;
using UnityEngine.UI;

/// <summary>A soft blood-red glow without an extra raster asset or material.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ExecutionPromptBackdrop : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect rect = GetPixelAdjustedRect();
        const int columns = 24, rows = 8;
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float u = (float)x / columns, v = (float)y / rows;
            float alpha = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(u * Mathf.PI)), 1.6f) * Mathf.Pow(Mathf.Clamp01(Mathf.Sin(v * Mathf.PI)), 1.5f) * .85f;
            vh.AddVert(new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v), new Color(.65f, .015f, .035f, alpha), new Vector2(u, v));
            if (x == columns || y == rows) continue;
            int a = y * (columns + 1) + x;
            vh.AddTriangle(a, a + columns + 1, a + 1); vh.AddTriangle(a + 1, a + columns + 1, a + columns + 2);
        }
    }
}
