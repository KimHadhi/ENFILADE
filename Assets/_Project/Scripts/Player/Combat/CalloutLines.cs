using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The callouts' lines (Tables/Callouts.csv; memo 4, Guts &amp; Blackpowder's callout wheel). Each row is one thing a
/// player can shout: the wheel slot it belongs to, when it fits (a word PlayerCallouts tests: the zombie's kind, being
/// pinned, the class and its state…), which wins when several fit (the highest order; equals take turns at random),
/// and whether it marks something (target pings the zombie, point pins the spot looked at). Its voice is the Voice
/// bank's "Callout/&lt;id&gt;" (Art/Audio/Voice/Hiro/Callout/&lt;id&gt;), its words the language's column. Kept in the
/// table so the lines, their order and their words can change without code; read on first use and again when the
/// tables reload.
/// </summary>
public static class CalloutLines
{
    public const string TableName = "Callouts";

    public sealed class Line
    {
        public string Id, Slot, When, Mark, Text;
        public int Order;
        /// <summary>Its clips in the Voice bank.</summary>
        public string VoiceKey => "Callout/" + Id;
        /// <summary>Saying it pings the zombie it is about, for the team.</summary>
        public bool MarksTarget => string.Equals(Mark, "target", StringComparison.OrdinalIgnoreCase);
        /// <summary>Saying it pins the spot the speaker looks at.</summary>
        public bool MarksPoint => string.Equals(Mark, "point", StringComparison.OrdinalIgnoreCase);
    }

    private static List<Line> lines;
    private static readonly Dictionary<string, Line> byId = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Line> fitting = new List<Line>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Forget();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        lines = null;
        byId.Clear();
    }

    /// <summary>The line with this id, or null.</summary>
    public static Line Find(string id)
    {
        Load();
        return id != null && byId.TryGetValue(id, out var line) ? line : null;
    }

    /// <summary>The order of the slot's line that has exactly this condition (the zombie kinds' rank), or fallback.</summary>
    public static int OrderOf(string slot, string when, int fallback = 0)
    {
        Load();
        foreach (var line in lines)
            if (string.Equals(line.Slot, slot, StringComparison.OrdinalIgnoreCase) && string.Equals(line.When, when, StringComparison.OrdinalIgnoreCase))
                return line.Order;
        return fallback;
    }

    /// <summary>
    /// The line of this slot that fits now (fits tests a row's condition): the highest order. Among equals the seed
    /// picks (the same seed, the same line: what the wheel shows is what is said), or chance when it is negative.
    /// </summary>
    public static Line Pick(string slot, Func<string, bool> fits, int seed = -1)
    {
        Load();
        fitting.Clear();
        int best = int.MinValue;
        foreach (var line in lines)
        {
            if (line.Order < best || !string.Equals(line.Slot, slot, StringComparison.OrdinalIgnoreCase) || !fits(line.When)) continue;
            if (line.Order > best)
            {
                best = line.Order;
                fitting.Clear();
            }
            fitting.Add(line);
        }
        if (fitting.Count == 0) return null;
        return fitting[seed >= 0 ? seed % fitting.Count : UnityEngine.Random.Range(0, fitting.Count)];
    }

    private static void Load()
    {
        if (lines != null) return;
        lines = new List<Line>();
        byId.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
            var line = new Line
            {
                Id = id, Slot = Cell(row, "slot"), When = Cell(row, "when", "always"), Mark = Cell(row, "mark"),
                Text = Cell(row, Texts.Language, Cell(row, Texts.Korean, id)),
                Order = int.TryParse(Cell(row, "order", "0"), out int order) ? order : 0
            };
            lines.Add(line);
            byId[id] = line;
        }
        // A fixed order whatever the table keeps its rows in, so one seed always picks the same line among equals.
        lines.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    private static string Cell(IReadOnlyDictionary<string, string> row, string field, string fallback = "")
        => row.TryGetValue(field, out string value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
}
