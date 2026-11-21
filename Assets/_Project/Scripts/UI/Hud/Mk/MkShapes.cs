using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD Mk II's flat shapes, drawn as one mesh per piece: the addon's draw.RoundedBox and surface.DrawRect, nothing else
/// (no outlines, no gradients, no slants). A piece says its boxes every frame it changes, in its frame's px from the top
/// left, y down (MkHud.Frame), and the mesh is built from them. Fills that must stay inside cells (the armour's ten, a
/// team row's shield) are cut to each cell by the piece, as the addon's stencil does.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MkShapes : MaskableGraphic
{
    private struct Shape
    {
        public float X, Y, W, H, Corner;
        public Color32 Colour;
    }

    // Segments of a rounded corner's quarter circle.
    private const int CornerSteps = 4;
    private readonly List<Shape> shapes = new List<Shape>();

    /// <summary>A shape layer filling <paramref name="frame"/>, which takes no clicks.</summary>
    public static MkShapes Make(string name, RectTransform frame)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(frame, false);
        rect.gameObject.layer = frame.gameObject.layer;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var shapes = rect.gameObject.AddComponent<MkShapes>();
        shapes.raycastTarget = false;
        return shapes;
    }

    /// <summary>Starts this frame's shapes over.</summary>
    public void Clear() => shapes.Clear();

    /// <summary>A box from its top left (px, y down), rounded by <paramref name="corner"/> px (0 for square).</summary>
    public void Box(float x, float y, float w, float h, Color colour, float corner = 0f)
    {
        if (w <= 0f || h <= 0f || colour.a <= .002f) return;
        shapes.Add(new Shape { X = x, Y = y, W = w, H = h, Corner = Mathf.Min(corner, w * .5f, h * .5f), Colour = colour });
    }

    /// <summary>The shapes said since Clear go to the screen.</summary>
    public void Show() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect area = rectTransform.rect;
        Color32 tint = color;
        foreach (var shape in shapes)
        {
            var colour = Multiply(shape.Colour, tint);
            float left = area.xMin + shape.X, top = area.yMax - shape.Y, right = left + shape.W, bottom = top - shape.H;
            float r = shape.Corner;
            if (r < .5f)
            {
                Quad(vh, left, bottom, right, top, colour);
                continue;
            }
            // The middle column whole, the two side strips between the corners, then the four corners as fans.
            Quad(vh, left + r, bottom, right - r, top, colour);
            Quad(vh, left, bottom + r, left + r, top - r, colour);
            Quad(vh, right - r, bottom + r, right, top - r, colour);
            Fan(vh, new Vector2(left + r, top - r), r, 90f, colour);
            Fan(vh, new Vector2(right - r, top - r), r, 0f, colour);
            Fan(vh, new Vector2(left + r, bottom + r), r, 180f, colour);
            Fan(vh, new Vector2(right - r, bottom + r), r, 270f, colour);
        }
    }

    private static Color32 Multiply(Color32 a, Color32 b)
        => new Color32((byte)(a.r * b.r / 255), (byte)(a.g * b.g / 255), (byte)(a.b * b.b / 255), (byte)(a.a * b.a / 255));

    private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 colour)
    {
        if (x1 <= x0 || y1 <= y0) return;
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0), colour, Vector2.zero);
        vh.AddVert(new Vector3(x0, y1), colour, Vector2.zero);
        vh.AddVert(new Vector3(x1, y1), colour, Vector2.zero);
        vh.AddVert(new Vector3(x1, y0), colour, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }

    // A quarter circle about its centre, from fromDegrees anticlockwise.
    private static void Fan(VertexHelper vh, Vector2 centre, float radius, float fromDegrees, Color32 colour)
    {
        int middle = vh.currentVertCount;
        vh.AddVert(centre, colour, Vector2.zero);
        for (int i = 0; i <= CornerSteps; i++)
        {
            float angle = (fromDegrees + 90f * i / CornerSteps) * Mathf.Deg2Rad;
            vh.AddVert(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, colour, Vector2.zero);
        }
        for (int i = 0; i < CornerSteps; i++) vh.AddTriangle(middle, middle + 1 + i, middle + 2 + i);
    }
}
