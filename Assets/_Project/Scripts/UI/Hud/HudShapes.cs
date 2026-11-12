using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's small drawn marks, as meshes so they stay sharp at any size and need no sprite: the two glyphs the user
/// picked out as fitting the game's look (2026-11-10, a reference of two white marks on grey) and the lines that go with
/// them. Hazard stripes say danger, can't or in progress; the nested diamond says a place or a state.
/// </summary>
public static class HudShapes
{
    /// <summary>A graphic of type T under parent, centred, not a raycast target.</summary>
    public static T Make<T>(string name, Transform parent, Color color) where T : MaskableGraphic
    {
        var made = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T)).GetComponent<T>();
        var rect = made.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        made.raycastTarget = false;
        made.color = color;
        return made;
    }

    internal static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector4.zero);
        mesh.AddVert(b, tint, Vector4.zero);
        mesh.AddVert(c, tint, Vector4.zero);
        mesh.AddVert(d, tint, Vector4.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }
}

/// <summary>
/// Stripes leaning 45° across the rect, each as wide as the gap between them, cut off at the rect's edges. Offset slides
/// them along (px), so a caller that adds to it each frame makes them run. Slant leans the rect itself into a
/// parallelogram as KitSkew does (the top edge that far right of the bottom), so it sits on a slanted plate.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HazardGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] private float stripe = 8f;
    [SerializeField] private float offset, slant;

    public float Stripe { get => stripe; set { if (!Mathf.Approximately(stripe, value)) { stripe = Mathf.Max(1f, value); SetVerticesDirty(); } } }
    public float Offset { get => offset; set { if (!Mathf.Approximately(offset, value)) { offset = value; SetVerticesDirty(); } } }
    public float Slant { get => slant; set { if (!Mathf.Approximately(slant, value)) { slant = value; SetVerticesDirty(); } } }

    private static readonly List<Vector2> band = new List<Vector2>(8), cut = new List<Vector2>(8);

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float step = stripe * 2f, w = rect.width, h = rect.height;
        Color32 tint = color;
        // A stripe starts at x on the bottom edge and leans right by the rect's height on its way up. Each is cut at the
        // rect's left and right sides, then leaned with the rect and drawn as a fan.
        for (float x = Mathf.Repeat(offset, step) - h - step; x < w; x += step)
        {
            band.Clear();
            band.Add(new Vector2(x, 0f)); band.Add(new Vector2(x + stripe, 0f)); band.Add(new Vector2(x + stripe + h, h)); band.Add(new Vector2(x + h, h));
            Cut(band, cut, 0f, true);
            Cut(cut, band, w, false);
            if (band.Count < 3) continue;
            int first = mesh.currentVertCount;
            foreach (var point in band)
                mesh.AddVert(new Vector2(rect.xMin + point.x + (point.y / h - .5f) * slant, rect.yMin + point.y), tint, Vector4.zero);
            for (int i = 1; i < band.Count - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
        }
    }

    // What of a convex shape lies right of x = limit (keepRight) or left of it.
    private static void Cut(List<Vector2> shape, List<Vector2> kept, float limit, bool keepRight)
    {
        kept.Clear();
        for (int i = 0; i < shape.Count; i++)
        {
            Vector2 a = shape[i], b = shape[(i + 1) % shape.Count];
            bool inA = keepRight ? a.x >= limit : a.x <= limit, inB = keepRight ? b.x >= limit : b.x <= limit;
            if (inA) kept.Add(a);
            if (inA != inB) kept.Add(new Vector2(limit, a.y + (b.y - a.y) * ((limit - a.x) / (b.x - a.x))));
        }
    }
}

