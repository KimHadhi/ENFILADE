using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How the wardrobe lays its pictures of pieces (OutfitIcons) over its plates and tiles, and how a piece is framed when
/// nothing says otherwise, from Tables/WardrobePictures.csv. The user drew it (2026-11-12: "내가 그려준 것처럼 이런 딱
/// 스타일리쉬하게 살짝 기울어져 있으면서 전체적으로 패널이 꽉 차게끔 그래야 잘 보이니까"): a picture much bigger than its
/// plate, over the plate's right part, the piece turned a little clockwise, and whatever falls outside the plate cut away
/// by the plate's own slanted shape ("밖으로 빠져 나온 부분은 안 보인다면 그건 어쩔 수 없지").
/// A picture is a square that lies straight: the lean is taken into the picture itself (OutfitStudio turns its lens,
/// OutfitFraming.roll), so the shooting window's square is exactly what is kept and every place that shows the picture
/// shows the same lean. On a plate the square is <c>size</c> plate heights a side, its middle <c>right</c> plate heights
/// in from the plate's right end and <c>drop</c> below the plate's middle; on a tile the same in tile heights. The
/// cutting is plain polygon clipping (Sutherland–Hodgman) against the plate's parallelogram as KitSkew leans it, used by
/// the pictures themselves (OutfitPictureGraphic) and by the shooting window's crop line (OutfitCropVeil), so the two
/// can't disagree.
/// Then (2026-11-12: "귀걸이 같은 건 … 검어서 그런가? 진짜 안 보이는데"): a dark piece's pictures are lifted in the studio
/// (the Exposure rows, OutfitStudio), and on the plates every picture gets a thin light outline round its silhouette and
/// its thin bits keep most of their opacity (the Edge and Thin rows, drawn by <see cref="PictureMaterial"/>'s shader
/// UI/ENFILADE/OutfitPicture), so a small dark piece reads on a dark plate without being taken again by hand.
/// </summary>
public static class OutfitPictureLook
{
    public const string TableName = "WardrobePictures";
    /// <summary>The plates' pictures' shader (Art/Resources/UI/OutfitPicture.shader, so it ships).</summary>
    public const string PictureShader = "UI/ENFILADE/OutfitPicture";
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth"), EdgeColorId = Shader.PropertyToID("_EdgeColor"),
        ThinAlphaId = Shader.PropertyToID("_ThinAlpha"), ThinBroadId = Shader.PropertyToID("_ThinBroad");
    private static Material pictureMaterial;
    private static bool pictureTried;

    /// <summary>Which of the wardrobe's pieces a picture lies over: a choice's plate, or a slot's tile.</summary>
    public enum Where { Plate, Tile }

    /// <summary>
    /// Where a picture lies over a plate or tile: its side, how far its middle is in from the right end and down from the
    /// middle (all in the plate's heights), how opaque it is (0-1), how much of its left side fades in (of its side), and
    /// how strong its outline is (0-1, of the Edge row's).
    /// </summary>
    public readonly struct Place
    {
        public readonly float Size, Right, Drop, Alpha, Fade, Edge;

        public Place(float size, float right, float drop, float alpha, float fade, float edge)
        {
            Size = size;
            Right = right;
            Drop = drop;
            Alpha = alpha;
            Fade = fade;
            Edge = edge;
        }
    }

    /// <summary>How pictures lie over the wardrobe's plates or tiles, as the table says now.</summary>
    public static Place Of(Where where) => where == Where.Tile ? Read("Tile", .4f) : Read("Plate", 0f);

    // The defaults are the table's own numbers, for a table that lacks a row.
    private static Place Read(string row, float fade) => new Place(Mathf.Clamp(Number(row, "size", 2.5f), .2f, 20f),
        Number(row, "right", .7f), Number(row, "drop", .1f), Mathf.Clamp01(Number(row, "alpha", .72f)), Mathf.Clamp01(Number(row, "fade", fade)),
        Mathf.Clamp01(Number(row, "edge", 1f)));

