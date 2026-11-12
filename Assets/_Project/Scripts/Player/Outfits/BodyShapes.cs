using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Every look a character's model was made able to take, by its own shape keys (BodyShapes.csv): a fuller chest, a
/// slimmer face, an elf's ears, hearts in the pupils, longer bangs, the cape off. The user found Mayo and Ichigo stuck
/// in one shape although their makers built them to be shaped, and asked for all of it, not only what a VRChat menu
/// would show (2026-11-12: "VRChat식에 고집할 필요 없고 저 캐릭터별로 커스터마이징 할 수 있는 기능들 다 넣는 느낌").
/// Left out on purpose: expressions (blinks, mouth shapes, tears, sweat), the keys clothes press the body in with
/// (Shrink, Kuikomi, Fit) and heel keys, which the shoes' slot holds. Kazusa, Hiro and Sil carry expression keys only,
/// so they have no rows.
/// A row is a slider (one key 0 to 100), a pair (one key making less, one making more: -100 to 100) or a choice (one
/// of several keys at 100, the 0th choice none). The player's values ride in the character's outfit (OutfitLook) as
/// "shape:option=value" entries, so each character keeps its own in settings.json, the other PCs get them with the
/// outfit, and an option left as the model came is not written at all. OutfitDresser puts them on first, as the body
/// the clothes then follow by key name (Modular Avatar's Blendshape Sync).
/// </summary>
public static class BodyShapes
{
    public const string TableName = "BodyShapes";
    /// <summary>Before an option's id in an outfit's text, so it is never read as a slot.</summary>
    public const string Prefix = "shape:";
    private const char ListSeparator = '|', KeySeparator = '+';
    // A choice's key counts as on from here (keys are made at 0 or 100).
    private const float OnFrom = 50f;

    public enum Kind { Slider, Pair, Choice }

    /// <summary>The body page's tabs, in order (BodyShapes.csv group).</summary>
    public static readonly string[] Groups = { "body", "face", "eyes", "brows", "ears", "hair", "animal", "outfit" };

    public sealed class Option
    {
        public string Skin { get; internal set; }
        public string Id { get; internal set; }
        public string Group { get; internal set; }
        public Kind Kind { get; internal set; }
        /// <summary>
        /// Slider: one entry, its keys. Pair: the keys that make less, then the keys that make more. Choice: each choice's
        /// keys from the first, choice 0 turning them all off.
        /// </summary>
        public IReadOnlyList<string[]> Keys { get; internal set; }

        public float Min => Kind == Kind.Pair ? -100f : 0f;
        public float Max => Kind == Kind.Choice ? Keys.Count : 100f;
        public string Name => Texts.Get("body." + Id);

        /// <summary>A choice's chip words, the 0th first; a choice the words don't reach reads as its number.</summary>
        public string[] Choices
        {
            get
            {
                var words = Words("body." + Id + ".choices");
                var choices = new string[Keys.Count + 1];
                for (int i = 0; i < choices.Length; i++)
                    choices[i] = words != null && i < words.Length && words[i].Length > 0 ? words[i] : i.ToString(CultureInfo.InvariantCulture);
                return choices;
            }
        }

        /// <summary>A pair's words for its two ends, left then right; null without them.</summary>
        public string[] Ends
        {
            get
            {
                var words = Words("body." + Id + ".ends");
                return words != null && words.Length == 2 ? words : null;
            }
        }

        internal string Entry => Prefix + Id;

        private static string[] Words(string id) => Texts.Has(id) ? Texts.Get(id).Split(ListSeparator) : null;
    }

    private static Dictionary<string, List<Option>> bySkin;
    private static readonly Dictionary<Option, float> own = new Dictionary<Option, float>();
    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Forget();

    private static void Forget()
    {
        bySkin = null;
        own.Clear();
    }

    /// <summary>The character's options in the table's order; empty when its model has none.</summary>
    public static IReadOnlyList<Option> For(string skinId)
    {
        Ensure();
        return skinId != null && bySkin.TryGetValue(skinId, out var options) ? options : (IReadOnlyList<Option>)Array.Empty<Option>();
    }

