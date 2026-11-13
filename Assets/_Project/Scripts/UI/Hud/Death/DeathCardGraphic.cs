using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The death screen's plates (DeathScreen, DeathCard), one mesh each, in the HUD's near-black, near-white and one pink:
/// - Title: the near-black plate the word sits on, leaning right like the HUD's plates, the HUD's pink triangle in its
///   top left corner and the pink shard past its right tip with a smaller one past that (the HUD band's accent). Behind
///   it a pink slash with a near-white core crosses at its own angle, thick at its left end and thin at its right, drawn
///   out from the left as the screen comes (Reveal).
/// - Band: the thin dark strip under the cause line, its ends cut on a slant, a pink tick at its left end.
/// - Tile: the plate of one of the three numbers, two opposite corners cut (the menus' card shape), a pink hairline over
///   its top left.
/// The rect is the plate; the slash and the shards reach past it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DeathCardGraphic : MaskableGraphic
{
    public enum Shape : byte { Title, Band, Tile }

    // Title: how far the slash reaches past the plate's left and right ends (px), its thickness at each end, its rise.
    private const float SlashLeft = 150f, SlashRight = 130f, SlashThick = 13f, SlashThin = 2f, SlashCore = .3f;
    // The corner triangle's width along the top and depth down the left edge, and the tile's cut corners (px).
    private const float CornerWide = 40f, CornerDeep = 34f, TileCut = 12f;
    private static readonly Color Ink = new Color(.047f, .047f, .059f, 1f);

    private Shape shape;
    private float slant = 24f, reveal = 1f, plateAlpha = .94f;

    /// <summary>How far the title's slash is drawn out from its left end (0-1).</summary>
    public float Reveal
    {
        get => reveal;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(reveal, value)) return;
            reveal = value;
            SetVerticesDirty();
        }
    }

    /// <summary>What it draws, how far its top edge sits right of its bottom one (px), and how solid its plate is.</summary>
    public void Configure(Shape kind, float cut, float solid)
    {
        shape = kind;
        slant = cut;
        plateAlpha = Mathf.Clamp01(solid);
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width < 1f || r.height < 1f) return;
        float a = color.a;
        Color ink = KitStyle.With(Ink, plateAlpha * a), pink = KitStyle.With(KitStyle.Accent, a), ice = KitStyle.With(KitStyle.Ice, a);
        switch (shape)
        {
            case Shape.Title: Title(vh, r, ink, pink, ice); break;
            case Shape.Band: Band(vh, r, ink, pink); break;
            default: Tile(vh, r, ink, pink); break;
        }
    }

    private void Title(VertexHelper vh, Rect r, Color ink, Color pink, Color ice)
    {
        // The slash first, so the plate covers its middle.
        if (reveal > .001f)
        {
            Vector2 from = new Vector2(r.xMin - SlashLeft, r.yMin + 6f), to = new Vector2(r.xMax + SlashRight, r.yMax - 8f);
            Vector2 end = Vector2.Lerp(from, to, reveal);
            float endThick = Mathf.Lerp(SlashThick, SlashThin, reveal);
            Taper(vh, from, end, SlashThick, endThick, pink);
            Taper(vh, from, end, SlashThick * SlashCore, endThick * SlashCore, KitStyle.With(KitStyle.Ice, pink.a * .9f));
        }
        Vector2 bottomLeft = new Vector2(r.xMin, r.yMin), bottomRight = new Vector2(r.xMax - slant, r.yMin);
        Vector2 topRight = new Vector2(r.xMax, r.yMax), topLeft = new Vector2(r.xMin + slant, r.yMax);
        BandGraphic.Poly(vh, ink, bottomLeft, bottomRight, topRight, topLeft);
        // A quiet near-white rule along the bottom, inside the plate.
        float rule = r.yMin + 12f;
        BandGraphic.Poly(vh, KitStyle.With(ice, ice.a * .32f), new Vector2(r.xMin + 30f, rule), new Vector2(r.xMax - slant - 30f, rule),
            new Vector2(r.xMax - slant - 30f + .6f, rule + 2.5f), new Vector2(r.xMin + 30.6f, rule + 2.5f));
        // The pink triangle in the top left corner, along the top edge and down the slanted left one.
        float depth = Mathf.Min(CornerDeep, r.height * .5f);
        Vector2 down = new Vector2(r.xMin + slant * (1f - depth / r.height), r.yMax - depth);
        BandGraphic.Poly(vh, pink, topLeft, topLeft + new Vector2(CornerWide, 0f), down);
        // The pink shard past the right tip, and a smaller one past it.
        BandGraphic.Poly(vh, pink, new Vector2(r.xMax + 8f, r.yMax), new Vector2(r.xMax + 40f, r.yMax), new Vector2(r.xMax + 14f, r.yMax - 26f));
        BandGraphic.Poly(vh, pink, new Vector2(r.xMax + 48f, r.yMax - 5f), new Vector2(r.xMax + 62f, r.yMax - 5f), new Vector2(r.xMax + 52f, r.yMax - 16f));
    }

    private void Band(VertexHelper vh, Rect r, Color ink, Color pink)
    {
        BandGraphic.Poly(vh, ink, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax - slant, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin + slant, r.yMax));
        BandGraphic.Poly(vh, pink, new Vector2(r.xMin + 10f, r.yMin), new Vector2(r.xMin + 16f, r.yMin),
            new Vector2(r.xMin + 16f + slant, r.yMax), new Vector2(r.xMin + 10f + slant, r.yMax));
    }

    private static void Tile(VertexHelper vh, Rect r, Color ink, Color pink)
    {
        float c = Mathf.Min(TileCut, r.height * .3f);
        BandGraphic.Poly(vh, ink, new Vector2(r.xMin + c, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax - c),
            new Vector2(r.xMax - c, r.yMax), new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin + c));
        BandGraphic.Poly(vh, pink, new Vector2(r.xMin, r.yMax + 4f), new Vector2(r.xMin + 52f, r.yMax + 4f),
            new Vector2(r.xMin + 52f, r.yMax + 6.5f), new Vector2(r.xMin, r.yMax + 6.5f));
    }

    // A strip from a to b, as thick as the two widths at its ends.
    private static void Taper(VertexHelper vh, Vector2 a, Vector2 b, float widthA, float widthB, Color colour)
    {
        Vector2 along = b - a;
        if (along.sqrMagnitude < 1e-4f) return;
        Vector2 side = new Vector2(-along.y, along.x).normalized * .5f;
        BandGraphic.Poly(vh, colour, a + side * widthA, b + side * widthB, b - side * widthB, a - side * widthA);
    }
}
