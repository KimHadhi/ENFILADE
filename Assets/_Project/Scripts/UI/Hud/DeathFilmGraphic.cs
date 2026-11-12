using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The death screen's film over the world (DeathScreen), drawn under the HUD as the MW3 Death Screen addon draws its
/// vignette and blood (the user's reference, GMod workshop 3833150063). None of the addon's textures are used (they come
/// from Call of Duty); both layers are drawn here from a fixed seed, so they look the same every death and on every
/// screen shape:
/// - Ink: the edges darken toward the corners, flecked with dark and dried-blood specks, a few in the HUD's pink; the
///   middle stays clear.
/// - Blood: soft splotches creeping in from the corners and edges, drips running down from the top edge, droplets
///   round them. The middle stays clear.
/// Its strength is the CanvasRenderer's alpha, so fading it never rebuilds the mesh. The mesh is built for the rect
/// it fills and again only when that changes.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DeathFilmGraphic : MaskableGraphic
{
    public enum Layer : byte { Ink, Blood }

    private const int Columns = 24, Rows = 14, Specks = 170, Droplets = 40, Drips = 5;
    private static readonly Color InkColour = new Color(.03f, .02f, .03f), Dried = new Color(.38f, .03f, .06f);
    private static readonly Color Blood = new Color(.4f, .02f, .05f), Bright = new Color(.52f, .03f, .06f);
    // Where the splotches sit (share of the screen, 0,0 bottom left) and how big they are (share of its short side).
    private static readonly Vector3[] Splotches =
    {
        new Vector3(.02f, .04f, .26f), new Vector3(.98f, .95f, .2f), new Vector3(0f, .55f, .12f),
        new Vector3(.99f, .18f, .1f), new Vector3(.62f, 1f, .09f), new Vector3(.25f, 1f, .07f),
    };

    private Layer layer;
    private int seed = 7331;

    public void Configure(Layer which, int fixedSeed)
    {
        layer = which;
        seed = fixedSeed;
        raycastTarget = false;
        color = Color.white;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width < 1f || r.height < 1f) return;
        var random = new System.Random(seed);
        float unit = Mathf.Min(r.width, r.height) / 1080f;
        if (layer == Layer.Ink)
        {
            Ring(vh, r);
            Flecks(vh, r, random, unit);
        }
        else Splatter(vh, r, random, unit);
    }

    // How far toward the edge a point is: 0 in the middle, about .7 at the middle of a side, 1 in a corner.
    private static float Edge(float u, float v)
    {
        float x = u * 2f - 1f, y = v * 2f - 1f;
        return Mathf.Sqrt(x * x * .86f + y * y) / 1.364f;
    }

    private static float Smooth(float from, float to, float x)
    {
        float t = Mathf.Clamp01((x - from) / (to - from));
        return t * t * (3f - 2f * t);
    }

    private static void Ring(VertexHelper vh, Rect r)
    {
        int first = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        for (int row = 0; row <= Rows; row++)
            for (int column = 0; column <= Columns; column++)
            {
                float u = column / (float)Columns, v = row / (float)Rows;
                float shade = Mathf.Pow(Smooth(.45f, 1f, Edge(u, v)), 1.3f);
                vertex.position = new Vector2(Mathf.Lerp(r.xMin, r.xMax, u), Mathf.Lerp(r.yMin, r.yMax, v));
                vertex.color = new Color(InkColour.r, InkColour.g, InkColour.b, shade);
                vh.AddVert(vertex);
            }
        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                int a = first + row * (Columns + 1) + column, b = a + 1, c = a + Columns + 1, d = c + 1;
                vh.AddTriangle(a, c, d);
                vh.AddTriangle(a, d, b);
            }
    }

    // Specks mostly where the ring is dark, more of them the nearer the corners.
    private static void Flecks(VertexHelper vh, Rect r, System.Random random, float unit)
    {
        int placed = 0;
        for (int tries = 0; placed < Specks && tries < Specks * 12; tries++)
        {
            float u = (float)random.NextDouble(), v = (float)random.NextDouble();
            float ring = Smooth(.55f, 1f, Edge(u, v));
            if ((float)random.NextDouble() > Mathf.Pow(ring, 1.4f)) continue;
            placed++;
            float size = (2f + 7f * Sq((float)random.NextDouble())) * unit;
            double pick = random.NextDouble();
            Color tint = pick < .7 ? InkColour : pick < .96 ? Dried : KitStyle.Accent;
            tint.a = (.45f + .5f * (float)random.NextDouble()) * Mathf.Min(1f, ring * 1.3f) * (pick >= .96 ? .6f : 1f);
            Blob(vh, new Vector2(Mathf.Lerp(r.xMin, r.xMax, u), Mathf.Lerp(r.yMin, r.yMax, v)), size, tint, tint, random, 6, .25f);
        }
    }

    private static void Splatter(VertexHelper vh, Rect r, System.Random random, float unit)
    {
        float shortSide = Mathf.Min(r.width, r.height);
        foreach (var spot in Splotches)
        {
            Vector2 at = new Vector2(Mathf.Lerp(r.xMin, r.xMax, spot.x), Mathf.Lerp(r.yMin, r.yMax, spot.y));
            float radius = spot.z * shortSide;
            Soft(vh, at, radius, random);
            // Droplets thrown round it.
            for (int i = 0; i < Droplets / Splotches.Length; i++)
            {
                float turn = (float)random.NextDouble() * Mathf.PI * 2f, far = radius * (.7f + .9f * (float)random.NextDouble());
                Vector2 drop = at + new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)) * far;
                Color tint = Bright;
                tint.a = .55f + .35f * (float)random.NextDouble();
                Blob(vh, drop, (1.5f + 4.5f * (float)random.NextDouble()) * unit, tint, tint, random, 7, .15f);
            }
        }
        // Drips from the top edge.
        for (int i = 0; i < Drips; i++)
        {
            float x = Mathf.Lerp(r.xMin, r.xMax, .1f + .8f * (float)random.NextDouble());
            float length = r.height * (.08f + .14f * (float)random.NextDouble()), width = (5f + 4f * (float)random.NextDouble()) * unit;
            Vector2 top = new Vector2(x, r.yMax), bottom = new Vector2(x + (float)(random.NextDouble() - .5) * 6f * unit, r.yMax - length);
            Color start = Blood, end = Blood;
            start.a = .75f;
            end.a = .1f;
            Streak(vh, top, bottom, width, width * .25f, start, end);
            Color bead = Bright;
            bead.a = .5f;
            Blob(vh, bottom, width * .8f, bead, bead, random, 8, .1f);
        }
    }

    // A soft splotch: solid in its core, fading to nothing at a ragged rim.
    private static void Soft(VertexHelper vh, Vector2 at, float radius, System.Random random)
    {
        const int Sides = 20;
        float p1 = (float)random.NextDouble() * 6.28f, p2 = (float)random.NextDouble() * 6.28f, p3 = (float)random.NextDouble() * 6.28f;
        int centre = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        Color core = Blood, mid = Blood, rim = Blood;
        core.a = .9f;
        mid.a = .55f;
        rim.a = 0f;
        vertex.position = at;
        vertex.color = core;
        vh.AddVert(vertex);
        for (int i = 0; i < Sides; i++)
        {
            float turn = i / (float)Sides * Mathf.PI * 2f;
            float rag = 1f + .22f * Mathf.Sin(3f * turn + p1) + .12f * Mathf.Sin(7f * turn + p2) + .07f * Mathf.Sin(13f * turn + p3);
            Vector2 way = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)) * (radius * rag);
            vertex.position = at + way * .55f;
            vertex.color = mid;
            vh.AddVert(vertex);
            vertex.position = at + way;
            vertex.color = rim;
            vh.AddVert(vertex);
        }
        for (int i = 0; i < Sides; i++)
        {
            int inner = centre + 1 + i * 2, outer = inner + 1;
            int nextInner = centre + 1 + (i + 1) % Sides * 2, nextOuter = nextInner + 1;
            vh.AddTriangle(centre, inner, nextInner);
            vh.AddTriangle(inner, outer, nextOuter);
            vh.AddTriangle(inner, nextOuter, nextInner);
        }
    }

    // A small solid blob with a slightly ragged edge.
    private static void Blob(VertexHelper vh, Vector2 at, float radius, Color inner, Color outer, System.Random random, int sides, float ragged)
    {
        int centre = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.position = at;
        vertex.color = inner;
        vh.AddVert(vertex);
        float start = (float)random.NextDouble() * 6.28f;
        for (int i = 0; i < sides; i++)
        {
            float turn = start + i / (float)sides * Mathf.PI * 2f;
            float reach = radius * (1f + ragged * ((float)random.NextDouble() * 2f - 1f));
            vertex.position = at + new Vector2(Mathf.Cos(turn), Mathf.Sin(turn)) * reach;
            vertex.color = outer;
            vh.AddVert(vertex);
        }
        for (int i = 0; i < sides; i++) vh.AddTriangle(centre, centre + 1 + i, centre + 1 + (i + 1) % sides);
    }

    private static void Streak(VertexHelper vh, Vector2 a, Vector2 b, float widthA, float widthB, Color colourA, Color colourB)
    {
        Vector2 along = b - a;
        if (along.sqrMagnitude < 1e-4f) return;
        Vector2 side = new Vector2(-along.y, along.x).normalized * .5f;
        int first = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.color = colourA;
        vertex.position = a + side * widthA; vh.AddVert(vertex);
        vertex.position = a - side * widthA; vh.AddVert(vertex);
        vertex.color = colourB;
        vertex.position = b - side * widthB; vh.AddVert(vertex);
        vertex.position = b + side * widthB; vh.AddVert(vertex);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }

    private static float Sq(float x) => x * x;
}
