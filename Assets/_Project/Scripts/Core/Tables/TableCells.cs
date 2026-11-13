using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Reading one cell of a table row (GameTables.TryGetRow) as what it means, in one place: row.Number("damage", 10f),
/// row.Text("name"), row.Text("title", "?"). Every file that reads rows used to carry its own copy of these lines.
/// Cells arrive trimmed and never empty (CsvTable drops empty cells), so a missing cell and an empty one are the same
/// thing: the fallback. Numbers are written with a point, whatever the PC's language (invariant culture).
/// </summary>
public static class TableCells
{
    /// <summary>The cell as a number; the fallback when the row lacks it or it isn't a number.</summary>
    public static float Number(this IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row != null && row.TryGetValue(key, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

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
