using TMPro;
using UnityEngine;

/// <summary>
/// The look our own HUD pieces share with the addon HUD (the user, 2026-11-22: "디자인을 통일감 있게", their pick: the
/// addon's colours only, no pink): the HUD Mk II's faint black box, its white text, its grey, its low red, loss orange and
/// over-full blue (read from its table, Tables/Hud/PlayerHud.csv Colour rows, so the two never drift apart), and the face
/// the addon HUD's font option names (AddonFonts.csv: Hitmarker Normal by default, Hangul from TMP's fallbacks). A piece
/// keeps its own shapes, layout and motion; only its surfaces take these. White is the emphasis (the slot in hand, what is
/// ready), as the addon's selector lights the chosen box.
/// </summary>
public static class HudTone
{
    private const string Table = HudAddons.PlayerHudLook.TableName, Row = "Colour";

    private static bool read;
    private static Color box, grey, low, loss, overOne;
    private static string face;
    private static TMP_FontAsset font;

    /// <summary>Text, lines and the emphasis.</summary>
    public static readonly Color Text = Color.white;
    /// <summary>Text on a white box (the addon's selector writes the chosen box's name in black).</summary>
    public static readonly Color Ink = Color.black;

    /// <summary>The addon's box: black at 50 of 255.</summary>
    public static Color Box { get { Read(); return box; } }
    /// <summary>A box that stands out (the slot in hand): the same black, twice as dense.</summary>
    public static Color BoxHeld { get { Read(); return With(box, Mathf.Min(1f, box.a * 2f)); } }
    /// <summary>The chosen box lit white, as the addon's selector draws it (alpha 200).</summary>
    public static Color Lit => new Color(1f, 1f, 1f, 200f / 255f);
    /// <summary>Quiet text: labels, kinds, empty slots (Colors.Ammo).</summary>
    public static Color Grey { get { Read(); return grey; } }
    public static Color Low { get { Read(); return low; } }
    public static Color Loss { get { Read(); return loss; } }
    public static Color OverOne { get { Read(); return overOne; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        read = false;
        face = null;
        font = null;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => read = false;

    public static Color With(Color colour, float alpha)
    {
        colour.a = alpha;
        return colour;
    }

    /// <summary>The addon HUD's face as a TMP font, or null while it can't be had (the label keeps its own).</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            var options = GameSettings.Current.addonPlayerHud;
            string wanted = options != null && !string.IsNullOrWhiteSpace(options.font) ? options.font : HudAddons.PlayerHudOptions.VideoFont;
            if (wanted == face && font != null) return font;
            face = wanted;
            string file = GameTables.Text(HudAddons.HudFont.TableName, wanted, "file", "");
            font = file.Length > 0 && FontFaces.Instance != null ? FontFaces.Instance.Font(file) : null;
            return font;
        }
    }

    /// <summary>Puts a label in the HUD's face, regular weight (the addon's text is never emboldened at the video's weight).</summary>
    public static void Apply(TMP_Text label)
    {
        if (label == null) return;
        var asset = Font;
        if (asset != null) label.font = asset;
        label.fontStyle &= ~FontStyles.Bold;
    }

    private static void Read()
    {
        if (read) return;
        read = true;
        box = Colour("background", new Color32(0, 0, 0, 50));
        grey = Colour("ammo", new Color32(155, 155, 155, 255));
        low = Colour("low", new Color32(255, 0, 0, 255));
        loss = Colour("loss", new Color32(255, 150, 0, 255));
        overOne = Colour("overOne", new Color32(150, 150, 255, 255));
    }

    private static Color Colour(string field, Color fallback)
    {
        string text = GameTables.Text(Table, Row, field, "");
        return text.Length > 0 && ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out var colour) ? colour : fallback;
    }
}
