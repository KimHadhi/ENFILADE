using UnityEngine;
using UnityEngine.UI;

// Draws slanted health segments without sprites or a custom shader.
// Optional loss trail: drawn from the current value up to Trailing, under the fill.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SlantedHealthGraphic : MaskableGraphic
{
    private float value, trailing, maximum, slant, gap;
    private double step;
    private bool blocks;
    private Color filled, empty, tickColor, trailColor, stripeColor, edgeColor;
    private bool stripesOn;
    private float stripePeriod = 10f, stripeOffset;
    private const float EdgeWidth = 2f;

    /// <summary>Optional trail decoration: lighter stripes (scrolling by Offset) and an edge line at the current value.</summary>
    public void SetTrailDecoration(bool stripes, float period, float offset, Color stripeTint, Color edgeTint)
    {
        stripesOn = stripes; stripePeriod = Mathf.Max(2f, period); stripeOffset = offset;
        stripeColor = stripeTint; edgeColor = edgeTint;
        SetVerticesDirty();
    }

    public void Configure(float current, float max, double tickStep, bool blockMode,
        float shear, float spacing, Color fill, Color background, Color ticks)
        => Configure(current, current, max, tickStep, blockMode, shear, spacing, fill, background, ticks, Color.clear);

    public void Configure(float current, float trail, float max, double tickStep, bool blockMode,
        float shear, float spacing, Color fill, Color background, Color ticks, Color trailTint)
    {
        value = current; trailing = Mathf.Max(current, trail); maximum = max; step = tickStep; blocks = blockMode;
        slant = shear; gap = spacing; filled = fill; empty = background; tickColor = ticks; trailColor = trailTint;
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
        bool showEdge = trailing > value + 0.0001f && trailColor.a > 0f && edgeColor.a > 0f;
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
                float trailRight = Mathf.Lerp(a, b, Mathf.Clamp01((float)((trailing - start) / (end - start))));
                float valueRight = Mathf.Lerp(a, b, Mathf.Clamp01((float)((value - start) / (end - start))));
                AddBand(mesh, rect, a, trailRight, shear, trailColor);
                AddStripes(mesh, rect, Mathf.Max(a, valueRight), trailRight, shear);
                AddBand(mesh, rect, a, valueRight, shear, filled);
                if (showEdge && value > start && value <= end) AddEdge(mesh, rect, valueRight, shear);
            }
        }
        else
        {
            AddBand(mesh, rect, 0f, span, shear, empty);
            float trailRight = span * Mathf.Clamp01(trailing / maximum), valueRight = span * Mathf.Clamp01(value / maximum);
            AddBand(mesh, rect, 0f, trailRight, shear, trailColor);
            AddStripes(mesh, rect, valueRight, trailRight, shear);
            AddBand(mesh, rect, 0f, valueRight, shear, filled);
            if (showEdge && value > 0f) AddEdge(mesh, rect, valueRight, shear);
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

    // Stripes share the bar's slant, so they read as part of the same parallelogram style.
    private void AddStripes(VertexHelper mesh, Rect rect, float from, float to, float shear)
    {
        if (!stripesOn || stripeColor.a <= 0f || trailColor.a <= 0f || to - from <= 0.01f) return;
        float period = stripePeriod, half = period * 0.5f;
        float phase = Mathf.Repeat(-stripeOffset, period); // growing offset slides stripes left, toward remaining health
        for (float s = phase + Mathf.Floor((from - phase) / period) * period; s < to; s += period)
            AddBand(mesh, rect, Mathf.Max(from, s), Mathf.Min(to, s + half), shear, stripeColor);
    }

    private void AddEdge(VertexHelper mesh, Rect rect, float x, float shear)
        => AddBand(mesh, rect, Mathf.Max(0f, x - EdgeWidth * 0.5f), x + EdgeWidth * 0.5f, shear, edgeColor);

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
