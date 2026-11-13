using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The faces' tables (Data/Resources/Tables), read on first use and again when the tables reload:
/// - Faces.csv: every mood a face can wear (pain, crying when downed, smug after a kill, a callout's own face…), which
///   wins when several want the face (priority), how fast it comes and goes, how long a moment of it stays, how much
///   blinking and talking it lets through, and how often it is picked as a pose when the character is being looked at
///   (pool: the menu, the hub's mirror, the dressing room, the results stage).
/// - FaceKeys.csv: each character's own shape keys for each mood, for the blink, for the mouth's five vowels while a
///   voice plays and for the eyes' glances. Rows of skin * are the fallback by the names VRChat and MMD avatars commonly
///   use (vrc.v_aa, あ, まばたき, 笑い, Blink, Joy…), so a character a modder brings makes faces with nothing set up
///   for it (the user's rule: custom characters come from modders' FBX files, nothing baked per character).
/// - FacePresets.csv: the expressions the characters' own creators made (their VRChat avatars' face animations: Mayo's
///   gesture and sub faces by Chocolate rice, Ichigo's facials by Hamini3Dbox), key for key and with the ear poses
///   they animate. A FaceKeys.csv row names one with @ ("@Smile", "@Open*0.8") and tweaks it with keys of its own, so
///   the faces are the creators' combined expressions (eyes, brows, mouth, blush, tears, ears together), not single keys
///   mixed by hand (the user 2026-11-12: "BOOTH에 나온 것 같은 다채로운 표정은 어디 가고").
/// - FaceLife.csv (key,value): the numbers of blinking, glancing and talking, and when the moods come.
/// </summary>
public static class FaceTables
{
    public const string MoodTable = "Faces", KeyTable = "FaceKeys", LifeTable = "FaceLife", PresetTable = "FacePresets";
    /// <summary>The skin of FaceKeys.csv's fallback rows, for any character without a row of its own.</summary>
    public const string AnySkin = "*";
    /// <summary>The FacePresets.csv preset whose bones are the rest the creator's clips turn the ears from.</summary>
    public const string RestPreset = "rest";
    // A key's names to try in turn, its weight after them (FaceKeys.csv keys: "name/other name=weight|…"); a preset by
    // name after PresetMark, its keys scaled after ScaleMark; a bone's Euler angles split by AxisSeparator.
    private const char EntrySeparator = '|', NameSeparator = '/', WeightMark = '=', PresetMark = '@', ScaleMark = '*', AxisSeparator = ',';
    // A FaceKeys.csv bones cell that keeps the ears at rest even when the row's preset moves them.
    private const string NoBones = "-";

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

    /// <summary>
    /// A bone a creator's expression turns (an animal ear): its name and the local rotation the clip gives it, as Unity
    /// Euler angles in degrees. The face turns the bone by how far this is from the rest preset's, so a model whose ears
    /// stand a little differently still flicks them the same way.
    /// </summary>
    public readonly struct Bone
    {
        public readonly string Name;
        public readonly Vector3 Euler;

        public Bone(string name, Vector3 euler)
        {
            Name = name;
            Euler = euler;
        }
    }

    private static List<Mood> moods;
    private static readonly Dictionary<string, Mood> byId = new Dictionary<string, Mood>(StringComparer.OrdinalIgnoreCase);
    // (skin, set) → keys, the skin and the set lower-case. A set is a mood's id, blink, talk.a…talk.o or look.left….
    private static readonly Dictionary<(string, string), Key[]> sets = new Dictionary<(string, string), Key[]>();
    private static readonly Dictionary<(string, string), Bone[]> setBones = new Dictionary<(string, string), Bone[]>();
    // (skin, preset) → the creator's keys and bones, both lower-case.
    private static readonly Dictionary<(string, string), Key[]> presets = new Dictionary<(string, string), Key[]>();
    private static readonly Dictionary<(string, string), Bone[]> presetBones = new Dictionary<(string, string), Bone[]>();
    private static readonly Dictionary<string, float> numbers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Key> parsed = new List<Key>();
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
        setBones.Clear();
        presets.Clear();
        presetBones.Clear();
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

    /// <summary>
    /// The bones (ears) the character's set turns, or null when it leaves them at rest: its row's bones cell, else those
    /// of the first preset it names that has any.
    /// </summary>
    public static Bone[] Bones(string skin, string set)
    {
        Load();
        return setBones.TryGetValue((Lower(skin), Lower(set)), out var bones) ? bones : null;
    }

