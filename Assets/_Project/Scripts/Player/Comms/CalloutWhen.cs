using System;
using System.Collections.Generic;

/// <summary>
/// When a callout line or a wheel slot fits, as its table cell says it (Tables/Player/Callouts.csv and CalloutSlots.csv,
/// column when). A cell is facts joined: "a &amp; b" needs both, "a | b" either ("&amp;" binds first), "!a" needs the
/// fact not to hold, and a fact may take one word after a colon ("kind:brute", "class:knight", "phase:assault"). An
/// empty cell or "always" fits always. What each fact means is CalloutFacts' business; here are only the words, so a
/// table can be checked without the game running (Terms: the console's /callouts lists every word no fact answers to).
/// Until 2026-11-18 a cell held one fixed word that code matched in a switch: two conditions together needed a new word
/// in code ("heal.detente", "knight.taunt"), and a word the switch didn't know was silently taken for a class id
/// ("point", meant as "a spot is looked at", made 이쪽으로! a line of the Point class alone). The user's list of that
/// day asks for "클래스·상황별 동적 콜아웃": class and situation are now facts a row combines, and a mod's row can too.
/// </summary>
public static class CalloutWhen
{
    /// <summary>One fact of a cell: its name, the word after its colon ("" when it has none), and whether it is negated.</summary>
    public readonly struct Term
    {
        public readonly string Name, Word;
        public readonly bool Not;

        public Term(string name, string word, bool not)
        {
            Name = name;
            Word = word;
            Not = not;
        }

        public override string ToString() => (Not ? "!" : "") + Name + (Word.Length > 0 ? ":" + Word : "");
    }

    /// <summary>What a fact answers: whether it holds, or null when no fact has that name.</summary>
    public delegate bool? Fact(string name, string word);

    private static readonly Term[][] Always = new Term[0][];
    // Cells as read: any of the groups fits, and a group fits when all its terms do. Cells are few and fixed, so they are
    // kept by their text for the session.
    private static readonly Dictionary<string, Term[][]> read = new Dictionary<string, Term[][]>(StringComparer.Ordinal);

    /// <summary>
    /// Whether the cell fits by these facts. A fact nobody answers makes its group not fit, negated or not: an unknown
    /// word never says a line by accident.
    /// </summary>
    public static bool Fits(string when, Fact fact)
    {
        var groups = Read(when);
        if (groups.Length == 0) return true;
        foreach (var group in groups)
        {
            bool all = true;
            foreach (var term in group)
            {
                bool? holds = fact(term.Name, term.Word);
                if (holds.HasValue && holds.Value != term.Not) continue;
                all = false;
                break;
            }
            if (all) return true;
        }
        return false;
    }

    /// <summary>Every fact the cell names, for checking a table's words against the facts there are.</summary>
    public static IEnumerable<Term> Terms(string when)
    {
        foreach (var group in Read(when))
            foreach (var term in group)
                yield return term;
    }

    private static Term[][] Read(string when)
    {
        if (string.IsNullOrWhiteSpace(when)) return Always;
        if (read.TryGetValue(when, out var known)) return known;
        var groups = new List<Term[]>();
        bool always = false;
        foreach (string either in when.Split('|'))
        {
            var terms = new List<Term>();
            foreach (string piece in either.Split('&'))
            {
                string text = piece.Trim();
                bool not = text.StartsWith("!", StringComparison.Ordinal);
                if (not) text = text.Substring(1).Trim();
                if (text.Length == 0) continue;
                // "always" adds nothing to a group it shares; alone (or negated: never) it is kept as a word of its own.
                if (!not && text.Equals("always", StringComparison.OrdinalIgnoreCase)) continue;
                int colon = text.IndexOf(':');
                string name = (colon < 0 ? text : text.Substring(0, colon)).Trim().ToLowerInvariant();
                string word = colon < 0 ? "" : text.Substring(colon + 1).Trim().ToLowerInvariant();
                terms.Add(new Term(name, word, not));
            }
            // A group with nothing left in it ("always", or "a | always") fits always, and so does the whole cell.
            if (terms.Count == 0) { always = true; break; }
            groups.Add(terms.ToArray());
        }
        var result = always ? Always : groups.ToArray();
        read[when] = result;
        return result;
    }
}
