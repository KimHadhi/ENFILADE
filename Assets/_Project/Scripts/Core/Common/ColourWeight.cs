using UnityEngine;

/// <summary>
/// How light a colour is to the eye (its weight: 0 black to 1 white, the relative luminance of an sRGB colour), and which
/// colours of a hue are light enough for a least weight. One measure for whoever must keep a colour readable on something
/// dark: the rule that a player's colour reads on the HUD's near-black plates (PlayerColours.Readable), and the colour
/// board, which shows only the colours that rule lets through (ColourPanel: its square runs from the dimmest allowed
/// brightness up, and to the strongest allowed strength across, so nothing on it has to be refused).
/// A colour is a hue, a strength and a brightness here (each 0 to 1, as Color.HSVToRGB takes them), always rounded to
/// whole steps before it is weighed, so what is promised of a colour is true of the very bytes that are kept and sent.
/// </summary>
public static class ColourWeight
{
    // Halvings when a least brightness or a greatest strength is looked for: past 8 the whole steps no longer change.
    private const int Halvings = 12;

    /// <summary>The colour's weight: 0 black to 1 white.</summary>
    public static float Of(Color32 colour)
    {
        return .2126f * Linear(colour.r) + .7152f * Linear(colour.g) + .0722f * Linear(colour.b);

        static float Linear(byte channel)
        {
            float c = channel / 255f;
            return c <= .04045f ? c / 12.92f : Mathf.Pow((c + .055f) / 1.055f, 2.4f);
        }
    }

    /// <summary>The colour of this hue, strength and brightness, in whole steps and opaque.</summary>
    public static Color32 Colour(float hue, float strength, float bright)
    {
        Color32 solid = Color.HSVToRGB(Mathf.Clamp01(hue), Mathf.Clamp01(strength), Mathf.Clamp01(bright));
        solid.a = 255;
        return solid;
    }

    /// <summary>
    /// The least brightness at which this hue and strength weighs <paramref name="least"/>: every brightness from it up
    /// does too. 0 where any does (least 0), and 1 where not even the brightest does (a deep blue against a high least:
    /// ask Strongest first).
    /// </summary>
    public static float Dimmest(float hue, float strength, float least)
    {
        if (least <= 0f) return 0f;
        if (Of(Colour(hue, strength, 1f)) < least) return 1f;
        float low = 0f, high = 1f;
        for (int i = 0; i < Halvings; i++)
        {
            float middle = (low + high) * .5f;
            if (Of(Colour(hue, strength, middle)) >= least) high = middle; else low = middle;
        }
        return high;
    }

    /// <summary>
    /// The greatest strength at which this hue, at its brightest, weighs <paramref name="least"/>: 1 for most hues, less
    /// for one whose pure colour is too dark (blue, against a least above its own weight), and every paler one does too.
    /// </summary>
    public static float Strongest(float hue, float least)
    {
        if (least <= 0f || Of(Colour(hue, 1f, 1f)) >= least) return 1f;
        float pale = 0f, strong = 1f;
        for (int i = 0; i < Halvings; i++)
        {
            float middle = (pale + strong) * .5f;
            if (Of(Colour(hue, middle, 1f)) >= least) pale = middle; else strong = middle;
        }
        return pale;
    }

    /// <summary>
    /// The colour itself when it weighs <paramref name="least"/> or more, else the nearest of its hue that does: brighter
    /// at its own strength, and, where that hue's brightest is still too dark, paler too. What it gives always weighs
    /// enough, so asking again changes nothing.
    /// </summary>
    public static Color32 AtLeast(Color32 colour, float least)
    {
        colour.a = 255;
        if (least <= 0f || Of(colour) >= least) return colour;
        Color.RGBToHSV(colour, out float hue, out float strength, out float bright);
        strength = Mathf.Min(strength, Strongest(hue, least));
        Color32 made = Colour(hue, strength, Mathf.Max(bright, Dimmest(hue, strength, least)));
        // Nothing above should fall short; were a step of rounding ever to, a step lighter at a time until it does not.
        for (int guard = 0; guard < 255 && Of(made) < least; guard++)
            made = new Color32((byte)Mathf.Min(255, made.r + 1), (byte)Mathf.Min(255, made.g + 1), (byte)Mathf.Min(255, made.b + 1), 255);
        return made;
    }
}
