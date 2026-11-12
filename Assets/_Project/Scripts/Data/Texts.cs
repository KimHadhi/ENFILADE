using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Every word the game shows a player, by id, from Resources/Tables/Texts.csv: the user writes them there, and a
/// translation is one more column (the user's playtest of 2026-11-11: "나중에 번역판을 위해서 좀 데이터 테이블로 정리한
/// 다음에 내가 손 봐야겠다"). The table's columns are the id, one per language (ko first; en waits empty) and #어디, a note
/// on where the words appear that the game never reads. Code asks for an id (Get), or for an id whose words hold {0}, {1}
/// for what goes in them (Format), never for the words themselves, so a translation can put them in its own order.
/// In a cell, \n is a line break, and rich text tags (&lt;color&gt;, &lt;b&gt;) work as in any TextMeshPro label.
/// An id the table lacks shows as itself in brackets, so a gap is plain to see; a row with no words in the chosen
/// language falls back to Korean. Editing the table and reloading the tables (GameTables.Reload, the console's table
/// reload) brings the new words in; a label already on screen takes them the next time it is written.
/// </summary>
public static class Texts
{
    public const string TableName = "Texts";
    /// <summary>The language every row has: the one the game is written in.</summary>
    public const string Korean = "ko";

    private static string language = Korean;

    /// <summary>The column read: "ko" now; a translation's column once there is one.</summary>
    public static string Language
    {
        get => language;
        set
        {
            language = string.IsNullOrWhiteSpace(value) ? Korean : value.Trim();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => language = Korean;

    /// <summary>
    /// The words for this id in the chosen language. Read from the table each time (two dictionary lookups, no copy
    /// unless the words hold a line break), so a reload of the tables shows at once to whatever asks next, before or
    /// after the others who listen for GameTables.Reloaded.
    /// </summary>
    public static string Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        if (!GameTables.TryGetRow(TableName, id, out var row)) return "[" + id + "]";
        // A default key in brackets ("[F] 문 열기") reads as the key the player chose in the key window (KeyBindings).
        if (row.TryGetValue(language, out string words) && words.Length > 0) return KeyBindings.Relabel(Unescape(words));
        if (language != Korean && row.TryGetValue(Korean, out words) && words.Length > 0) return KeyBindings.Relabel(Unescape(words));
        return "[" + id + "]";
    }

    /// <summary>The words for this id with {0}, {1} ... filled in.</summary>
    public static string Format(string id, params object[] values)
    {
        string template = Get(id);
        if (values == null || values.Length == 0) return template;
        try { return string.Format(CultureInfo.CurrentCulture, template, values); }
        // A slip in the table ({ without its number) must never break the screen showing it: the words go up as written.
        catch (FormatException) { return template; }
    }

    /// <summary>Whether the table has this id at all (for words that are optional, such as a zombie look's name).</summary>
    public static bool Has(string id) => !string.IsNullOrEmpty(id) && GameTables.TryGetRow(TableName, id, out _);

    // A cell can't hold a line break the spreadsheet keeps tidily, so "\n" stands for one.
    private static string Unescape(string words) => words.IndexOf('\\') < 0 ? words : words.Replace("\\n", "\n");
}