    /// <summary>How far every picture leans clockwise unless its piece is turned otherwise (°; OutfitFraming.roll adds to it).</summary>
    public static float AutoTilt => Number("Auto", "tilt", 7f);
    /// <summary>How far from above a piece is seen unless it is turned otherwise (°; OutfitFraming.pitch adds to it).</summary>
    public static float AutoPitch => Number("Auto", "pitch", 8f);
    /// <summary>How much of the picture the whole leaning piece fills, its longer side (OutfitFraming.zoom multiplies it).</summary>
    public static float AutoFill => Mathf.Clamp(Number("Auto", "fill", .9f), .05f, 4f);
    /// <summary>How far either way the shooting window's lean slider goes (°).</summary>
    public static float TiltRange => Mathf.Clamp(Number("Shoot", "tiltRange", 45f), 5f, 180f);
    /// <summary>
    /// Parts of a piece at least this far apart (m) are framed apart: a pair of gloves on outstretched hands (33-83 cm),
    /// never a pair of shoes or socks (2-5 cm), measured on the wardrobe's own outfits (2026-11-12).
    /// </summary>
    public static float AutoSplitGap => Mathf.Max(.001f, Number("Auto", "splitGap", .15f));
    /// <summary>Or at least this many times the larger part's own width that way apart (a pair of earrings: 2.7-4).</summary>
    public static float AutoSplitRatio => Mathf.Max(.1f, Number("Auto", "splitRatio", 2f));
    /// <summary>A part with less than this share of the piece's vertices, that far from the rest, is a stray and isn't framed.</summary>
    public static float AutoNoise => Mathf.Clamp(Number("Auto", "noise", .01f), 0f, .5f);

    // ---- Exposure (the studio's) ----

    /// <summary>
    /// The mean brightness, in linear light, a piece's pictures are lifted toward when they come out darker (.18 is a
    /// middle grey); a piece already as light is left as it is.
    /// </summary>
    public static float ExposureTarget => Mathf.Clamp(Number("Exposure", "target", .18f), 0f, 1f);
    /// <summary>At most this many times brighter.</summary>
    public static float ExposureMost => Mathf.Clamp(Number("Exposure", "most", 5f), 1f, 16f);
    /// <summary>Lifted light above this (0-1, linear) bends softly under white instead of burning out.</summary>
    public static float ExposureKnee => Mathf.Clamp(Number("Exposure", "knee", .75f), .1f, .99f);
    /// <summary>No more lift than takes the brightest twentieth of the piece past this (linear; the knee bends what passes 1).</summary>
    public static float ExposureCeiling => Mathf.Clamp(Number("Exposure", "ceiling", 1.2f), .1f, 8f);

    // ---- The plates' outline and thin bits ----

    /// <summary>
    /// The plates' pictures' material: see-through as the table says, with a thin light outline round the piece and its
    /// thin bits kept nearly opaque. Null when its shader isn't in the game: the pictures are drawn plainly then.
    /// </summary>
    public static Material PictureMaterial
    {
        get
        {
            if (pictureMaterial == null && !pictureTried)
            {
                pictureTried = true;
                var shader = Shader.Find(PictureShader);
                if (shader != null && shader.isSupported)
                {
                    pictureMaterial = new Material(shader) { name = "Outfit Picture", hideFlags = HideFlags.HideAndDontSave };
                    ApplyLook();
                    GameTables.Reloaded += ApplyLook;
                }
                else Debug.LogWarning($"[OutfitPictures] {PictureShader} 셰이더가 없어서 옷장 사진을 테두리 없이 그립니다.");
            }
            return pictureMaterial;
        }
    }

    // The Edge and Thin rows into the material (again whenever the table is saved while playing).
    private static void ApplyLook()
    {
        if (pictureMaterial == null) return;
        pictureMaterial.SetFloat(EdgeWidthId, Mathf.Clamp(Number("Edge", "width", 2f), 0f, 12f));
        var edge = ColorUtility.TryParseHtmlString(Text("Edge", "colour", "#FFFFFF"), out var parsed) ? parsed : Color.white;
        edge.a = Mathf.Clamp01(Number("Edge", "alpha", .8f));
        pictureMaterial.SetColor(EdgeColorId, edge);
        pictureMaterial.SetFloat(ThinAlphaId, Mathf.Clamp01(Number("Thin", "alpha", .95f)));
        pictureMaterial.SetFloat(ThinBroadId, Mathf.Clamp(Number("Thin", "broad", .6f), .05f, 1f));
    }

