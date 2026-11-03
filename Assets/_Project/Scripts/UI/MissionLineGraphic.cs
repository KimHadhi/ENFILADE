using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The line the objective bar stands on, which is also its progress: one segment per target while they fit, a plain line
/// past that; the done part in the bar's accent and the rest a darker track. The segment that filled last flashes, and a
/// thin tail runs past the bar's end to a small square.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MissionLineGraphic : MaskableGraphic
{
    private const float TailLine = 1f, EndSquare = 3f;
    private int segments;
    private float fill, flash, tail, gap;
    private Color done, track;

    /// <summary>
    /// segments: one per target, 0 for a plain line. fill: the done share (0-1). flash: 0-1 of white on the segment that
    /// filled last. tail: px at the right end that run past the bar.
    /// </summary>
    public void Set(int segments, float fill, float flash, float tail, float gap, Color done, Color track)
    {
        fill = Mathf.Clamp01(fill); flash = Mathf.Clamp01(flash); tail = Mathf.Max(0f, tail); gap = Mathf.Max(0f, gap);
        if (segments == this.segments && fill == this.fill && flash == this.flash && tail == this.tail && gap == this.gap
            && done == this.done && track == this.track) return;
        this.segments = segments; this.fill = fill; this.flash = flash; this.tail = tail; this.gap = gap;
        this.done = done; this.track = track;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float end = rect.xMax - tail, width = end - rect.xMin;
        if (width <= 0f || rect.height <= 0f) return;
        float filled = rect.xMin + width * fill;
        float each = segments > 1 ? (width - gap * (segments - 1)) / segments : 0f;
        if (each >= 2f)
        {
            int last = Mathf.CeilToInt(fill * segments - .001f) - 1;
            for (int i = 0; i < segments; i++)
            {
                float x0 = rect.xMin + i * (each + gap), x1 = x0 + each;
                Quad(mesh, x0, Mathf.Min(x1, filled), rect.yMin, rect.yMax, i == last ? Color.Lerp(done, Color.white, flash) : done);
                Quad(mesh, Mathf.Max(x0, filled), x1, rect.yMin, rect.yMax, track);
            }
        }
        else
        {
            Quad(mesh, rect.xMin, filled, rect.yMin, rect.yMax, Color.Lerp(done, Color.white, flash * .5f));
            Quad(mesh, filled, end, rect.yMin, rect.yMax, track);
        }
        if (tail < EndSquare + gap * 2f) return;
        float middle = rect.center.y;
        Quad(mesh, end + gap, rect.xMax - EndSquare - gap, middle - TailLine * .5f, middle + TailLine * .5f, track);
        Quad(mesh, rect.xMax - EndSquare, rect.xMax, middle - EndSquare * .5f, middle + EndSquare * .5f, done);
    }

    private void Quad(VertexHelper mesh, float x0, float x1, float y0, float y1, Color tint)
    {
        if (x1 <= x0) return;
        int index = mesh.currentVertCount;
        Color32 shade = tint * color;
        mesh.AddVert(new Vector2(x0, y0), shade, Vector2.zero);
        mesh.AddVert(new Vector2(x0, y1), shade, Vector2.zero);
        mesh.AddVert(new Vector2(x1, y1), shade, Vector2.zero);
        mesh.AddVert(new Vector2(x1, y0), shade, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
