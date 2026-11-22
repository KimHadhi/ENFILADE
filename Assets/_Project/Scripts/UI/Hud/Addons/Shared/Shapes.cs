using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The one picture every flat shape is drawn with: white, holding a quarter disc for rounded corners. Garry's Mod rounds
    /// draw.RoundedBox's corners with corner pictures (gui/corner8 ... corner512) between flat rectangles; with the disc
    /// and a solid white spot in one picture, rectangles, outlines, lines, polygons and rounded boxes all share one mesh
    /// and one draw call, in the order they were drawn. The disc is centred on the picture's bottom-right corner (u, v = 1,
    /// 1 in the addons' top-down coordinates) as GMod's corner pictures are, and flat colour samples that same corner,
    /// deep inside it. Mipmaps keep a corner's edge about one pixel soft at any radius, as the sized corner pictures did.
    /// </summary>
    internal static class Shapes
    {
        private const int Size = 128;

        /// <summary>Where a flat colour samples (the addons' top-down u, v).</summary>
        public const float SolidU = 1f, SolidV = 1f;

        private static Texture2D picture;

        public static Texture2D Picture => picture != null ? picture : picture = Make();

        private static Texture2D Make()
        {
            var made = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false)
            {
                name = "HudAddons Shapes",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            var pixels = new Color32[Size * Size];
            for (int row = 0; row < Size; row++)
            for (int column = 0; column < Size; column++)
            {
                // A texture's rows run up from the bottom; the addons' v runs down from the top.
                float u = (column + .5f) / Size, v = 1f - (row + .5f) / Size;
                float du = 1f - u, dv = 1f - v;
                float coverage = Mathf.Clamp01((1f - Mathf.Sqrt(du * du + dv * dv)) * Size + .5f);
                pixels[row * Size + column] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
            }
            made.SetPixels32(pixels);
            made.Apply(true, true);
            return made;
        }
    }
}
