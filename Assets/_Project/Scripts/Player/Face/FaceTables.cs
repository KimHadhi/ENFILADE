using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The faces' tables (Resources/Tables), read on first use and again when the tables reload:
/// - Faces.csv: every mood a face can wear (pain, crying when downed, smug after a kill, a callout's own face…), which
///   wins when several want the face (priority), how fast it comes and goes, how long a moment of it stays, how much
///   blinking and talking it lets through, and how often it is picked as a pose when the character is being looked at
///   (pool: the menu, the hub's mirror, the dressing room, the results stage).
/// - FaceKeys.csv: each character's own shape keys for each mood, for the blink, for the mouth's five vowels while a
///   voice plays and for the eyes' glances. Rows of skin * are the fallback by the names VRChat and MMD avatars commonly
///   use (vrc.v_aa, あ, まばたき, 笑い, Blink, Joy…), so a character a modder brings makes faces with nothing set up
///   for it (the user's rule: custom characters come from modders' FBX files, nothing baked per character).
/// - FaceLife.csv (key,value): the numbers of blinking, glancing and talking, and when the moods come.
/// </summary>
public static class FaceTables
{
    public const string MoodTable = "Faces", KeyTable = "FaceKeys", LifeTable = "FaceLife";
    /// <summary>The skin of FaceKeys.csv's fallback rows, for any character without a row of its own.</summary>
    public const string AnySkin = "*";
    // A key's names to try in turn, its weight after them (FaceKeys.csv keys: "name/other name=weight|…").
    private const char EntrySeparator = '|', NameSeparator = '/', WeightMark = '=';

    public sealed class Mood
    {
        public string Id;
        public int Priority;
        /// <summary>Seconds to come in and to go; a moment of it stays this long (s, 0 for one held on and off).</summary>
        public float In, Out, Hold;
        /// <summary>How much blinking and talking come through on top of it (0-1).</summary>
        public float Blink, Talk;
        /// <summary>How often it is picked as a pose while the character is being looked at (0 never).</summary>
        public float Pool;
    }

    /// <summary>One shape key of a set: the names to try in turn (the first the model has) and its weight (0-100).</summary>
    public readonly struct Key
    {
        public readonly string[] Names;
        public readonly float Weight;

        public Key(string[] names, float weight)
        {
            Names = names;
            Weight = weight;
        }
    }

    private static List<Mood> moods;
    private static readonly Dictionary<string, Mood> byId = new Dictionary<string, Mood>(StringComparer.OrdinalIgnoreCase);
    // (skin, set) → keys, the skin and the set lower-case. A set is a mood's id, blink, talk.a…talk.o or look.left….
    private static readonly Dictionary<(string, string), Key[]> sets = new Dictionary<(string, string), Key[]>();
    private static readonly Dictionary<string, float> numbers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Forget();
        listening = false;
    }

    private static void Forget()
    {
        moods = null;
        byId.Clear();
        sets.Clear();
        numbers.Clear();
    }

    /// <summary>Every mood, in the table's order.</summary>
    public static IReadOnlyList<Mood> Moods
    {
        get
        {
            Load();
            return moods;
        }
    }

    /// <summary>The mood with this id, or null.</summary>
    public static Mood Find(string id)
    {
        Load();
        return id != null && byId.TryGetValue(id, out var mood) ? mood : null;
    }

    /// <summary>The character's own keys for a set (a mood, blink, talk.a, look.left…), or null when it has no row.</summary>
    public static Key[] Own(string skin, string set)
    {
        Load();
        return sets.TryGetValue((Lower(skin), Lower(set)), out var keys) ? keys : null;
    }

    /// <summary>The fallback keys for a set (FaceKeys.csv skin *), or null.</summary>
    public static Key[] Fallback(string set) => Own(AnySkin, set);

    /// <summary>A FaceLife.csv number ("Blink.every"), or <paramref name="fallback"/> when the table hasn't it.</summary>
    public static float Number(string key, float fallback)
    {
        Load();
        if (numbers.TryGetValue(key, out float value)) return value;
        value = fallback;
        int dot = key.IndexOf('.');
        if (dot > 0 && GameTables.TryGetRow(LifeTable, key.Substring(0, dot), out var row)
            && row.TryGetValue(key.Substring(dot + 1), out string cell)
            && float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float read))
            value = read;
        numbers[key] = value;
        return value;
    }

    private static void Load()
    {
        if (!listening)
        {
            GameTables.Reloaded += Forget;
            listening = true;
        }
        if (moods != null) return;
        moods = new List<Mood>();
        foreach (string id in GameTables.RowIds(MoodTable))
        {
            if (!GameTables.TryGetRow(MoodTable, id, out var row) || byId.ContainsKey(id)) continue;
            var mood = new Mood
            {
                Id = id, Priority = Mathf.RoundToInt(Float(row, "priority", 0f)),
                In = Mathf.Max(.01f, Float(row, "in", .1f)), Out = Mathf.Max(.01f, Float(row, "out", .3f)),
                Hold = Mathf.Max(0f, Float(row, "hold", 0f)),
                Blink = Mathf.Clamp01(Float(row, "blink", 1f)), Talk = Mathf.Clamp01(Float(row, "talk", 1f)),
                Pool = Mathf.Max(0f, Float(row, "pool", 0f))
            };
            moods.Add(mood);
            byId[id] = mood;
        }
        // A fixed order whatever the table's dictionary hands back, so every PC breaks a tie of priorities the same way.
        moods.Sort((a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : string.CompareOrdinal(a.Id, b.Id));
        foreach (string id in GameTables.RowIds(KeyTable))
        {
            if (!GameTables.TryGetRow(KeyTable, id, out var row)) continue;
            string skin = Cell(row, "skin"), set = Cell(row, "mood");
            var keys = Parse(Cell(row, "keys"));
            if (skin.Length == 0 || set.Length == 0 || keys.Length == 0)
            {
                Debug.LogWarning($"[Faces] {KeyTable}.csv [{id}]: skin·mood·keys가 비어 있어 건너뜁니다.");
                continue;
            }
            sets[(Lower(skin), Lower(set))] = keys;
        }
    }

    // "Eyelid_close_smile=100|Eyebrows_Joy=80|vrc.blink/Blink=100": a weight left out is 100.
    private static Key[] Parse(string cell)
    {
        var keys = new List<Key>();
        foreach (string entry in cell.Split(EntrySeparator))
        {
            string text = entry.Trim();
            if (text.Length == 0) continue;
            float weight = 100f;
            int mark = text.LastIndexOf(WeightMark);
            if (mark > 0 && float.TryParse(text.Substring(mark + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float read))
            {
                weight = Mathf.Clamp(read, 0f, 100f);
                text = text.Substring(0, mark);
            }
            var names = new List<string>();
            foreach (string name in text.Split(NameSeparator))
                if (name.Trim().Length > 0) names.Add(name.Trim());
            if (names.Count > 0) keys.Add(new Key(names.ToArray(), weight));
        }
        return keys.ToArray();
    }

    private static float Float(IReadOnlyDictionary<string, string> row, string column, float fallback) =>
        row.TryGetValue(column, out string cell) && float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;

    private static string Cell(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out string value) && value != null ? value.Trim() : "";

    private static string Lower(string text) => (text ?? "").Trim().ToLowerInvariant();
}
