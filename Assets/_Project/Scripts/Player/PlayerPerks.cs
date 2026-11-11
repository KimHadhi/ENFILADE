using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The perk cards this PC's player has picked (stage 6C, the user's rules on 2026-11-09): a full ultimate gauge buys a
/// pick of one card from three (PerkCardScreen), each card raises one perk a level, and the perks last the whole
/// campaign, from chapter to chapter, until the title screen. The cards and their numbers are Tables/Perks. Only this
/// player's own perks exist here; each one works where its effect is judged, which is always this PC (the shooter's
/// hits, this player's health, getting a friend up). The effect lookups below are what the game's systems ask.
/// Class cards (bundle 4, after GMod Horde's 2401598805 perk tree: a base perk, then tiers of two choices) are dealt to
/// one class only and raise one of its numbers in Tables/ClassAbilities (PlayerClass.Ability adds them in). Within a
/// class the cards of one tier are rivals: once one is taken the other is never dealt again, and a tier is dealt only
/// once the tier below has its card.
/// </summary>
public static class PlayerPerks
{
    public const string TableName = "Perks";

    /// <summary>One card in the table.</summary>
    public sealed class Perk
    {
        public string Id, Name, Tag, Icon, Text;
        /// <summary>A class card's class (Tables/Classes id), dealt to that class only; empty for every class.</summary>
        public string Class = "";
        /// <summary>Its tier in its class's tree (Horde's): one card per tier, the tier below first; 0 for none.</summary>
        public int Tier;
        /// <summary>The Tables/ClassAbilities number it raises by Value a level ("Ghost" and "hunterMax"); empty for none.</summary>
        public string AbilityRow = "", AbilityName = "";
        public int Levels;
        /// <summary>Shown: the card's number is what it adds times this (-100 shows a -.05 share as 5%).</summary>
        public float Value, Weight, Shown = 1f;

        /// <summary>The effect line at <paramref name="level"/> ({0} = everything up to that level).</summary>
        public string Describe(int level)
        {
            string total = (Value * Mathf.Max(0, level) * Shown).ToString("0.##", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Text) ? total : Text.Replace("{0}", total);
        }
    }

    // The perk ids the code knows; the table can add others for later.
    public const string HeadshotDamage = "gun_head", GunDamage = "gun_body", FistDamage = "melee_power", ExecutionHeal = "melee_butcher",
        Toughness = "body_tough", Grit = "body_grit", Rescue = "team_rescue", Charge = "gauge_charge";

    private static readonly Dictionary<string, int> levels = new Dictionary<string, int>();
    private static List<Perk> catalog;
    // What the cards held add to each ClassAbilities number (row → name → sum), for the class they were summed for;
    // null to sum again.
    private static Dictionary<string, Dictionary<string, float>> bonuses;
    private static string bonusClass;

