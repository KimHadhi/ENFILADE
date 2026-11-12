using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The weapon slots' flat pieces (WeaponSlotsHUD), drawn as shapes so they stay crisp at any size: a plate with its top
/// left and bottom right corners cut (with a line of colour down its left side for the slot in hand), a number tab leaning
/// as the HUD's slants do (its top to the right), and the pink corner and small shard of the HUD's strip.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SlotShape : MaskableGraphic
{
    public enum Kind { Plate, Tab, Corner, Shard }

    private Kind kind;
    private float cut, shear, edgeWidth;
    private Color edgeColour;

    /// <summary>What it draws: cut is a plate's corner cut (px), shear how far a tab's top runs right of its bottom (px).</summary>
    public void Set(Kind kind, float cut = 0f, float shear = 0f)
    {
        this.kind = kind;
        this.cut = Mathf.Max(0f, cut);
        this.shear = shear;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>A plate's line of colour inside its left side, width px thick, following its cut corner; 0 for none.</summary>
    public void Edge(Color colour, float width)
    {
        width = colour.a > .004f ? Mathf.Max(0f, width) : 0f;
        if (Mathf.Abs(width - edgeWidth) < .01f && (Vector4)colour == (Vector4)edgeColour) return;
        edgeColour = colour;
        edgeWidth = width;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;
        switch (kind)
        {
            case Kind.Plate:
            {
                float c = Mathf.Min(cut, r.width * .5f, r.height * .5f);
                Fan(mesh, color, new Vector2(r.xMin + c, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin + c),
                    new Vector2(r.xMax - c, r.yMin), new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax - c));
                if (edgeWidth > 0f)
                {
                    // Up the left side and into the cut corner as far as the line is thick, so it never pokes past the cut.
                    float w = Mathf.Min(edgeWidth, r.width, c > 0f ? c : edgeWidth);
                    Fan(mesh, edgeColour, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax - c),
                        new Vector2(r.xMin + w, c > 0f ? r.yMax - c + w : r.yMax), new Vector2(r.xMin + w, r.yMin));
                }
                break;
            }
            case Kind.Tab:
            {
                float s = shear * .5f;
                Fan(mesh, color, new Vector2(r.xMin - s, r.yMin), new Vector2(r.xMin + s, r.yMax), new Vector2(r.xMax + s, r.yMax),
                    new Vector2(r.xMax - s, r.yMin));
                break;
            }
            case Kind.Corner:
                Fan(mesh, color, new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin));
                break;
            case Kind.Shard:
                Fan(mesh, color, new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax - r.height * .3f),
                    new Vector2(r.xMin + r.width * .2f, r.yMin));
                break;
        }
    }

    // A convex polygon as a fan from its first point.
    private static void Fan(VertexHelper mesh, Color colour, params Vector2[] points)
    {
        int start = mesh.currentVertCount;
        Color32 c = colour;
        foreach (var point in points) mesh.AddVert(point, c, Vector2.zero);
        for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
    }
}
