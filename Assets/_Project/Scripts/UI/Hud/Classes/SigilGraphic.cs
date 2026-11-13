using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A class sigil on the HUD (MomentLook): its strokes drawn as if by one pen up to <see cref="Progress"/>, each a bright
/// core over a soft glow in the moment's colour, white strokes white, filled shapes filled (the Détente's white cross).
/// A ring can stamp out from it (<see cref="Ring"/> 0 to 1). It fills the smaller side of its rect with the sigil's
/// 100-unit box. The ultimate's notice wears one (MomentHUD).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SigilGraphic : MaskableGraphic
{
    private MomentLook.Sigil sigil;
    private float progress = 1f, ring = -1f, turn, core = 3f, wide = 10f;
    private Color tint = Color.white;

    public MomentLook.Sigil Sigil { get => sigil; set { if (sigil != value) { sigil = value; SetVerticesDirty(); } } }
    /// <summary>How much of the sigil is drawn, 0 to 1.</summary>
    public float Progress { get => progress; set { value = Mathf.Clamp01(value); if (!Mathf.Approximately(progress, value)) { progress = value; SetVerticesDirty(); } } }
    /// <summary>The stamp ring's way out, 0 to 1; below 0 none.</summary>
    public float Ring { get => ring; set { if (!Mathf.Approximately(ring, value)) { ring = value; SetVerticesDirty(); } } }
    /// <summary>The moment's colour for its strokes.</summary>
    public Color Tint { get => tint; set { if (tint != value) { tint = value; SetVerticesDirty(); } } }

    /// <summary>The strokes' core and glow widths, in px at the rect's size.</summary>
    public void Widths(float coreWidth, float glowWidth)
    {
        core = Mathf.Max(.5f, coreWidth);
        wide = Mathf.Max(core, glowWidth);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float size = Mathf.Min(rect.width, rect.height), k = size / 100f, cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
        Vector2 middle = rect.center;
        float alpha = color.a;
        Vector2 Place(Vector2 p) => middle + new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos) * k;
        Color main = new Color(tint.r, tint.g, tint.b, tint.a * alpha), white = new Color(1f, 1f, 1f, alpha);
        MomentLook.Walk(sigil, progress,
            (a, b, isWhite) =>
            {
                Color c = isWhite ? white : main;
                Line(vh, Place(a), Place(b), wide, new Color(c.r, c.g, c.b, c.a * .22f));
                Line(vh, Place(a), Place(b), core * 2f, new Color(c.r, c.g, c.b, c.a * .85f));
                Line(vh, Place(a), Place(b), core, Color.Lerp(c, new Color(1f, 1f, 1f, c.a), .7f));
            },
            (points, isWhite) =>
            {
                int first = vh.currentVertCount;
                var vertex = UIVertex.simpleVert;
                vertex.color = isWhite ? white : main;
                foreach (var p in points) { vertex.position = Place(p); vh.AddVert(vertex); }
                for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(first, first + i, first + i + 1);
            });
        if (ring >= 0f && ring < 1f)
        {
            float radius = size * .5f * Mathf.Lerp(.62f, 1f, 1f - (1f - ring) * (1f - ring) * (1f - ring));
            Color c = new Color(1f, 1f, 1f, alpha * (1f - ring));
            const int Segments = 40;
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / Segments, a1 = (i + 1) * Mathf.PI * 2f / Segments;
                Line(vh, middle + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, middle + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius,
                    Mathf.Max(1f, core * 1.1f), c);
            }
        }
    }

    // A straight piece `width` thick from a to b, run a little past both ends so joints meet.
    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 along = b - a;
        float length = along.magnitude;
        if (length < 1e-4f || color.a <= .003f) return;
        Vector2 dir = along / length, side = new Vector2(-dir.y, dir.x) * (width * .5f);
        a -= dir * (width * .35f);
        b += dir * (width * .35f);
        int first = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = a + side; vh.AddVert(vertex);
        vertex.position = b + side; vh.AddVert(vertex);
        vertex.position = b - side; vh.AddVert(vertex);
        vertex.position = a - side; vh.AddVert(vertex);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}
