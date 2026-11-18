using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a class shows wherever it is named: its colour, its icon and its title, each from one place. Whatever says which
/// class someone plays or which class a card belongs to asks here (the Tab board's rows, the cards' badges and decks, the
/// kill feed's flairs), so a class reads the same everywhere and a mod's class needs only its rows: Tables/Classes
/// ClassColours.csv for the colour, Resources/UI/Icons/Class_&lt;id&gt; for the icon, Classes.csv for the title. A variant
/// without a row or an icon of its own takes its base class's (PlayerClass.KitOf).
/// </summary>
public static class ClassLook
{
    public const string ColourTable = "ClassColours";

    private static readonly Dictionary<string, Color> colours = new Dictionary<string, Color>();
    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        colours.Clear();
        listening = false;
    }

    /// <summary>
    /// A class's colour (ClassColours.csv colour), or clear for a class the table doesn't name and for no class at all:
    /// the caller then keeps its own colour.
    /// </summary>
    public static Color Colour(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId)) return Color.clear;
        string id = classId.Trim();
        if (colours.TryGetValue(id, out Color known)) return known;
        // A saved table is read again (the table is edited while the game runs).
        if (!listening) { listening = true; GameTables.Reloaded += colours.Clear; }
        if (!GameTables.TryGetRow(ColourTable, id, out var cells)) GameTables.TryGetRow(ColourTable, PlayerClass.KitOf(id), out cells);
        Color colour = cells != null && cells.TryGetValue("colour", out string text) && ColorUtility.TryParseHtmlString(text.Trim(), out var read)
            ? read : Color.clear;
        colours[id] = colour;
        return colour;
    }

    /// <summary>A class's colour, or <paramref name="fallback"/> where it has none.</summary>
    public static Color Colour(string classId, Color fallback)
    {
        Color colour = Colour(classId);
        return colour.a > 0f ? colour : fallback;
    }

    /// <summary>The ClassColours row for a card that belongs to no class (everyone's cards).</summary>
    public const string Common = "common";

    /// <summary>
    /// A card's colour by the deck it belongs to: its class's, and the table's "common" row for a card of no class, so
    /// cards sort by colour wherever they are listed (the user, 2026-11-18: "클래스별 고유 색상을 부여해 카드를
    /// 분류·정렬").
    /// </summary>
    public static Color CardColour(string cardClass) => Colour(string.IsNullOrWhiteSpace(cardClass) ? Common : cardClass, KitStyle.Text);

    /// <summary>A class's icon (white, to be tinted), or null for a class without one and for no class.</summary>
    public static Texture2D Icon(string classId)
        => string.IsNullOrWhiteSpace(classId) ? null : KitButtons.Icon(MainMenuClasses.Icon(classId.Trim()));

    /// <summary>A class's name as the player reads it, "" for no class.</summary>
    public static string Title(string classId) => string.IsNullOrWhiteSpace(classId) ? "" : ClassGrowth.TitleOf(classId.Trim());
}
