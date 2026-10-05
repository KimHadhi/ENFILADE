using UnityEngine;
using UnityEngine.UI;

// Draws slanted health segments without sprites or a custom shader.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SlantedHealthGraphic : MaskableGraphic
{
    private float value, maximum, slant, gap;
    private double step;
    private bool blocks;
    private Color filled, empty, tickColor;

    public void Configure(float current, float max, double tickStep, bool blockMode,
        float shear, float spacing, Color fill, Color background, Color ticks)
    {
        value = current; maximum = max; step = tickStep; blocks = blockMode;
        slant = shear; gap = spacing; filled = fill; empty = background; tickColor = ticks;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (maximum <= 0f || step <= 0d || rect.width <= 0f || rect.height <= 0f) return;
        float shear = Mathf.Clamp(slant, 0f, rect.width * 0.25f);
        float span = rect.width - shear;
        if (blocks)
        {
            // Block count follows the configured HP threshold, not the compact tick limit.
            int count = Mathf.Max(1, Mathf.CeilToInt((float)(maximum / step)));
            for (int i = 0; i < count; i++)
            {
                double start = i * step;
                double end = System.Math.Min((i + 1) * step, maximum);
                float left = (float)(start / maximum * span);
                float right = (float)(end / maximum * span);
                float spacing = Mathf.Min(gap, (right - left) * 0.5f);
                float a = left + (i == 0 ? 0f : spacing * 0.5f);
                float b = right - (i == count - 1 ? 0f : spacing * 0.5f);
                AddBand(mesh, rect, a, b, shear, empty);
                float fraction = Mathf.Clamp01((float)((value - start) / (end - start)));
                AddBand(mesh, rect, a, Mathf.Lerp(a, b, fraction), shear, filled);
            }
        }
        else
        {
            AddBand(mesh, rect, 0f, span, shear, empty);
            AddBand(mesh, rect, 0f, span * Mathf.Clamp01(value / maximum), shear, filled);
            const float fraction = 0.38f;
            for (int i = 1; i < 20 && i * step < maximum; i++)
            {
                float x = (float)(i * step / maximum * span);
                float half = Mathf.Min(0.6f, Mathf.Min(x, span - x));
                AddQuad(mesh,
                    new Vector2(rect.xMin + x - half + shear * (1f - fraction), rect.yMax - rect.height * fraction),
                    new Vector2(rect.xMin + x - half + shear, rect.yMax),
                    new Vector2(rect.xMin + x + half + shear, rect.yMax),
                    new Vector2(rect.xMin + x + half + shear * (1f - fraction), rect.yMax - rect.height * fraction), tickColor);
            }
        }
    }

    private void AddBand(VertexHelper mesh, Rect rect, float left, float right, float shear, Color tint)
    {
        if (right <= left || tint.a <= 0f) return;
        AddQuad(mesh,
            new Vector2(rect.xMin + left, rect.yMin),
            new Vector2(rect.xMin + left + shear, rect.yMax),
            new Vector2(rect.xMin + right + shear, rect.yMax),
            new Vector2(rect.xMin + right, rect.yMin), tint);
    }

    private void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int index = mesh.currentVertCount;
        Color32 vertexColor = tint * color;
        mesh.AddVert(a, vertexColor, Vector2.zero);
        mesh.AddVert(b, vertexColor, Vector2.zero);
        mesh.AddVert(c, vertexColor, Vector2.zero);
        mesh.AddVert(d, vertexColor, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