    /// <summary>The character's ears as the creator's clips have them at rest (FacePresets.csv preset rest), or null.</summary>
    public static Bone[] Rest(string skin)
    {
        Load();
        return presetBones.TryGetValue((Lower(skin), RestPreset), out var bones) ? bones : null;
    }

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
                Id = id, Priority = Mathf.RoundToInt(row.Number("priority", 0f)),
                In = Mathf.Max(.01f, row.Number("in", .1f)), Out = Mathf.Max(.01f, row.Number("out", .3f)),
                Hold = Mathf.Max(0f, row.Number("hold", 0f)),
                Blink = Mathf.Clamp01(row.Number("blink", 1f)), Talk = Mathf.Clamp01(row.Number("talk", 1f)),
                Pool = Mathf.Max(0f, row.Number("pool", 0f))
            };
            moods.Add(mood);
            byId[id] = mood;
        }
        // A fixed order whatever the table's dictionary hands back, so every PC breaks a tie of priorities the same way.
        moods.Sort((a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : string.CompareOrdinal(a.Id, b.Id));
        // The creators' presets first: the key rows name them.
        foreach (string id in GameTables.RowIds(PresetTable))
        {
            if (!GameTables.TryGetRow(PresetTable, id, out var row)) continue;
            string skin = Lower(row.Text("skin")), preset = Lower(row.Text("preset"));
            if (skin.Length == 0 || preset.Length == 0) continue;
            presets[(skin, preset)] = Parse(skin, row.Text("keys"), out _);
            var bones = ParseBones(row.Text("bones"));
            if (bones != null) presetBones[(skin, preset)] = bones;
        }
        foreach (string id in GameTables.RowIds(KeyTable))
        {
            if (!GameTables.TryGetRow(KeyTable, id, out var row)) continue;
            string skin = row.Text("skin"), set = row.Text("mood");
            var keys = Parse(Lower(skin), row.Text("keys"), out var presetBonesOf);
            if (skin.Length == 0 || set.Length == 0 || keys.Length == 0)
            {
                Debug.LogWarning($"[Faces] {KeyTable}.csv [{id}]: skin·mood·keys가 비어 있어 건너뜁니다.");
                continue;
            }
            sets[(Lower(skin), Lower(set))] = keys;
            string bonesCell = row.Text("bones");
            Bone[] bones = null;
            // "@KemonoEar_Down": a preset's ears (Ichigo's creator animates hers apart from the faces).
            if (bonesCell.Length > 1 && bonesCell[0] == PresetMark) presetBones.TryGetValue((Lower(skin), Lower(bonesCell.Substring(1))), out bones);
            else if (bonesCell != NoBones) bones = ParseBones(bonesCell) ?? presetBonesOf;
            if (bones != null) setBones[(Lower(skin), Lower(set))] = bones;
        }
    }

    // "@Open|Mouth_Smile2=40|vrc.blink/Blink=100": a preset's keys (scaled by "@Open*0.6"), then keys of the row's own,
    // a weight left out being 100; a key the row names again takes the row's weight (0 takes the preset's key away).
    // presetBonesOf: the bones of the first preset named that has any.
    private static Key[] Parse(string skin, string cell, out Bone[] presetBonesOf)
    {
        presetBonesOf = null;
        parsed.Clear();
        foreach (string entry in cell.Split(EntrySeparator))
        {
            string text = entry.Trim();
            if (text.Length == 0) continue;
            if (text[0] == PresetMark)
            {
                float scale = 1f;
                string name = text.Substring(1).Trim();
                int star = name.LastIndexOf(ScaleMark);
                if (star > 0 && float.TryParse(name.Substring(star + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float times))
                {
                    scale = Mathf.Max(0f, times);
                    name = name.Substring(0, star).Trim();
                }
                if (!presets.TryGetValue((skin, Lower(name)), out var preset))
                {
                    Debug.LogWarning($"[Faces] {KeyTable}.csv: {skin}에 '{name}' 프리셋이 없습니다({PresetTable}.csv).");
                    continue;
                }
                foreach (var key in preset) Put(new Key(key.Names, Mathf.Clamp(key.Weight * scale, 0f, 100f)));
                if (presetBonesOf == null) presetBones.TryGetValue((skin, Lower(name)), out presetBonesOf);
                continue;
            }
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
            if (names.Count > 0) Put(new Key(names.ToArray(), weight));
        }
        return parsed.ToArray();
    }

    // A key named again replaces the earlier one (the row's own tweak over its preset's).
    private static void Put(Key key)
    {
        for (int i = 0; i < parsed.Count; i++)
        {
            if (!string.Equals(parsed[i].Names[0], key.Names[0], StringComparison.Ordinal)) continue;
            parsed[i] = key;
            return;
        }
        parsed.Add(key);
    }

    // "L_Kemomimi=48,0,-67.4|R_Kemomimi=48,0,67.4": each bone's local Euler angles; null when the cell names none.
    private static Bone[] ParseBones(string cell)
    {
        if (string.IsNullOrEmpty(cell) || cell == NoBones) return null;
        var bones = new List<Bone>();
        foreach (string entry in cell.Split(EntrySeparator))
        {
            string text = entry.Trim();
            int mark = text.IndexOf(WeightMark);
            if (mark <= 0) continue;
            string[] axes = text.Substring(mark + 1).Split(AxisSeparator);
            if (axes.Length != 3) continue;
            var euler = Vector3.zero;
            bool read = true;
            for (int a = 0; a < 3 && read; a++)
            {
                read = float.TryParse(axes[a].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
                euler[a] = value;
            }
            if (read) bones.Add(new Bone(text.Substring(0, mark).Trim(), euler));
        }
        return bones.Count > 0 ? bones.ToArray() : null;
    }

    private static string Lower(string text) => (text ?? "").Trim().ToLowerInvariant();
}
