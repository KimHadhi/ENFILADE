using UnityEngine;

/// <summary>Plain shapes for player-drawn crosshairs, made in code. A square needs none: the view falls back to white.</summary>
public static class CrosshairShapes
{
    private const int Size = 64;
    private static Sprite circle;

    /// <summary>Whether this is <see cref="Circle"/>, without making it when nothing has asked for it yet.</summary>
    public static bool IsCircle(Sprite sprite) => sprite != null && sprite == circle;

    /// <summary>A white disc with a soft one-texel edge, mipmapped so it stays round when drawn a few pixels wide.</summary>
    public static Sprite Circle
    {
        get
        {
            if (circle != null) return circle;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Crosshair Circle", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[Size * Size];
            float radius = Size * .5f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float distance = new Vector2(x + .5f - radius, y + .5f - radius).magnitude;
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - distance) * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            circle = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(.5f, .5f));
            circle.name = texture.name;
            circle.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return circle;
        }
    }
}
