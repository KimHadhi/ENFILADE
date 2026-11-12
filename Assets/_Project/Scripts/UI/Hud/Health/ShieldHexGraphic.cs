using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shield's honeycomb over the whole screen (ShieldFeedback): one grid mesh whose vertex alpha is how much light each
/// part of the screen has (ShieldFeedback.LightAt), over a texture of hexagon cells with bright walls and dim, uneven
/// faces. The cells are laid out for a 16:9 screen so they stay regular. Tinted by the graphic's colour.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ShieldHexGraphic : MaskableGraphic
{
    private const int Columns = 32, Rows = 18;
    // The texture's size, and its cells' circumradius and wall width, in its px (about 2.1 screen px each at 1080p).
    private const int Width = 512, Height = 288;
    private const float CellRadius = 13f, Wall = 1.3f;
    private static Texture2D cells;
    internal ShieldFeedback Owner;

    public override Texture mainTexture => Cells;

    /// <summary>Builds the cell texture now (at spawn), not on the first hit.</summary>
    internal static void Prepare() => _ = Cells;

    private static Texture2D Cells
    {
        get
        {
            if (cells != null) return cells;
            cells = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                { name = "Shield Honeycomb", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[Width * Height];
            float apothem = CellRadius * .8660254f;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var point = new Vector2(x + .5f, y + .5f);
                Vector2Int cell = Nearest(point);
                Vector2 centre = new Vector2(CellRadius * 1.5f * cell.x, CellRadius * 1.7320508f * (cell.y + cell.x * .5f));
                float inside = -Hexagon(point - centre, apothem);
                // Bright walls, a glow just inside them, and each face a little brighter or dimmer than its neighbours.
                float wall = 1f - Mathf.SmoothStep(Wall * .5f, Wall * 1.5f, inside);
                float glow = Mathf.Exp(-inside / 2.2f) * .45f;
                float face = .1f + .22f * Hash(cell.x, cell.y);
                float alpha = Mathf.Clamp01(Mathf.Max(wall, face + glow));
                pixels[y * Width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            cells.SetPixels32(pixels);
            cells.Apply(false, true);
            return cells;
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (Owner == null) return;
        Rect rect = GetPixelAdjustedRect();
        Color tint = color;
        for (int y = 0; y <= Rows; y++)
        for (int x = 0; x <= Columns; x++)
        {
            float u = (float)x / Columns, v = (float)y / Rows;
            Color vertex = tint;
            vertex.a *= Owner.LightAt(new Vector2(u * 2f - 1f, v * 2f - 1f));
            mesh.AddVert(new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v), vertex, new Vector2(u, v));
            if (x == Columns || y == Rows) continue;
            int a = y * (Columns + 1) + x;
            mesh.AddTriangle(a, a + Columns + 1, a + 1);
            mesh.AddTriangle(a + 1, a + Columns + 1, a + Columns + 2);
        }
    }

    // The flat-topped hexagon whose centre is nearest: axial coordinates, rounded through cube coordinates.
    private static Vector2Int Nearest(Vector2 point)
    {
        float q = point.x * (2f / 3f) / CellRadius;
        float r = (-point.x / 3f + point.y * .57735027f) / CellRadius;
        float s = -q - r;
        float rq = Mathf.Round(q), rr = Mathf.Round(r), rs = Mathf.Round(s);
        float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        return new Vector2Int((int)rq, (int)rr);
    }

    // Signed distance to a flat-topped hexagon of this apothem (below 0 inside), after Inigo Quilez's sdHexagon.
    private static float Hexagon(Vector2 p, float apothem)
    {
        const float kx = -.8660254f, ky = .5f, kz = .57735027f;
        p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
        float fold = 2f * Mathf.Min(kx * p.x + ky * p.y, 0f);
        p -= new Vector2(kx, ky) * fold;
        p -= new Vector2(Mathf.Clamp(p.x, -kz * apothem, kz * apothem), apothem);
        return p.magnitude * Mathf.Sign(p.y);
    }

    private static float Hash(int x, int y)
    {
        float value = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }
}
