using UnityEngine;
using UnityEngine.UI;

/// <summary>A kill feed row's plate: slanted at the left end, shading from one colour to another, with a band of light that can cross it.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KillRowPlate : MaskableGraphic
{
    private const float Shear = 9f, SheenWidth = .22f;
    // Steps of the burn's fade along the plate: FeedFlame's pow(1 - smoothstep(cool), 1.6).
    private static readonly float[] BurnSteps = { 0f, .25f, .5f, .75f, 1f };
    private Color from, to, sheenTint, edgeTint, tint, traceTint;
    private float sheen = -1f, burn, edgeWidth, tintStrength, traceAt = -1f, traceLength, traceWidth;

    public void Paint(Color left, Color right)
    {
        from = left; to = right;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>
    /// A recent row's highlight (backlog 332): lines of tint along the plate's top and bottom and down its slanted end,
    /// width px thick, strongest at the killer's end. tint's alpha is how strong; 0 or no width draws none.
    /// </summary>
    public void Highlight(Color tint, float width)
    {
        width = tint.a > .004f ? Mathf.Max(0f, width) : 0f;
        if (Mathf.Abs(width - edgeWidth) < .01f && Mathf.Abs(tint.a - edgeTint.a) < .004f
            && (Vector4)tint == (Vector4)edgeTint) return;
        edgeTint = tint; edgeWidth = width;
        SetVerticesDirty();
    }

    /// <summary>
    /// px of the left end that the streak's fire burns, 0 for none. The plate clears there as the fire covers it, so when
    /// the row fades the plate's end never shows through the thinning fire and the two go as one.
    /// </summary>
    public void Burn(float length)
    {
        burn = Mathf.Max(0f, length);
        SetVerticesDirty();
    }

    /// <summary>The whole plate washed with a colour (a flair's flash), in its own slanted shape; strength 0 for none.</summary>
    public void Tint(Color colour, float strength)
    {
        strength = Mathf.Clamp01(strength);
        if (Mathf.Abs(strength - tintStrength) < .004f && (Vector4)colour == (Vector4)tint) return;
        tint = colour; tintStrength = strength;
        SetVerticesDirty();
    }

    /// <summary>
    /// A light running round the plate's own outline (a flair's trace: the ultimate's): its head at position (0-1 round
    /// the outline from the slanted left end's foot, up it, along the top, down the right end and back along the bottom),
    /// its tail length of the way round behind it, fading, width px thick inside the edge. Below 0: none.
    /// </summary>
    public void Trace(float position, float length, Color colour, float width)
    {
        traceAt = position; traceLength = Mathf.Clamp01(length); traceTint = colour; traceWidth = Mathf.Max(0f, width);
        SetVerticesDirty();
    }

    /// <summary>position is 0-1 along the plate; below 0 there is no band.</summary>
    public void Sheen(float position, Color tint)
    {
        sheen = position; sheenTint = tint;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float burnt = Mathf.Clamp01(burn / Mathf.Max(1f, rect.width - Shear));
        if (burnt > 0f)
            for (int i = 1; i < BurnSteps.Length; i++)
            {
                float a = BurnSteps[i - 1] * burnt, b = BurnSteps[i] * burnt;
                Band(mesh, rect, a, b, Burnt(a, BurnSteps[i - 1]), Burnt(b, BurnSteps[i]));
            }
        Band(mesh, rect, burnt, 1f, Color.Lerp(from, to, burnt), to);
        if (edgeWidth > 0f)
        {
            float line = Mathf.Min(edgeWidth, rect.height * .25f), span = Mathf.Max(1f, rect.width - Shear);
            Color strong = edgeTint, faint = edgeTint;
            faint.a *= .4f;
            Strip(mesh, rect, burnt, 1f, rect.yMax - line, rect.yMax, strong, faint);
            Strip(mesh, rect, burnt, 1f, rect.yMin, rect.yMin + line, strong, faint);
            // Down the slanted end only where it is the plate's own; a burning end belongs to the streak's fire.
            if (burnt <= 0f) Strip(mesh, rect, 0f, Mathf.Min(1f, line / span), rect.yMin, rect.yMax, strong, strong);
        }
        if (tintStrength > .004f)
        {
            Color wash = tint;
            wash.a *= tintStrength;
            Band(mesh, rect, burnt, 1f, wash, wash);
        }
        if (traceAt >= 0f && traceLength > 0f && traceWidth > 0f) Outline(mesh, rect);
        if (sheen < 0f) return;
        Color clear = sheenTint;
        clear.a = 0f;
        float start = Mathf.Max(0f, sheen - SheenWidth), end = Mathf.Min(1f, sheen + SheenWidth), middle = Mathf.Clamp(sheen, 0f, 1f);
        Band(mesh, rect, start, middle, clear, sheenTint);
        Band(mesh, rect, middle, end, sheenTint, clear);
    }

    // The trace: the outline as four edges (left end up, top, right end down, bottom), each part of [head - length, head]
    // drawn as a strip inside it, brightest at the head.
    private void Outline(VertexHelper mesh, Rect rect)
    {
        var corners = new[]
        {
            new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin + Shear, rect.yMax),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax - Shear, rect.yMin)
        };
        float total = 0f;
        var lengths = new float[4];
        for (int i = 0; i < 4; i++) { lengths[i] = Vector2.Distance(corners[i], corners[(i + 1) % 4]); total += lengths[i]; }
        float head = Mathf.Repeat(traceAt, 1f) * total, tail = head - traceLength * total;
        float walked = 0f;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = corners[i], b = corners[(i + 1) % 4], way = (b - a).normalized;
            // Clockwise round the plate, so inside is to the right of the way along.
            Vector2 inside = new Vector2(way.y, -way.x) * traceWidth;
            // The lit stretch may wrap past the start: test it and its copy one lap back.
            for (int lap = 0; lap < 2; lap++)
            {
                float start = Mathf.Max(walked, tail + lap * total), end = Mathf.Min(walked + lengths[i], head + lap * total);
                if (end <= start) continue;
                Color c0 = traceTint, c1 = traceTint;
                c0.a *= Mathf.Clamp01((start - lap * total - tail) / Mathf.Max(1f, head - tail));
                c1.a *= Mathf.Clamp01((end - lap * total - tail) / Mathf.Max(1f, head - tail));
                Vector2 p0 = a + way * (start - walked), p1 = a + way * (end - walked);
                int index = mesh.currentVertCount;
                Color32 k0 = c0 * color, k1 = c1 * color;
                mesh.AddVert(p0, k0, Vector2.zero);
                mesh.AddVert(p1, k1, Vector2.zero);
                mesh.AddVert(p1 + inside, k1, Vector2.zero);
                mesh.AddVert(p0 + inside, k0, Vector2.zero);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index + 2, index + 3, index);
            }
            walked += lengths[i];
        }
    }

    // The plate's colour at position along it, with what the fire leaves of it at step through the burn.
    private Color Burnt(float position, float step)
    {
        Color color = Color.Lerp(from, to, position);
        float cool = step * step * (3f - 2f * step);
        color.a *= 1f - Mathf.Pow(1f - cool, 1.6f);
        return color;
    }

    // A band between heights y0 and y1 that keeps the plate's lean, so an edge line follows the slanted end.
    private void Strip(VertexHelper mesh, Rect rect, float a, float b, float y0, float y1, Color left, Color right)
    {
        if (b <= a || y1 <= y0) return;
        float span = rect.width - Shear;
        float x0 = rect.xMin + a * span, x1 = rect.xMin + b * span;
        float lean0 = Shear * (y0 - rect.yMin) / rect.height, lean1 = Shear * (y1 - rect.yMin) / rect.height;
        int index = mesh.currentVertCount;
        Color32 l = left * color, r = right * color;
        mesh.AddVert(new Vector2(x0 + lean0, y0), l, Vector2.zero);
        mesh.AddVert(new Vector2(x0 + lean1, y1), l, Vector2.zero);
        mesh.AddVert(new Vector2(x1 + lean1, y1), r, Vector2.zero);
        mesh.AddVert(new Vector2(x1 + lean0, y0), r, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }

    // Parallel to the left end, so the band of light leans with the plate.
    private void Band(VertexHelper mesh, Rect rect, float a, float b, Color left, Color right)
    {
        if (b <= a) return;
        float span = rect.width - Shear;
        float x0 = rect.xMin + a * span, x1 = rect.xMin + b * span;
        int index = mesh.currentVertCount;
        Color32 l = left * color, r = right * color;
        mesh.AddVert(new Vector2(x0, rect.yMin), l, Vector2.zero);
        mesh.AddVert(new Vector2(x0 + Shear, rect.yMax), l, Vector2.zero);
        mesh.AddVert(new Vector2(x1 + Shear, rect.yMax), r, Vector2.zero);
        mesh.AddVert(new Vector2(x1, rect.yMin), r, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
