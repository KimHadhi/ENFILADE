using System.Collections.Generic;
using UnityEngine;

// HudOverlapCheck: Pieces. One part of the class; the rest is in HudOverlapCheck.cs and the files beside it.
public sealed partial class HudOverlapCheck
{
    // The piece a graphic belongs to, or null when HudOverlap.csv leaves it out: the nearest piece row above it, else the
    // child of the nearest group above it (its canvas is a group unless listed).
    private static Transform PieceOf(Transform from, Transform canvasRoot)
    {
        Transform found = null, child = null;
        for (Transform t = from; t != null; t = t.parent)
        {
            string kind = Kind(t);
            if (kind == SkipKind) return null;
            if (kind == null && t == canvasRoot) kind = GroupKind;
            if (found == null)
            {
                if (kind == PieceKind) found = t;
                else if (kind == GroupKind) found = child != null ? child : t;
            }
            if (t == canvasRoot) break;
            child = t;
        }
        return found;
    }

    private static string Kind(Transform t)
    {
        if (kinds.TryGetValue(t, out string known)) return known;
        string kind = null;
        foreach (var rule in rules)
        {
            if (!Matches(rule, t)) continue;
            kind = rule.Kind;
            break;
        }
        kinds[t] = kind;
        return kind;
    }

    private static bool Matches(Rule rule, Transform t)
    {
        for (int i = 0; i < rule.Path.Length; i++, t = t.parent)
        {
            if (t == null) return false;
            string name = Plain(t.name), wanted = rule.Path[i];
            bool same = i == 0 && rule.Prefix ? name.StartsWith(wanted, System.StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, wanted, System.StringComparison.OrdinalIgnoreCase);
            if (!same) return false;
        }
        return true;
    }

    private static string Plain(string name) => name.EndsWith("(Clone)") ? name.Substring(0, name.Length - 7).TrimEnd() : name;

    private static void ReadRules()
    {
        rules = new List<Rule>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !row.TryGetValue("kind", out string kind)) continue;
            var path = id.Split('/');
            System.Array.Reverse(path);
            for (int i = 0; i < path.Length; i++) path[i] = path[i].Trim();
            bool prefix = path[0].EndsWith("*");
            if (prefix) path[0] = path[0].Substring(0, path[0].Length - 1);
            rules.Add(new Rule { Path = path, Prefix = prefix, Kind = kind.Trim().ToLowerInvariant() });
        }
        // The longest paths first, so "Alerts/Mark" wins over a plain "Mark"; exact names before prefixes.
        rules.Sort((a, b) => a.Path.Length != b.Path.Length ? b.Path.Length.CompareTo(a.Path.Length) : a.Prefix.CompareTo(b.Prefix));
    }

    // A piece's name as the log writes it: its path from its canvas, without groups' "(Clone)".
    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent)
        {
            parts.Add(Plain(t.name));
            if (t.TryGetComponent(out Canvas canvas) && canvas.isRootCanvas) break;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
