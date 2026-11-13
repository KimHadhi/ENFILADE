using System;
using System.Text;
using UnityEngine;

// KeyBindings: Words that name a key. One part of the class; the rest is in KeyBindings.cs and the files beside it.
public static partial class KeyBindings
{
    private const string MoveLabel = "WASD";

    private static bool anyMoved;

    /// <summary>
    /// The words with each bracketed default key ("[F]", "[좌클릭·F]", "[WASD]") read as the key the player put on that
    /// action, so every prompt follows the key window (Texts.Get calls it). Untouched while every such key is its default.
    /// </summary>
    public static string Relabel(string words)
    {
        if (!anyMoved || !Application.isPlaying || string.IsNullOrEmpty(words) || words.IndexOf('[') < 0) return words;
        if (relabeled.TryGetValue(words, out string done)) return done;
        var built = new StringBuilder(words.Length + 8);
        int at = 0;
        while (at < words.Length)
        {
            int open = words.IndexOf('[', at);
            int close = open >= 0 ? words.IndexOf(']', open + 1) : -1;
            if (open < 0 || close < 0) { built.Append(words, at, words.Length - at); break; }
            built.Append(words, at, open + 1 - at);
            var pieces = words.Substring(open + 1, close - open - 1).Split('·');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) built.Append('·');
                built.Append(LabelFor(pieces[i].Trim()) ?? pieces[i]);
            }
            built.Append(']');
            at = close + 1;
        }
        string result = built.ToString();
        if (relabeled.Count < 2048) relabeled[words] = result;
        return result;
    }

    // The key now on the action this default key's label stands for, or null to leave the label (unknown, or unchanged).
    private static string LabelFor(string label)
    {
        if (label == MoveLabel)
        {
            var move = Find("Player/Move");
            if (move == null) return null;
            // W, A, S, D are up, left, down, right.
            var order = new[] { 0, 2, 1, 3 };
            var keys = new string[4];
            bool moved = false, single = true;
            for (int i = 0; i < 4; i++)
            {
                int index = Slot(Entries[order[i]], 0);
                moved |= !IsDefault(move, index);
                keys[i] = KeyName(move, index);
                single &= keys[i].Length == 1;
            }
            return moved ? string.Join(single ? "" : "/", keys) : null;
        }
        foreach (var (key, path) in Labels)
        {
            if (!string.Equals(key, label, StringComparison.OrdinalIgnoreCase)) continue;
            var action = Find(path);
            return action != null && !IsDefault(action, FirstPlain(action)) ? Name(action) : null;
        }
        return null;
    }
}
