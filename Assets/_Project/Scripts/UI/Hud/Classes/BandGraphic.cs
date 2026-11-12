using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ultimate notice's band (MomentHUD), one mesh. Its parts:
/// - the near-black plate, its right end cut on a slant;
/// - the class colour along its top;
/// - the class's own end (a blade's point, rank bars, a rangefinder's ticks, twin cuts, a heartbeat, a nested diamond,
///   hazard stripes);
/// - the emblem's square;
/// - the HUD's pink shard past its tip, and a smaller one past that.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BandGraphic : MaskableGraphic
{
    public enum Cap : byte { Blade, Ranks, Ticks, Twin, Pulse, Diamond, Hazard }

    private float width = 470f, height = 52f, slant = 18f, flash;
    private Cap cap;
    private Color tint = Color.white;

    /// <summary>This class's lean (° counter-clockwise, before the HUD tilt setting).</summary>
    public float Lean { get; private set; }
    /// <summary>The class colour's white flash on its first frames, 0 to 1.</summary>
    public float Flash { get => flash; set { value = Mathf.Clamp01(value); if (!Mathf.Approximately(flash, value)) { flash = value; SetVerticesDirty(); } } }

    public static Cap CapOf(string classId) => (classId ?? "").ToLowerInvariant() switch
    {
        PlayerClass.Knight => Cap.Blade,
        PlayerClass.Assault => Cap.Ranks,
        PlayerClass.Ghost => Cap.Ticks,
        PlayerClass.Point => Cap.Twin,
        PlayerClass.Detente => Cap.Pulse,
        PlayerClass.Schiesshund => Cap.Diamond,
        _ => Cap.Hazard,
    };

    public void Shape(float bandWidth, float bandHeight, float cut, Cap end, Color colour)
    {
        width = bandWidth; height = bandHeight; slant = cut; cap = end; tint = colour;
        // Each its own lean, never one shared slant (the HUD's rule). MomentHUD turns the band clockwise only, its right
        // end down: raised, the end would reach the chat's input above (stance-room's measure, 2026-11-12).
        Lean = MomentLook.Number("Notice", "lean." + end.ToString().ToLowerInvariant(), end switch
        {
            Cap.Blade => 2f, Cap.Ranks => 3f, Cap.Ticks => 1f, Cap.Twin => 2.5f, Cap.Pulse => 1.5f, Cap.Diamond => 2.75f, _ => .5f,
        });
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float w = width, h = height, a = color.a;
        Color ink = new Color(.047f, .047f, .059f, .93f * a), stripe = Color.Lerp(tint, Color.white, flash * .85f);
        stripe.a = a;
        Color mark = new Color(tint.r, tint.g, tint.b, a), pink = new Color(KitStyle.Accent.r, KitStyle.Accent.g, KitStyle.Accent.b, a);
        bool blade = cap == Cap.Blade;
        // The plate.
        if (blade) Poly(vh, ink, new Vector2(0f, h), new Vector2(w - 30f, h), new Vector2(w + 26f, h * .55f), new Vector2(w - slant - 34f, 0f), new Vector2(0f, 0f));
        else Poly(vh, ink, new Vector2(0f, h), new Vector2(w, h), new Vector2(w - slant, 0f), new Vector2(0f, 0f));
        // The class colour along its top: a heartbeat in it for the Détente.
        float top = blade ? w - 32f : w - 1f;
        if (cap == Cap.Pulse)
        {
            Vector2[] beat = { new Vector2(0f, 0f), new Vector2(w - 122f, 0f), new Vector2(w - 112f, 2f), new Vector2(w - 103f, -12f),
                new Vector2(w - 94f, 14f), new Vector2(w - 86f, -5f), new Vector2(w - 78f, 0f), new Vector2(w - 2f, 0f) };
            for (int i = 1; i < beat.Length; i++) Line(vh, beat[i - 1] + new Vector2(0f, h - 2.5f), beat[i] + new Vector2(0f, h - 2.5f), 5f, stripe);
        }
        else Poly(vh, stripe, new Vector2(0f, h), new Vector2(top, h), new Vector2(top - 3f, h - 5f), new Vector2(0f, h - 5f));
        // The class's own end.
        switch (cap)
        {
            case Cap.Ranks:
                for (int i = 0; i < 3; i++) { float x = w - 96f + i * 16f; Poly(vh, mark, new Vector2(x, h - 12f), new Vector2(x + 8f, h - 12f), new Vector2(x + 2f, 10f), new Vector2(x - 6f, 10f)); }
                break;
            case Cap.Ticks:
                for (int i = 0; i < 13; i++) { float x = w - 200f + i * 14f, length = i % 4 == 0 ? 10f : 5f; Poly(vh, mark, new Vector2(x, h - 5f), new Vector2(x + 2f, h - 5f), new Vector2(x + 2f, h - 5f - length), new Vector2(x, h - 5f - length)); }
                break;
            case Cap.Twin:
                for (int i = 0; i < 2; i++) { float x = w - 74f + i * 14f; Line(vh, new Vector2(x + 14f, h - 10f), new Vector2(x, 8f), 3f, mark); }
                break;
            case Cap.Diamond:
            {
                Vector2 c = new Vector2(w - 56f, h * .48f);
                DiamondLine(vh, c, 16f, 2.5f, mark);
                Poly(vh, mark, c + new Vector2(0f, 7f), c + new Vector2(7f, 0f), c + new Vector2(0f, -7f), c + new Vector2(-7f, 0f));
                break;
            }
            case Cap.Hazard:
                for (int i = 0; i < 6; i++) { float x = w - 108f + i * 16f; Poly(vh, mark, new Vector2(x + 12f, h - 9f), new Vector2(x + 19f, h - 9f), new Vector2(x + 7f, 4f), new Vector2(x, 4f)); }
                break;
        }
        // The emblem's square: dark, rimmed in the class colour.
        float block = h - 12f;
        Poly(vh, new Color(.02f, .02f, .03f, a), new Vector2(9f, 5f), new Vector2(9f + block, 5f), new Vector2(9f + block, 5f + block), new Vector2(9f, 5f + block));
        Frame(vh, new Rect(9f, 5f, block, block), 1.5f, mark);
        // The HUD's pink shard past the tip, and a smaller one past it.
        float tip = blade ? w + 30f : w + 6f;
        Poly(vh, pink, new Vector2(tip, h), new Vector2(tip + 28f, h), new Vector2(tip + 6f, h - 22f));
        Poly(vh, pink, new Vector2(tip + 36f, h - 4f), new Vector2(tip + 48f, h - 4f), new Vector2(tip + 39f, h - 13f));
    }

    private static void Poly(VertexHelper vh, Color color, params Vector2[] points)
    {
        int first = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        foreach (var p in points) { vertex.position = p; vh.AddVert(vertex); }
        for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(first, first + i, first + i + 1);
    }

    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 along = b - a;
        if (along.sqrMagnitude < 1e-6f) return;
        Vector2 side = new Vector2(-along.y, along.x).normalized * (width * .5f);
        Poly(vh, color, a + side, b + side, b - side, a - side);
    }

    private static void DiamondLine(VertexHelper vh, Vector2 c, float r, float width, Color color)
    {
        Vector2[] p = { c + new Vector2(0f, r), c + new Vector2(r, 0f), c + new Vector2(0f, -r), c + new Vector2(-r, 0f) };
        for (int i = 0; i < 4; i++) Line(vh, p[i], p[(i + 1) % 4], width, color);
    }

    private static void Frame(VertexHelper vh, Rect r, float width, Color color)
    {
        Line(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), width, color);
        Line(vh, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), width, color);
        Line(vh, new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), width, color);
        Line(vh, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), width, color);
    }
}
