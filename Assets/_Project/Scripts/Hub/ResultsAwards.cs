using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The chapter's titles (memo 37, KF2's team awards with a joke or two, Weakest Link style): each row of Tables/Results is
/// one title for the best at one number (최다 처치, 최고 피해, 구조왕, ..., 가장 많이 쓰러짐), given to whoever has the most
/// of it, all of them on a tie, and to nobody under its least. The lineup puts each player's best title over them
/// (ResultsShow), the closing sheet lists them all (ResultsSheet). What a title says lives in Texts (results.award.*).
/// </summary>
public static class ResultsAwards
{
    public const string TableName = "Results";

    /// <summary>One title: which number it goes by, how it is shown, and in which order titles count (lower first).</summary>
    public sealed class Award
    {
        public string Id = "", Stat = "", Unit = "";
        public int Order;
        public float Least;
        public string Label => Texts.Get("results.award." + Id);
        public string Line => Texts.Get("results.award." + Id + ".line");
        public bool IsMoney => Unit == "money";
    }

    /// <summary>A title someone won, with the number that won it.</summary>
    public sealed class Won
    {
        public Award Award;
        public MatchResults.Entry Entry;
        public double Value;
        public string Shown => Format(Award, Value);
    }

    /// <summary>Every title in the table's order.</summary>
    public static List<Award> All()
    {
        var all = new List<Award>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
            all.Add(new Award
            {
                Id = id, Stat = Cell(row, "stat", id), Unit = Cell(row, "unit", ""),
                Order = Mathf.RoundToInt(Number(row, "order", all.Count + 1)), Least = Number(row, "least", 1f)
            });
        }
        all.Sort((a, b) => a.Order.CompareTo(b.Order));
        return all;
    }

    /// <summary>Who won what, in the titles' order (a tie gives the title to each).</summary>
    public static List<Won> Evaluate(IReadOnlyList<MatchResults.Entry> entries)
    {
        var won = new List<Won>();
        foreach (var award in All())
        {
            double best = 0d;
            foreach (var entry in entries) best = System.Math.Max(best, ValueOf(entry, award.Stat));
            if (best <= 0d || best < award.Least) continue;
            foreach (var entry in entries)
                if (System.Math.Abs(ValueOf(entry, award.Stat) - best) < .0001d) won.Add(new Won { Award = award, Entry = entry, Value = best });
        }
        return won;
    }

    /// <summary>The titles one player won, best first.</summary>
    public static List<Won> Of(List<Won> all, MatchResults.Entry entry) => all.FindAll(w => w.Entry == entry);

    /// <summary>The players from the one who won the most (then did the most damage) down: the lineup puts the first in
    /// the middle and the rest out to either side.</summary>
    public static List<MatchResults.Entry> Ranked(IReadOnlyList<MatchResults.Entry> entries, List<Won> won)
    {
        var ranked = new List<MatchResults.Entry>(entries);
        ranked.Sort((a, b) =>
        {
            int titles = Of(won, b).Count.CompareTo(Of(won, a).Count);
            if (titles != 0) return titles;
            int damage = b.Damage.CompareTo(a.Damage);
            return damage != 0 ? damage : a.Client.CompareTo(b.Client);
        });
        return ranked;
    }

    /// <summary>A player's number for a stat column of Tables/Results.</summary>
    public static double ValueOf(MatchResults.Entry entry, string stat)
    {
        switch (stat)
        {
            case "kills": return entry.Kills;
            case "damage": return Mathf.Round(entry.Damage);
            case "headshots": return entry.Headshots;
            case "revives": return entry.Revives;
            case "healing": return Mathf.Round(entry.Healing);
            case "farthest": return System.Math.Round(entry.Farthest, 1);
            case "streak": return entry.BestStreak;
            case "takedowns": return entry.Takedowns;
            case "executions": return entry.Executions;
            case "money": return entry.Earned;
            case "downs": return entry.Downs;
            case "deaths": return entry.Deaths;
        }
        return 0d;
    }

    /// <summary>A number as a title shows it: whole, metres with one decimal, or money (the icon goes beside it).</summary>
    public static string Format(Award award, double value)
    {
        if (award.Unit == "m") return value.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        if (award.IsMoney) return Wallet.Format((long)value);
        return ((long)System.Math.Round(value)).ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string Cell(IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row.TryGetValue(key, out string text) && text.Trim().Length > 0 ? text.Trim() : fallback;

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
