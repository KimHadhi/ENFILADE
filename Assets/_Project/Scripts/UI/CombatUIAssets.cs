using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Procedural white icons and blur sprites for combat UI, so the feature works without imported art.
/// Textures are white with alpha coverage; tint them with Graphic.color.
/// </summary>
public static class CombatUIAssets
{
    private static Texture2D criticalBurst, skull, glowTexture, stripes, counterArrow, executionKnife, slashLine, bladeSlash;
    private static Sprite glowSprite;
    private static readonly Dictionary<(TMP_FontAsset, Color, float), Material> outlined = new Dictionary<(TMP_FontAsset, Color, float), Material>();

    /// <summary>Eight-point burst: long cardinal tips, shorter diagonals.</summary>
    public static Texture2D CriticalBurst => criticalBurst != null ? criticalBurst : criticalBurst = Rasterize("Critical Burst (Runtime)", 64, BurstCoverage);
    public static Texture2D Skull => skull != null ? skull : skull = Rasterize("Kill Skull (Runtime)", 64, SkullDistance);
    /// <summary>Counter-clockwise return arrow: a ring with a gap and an arrowhead.</summary>
    public static Texture2D CounterArrow => counterArrow != null ? counterArrow : counterArrow = Rasterize("Counter Arrow (Runtime)", 64, CounterShape);
    /// <summary>Tactical knife tipped up-left with a crescent slash trail sweeping below it: the execution mark.</summary>
    public static Texture2D ExecutionKnife => executionKnife != null ? executionKnife : executionKnife = Rasterize("Execution Knife (Runtime)", 64, ExecutionShape);

