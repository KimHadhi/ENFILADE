using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// After the 글자 편집기 saves during Play, labels already up that show a changed line's old words take the new words at
/// once: most screens write their words only when they open, so without this a rewrite would show the next time.
/// Words with holes ({0}) are matched as a pattern, keeping what the code filled in; a line the code splits at '|' is
/// matched piece by piece; a longer phrase is also replaced inside a label that strings several lines together (the
/// title screen's ticker). Inactive labels count too (a menu page not open now).
/// </summary>
internal static class TextLiveSwap
{
    // Shorter words are left to whole-label matches: a word that short could sit inside unrelated text.
    private const int InsideFrom = 8;
    private static readonly Regex Hole = new Regex(@"\{\{|\}\}|\{([^{}:,]+)(?:[:,][^{}]*)?\}");

    private sealed class Swap
    {
        public Regex Whole;
        public string Before, After;
        public bool HasHoles;
    }

    /// <summary>Swaps words on labels for each (shown before, shown after) pair; returns how many labels changed.</summary>
    internal static int Run(IEnumerable<(string before, string after)> changes)
    {
        if (!Application.isPlaying) return 0;
        var swaps = new List<Swap>();
        foreach (var (before, after) in changes)
        {
            if (string.IsNullOrEmpty(before) || before == after) continue;
            Add(swaps, before, after ?? "");
            var oldPieces = before.Split('|');
            var newPieces = (after ?? "").Split('|');
            if (oldPieces.Length > 1 && oldPieces.Length == newPieces.Length)
                for (int i = 0; i < oldPieces.Length; i++)
                    if (oldPieces[i].Trim().Length > 0 && oldPieces[i] != newPieces[i]) Add(swaps, oldPieces[i].Trim(), newPieces[i].Trim());
        }
        if (swaps.Count == 0) return 0;
        int changed = 0;
        foreach (var label in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string text = label.text;
            if (string.IsNullOrEmpty(text)) continue;
            string now = text;
            foreach (var swap in swaps)
            {
                var match = swap.Whole.Match(now);
                if (match.Success)
                {
                    now = Fill(swap.After, match);
                    break;
                }
                if (!swap.HasHoles && swap.Before.Length >= InsideFrom && now.Contains(swap.Before)) now = now.Replace(swap.Before, swap.After);
            }
            if (now == text) continue;
            label.text = now;
            changed++;
        }
        return changed;
    }

    private static void Add(List<Swap> swaps, string before, string after)
    {
        // Each hole of the old words becomes a group (a repeated hole must say the same both times).
        var pattern = new StringBuilder("^");
        var named = new HashSet<string>();
        int at = 0;
        bool holes = false;
        foreach (Match hole in Hole.Matches(before))
        {
            pattern.Append(Regex.Escape(before.Substring(at, hole.Index - at)));
            if (hole.Groups[1].Success)
            {
                string group = GroupName(hole.Groups[1].Value);
                pattern.Append(named.Add(group) ? $"(?<{group}>.*?)" : $"\\k<{group}>");
                holes = true;
            }
            else pattern.Append(Regex.Escape(hole.Value.Substring(0, 1)));
            at = hole.Index + hole.Length;
        }
        pattern.Append(Regex.Escape(before.Substring(at))).Append('$');
        swaps.Add(new Swap { Whole = new Regex(pattern.ToString(), RegexOptions.Singleline), Before = before, After = after, HasHoles = holes });
    }

    // The new words with each hole given what the old label had there; a hole the old words lacked stays as written.
    private static string Fill(string after, Match match)
    {
        return Hole.Replace(after, hole =>
        {
            if (!hole.Groups[1].Success) return hole.Value.Substring(0, 1);
            var value = match.Groups[GroupName(hole.Groups[1].Value)];
            return value.Success ? value.Value : hole.Value;
        });
    }

    // A regex group name for a hole ("0", "Assault.chargeSeconds").
    private static string GroupName(string hole) => "h" + new string(hole.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