/// <summary>A diamond's outline with a smaller filled diamond inside it, filling the rect.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DiamondGraphic : MaskableGraphic
{
    [SerializeField, Range(0f, 1f)] private float core = .46f;
    [SerializeField, Range(.02f, .5f)] private float line = .18f;

    /// <summary>The inner diamond's size and the outline's thickness, as shares of the outer diamond's half-width.</summary>
    public void Shape(float coreShare, float lineShare)
    {
        core = Mathf.Clamp01(coreShare); line = Mathf.Clamp(lineShare, .02f, .5f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Vector2 middle = rect.center, half = rect.size * .5f;
        Color32 tint = color;
        Vector2 At(int corner, float share) => middle + share * (corner == 0 ? new Vector2(0f, half.y) : corner == 1 ? new Vector2(half.x, 0f)
            : corner == 2 ? new Vector2(0f, -half.y) : new Vector2(-half.x, 0f));
        float inner = 1f - line;
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            HudShapes.Quad(mesh, At(i, 1f), At(next, 1f), At(next, inner), At(i, inner), tint);
        }
        if (core > 0f) HudShapes.Quad(mesh, At(0, core), At(1, core), At(2, core), At(3, core), tint);
    }
}

/// <summary>
/// A row of rangefinder ticks along the rect's width, standing on its bottom edge: every `every`th one the rect's full
/// height, the rest a share of it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TickRowGraphic : MaskableGraphic
{
    [SerializeField, Min(2)] private int count = 27;
    [SerializeField, Min(1)] private int every = 5;
    [SerializeField, Min(.5f)] private float thickness = 2f;
    [SerializeField, Range(0f, 1f)] private float shortShare = .5f;

    public void Shape(int ticks, int tallEvery, float tickThickness, float shortTickShare)
    {
        count = Mathf.Max(2, ticks); every = Mathf.Max(1, tallEvery); thickness = Mathf.Max(.5f, tickThickness); shortShare = Mathf.Clamp01(shortTickShare);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Color32 tint = color;
        int middle = (count - 1) / 2;
        for (int i = 0; i < count; i++)
        {
            float x = rect.xMin + rect.width * i / (count - 1), tall = (i - middle) % every == 0 ? rect.height : rect.height * shortShare;
            HudShapes.Quad(mesh, new Vector2(x - thickness * .5f, rect.yMin), new Vector2(x + thickness * .5f, rect.yMin),
                new Vector2(x + thickness * .5f, rect.yMin + tall), new Vector2(x - thickness * .5f, rect.yMin + tall), tint);
        }
    }
}

/// <summary>
/// Focus lines, a manga panel's: thin wedges from the rect's edge in toward its middle, stopping short of it at uneven
/// lengths. Every `accentEvery`th one takes the accent colour. Inner is how near the middle they come, as a share of the
/// rect's half-height; set it each frame to make them rush in.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FocusLinesGraphic : MaskableGraphic
{
    [SerializeField, Min(4)] private int count = 44;
    [SerializeField, Range(0f, 2f)] private float inner = .5f;
    [SerializeField, Min(1)] private int accentEvery = 5;
    [SerializeField] private Color accent = Color.white;

    public float Inner { get => inner; set { if (!Mathf.Approximately(inner, value)) { inner = Mathf.Max(0f, value); SetVerticesDirty(); } } }
    public Color Accent { get => accent; set { accent = value; SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Vector2 middle = rect.center;
        // Far enough out to pass the rect's corners.
        float far = rect.size.magnitude * .55f, near = rect.height * .5f * inner;
        for (int i = 0; i < count; i++)
        {
            // The same uneven wheel every time: no randomness, so it doesn't boil from frame to frame.
            float jitter = Mathf.Repeat(Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f, 1f);
            float angle = (i + jitter * .6f) / count * Mathf.PI * 2f, width = .009f + jitter * .006f;
            float from = near * (.8f + Mathf.Repeat(jitter * 7.3f, 1f) * .7f);
            Color32 tint = i % accentEvery == 0 ? accent * new Color(1f, 1f, 1f, color.a) : color;
            Vector2 tip = middle + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * from;
            Vector2 a = middle + new Vector2(Mathf.Cos(angle - width), Mathf.Sin(angle - width)) * far;
            Vector2 b = middle + new Vector2(Mathf.Cos(angle + width), Mathf.Sin(angle + width)) * far;
            int first = mesh.currentVertCount;
            mesh.AddVert(tip, tint, Vector4.zero);
            mesh.AddVert(a, tint, Vector4.zero);
            mesh.AddVert(b, tint, Vector4.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
        }
    }
}