    /// <summary>
    /// What <see cref="PictureMaterial"/>'s shader reads off each corner of a picture: <paramref name="place"/>'s
    /// opacity (uv0.z) and outline (uv0.w).
    /// </summary>
    public static void Carry(List<UIVertex> polygon, Place place)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            var vertex = polygon[i];
            vertex.uv0 = new Vector4(vertex.uv0.x, vertex.uv0.y, place.Alpha, place.Edge);
            polygon[i] = vertex;
        }
    }

    private static float Number(string row, string field, float fallback) =>
        GameTables.Number(TableName, row, field, fallback);

    private static string Text(string row, string field, string fallback) =>
        GameTables.TryGetRow(TableName, row, out var cells) && cells.TryGetValue(field, out string text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim() : fallback;

    // ---- Where it lies ----

    /// <summary>The picture's square over a plate whose rect is <paramref name="plate"/> (in its own space, y up).</summary>
    public static Rect Square(Rect plate, Place place)
    {
        float height = plate.height, side = Mathf.Max(1f, place.Size * height);
        float x = plate.xMax - place.Right * height, y = plate.center.y - place.Drop * height;
        return new Rect(x - side * .5f, y - side * .5f, side, side);
    }

    /// <summary>
    /// How far in from a plate's left end its picture begins (px, for a plate of <paramref name="size"/>): what the
    /// plate's words keep to the left of.
    /// </summary>
    public static float LeftOf(Vector2 size, Place place) => size.x - place.Right * size.y - place.Size * size.y * .5f;

    /// <summary>
    /// The plate's own outline as KitSkew leans it (its top edge <paramref name="slant"/> px right of its bottom edge),
    /// counter-clockwise from the bottom left.
    /// </summary>
    public static void Shape(Rect plate, float slant, List<Vector2> into)
    {
        float half = slant * .5f;
        into.Clear();
        into.Add(new Vector2(plate.xMin - half, plate.yMin));
        into.Add(new Vector2(plate.xMax - half, plate.yMin));
        into.Add(new Vector2(plate.xMax + half, plate.yMax));
        into.Add(new Vector2(plate.xMin + half, plate.yMax));
    }

    // ---- Cutting ----

    /// <summary>
    /// The square's corners, each with its place in the picture (0-1, from the bottom left) and <paramref name="tint"/>,
    /// cut down to the convex counter-clockwise <paramref name="shape"/>; nothing left when they don't meet.
    /// </summary>
    public static void Cut(Rect square, IReadOnlyList<Vector2> shape, Color32 tint, List<UIVertex> into, List<UIVertex> scratch)
    {
        Corners(square, tint, into);
        for (int i = 0; i < shape.Count && into.Count > 0; i++) Clip(into, shape[i], shape[(i + 1) % shape.Count], scratch);
    }

    /// <summary>A rect's four corners counter-clockwise from the bottom left, each with its place in the rect (0-1).</summary>
    public static void Corners(Rect rect, Color32 tint, List<UIVertex> into)
    {
        into.Clear();
        into.Add(Vertex(rect.xMin, rect.yMin, 0f, 0f, tint));
        into.Add(Vertex(rect.xMax, rect.yMin, 1f, 0f, tint));
        into.Add(Vertex(rect.xMax, rect.yMax, 1f, 1f, tint));
        into.Add(Vertex(rect.xMin, rect.yMax, 0f, 1f, tint));
    }

    /// <summary>
    /// Keeps, in place, the part of the convex <paramref name="polygon"/> on the left of the line from
    /// <paramref name="a"/> to <paramref name="b"/> (inside, for an edge of a counter-clockwise outline).
    /// </summary>
    public static void Clip(List<UIVertex> polygon, Vector2 a, Vector2 b, List<UIVertex> scratch)
    {
        scratch.Clear();
        int count = polygon.Count;
        for (int i = 0; i < count; i++)
        {
            var from = polygon[(i + count - 1) % count];
            var to = polygon[i];
            float sideFrom = Side(a, b, from.position), sideTo = Side(a, b, to.position);
            if (sideTo >= 0f)
            {
                if (sideFrom < 0f) scratch.Add(Between(from, to, sideFrom / (sideFrom - sideTo)));
                scratch.Add(to);
            }
            else if (sideFrom >= 0f) scratch.Add(Between(from, to, sideFrom / (sideFrom - sideTo)));
        }
        polygon.Clear();
        polygon.AddRange(scratch);
    }

    /// <summary>Puts a convex polygon into the mesh as a fan of triangles.</summary>
    public static void Fan(VertexHelper mesh, List<UIVertex> polygon)
    {
        if (polygon.Count < 3) return;
        int start = mesh.currentVertCount;
        foreach (var vertex in polygon) mesh.AddVert(vertex);
        for (int i = 1; i < polygon.Count - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
    }

    // Twice the area of a, b, p: above zero with p on the left of a → b.
    private static float Side(Vector2 a, Vector2 b, Vector3 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    private static UIVertex Between(UIVertex from, UIVertex to, float t)
    {
        var vertex = from;
        vertex.position = Vector3.Lerp(from.position, to.position, t);
        vertex.uv0 = Vector4.Lerp(from.uv0, to.uv0, t);
        vertex.color = Color32.Lerp(from.color, to.color, t);
        return vertex;
    }

    private static UIVertex Vertex(float x, float y, float u, float v, Color32 tint)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = new Vector3(x, y, 0f);
        vertex.uv0 = new Vector4(u, v, 0f, 0f);
        vertex.color = tint;
        return vertex;
    }
}
