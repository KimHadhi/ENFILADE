using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A move's clips by the way they go, as the tables write them ("clip@degrees;…", 0 ahead, 90 right): the rolls' (Player.csv
/// Body.*Ways, PlayerBody.Ways) and the dodges' (Ability.*Ways, DodgeMove).
/// </summary>
public static class ClipWays
{
    private static readonly Dictionary<string, List<(string clip, float way)>> known = new Dictionary<string, List<(string, float)>>();

    /// <summary>The clips and ways a set lists; an unreadable entry is left out.</summary>
    public static IReadOnlyList<(string clip, float way)> Of(string set)
    {
        set ??= "";
        if (known.TryGetValue(set, out var list)) return list;
        list = new List<(string, float)>();
        foreach (string entry in set.Split(';'))
        {
            int at = entry.IndexOf('@');
            if (at <= 0) continue;
            if (float.TryParse(entry.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float degrees))
                list.Add((entry.Substring(0, at).Trim(), degrees));
        }
        known[set] = list;
        return list;
    }

    /// <summary>The clip whose way is nearest this heading (degrees), at most <paramref name="within"/> from it; null for none.</summary>
    public static string Nearest(string set, float heading, float within)
    {
        string best = null;
        float gap = within;
        foreach (var (clip, way) in Of(set))
        {
            float off = Mathf.Abs(Mathf.DeltaAngle(way, heading));
            if (off > gap) continue;
            gap = off;
            best = clip;
        }
        return best;
    }
}