    /// <summary>A perk went up a level, or all were cleared.</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        levels.Clear();
        catalog = null;
        bonuses = null;
        Changed = null;
        // A table saved during Play deals and counts by its new rows (class cards are tuned live).
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        catalog = null;
        bonuses = null;
    }

    /// <summary>Every perk in the table, in its order.</summary>
    public static IReadOnlyList<Perk> Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            catalog = new List<Perk>();
            foreach (string id in GameTables.RowIds(TableName))
            {
                if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
                string ability = Text(row, "ability", "");
                int dot = ability.IndexOf('.');
                catalog.Add(new Perk
                {
                    Id = id, Name = Text(row, "name", id), Tag = Text(row, "tag", ""), Icon = Text(row, "icon", ""),
                    Text = Text(row, "text", ""), Levels = Mathf.Max(1, (int)Number(row, "levels", 1f)),
                    Value = Number(row, "value", 0f), Weight = Mathf.Max(0f, Number(row, "weight", 1f)),
                    Class = Text(row, "class", ""), Tier = Mathf.Max(0, (int)Number(row, "tier", 0f)),
                    AbilityRow = dot > 0 ? ability.Substring(0, dot).Trim() : "",
                    AbilityName = dot > 0 ? ability.Substring(dot + 1).Trim() : "",
                    Shown = Number(row, "shown", 1f)
                });
            }
            return catalog;
        }
    }

    public static Perk Find(string id)
    {
        foreach (var perk in Catalog) if (perk.Id == id) return perk;
        return null;
    }

    public static int Level(string id) => id != null && levels.TryGetValue(id, out int level) ? level : 0;

    /// <summary>Everything the perk adds at its level now (its value × level), 0 without it.</summary>
    public static float Total(string id)
    {
        var perk = Find(id);
        return perk != null ? perk.Value * Mathf.Min(Level(id), perk.Levels) : 0f;
    }

    /// <summary>The perks held, in the table's order, with their levels.</summary>
    public static IEnumerable<(Perk perk, int level)> Held()
    {
        foreach (var perk in Catalog)
        {
            int level = Level(perk.Id);
            if (level > 0) yield return (perk, level);
        }
    }

    /// <summary>
    /// Whether <paramref name="perk"/> may be dealt to this PC's player now: it has a weight and a level left, and a class
    /// card besides needs this player's class, no rival of its tier taken (Horde's one of two) and, above the first tier,
    /// a card of the tier below (Horde's tiers open in turn).
    /// </summary>
    public static bool Drawable(Perk perk)
    {
        if (perk == null || perk.Weight <= 0f || Level(perk.Id) >= perk.Levels || !ForClass(perk, PlayerClass.CurrentId)) return false;
        if (perk.Tier <= 0) return true;
        // The nearest tier below that has cards at all, and whether one of them is held.
        int below = 0;
        bool held = false;
        foreach (var other in Catalog)
        {
            if (other == perk || other.Tier <= 0 || !SameClass(other, perk)) continue;
            bool owned = Level(other.Id) > 0;
            if (other.Tier == perk.Tier)
            {
                if (owned) return false;
                continue;
            }
            if (other.Tier > perk.Tier) continue;
            if (other.Tier > below) { below = other.Tier; held = owned; }
            else if (other.Tier == below) held |= owned;
        }
        return below == 0 || held;
    }

    /// <summary>The names of the cards taking <paramref name="perk"/> would shut out (its tier's others), "" for none.</summary>
    public static string Rivals(Perk perk)
    {
        if (perk == null || perk.Tier <= 0) return "";
        string names = "";
        foreach (var other in Catalog)
            if (other != perk && other.Tier == perk.Tier && SameClass(other, perk)) names += (names.Length > 0 ? ", " : "") + other.Name;
        return names;
    }

    /// <summary>Up to <paramref name="count"/> different perks not yet at their top level, the heavier ones likelier.</summary>
    public static List<Perk> Draw(int count)
    {
        var pool = new List<Perk>();
        foreach (var perk in Catalog) if (Drawable(perk)) pool.Add(perk);
        var drawn = new List<Perk>();
        while (drawn.Count < count && pool.Count > 0)
        {
            float sum = 0f;
            foreach (var perk in pool) sum += perk.Weight;
            float pick = UnityEngine.Random.value * sum;
            int index = 0;
            for (; index < pool.Count - 1; index++)
            {
                pick -= pool[index].Weight;
                if (pick <= 0f) break;
            }
            drawn.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return drawn;
    }

    /// <summary>Raises the perk a level (up to its top).</summary>
    public static void Raise(string id)
    {
        var perk = Find(id);
        if (perk == null) return;
        levels[id] = Mathf.Min(perk.Levels, Level(id) + 1);
        bonuses = null;
        Changed?.Invoke();
    }

    /// <summary>Forgets every perk (back at the title screen: a new campaign starts bare).</summary>
    public static void Clear()
    {
        if (levels.Count == 0) return;
        levels.Clear();
        bonuses = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// What this PC's player's cards add to Tables/ClassAbilities' <paramref name="row"/>.<paramref name="name"/>: each
    /// card for their class (or for every class) that raises it, its value times its level. PlayerClass.Ability adds it in.
    /// </summary>
    public static float AbilityBonus(string row, string name)
    {
        if (levels.Count == 0 || string.IsNullOrEmpty(row) || string.IsNullOrEmpty(name)) return 0f;
        string mine = PlayerClass.CurrentId;
        if (bonuses == null || !string.Equals(bonusClass, mine, StringComparison.OrdinalIgnoreCase)) SumBonuses(mine);
        return bonuses.TryGetValue(row, out var names) && names.TryGetValue(name, out float sum) ? sum : 0f;
    }

    private static void SumBonuses(string mine)
    {
        bonusClass = mine;
        bonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        foreach (var perk in Catalog)
        {
            int level = Mathf.Min(Level(perk.Id), perk.Levels);
            if (level <= 0 || perk.AbilityRow.Length == 0 || perk.AbilityName.Length == 0 || !ForClass(perk, mine)) continue;
            if (!bonuses.TryGetValue(perk.AbilityRow, out var names))
                bonuses[perk.AbilityRow] = names = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            names.TryGetValue(perk.AbilityName, out float sum);
            names[perk.AbilityName] = sum + perk.Value * level;
        }
    }

    // A card for every class, or for this one.
    private static bool ForClass(Perk perk, string classId)
        => string.IsNullOrEmpty(perk.Class) || string.Equals(perk.Class, classId, StringComparison.OrdinalIgnoreCase);

    private static bool SameClass(Perk a, Perk b) => string.Equals(a.Class, b.Class, StringComparison.OrdinalIgnoreCase);

    // ---- What the game's systems ask ----

    /// <summary>The bullet's damage factor for this PC's shooter: head or body.</summary>
    public static float GunFactor(bool headshot) => 1f + Total(headshot ? HeadshotDamage : GunDamage) / 100f;

    /// <summary>The fists' damage factor.</summary>
    public static float FistFactor => 1f + Total(FistDamage) / 100f;

    /// <summary>Health an execution gives back.</summary>
    public static float ExecutionHealth => Total(ExecutionHeal);

    /// <summary>Health added on top of the body's own.</summary>
    public static float ExtraHealth => Total(Toughness);

    /// <summary>How much longer a downed player lasts (factor on the bleed-out time).</summary>
    public static float BleedFactor => 1f + Total(Grit) / 100f;

    /// <summary>How long getting a friend up takes (factor on the time, never below a fifth).</summary>
    public static float ReviveFactor => Mathf.Max(.2f, 1f - Total(Rescue) / 100f);

    /// <summary>How fast the gauge fills (factor on each hit's charge).</summary>
    public static float ChargeFactor => 1f + Total(Charge) / 100f;

    /// <summary>Whether <paramref name="source"/> is this PC's own player, whose perks these are.</summary>
    public static bool IsMine(GameObject source)
        => source != null && source.GetComponentInParent<PlayerMovement>() is PlayerMovement player && player.IsLocal;

    private static string Text(IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}
