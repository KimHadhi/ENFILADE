using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A wardrobe picture laid over its plate or tile as the user drew it (2026-11-12: "이런 딱 스타일리쉬하게 살짝 기울어져
/// 있으면서 전체적으로 패널이 꽉 차게끔"): much bigger than the plate, over its right part, cut by the plate's own slanted
/// shape (where and how big: OutfitPictureLook, Tables/WardrobePictures.csv). It stretches over the plate, and its mesh
/// is the picture's square cut down to the plate's parallelogram, so it needs no mask (a stencil per plate would break
/// the list's batching), still clips inside the scrolled list (RectMask2D), and follows a change of the table at once.
/// Where the table says so (tiles: their words run on over the picture's left side) the left side fades in.
/// </summary>
[DisallowMultipleComponent]
public sealed class OutfitPictureGraphic : MaskableGraphic
{
    private static readonly List<Vector2> shape = new List<Vector2>(4);
    private static readonly List<UIVertex> polygon = new List<UIVertex>(8), part = new List<UIVertex>(8), scratch = new List<UIVertex>(8);
    private Texture picture;
    private OutfitPictureLook.Where where;
    private float slant;

    public override Texture mainTexture => picture != null ? picture : s_WhiteTexture;

    /// <summary>The picture shown; none draws nothing.</summary>
    public Texture Picture
    {
        get => picture;
        set
        {
            if (picture == value) return;
            picture = value;
            SetMaterialDirty();
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// Lays <paramref name="picture"/> over <paramref name="plate"/> (a plate or a tile, leaning <paramref name="slant"/>
    /// px as KitSkew leans it), in front of what the plate holds so far: add the plate's words after it.
    /// </summary>
    public static OutfitPictureGraphic Lay(RectTransform plate, OutfitPictureLook.Where where, float slant, Texture picture)
    {
        var image = KitStyle.Stretch("Picture", plate).gameObject.AddComponent<OutfitPictureGraphic>();
        image.raycastTarget = false;
        image.where = where;
        image.slant = slant;
        image.Picture = picture;
        return image;
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

    // WardrobePictures.csv saved while playing: the picture moves at once.
    private void Changed() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var plate = rectTransform.rect;
        if (picture == null || plate.width <= 0f || plate.height <= 0f) return;
        var place = OutfitPictureLook.Of(where);
        var square = OutfitPictureLook.Square(plate, place);
        OutfitPictureLook.Shape(plate, slant, shape);
        var tint = color;
        tint.a *= place.Alpha;
        OutfitPictureLook.Cut(square, shape, tint, polygon, scratch);
        if (polygon.Count < 3) return;
        float fadeEnd = square.xMin + place.Fade * square.width;
        if (place.Fade <= 0f)
        {
            OutfitPictureLook.Fan(mesh, polygon);
            return;
        }
        // Left of where the fade ends, each corner as clear as it is near the square's left edge (the clearness is even
        // across a piece split there, so the fan's blend is exact); the rest whole.
        part.Clear();
        part.AddRange(polygon);
        OutfitPictureLook.Clip(part, new Vector2(fadeEnd, 0f), new Vector2(fadeEnd, 1f), scratch);
        for (int i = 0; i < part.Count; i++)
        {
            var vertex = part[i];
            float clear = Mathf.Clamp01((vertex.position.x - square.xMin) / Mathf.Max(.01f, fadeEnd - square.xMin));
            var shade = vertex.color;
            shade.a = (byte)Mathf.RoundToInt(shade.a * clear);
            vertex.color = shade;
            part[i] = vertex;
        }
        OutfitPictureLook.Fan(mesh, part);
        OutfitPictureLook.Clip(polygon, new Vector2(fadeEnd, 1f), new Vector2(fadeEnd, 0f), scratch);
        OutfitPictureLook.Fan(mesh, polygon);
    }
}
