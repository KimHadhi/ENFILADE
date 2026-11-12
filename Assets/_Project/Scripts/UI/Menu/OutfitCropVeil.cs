using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Over the shooting window's square (OutfitShootView): what a plate of the wardrobe cuts away is dimmed and the plate's
/// edge drawn in pink, so what the user frames inside the line is just what the list's plate keeps (the user, 2026-11-12:
/// "밖으로 빠져 나온 부분은 안 보인다면 그건 어쩔 수 없지"). The plate is the wardrobe's own, its size and its lean; where the
/// picture lies over it is OutfitPictureLook's, the very numbers and cutting the plates' pictures use (OutfitPictureGraphic),
/// so a change of WardrobePictures.csv moves the line too.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OutfitCropVeil : MaskableGraphic
{
    // The edge's width in the window (px), drawn inside the kept part.
    private const float EdgeWidth = 2f;
    private static readonly List<Vector2> shape = new List<Vector2>(4);
    private static readonly List<UIVertex> kept = new List<UIVertex>(8), piece = new List<UIVertex>(8), scratch = new List<UIVertex>(8);
    private Vector2 plate;
    private float slant;
    private Color dim, edge;

    /// <summary>
    /// Lays the veil over <paramref name="view"/>, the square showing the picture whole, for a plate of
    /// <paramref name="plate"/> px leaning <paramref name="slant"/> px.
    /// </summary>
    public static OutfitCropVeil Lay(RectTransform view, Vector2 plate, float slant, Color dim, Color edge)
    {
        var veil = KitStyle.Stretch("Plate Crop", view).gameObject.AddComponent<OutfitCropVeil>();
        veil.raycastTarget = false;
        veil.plate = plate;
        veil.slant = slant;
        veil.dim = dim;
        veil.edge = edge;
        veil.SetVerticesDirty();
        return veil;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        GameTables.Reloaded += Changed;
    }

    protected override void OnDisable()
    {
        GameTables.Reloaded -= Changed;
        base.OnDisable();
    }

    private void Changed() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var view = rectTransform.rect;
        if (view.width <= 0f || view.height <= 0f || plate.x <= 0f || plate.y <= 0f) return;
        // The plate as the wardrobe lays it, its picture's square, and the plate's outline seen in the view, which is the square.
        var outline = new Rect(-plate * .5f, plate);
        var square = OutfitPictureLook.Square(outline, OutfitPictureLook.Of(OutfitPictureLook.Where.Plate));
        OutfitPictureLook.Shape(outline, slant, shape);
        for (int i = 0; i < shape.Count; i++)
            shape[i] = new Vector2(view.xMin + (shape[i].x - square.xMin) / square.width * view.width,
                view.yMin + (shape[i].y - square.yMin) / square.height * view.height);
        // Dimmed: the view less the plate, as one convex piece outside each of the plate's edges in turn.
        OutfitPictureLook.Corners(view, dim, kept);
        for (int i = 0; i < shape.Count && kept.Count > 0; i++)
        {
            Vector2 a = shape[i], b = shape[(i + 1) % shape.Count];
            piece.Clear();
            piece.AddRange(kept);
            OutfitPictureLook.Clip(piece, b, a, scratch);
            OutfitPictureLook.Fan(mesh, piece);
            OutfitPictureLook.Clip(kept, a, b, scratch);
        }
        // What is left is the part the plate keeps: its outline in pink, inside it.
        for (int i = 0; i < kept.Count && kept.Count >= 3; i++) Line(mesh, kept[i].position, kept[(i + 1) % kept.Count].position);
    }

    // A side of the kept part as a thin band on its inner side (the part is counter-clockwise: inside is on the left).
    private void Line(VertexHelper mesh, Vector2 from, Vector2 to)
    {
        Vector2 along = to - from;
        if (along.sqrMagnitude < 1e-4f) return;
        Vector2 inward = new Vector2(-along.y, along.x).normalized * EdgeWidth;
        int start = mesh.currentVertCount;
        Color32 tint = edge;
        mesh.AddVert(from, tint, Vector4.zero);
        mesh.AddVert(to, tint, Vector4.zero);
        mesh.AddVert(to + inward, tint, Vector4.zero);
        mesh.AddVert(from + inward, tint, Vector4.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
