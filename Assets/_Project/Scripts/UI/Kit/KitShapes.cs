using System;
using UnityEngine;

/// <summary>
/// The menus' few shapes, drawn once in code into small white textures and tinted where they are used: the cut-corner
/// card (a 9-sliced sprite), the rangefinder rings, soft glows, a light beam, the screen's dark edges, film grain and a
/// soft dot. Made on first use and kept.
/// </summary>
public static class KitShapes
{
    // The cut-corner sprite: its size and the 9-slice border (px).
    private const int ChamferSize = 32, ChamferBorder = 12;
    /// <summary>How much of each cut corner is cut, along both edges (sprite px): KitHit cuts clicks off the same corners.</summary>
    public const float ChamferCut = 10f;

    private static Sprite chamfer;
    private static Texture2D ring, dashes, glow, beam, vignette, grain, dot, fade;

    /// <summary>White, with its top-right and bottom-left corners cut on the diagonal; sliced so the cut stays 10 px.</summary>
    public static Sprite Chamfer
    {
        get
        {
            if (chamfer != null) return chamfer;
            var texture = Draw("Kit Chamfer", ChamferSize, ChamferSize, (x, y) =>
            {
                float sum = x + y + 1f, far = 2f * ChamferSize - ChamferCut;
                float topRight = Mathf.Clamp01(.5f - (sum - far) / 1.4142f);
                float bottomLeft = Mathf.Clamp01(.5f + (sum - ChamferCut) / 1.4142f);
                return Mathf.Min(topRight, bottomLeft);
            });
            chamfer = Sprite.Create(texture, new Rect(0f, 0f, ChamferSize, ChamferSize), new Vector2(.5f, .5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(ChamferBorder, ChamferBorder, ChamferBorder, ChamferBorder));
            chamfer.name = "Kit Chamfer";
            return chamfer;
        }
    }

    /// <summary>A thin circle with a tick every 5 degrees, longer every 30: the rangefinder's outer ring.</summary>
    public static Texture2D Ring => ring != null ? ring : ring = Draw("Kit Ring", 512, 512, (x, y) =>
    {
        Polar(x, y, 512, out float r, out float degrees, out float pixels);
        float line = Band(r, .985f, 1.2f / pixels);
        float nearest = Mathf.Round(degrees / 5f) * 5f;
        float across = Mathf.Abs(Mathf.DeltaAngle(degrees, nearest)) * Mathf.Deg2Rad * r * pixels;
        bool major = Mathf.RoundToInt(nearest) % 30 == 0;
        float inner = major ? .9f : .945f;
        float tick = r > inner && r < .985f ? Mathf.Clamp01((major ? 1.4f : 1f) - across) : 0f;
        return Mathf.Max(line, tick);
    });

    /// <summary>A thin dashed circle: the rangefinder's inner ring.</summary>
    public static Texture2D Dashes => dashes != null ? dashes : dashes = Draw("Kit Dashes", 512, 512, (x, y) =>
    {
        Polar(x, y, 512, out float r, out float degrees, out float pixels);
        float phase = Mathf.Repeat(degrees, 12f);
        float on = Mathf.Clamp01(Mathf.Min(phase, 7f - phase) * r * pixels * Mathf.Deg2Rad + .5f);
        return Band(r, .97f, 1.5f / pixels) * on;
    });

    /// <summary>A soft round glow, brightest in the middle.</summary>
    public static Texture2D Glow => glow != null ? glow : glow = Draw("Kit Glow", 128, 128, (x, y) =>
    {
        Polar(x, y, 128, out float r, out _, out _);
        return Mathf.Clamp01(Mathf.Exp(-r * r * 3.2f) - Mathf.Exp(-3.2f)) / (1f - Mathf.Exp(-3.2f));
    });

    /// <summary>A soft upright shaft of light, fading toward its foot.</summary>
    public static Texture2D Beam => beam != null ? beam : beam = Draw("Kit Beam", 64, 256, (x, y) =>
    {
        float across = (x + .5f) / 32f - 1f, up = (y + .5f) / 256f;
        return Mathf.Exp(-across * across * 6f) * Mathf.SmoothStep(0f, 1f, up) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - up) / .15f));
    });

    /// <summary>The screen's dark edges: clear in the middle, darkening toward the corners.</summary>
    public static Texture2D Vignette => vignette != null ? vignette : vignette = Draw("Kit Vignette", 256, 256, (x, y) =>
    {
        Polar(x, y, 256, out float r, out _, out _);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1.42f, r));
    });

    /// <summary>Film grain: grey noise, opaque, to be laid over everything very faintly.</summary>
    public static Texture2D Grain
    {
        get
        {
            if (grain != null) return grain;
            var random = new System.Random(1987);
            grain = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "Kit Grain", hideFlags = HideFlags.DontSave };
            var pixels = new Color32[128 * 128];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = (byte)random.Next(256);
                pixels[i] = new Color32(value, value, value, 255);
            }
            grain.SetPixels32(pixels);
            grain.wrapMode = TextureWrapMode.Repeat;
            grain.filterMode = FilterMode.Point;
            grain.Apply(false, true);
            return grain;
        }
    }

    /// <summary>Solid at its left edge, fading out to the right (flip it with the image's uvRect for the other way).</summary>
    public static Texture2D Fade => fade != null ? fade : fade = Draw("Kit Fade", 128, 4, (x, y) =>
    {
        float u = (x + .5f) / 128f;
        return Mathf.Pow(1f - u, 1.6f);
    });

    /// <summary>A small soft dot (dust in the light, status lights).</summary>
    public static Texture2D Dot => dot != null ? dot : dot = Draw("Kit Dot", 16, 16, (x, y) =>
    {
        Polar(x, y, 16, out float r, out _, out _);
        return Mathf.Clamp01((1f - r) * 2.2f);
    });

    // Where a pixel is from the middle of a square texture: r from 0 (middle) to 1 (edge's midpoint), the angle in
    // degrees (0 up, clockwise), and how many pixels r = 1 is.
    private static void Polar(int x, int y, int size, out float r, out float degrees, out float pixels)
    {
        pixels = size * .5f;
        float dx = (x + .5f - pixels) / pixels, dy = (y + .5f - pixels) / pixels;
        r = Mathf.Sqrt(dx * dx + dy * dy);
        degrees = Mathf.Repeat(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg, 360f);
    }

    // 1 on a circle of radius at, fading out over a pixel either side of half width.
    private static float Band(float r, float at, float halfWidth) => Mathf.Clamp01(1f - (Mathf.Abs(r - at) - halfWidth) / (halfWidth + .004f));

    private static Texture2D Draw(string name, int width, int height, Func<int, int, float> alpha)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
        texture.SetPixels32(pixels);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(false, true);
        return texture;
    }
}
