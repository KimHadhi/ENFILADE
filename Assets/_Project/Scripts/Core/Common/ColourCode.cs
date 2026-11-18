using UnityEngine;

/// <summary>
/// A colour as a player types it and a setting keeps it: "#RRGGBB". One writer and one reader for every place a colour
/// is held as words (the player's colour and the colours of their own they keep, the colour board's code box), so they
/// agree on what counts as one: six hex digits, with the '#' or without, and nothing else. No colour names and no alpha:
/// "red" typed into a code box is not a colour, and a colour chosen is never see-through.
/// </summary>
public static class ColourCode
{
    /// <summary>The colour as "#RRGGBB".</summary>
    public static string Of(Color32 colour) => "#" + ColorUtility.ToHtmlStringRGB(colour);

    /// <summary>Reads "#RRGGBB" or "RRGGBB", spaces round it allowed. False for anything else.</summary>
    public static bool TryRead(string text, out Color32 colour)
    {
        colour = new Color32(255, 255, 255, 255);
        if (text == null) return false;
        string digits = text.Trim();
        if (digits.StartsWith("#")) digits = digits.Substring(1);
        if (digits.Length != 6) return false;
        int code = 0;
        foreach (char c in digits)
        {
            int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            if (digit < 0) return false;
            code = code * 16 + digit;
        }
        colour = new Color32((byte)(code >> 16), (byte)(code >> 8), (byte)code, 255);
        return true;
    }
}