    /// <summary>Tapered sword-cut stroke along +x: needle-thin at both ends, thickest near the right (where the cut starts).</summary>
    public static Texture2D BladeSlash
    {
        get
        {
            if (bladeSlash != null) return bladeSlash;
            const int width = 128, height = 16;
            bladeSlash = NewTexture("Blade Slash (Runtime)", width, height, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width, v = Mathf.Abs((y + .5f) / height * 2f - 1f);
                float half = Mathf.Pow(Mathf.Sin(Mathf.PI * u), .6f) * (.55f + .45f * u);
                float a = Mathf.Clamp01((half - v) / .18f);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            bladeSlash.SetPixels32(pixels); bladeSlash.Apply(false, true);
            return bladeSlash;
        }
    }

    /// <summary>Thin horizontal streak, opaque in the middle and fading at both ends and edges.</summary>
    public static Texture2D SlashLine
    {
        get
        {
            if (slashLine != null) return slashLine;
            const int width = 128, height = 8;
            slashLine = NewTexture("Slash Line (Runtime)", width, height, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width, v = Mathf.Abs((y + .5f) / height * 2f - 1f);
                float ends = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, 1f - u) / .22f));
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - v) / .45f));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(ends * edge * 255f));
            }
            slashLine.SetPixels32(pixels); slashLine.Apply(false, true);
            return slashLine;
        }
    }

    /// <summary>Tileable 45° stripes, half duty, white with alpha. Use with RawImage.uvRect to repeat.</summary>
    public static Texture2D TrailStripes
    {
        get
        {
            if (stripes != null) return stripes;
            const int size = 16;
            stripes = NewTexture("Trail Stripes (Runtime)", size, size, false);
            stripes.wrapMode = TextureWrapMode.Repeat;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int inside = 0;
                for (int s = 0; s < 16; s++)
                {
                    float u = ((x + (s % 4 + .5f) / 4f) + (y + (s / 4 + .5f) / 4f)) % size;
                    if (u < size * .5f) inside++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 16));
            }
            stripes.SetPixels32(pixels); stripes.Apply(false, true);
            return stripes;
        }
    }

    /// <summary>Gaussian glow sliced horizontally, so any number width gets the same soft radius.</summary>
    public static Sprite SoftGlow
    {
        get
        {
            if (glowSprite != null) return glowSprite;
            const int width = 128, height = 64; const float radius = 32f;
            glowTexture = NewTexture("Soft Glow (Runtime)", width, height, false);
            var pixels = new Color32[width * height];
            float floor = Mathf.Exp(-3.2f);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Distance to a horizontal core segment, normalized by the blur radius.
                float px = x + .5f, py = y + .5f;
                float dx = Mathf.Max(0f, Mathf.Max(radius - px, px - (width - radius)));
                float dy = py - height * .5f;
                float d2 = (dx * dx + dy * dy) / (radius * radius);
                float a = Mathf.Clamp01((Mathf.Exp(-3.2f * d2) - floor) / (1f - floor));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            glowTexture.SetPixels32(pixels); glowTexture.Apply(false, false);
            glowSprite = Sprite.Create(glowTexture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, 0f, radius, 0f));
            glowSprite.name = "Soft Glow (Runtime)"; glowSprite.hideFlags = HideFlags.HideAndDontSave;
            return glowSprite;
        }
    }

    /// <summary>Shared TMP material with a dark outline, one per font/colour/width.</summary>
    public static Material Outlined(TMP_FontAsset font, Color outline, float width)
    {
        if (font == null || font.material == null) return null;
        var key = (font, outline, width);
        if (outlined.TryGetValue(key, out var material) && material != null) return material;
        material = new Material(font.material) { name = font.name + " Outline (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        material.EnableKeyword(ShaderUtilities.Keyword_Outline);
        if (material.HasProperty(ShaderUtilities.ID_OutlineWidth)) material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        if (material.HasProperty(ShaderUtilities.ID_OutlineColor)) material.SetColor(ShaderUtilities.ID_OutlineColor, outline);
        if (material.HasProperty(ShaderUtilities.ID_FaceDilate)) material.SetFloat(ShaderUtilities.ID_FaceDilate, width * .5f);
        outlined[key] = material;
        return material;
    }

    /// <summary>
    /// Ellipsis when TMP can find "…" for this text's font and style, otherwise Truncate. TMP would switch to Truncate
    /// anyway, with a console warning per text: the Korean UI fonts have no ellipsis, and bold text in a font without a
    /// bold typeface only searches fallbacks, never the regular glyphs. Call after setting the font and style.
    /// </summary>
    public static TextOverflowModes ClippedOverflow(TMP_Text text)
    {
        if (text == null || text.font == null) return TextOverflowModes.Truncate;
        FontStyles style = text.fontStyle;
        FontWeight weight = (style & FontStyles.Bold) == FontStyles.Bold ? FontWeight.Bold : text.fontWeight;
        bool CanDraw(TMP_FontAsset font) => font != null
            && TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, font, true, style, weight, out _) != null;
        if (CanDraw(text.font) || CanDraw(TMP_Settings.defaultFontAsset)) return TextOverflowModes.Ellipsis;
        var fallbacks = TMP_Settings.fallbackFontAssets;
        if (fallbacks != null)
            foreach (var font in fallbacks)
                if (CanDraw(font)) return TextOverflowModes.Ellipsis;
        return TextOverflowModes.Truncate;
    }

    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred)
    {
        if (preferred != null) return preferred;
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) return settings.promptFont;
        return TMP_Settings.defaultFontAsset;
    }

    private delegate float Shape(Vector2 p);

    /// <summary>4x4 supersampled coverage of a shape whose distance is negative inside. Coordinates span [-1, 1].</summary>
    private static Texture2D Rasterize(string name, int size, Shape distance)
    {
        var texture = NewTexture(name, size, size, true);
        var pixels = new Color32[size * size];
        const int samples = 4;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int inside = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                var p = new Vector2((x + (sx + .5f) / samples) / size * 2f - 1f, (y + (sy + .5f) / samples) / size * 2f - 1f);
                if (distance(p) < 0f) inside++;
            }
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / (samples * samples)));
        }
        texture.SetPixels32(pixels); texture.Apply(true, true);
        return texture;
    }

    private static Texture2D NewTexture(string name, int width, int height, bool mips)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, mips)
        { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
    }

    private static readonly Vector2[] burst = BuildBurst();
    private static Vector2[] BuildBurst()
    {
        var points = new Vector2[16];
        for (int i = 0; i < points.Length; i++)
        {
            float radius = i % 2 == 1 ? .36f : (i / 2) % 2 == 0 ? .96f : .64f;
            float angle = (90f + i * 22.5f) * Mathf.Deg2Rad;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }
    // Crossing test: -1 inside the star polygon, 1 outside.
    private static float BurstCoverage(Vector2 p) => InPolygon(p, burst) ? -1f : 1f;

    private static float SkullDistance(Vector2 p)
    {
        float head = Mathf.Min(Circle(p, new Vector2(0f, .2f), .72f), RoundBox(p, new Vector2(0f, -.42f), new Vector2(.42f, .3f), .14f));
        float notches = Mathf.Min(Circle(p, new Vector2(-.58f, -.3f), .16f), Circle(p, new Vector2(.58f, -.3f), .16f));
        float eyes = Mathf.Min(Circle(p, new Vector2(-.29f, .1f), .21f), Circle(p, new Vector2(.29f, .1f), .21f));
        float nose = RoundBox(p, new Vector2(0f, -.19f), new Vector2(.065f, .085f), .03f);
        float teeth = Mathf.Min(RoundBox(p, new Vector2(-.15f, -.63f), new Vector2(.03f, .15f), 0f),
            Mathf.Min(RoundBox(p, new Vector2(0f, -.63f), new Vector2(.03f, .15f), 0f), RoundBox(p, new Vector2(.15f, -.63f), new Vector2(.03f, .15f), 0f)));
        float holes = Mathf.Min(Mathf.Min(notches, eyes), Mathf.Min(nose, teeth));
        return Mathf.Max(head, -holes);
    }
    private static float CounterShape(Vector2 p)
    {
        Vector2 centre = new Vector2(.08f, -.12f); const float radius = .5f, half = .15f;
        Vector2 d = p - centre;
        float angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 360f);
        bool arc = Mathf.Abs(d.magnitude - radius) < half && (angle >= 200f || angle <= 110f);
        float end = 110f * Mathf.Deg2Rad;
        Vector2 normal = new Vector2(Mathf.Cos(end), Mathf.Sin(end)), tangent = new Vector2(-normal.y, normal.x);
        Vector2 tip = centre + normal * radius;
        bool head = InTriangle(p, tip + tangent * .42f, tip + normal * .36f, tip - normal * .36f);
        return arc || head ? -1f : 1f;
    }
    // Execution knife outlines, y down (drafted as SVG). Blade: straight edge, curved belly to a clipped tip.
    private static readonly Vector2[] knifeBlade = Concat(new[] { new Vector2(-.17f, .12f) },
        Quadratic(new Vector2(-.17f, -.42f), new Vector2(-.15f, -.8f), new Vector2(.12f, -.98f)), new[] { new Vector2(.17f, -.68f), new Vector2(.17f, .12f) });
    // Slash trail: crescent between two curves from near the tip down to the bottom, bulging lower-left.
    private static readonly Vector2[] slashTrail = Concat(
        Quadratic(new Vector2(-.86f, -.22f), new Vector2(-.82f, .98f), new Vector2(.42f, .9f)),
        Quadratic(new Vector2(.42f, .9f), new Vector2(-.42f, .62f), new Vector2(-.86f, -.22f)));

    private static float ExecutionShape(Vector2 p)
    {
        var q = new Vector2(p.x, -p.y);
        if (InPolygon(q, slashTrail)) return -1f;
        // Knife space: undo the -40° tilt about its offset centre.
        Vector2 d = q - new Vector2(.18f, -.05f);
        float cos = Mathf.Cos(40f * Mathf.Deg2Rad), sin = Mathf.Sin(40f * Mathf.Deg2Rad);
        var k = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
        bool knife = InPolygon(k, knifeBlade) || RoundBox(k, new Vector2(0f, .165f), new Vector2(.3f, .065f), .04f) < 0f
            || RoundBox(k, new Vector2(0f, .505f), new Vector2(.11f, .275f), .06f) < 0f
            || RoundBox(k, new Vector2(0f, .81f), new Vector2(.14f, .05f), .04f) < 0f;
        return knife ? -1f : 1f;
    }
    private static Vector2[] Quadratic(Vector2 a, Vector2 control, Vector2 b)
    {
        var points = new Vector2[13];
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / (points.Length - 1f);
            points[i] = Vector2.Lerp(Vector2.Lerp(a, control, t), Vector2.Lerp(control, b, t), t);
        }
        return points;
    }
    private static Vector2[] Concat(params Vector2[][] runs)
    {
        var points = new List<Vector2>();
        foreach (var run in runs) points.AddRange(run);
        return points.ToArray();
    }
    private static bool InPolygon(Vector2 p, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }
    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
        bool negative = d1 < 0f || d2 < 0f || d3 < 0f, positive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(negative && positive);
    }
    private static float Edge(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    private static float Circle(Vector2 p, Vector2 centre, float radius) => (p - centre).magnitude - radius;
    private static float RoundBox(Vector2 p, Vector2 centre, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + Vector2.one * radius;
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }
}
