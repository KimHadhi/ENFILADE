using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A chat line's plate (ChatMessageItem): a bar in the speaker's colour at the outer end, then the plate, easing off
/// toward its inner end, which leans like the HUD's other plates ('/', as the kill feed's rows do at theirs). The chat
/// is on the left, so its outer end is the left one. Bare text was all the chat had before 2026-11-12 and it vanished
/// over a bright sky; the plate is what keeps every line readable over any scene. A flash of a colour (the pop) can lie
/// over the plate and the bar can stand wider for it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChatLinePlate : MaskableGraphic
{
    private Color fill, fillEnd, bar, flashTint;
    private float barWidth, barNow, shear, flash;

    /// <summary>The plate's colours at its outer and inner ends, the bar's colour and width, and the lean (px).</summary>
    public void Paint(Color outer, Color inner, Color barColor, float width, float lean)
    {
        fill = outer; fillEnd = inner; bar = barColor;
        barWidth = barNow = Mathf.Max(0f, width);
        shear = Mathf.Max(0f, lean);
        flash = 0f;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>
    /// The pop: <paramref name="amount"/> of <paramref name="tint"/> over the plate (0 for none) and the bar at
    /// <paramref name="width"/> px. Redraws only when something moved enough to see.
    /// </summary>
    public void Flash(Color tint, float amount, float width)
    {
        amount = Mathf.Clamp01(amount);
        if (Mathf.Abs(amount - flash) < .004f && Mathf.Abs(width - barNow) < .05f && (Vector4)tint == (Vector4)flashTint) return;
        flash = amount; flashTint = tint; barNow = Mathf.Max(0f, width);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        // The plate starts past the bar's own width, so a bar flaring wider on arrival lies over the plate, not beside it.
        float from = rect.xMin + Mathf.Min(barWidth, rect.width);
        float lean = Mathf.Min(shear, Mathf.Max(0f, rect.xMax - from));
        Color outer = Color.Lerp(fill, flashTint, flash), inner = Color.Lerp(fillEnd, flashTint, flash);
        // '/' at the inner end: the bottom corner sits one lean short of the top one.
        Quad(mesh, new Vector2(from, rect.yMin), new Vector2(from, rect.yMax), new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMax - lean, rect.yMin), outer, inner);
        float barEnd = rect.xMin + Mathf.Min(barNow, rect.width);
        if (barEnd > rect.xMin)
            Quad(mesh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax), new Vector2(barEnd, rect.yMax),
                new Vector2(barEnd, rect.yMin), bar, bar);
    }

    // Corners from the bottom left round by the top; the left pair takes one colour and the right pair the other.
    private void Quad(VertexHelper mesh, Vector2 bottomLeft, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Color left, Color right)
    {
        int index = mesh.currentVertCount;
        Color32 l = left * color, r = right * color;
        mesh.AddVert(bottomLeft, l, Vector2.zero);
        mesh.AddVert(topLeft, l, Vector2.zero);
        mesh.AddVert(topRight, r, Vector2.zero);
        mesh.AddVert(bottomRight, r, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