    /// <summary>The value the player chose for the option, from the outfit's text; false when it is as the model came.</summary>
    public static bool TryChosen(OutfitLook look, Option option, out float value)
    {
        value = 0f;
        return look != null && option != null && look.TryGet(option.Entry, out var pick)
            && float.TryParse(pick.Item, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Writes the option's value into the outfit; null takes it out, back to the model as it came.</summary>
    public static void Choose(OutfitLook look, Option option, float? value)
    {
        if (look == null || option == null) return;
        look.Set(option.Entry, value.HasValue
            ? Mathf.RoundToInt(Mathf.Clamp(value.Value, option.Min, option.Max)).ToString(CultureInfo.InvariantCulture) : null);
    }

    /// <summary>What the option is on the character's model as it came (its prefab), the slider's "원래" mark.</summary>
    public static float Own(PlayerBodyProfile.Skin skin, Option option)
    {
        if (option == null) return 0f;
        if (own.TryGetValue(option, out float found)) return found;
        var meshes = skin?.Model != null ? skin.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true) : Array.Empty<SkinnedMeshRenderer>();
        found = Read(meshes, option);
        own[option] = found;
        return found;
    }

    /// <summary>The option as the outfit has it: the player's value, else the model's own.</summary>
    public static float Value(OutfitLook look, PlayerBodyProfile.Skin skin, Option option) =>
        TryChosen(look, option, out float value) ? value : Own(skin, option);

    /// <summary>How many of the character's options the outfit changes.</summary>
    public static int Changed(OutfitLook look, string skinId)
    {
        int changed = 0;
        foreach (var option in For(skinId))
            if (TryChosen(look, option, out _)) changed++;
        return changed;
    }

    /// <summary>The outfit without its body options (what the wardrobe's 원래 옷으로 empties).</summary>
    public static OutfitLook Clothes(OutfitLook look) => Filter(look, false);

    /// <summary>Only the outfit's body options (what the wardrobe's 원래 옷으로 keeps).</summary>
    public static OutfitLook Shapes(OutfitLook look) => Filter(look, true);

    private static OutfitLook Filter(OutfitLook look, bool shapes)
    {
        var kept = new OutfitLook();
        if (look == null) return kept;
        foreach (string slot in look.Slots)
        {
            if (slot.StartsWith(Prefix, StringComparison.Ordinal) != shapes || !look.TryGet(slot, out var pick)) continue;
            kept.Set(slot, pick.Item, pick.Colour);
        }
        return kept;
    }

    /// <summary>
    /// Puts the outfit's body options on the character's own meshes (every one with the keys: the body, its own clothes,
    /// hair lying on the chest). Returns the keys set, for the clothes put on afterwards to follow, a key set to 0 too.
    /// </summary>
    public static HashSet<string> Apply(Renderer[] own, string skinId, OutfitLook look)
    {
        var touched = new HashSet<string>(StringComparer.Ordinal);
        if (own == null || look == null) return touched;
        List<SkinnedMeshRenderer> meshes = null;
        foreach (var option in For(skinId))
        {
            if (!TryChosen(look, option, out float value)) continue;
            if (meshes == null)
            {
                meshes = new List<SkinnedMeshRenderer>();
                foreach (var renderer in own)
                    if (renderer is SkinnedMeshRenderer skinned) meshes.Add(skinned);
            }
            Set(meshes, option, value, touched);
        }
        return touched;
    }

    /// <summary>Sets the option's keys on these meshes for the value (the wardrobe's live slider uses it too).</summary>
    public static void Set(IReadOnlyList<SkinnedMeshRenderer> meshes, Option option, float value, ICollection<string> touched = null)
    {
        if (meshes == null || option == null) return;
        switch (option.Kind)
        {
            case Kind.Slider:
                Put(meshes, option.Keys[0], Mathf.Clamp(value, 0f, 100f), touched);
                break;
            case Kind.Pair:
                Put(meshes, option.Keys[0], Mathf.Clamp(-value, 0f, 100f), touched);
                Put(meshes, option.Keys[1], Mathf.Clamp(value, 0f, 100f), touched);
                break;
            default:
                // Every choice off first: two choices may share a key (one of them and "all of them").
                int chosen = Mathf.RoundToInt(value);
                foreach (var keys in option.Keys) Put(meshes, keys, 0f, touched);
                if (chosen >= 1 && chosen <= option.Keys.Count) Put(meshes, option.Keys[chosen - 1], 100f, touched);
                break;
        }
    }

    private static void Put(IReadOnlyList<SkinnedMeshRenderer> meshes, string[] keys, float weight, ICollection<string> touched)
    {
        foreach (string key in keys)
        {
            touched?.Add(key);
            foreach (var mesh in meshes)
            {
                int index = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetBlendShapeIndex(key) : -1;
                if (index >= 0) mesh.SetBlendShapeWeight(index, weight);
            }
        }
    }

    // The option as these meshes have it: a slider's key, a pair's more less its less, the choice whose keys are exactly
    // those on (0 when none is).
    private static float Read(IReadOnlyList<SkinnedMeshRenderer> meshes, Option option)
    {
        switch (option.Kind)
        {
            case Kind.Slider:
                return Weight(meshes, option.Keys[0][0]);
            case Kind.Pair:
                return Weight(meshes, option.Keys[1][0]) - Weight(meshes, option.Keys[0][0]);
            default:
                var on = new HashSet<string>(StringComparer.Ordinal);
                foreach (var keys in option.Keys)
                    foreach (string key in keys)
                        if (Weight(meshes, key) >= OnFrom) on.Add(key);
                if (on.Count == 0) return 0f;
                for (int i = 0; i < option.Keys.Count; i++)
                    if (on.SetEquals(option.Keys[i])) return i + 1;
                return 0f;
        }
    }

    // The first mesh's weight for the key that has it set, else 0: the rule dressing reads the body by (ShapesNow).
    private static float Weight(IReadOnlyList<SkinnedMeshRenderer> meshes, string key)
    {
        foreach (var mesh in meshes)
        {
            int index = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetBlendShapeIndex(key) : -1;
            if (index < 0) continue;
            float weight = mesh.GetBlendShapeWeight(index);
            if (weight != 0f) return weight;
        }
        return 0f;
    }

    private static void Ensure()
    {
        if (!listening)
        {
            GameTables.Reloaded += Forget;
            listening = true;
        }
        if (bySkin != null) return;
        bySkin = new Dictionary<string, List<Option>>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var cells)) continue;
            string skin = Cell(cells, "skin"), option = Cell(cells, "option"), group = Cell(cells, "group");
            var kind = Cell(cells, "kind").ToLowerInvariant() switch
            {
                "slider" => Kind.Slider,
                "pair" => Kind.Pair,
                "choice" => Kind.Choice,
                _ => (Kind)(-1),
            };
            var keys = new List<string[]>();
            foreach (string part in Cell(cells, "keys").Split(ListSeparator))
            {
                var names = new List<string>();
                foreach (string key in part.Split(KeySeparator))
                    if (key.Trim().Length > 0) names.Add(key.Trim());
                if (names.Count > 0) keys.Add(names.ToArray());
            }
            bool fits = kind == Kind.Slider ? keys.Count == 1 : kind == Kind.Pair ? keys.Count == 2 : kind == Kind.Choice && keys.Count > 0;
            if (skin.Length == 0 || option.Length == 0 || !fits)
            {
                Debug.LogWarning($"[BodyShapes] {TableName}.csv [{id}]: skin·option·kind·keys가 맞지 않아 건너뜁니다.");
                continue;
            }
            if (!bySkin.TryGetValue(skin, out var options)) bySkin[skin] = options = new List<Option>();
            options.Add(new Option { Skin = skin, Id = option, Group = group.Length > 0 ? group : Groups[0], Kind = kind, Keys = keys });
        }
    }

    private static string Cell(IReadOnlyDictionary<string, string> cells, string column) => TableCells.Text(cells, column);
}
