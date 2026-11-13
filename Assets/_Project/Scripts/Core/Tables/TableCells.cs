using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Reading one cell of a table row (GameTables.TryGetRow) as what it means, in one place: row.Number("damage", 10f),
/// row.Text("name"), row.Text("title", "?"). Every file that reads rows used to carry its own copy of these lines.
/// Cells arrive trimmed and never empty (CsvTable drops empty cells), so a missing cell and an empty one are the same
/// thing: the fallback. Numbers are written with a point, whatever the PC's language (invariant culture).
/// TryNumber, TryWhole and TryFlag are the only places that turn written text into a value; a table bound to fields
/// (TableFieldWriter), a row read by hand and a console command all come through them. When a single cell of a known
/// table and row is wanted, GameTables.Number / Flag / Text ask for it in one call.
/// </summary>
public static class TableCells
{
    /// <summary>
    /// Text as a number, the one way the game reads a number a person wrote (a table cell, a mod, a console command):
    /// a point for the decimals whatever the PC's language, spaces round it ignored, a closing % allowed ("50%" is 50),
    /// and "NaN" or "Infinity" refused. Tables bound to fields (TableFieldWriter) and rows read by hand both come here,
    /// so a cell means the same number wherever it is read.
    /// </summary>
    public static bool TryNumber(string text, out float value)
    {
        value = 0f;
        if (string.IsNullOrEmpty(text)) return false;
        text = text.Trim();
        if (text.Length > 0 && text[text.Length - 1] == '%') text = text.Substring(0, text.Length - 1).TrimEnd();
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value)) return true;
        value = 0f;
        return false;
    }

    /// <summary>
    /// Text as a whole number: exactly when it is written as one (a float would round 16777217), and "3.0" or "50%" too
    /// as long as nothing is left after the point. Refused when it does not fit an int.
    /// </summary>
    public static bool TryWhole(string text, out int value)
    {
        if (text != null && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        value = 0;
        if (!TryNumber(text, out float number) || number != (float)System.Math.Round(number) || number < -2147483648f || number >= 2147483648f) return false;
        value = (int)number;
        return true;
    }

    /// <summary>
    /// Text as a yes or a no, in the words a table may use: true/1/yes/y/o/on, false/0/no/n/x/off, either case.
    /// Anything else is neither, and the caller keeps what it had.
    /// </summary>
    public static bool TryFlag(string text, out bool on)
    {
        on = false;
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "true": case "1": case "yes": case "y": case "o": case "on": on = true; return true;
            case "false": case "0": case "no": case "n": case "x": case "off": return true;
            default: return false;
        }
    }

    /// <summary>The cell as a number; the fallback when the row lacks it or it isn't a number.</summary>
    public static float Number(this IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row != null && row.TryGetValue(key, out string text) && TryNumber(text, out float value) ? value : fallback;

    /// <summary>The cell as a whole number; the fallback when the row lacks it or it isn't one.</summary>
    public static int Whole(this IReadOnlyDictionary<string, string> row, string key, int fallback)
        => row != null && row.TryGetValue(key, out string text) && TryWhole(text, out int value) ? value : fallback;

    /// <summary>The cell as a yes or a no; the fallback when the row lacks it or it says neither.</summary>
    public static bool Flag(this IReadOnlyDictionary<string, string> row, string key, bool fallback)
        => row != null && row.TryGetValue(key, out string text) && TryFlag(text, out bool on) ? on : fallback;

    /// <summary>The cell's words, trimmed; "" when the row lacks it.</summary>
    public static string Text(this IReadOnlyDictionary<string, string> row, string key)
        => row != null && row.TryGetValue(key, out string text) && text != null ? text.Trim() : "";

    /// <summary>The cell's words, trimmed; the fallback when the row lacks it or it is blank.</summary>
    public static string Text(this IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row != null && row.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;

    /// <summary>Three cells as a vector: the name with X, Y and Z after it, each falling back to the fallback's own.</summary>
    public static Vector3 Vector(this IReadOnlyDictionary<string, string> row, string name, Vector3 fallback)
        => new Vector3(row.Number(name + "X", fallback.x), row.Number(name + "Y", fallback.y), row.Number(name + "Z", fallback.z));

    /// <summary>A cell that lists several things with '|' between them: each one trimmed, blanks left out.</summary>
    public static IEnumerable<string> Parts(string list)
    {
        if (string.IsNullOrWhiteSpace(list)) yield break;
        foreach (string part in list.Split('|'))
            if (!string.IsNullOrWhiteSpace(part)) yield return part.Trim();
    }
}
