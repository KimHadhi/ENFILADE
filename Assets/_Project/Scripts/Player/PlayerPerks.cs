using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// The perk cards this PC's player has picked (stage 6C, the user's rules on 2026-11-09): a full ultimate gauge buys a
/// pick of one card from three (PerkCardScreen), each card raises one perk a level, and the perks last the whole
/// campaign, from chapter to chapter, until the title screen. The cards and their numbers are Tables/Perks. Only this
/// player's own perks exist here; each one works where its effect is judged, which is always this PC (the shooter's
/// hits, this player's health, getting a friend up). The effect lookups below are what the game's systems ask.
/// <para>
/// Class cards (2026-11-11, the user's "클래스별 스킬(카드)"): a card with a class in its row is dealt only to a player of
/// that class, and before the cards everyone can draw (Draw, 2026-11-12); a card with "after" waits until that many levels of its class's
/// other cards are held, which is how a class's final card (궁극) comes late; and a card naming a ClassAbilities number
/// ("ability") simply adds to it, so most class cards need no code (PlayerClass.Ability reads the sum). Every card says
/// why it does what it does (the why line, the user's rule that an effect comes with a reason that makes it believable).
/// What the code-backed class cards do is ClassCards' (Player/Classes).
/// </para>
/// <para>
/// In the hub the picks are practice (BeginPractice, 2026-11-12): dealt by the same rule from the range's kills, working
/// as cards do, and gone again when the hub is left or a class is picked there (EndPractice, RestartPractice), so the
/// campaign's own cards are what a chapter gets.
/// </para>
/// </summary>
public static class PlayerPerks
{
    public const string TableName = "Perks";

    /// <summary>One card in the table.</summary>
    public sealed class Perk
    {
        public string Id, Name, Class, Tag, Icon, Text, Why, Ability;
        public int Levels, After;
        public float Value, Value2, Display = 1f, Weight;
        /// <summary>A class's final card (궁극): drawn last, shown apart.</summary>
        public bool Final;

        /// <summary>
        /// The effect line at <paramref name="level"/>: {0} is everything up to that level (value × level, times its
        /// display factor), {1} the same of value2, and {Row.name} a number of Tables/ClassAbilities as it stands.
        /// </summary>
        public string Describe(int level)
        {
            int at = Mathf.Max(0, level);
            string text = string.IsNullOrEmpty(Text) ? "{0}" : Text;
            text = text.Replace("{0}", Shown(Value * at * Display)).Replace("{1}", Shown(Value2 * at));
            return Abilities(text);
        }

        /// <summary>The why line, its {Row.name} numbers filled in.</summary>
        public string Reason => string.IsNullOrEmpty(Why) ? "" : Abilities(Why);
    }

    // The perk ids the code knows; the table can add others for later.
    public const string HeadshotDamage = "gun_head", GunDamage = "gun_body", FistDamage = "melee_power", ExecutionHeal = "melee_butcher",
        Toughness = "body_tough", Grit = "body_grit", Rescue = "team_rescue", Charge = "gauge_charge",
        Heriot = "heriot", Momentum = "momentum";
    // The class cards with code behind them (ClassCards, ClassKit, PlayerClass).
    public const string KnightDeflect = "knight_deflect", KnightReversal = "knight_reversal", KnightGauntlet = "knight_gauntlet",
        KnightShadow = "knight_shadow", AssaultDanger = "assault_danger", AssaultBroken = "assault_broken", AssaultCavalry = "assault_cavalry",
        GhostPierce = "ghost_pierce", GhostWeakpoint = "ghost_weakpoint", GhostTiger = "ghost_whitetiger", PointGunslinger = "point_gunslinger",
        HoundAce = "hound_ace", SupportTorch = "support_torch", KnightShadowstep = "knight_shadowstep",
        SupportWire = "support_wire", SupportCrate = "support_crate", SupportLight = "support_light";

    private static readonly Dictionary<string, int> levels = new Dictionary<string, int>();
    private static List<Perk> catalog;
    // What the held cards add to ClassAbilities numbers, by row then name (null: to be summed again).
    private static Dictionary<string, Dictionary<string, float>> bonuses;
    // Momentum (기세): until when this player's hits keep it going, and how long one hit buys.
    private static float momentumUntil = -99f, momentumSeconds = 1f;

    /// <summary>A perk went up a level, or all were cleared.</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        levels.Clear();
        kept = null;
        catalog = null;
        bonuses = null;
        Changed = null;
        momentumUntil = -99f;
        // A table saved during play is read again (GameTables.Reload), the cards included.
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
                catalog.Add(new Perk
                {
                    Id = id, Name = Text(row, "name", id), Class = Text(row, "class", ""), Tag = Text(row, "tag", ""),
                    Icon = Text(row, "icon", ""), Text = Text(row, "text", ""), Why = Text(row, "why", ""), Ability = Text(row, "ability", ""),
                    Levels = Mathf.Max(1, (int)Number(row, "levels", 1f)), After = Mathf.Max(0, (int)Number(row, "after", 0f)),
                    Value = Number(row, "value", 0f), Value2 = Number(row, "value2", 0f), Display = Number(row, "display", 1f),
                    Weight = Mathf.Max(0f, Number(row, "weight", 1f)), Final = Number(row, "final", 0f) > .5f
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

    public static bool Has(string id) => Level(id) > 0;

    /// <summary>Everything the perk adds at its level now (its value × level), 0 without it.</summary>
    public static float Total(string id)
    {
        var perk = Find(id);
        return perk != null ? perk.Value * Mathf.Min(Level(id), perk.Levels) : 0f;
    }

    /// <summary>The same of its second number (value2 × level).</summary>
    public static float Total2(string id)
    {
        var perk = Find(id);
        return perk != null ? perk.Value2 * Mathf.Min(Level(id), perk.Levels) : 0f;
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
    /// Whether this card may be dealt now: not at its top level, everyone's or this player's class's, and its class's
    /// other cards held to its "after" (a final card waits for them).
    /// </summary>
    public static bool Dealable(Perk perk)
    {
        if (perk == null || perk.Weight <= 0f || Level(perk.Id) >= perk.Levels) return false;
        if (string.IsNullOrEmpty(perk.Class)) return true;
        return PlayerClass.Is(perk.Class) && ClassLevels(perk.Class) >= perk.After;
    }

    /// <summary>Any card left to deal this player.</summary>
    public static bool AnyLeft()
    {
        foreach (var perk in Catalog) if (Dealable(perk)) return true;
        return false;
    }

    /// <summary>Levels held of a class's cards, its final card left out (what a final card's "after" counts).</summary>
    public static int ClassLevels(string classId)
    {
        int sum = 0;
        foreach (var perk in Catalog)
            if (!perk.Final && string.Equals(perk.Class, classId, StringComparison.OrdinalIgnoreCase)) sum += Mathf.Min(Level(perk.Id), perk.Levels);
        return sum;
    }

    /// <summary>The class this card belongs to, as the class table names it (empty: everyone's).</summary>
    public static string ClassTitle(Perk perk) => perk == null || string.IsNullOrEmpty(perk.Class) ? "" : PlayerClass.Find(perk.Class)?.Title ?? perk.Class;

    /// <summary>
    /// Up to <paramref name="count"/> different perks dealable now, the heavier ones likelier: this player's class's own
    /// cards first, and everyone's only once the class has none left to deal (the user, 2026-11-12: "처음에는 클래스 카드만
    /// 나오다가 클래스 카드에서 뽑을 게 더 없으면 공통 카드가 나오다가 더 뽑을 카드가 없으면 그제서야 궁극기로"), so a class
    /// grows into itself before anything else. A class hand of fewer than <paramref name="count"/> is dealt as it is, not
    /// filled up with everyone's, which would let those be picked ahead of the class's own.
    /// </summary>
    public static List<Perk> Draw(int count)
    {
        var pool = new List<Perk>();
        // Dealable already keeps another class's cards out, so any card with a class here is this player's.
        foreach (var perk in Catalog) if (!string.IsNullOrEmpty(perk.Class) && Dealable(perk)) pool.Add(perk);
        if (pool.Count == 0) foreach (var perk in Catalog) if (Dealable(perk)) pool.Add(perk);
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
        momentumUntil = -99f;
        // Practising, the campaign's own cards go too, so nothing comes back when practice ends, whichever comes first
        // (the hub left for the title screen: its end and this Clear run in either order).
        kept?.Clear();
        if (levels.Count == 0) return;
        levels.Clear();
        bonuses = null;
        Changed?.Invoke();
    }

    // ---- Practice (the hub, 2026-11-12) ----

    // The cards held when practice began (the campaign's own so far), or null when not practising.
    private static Dictionary<string, int> kept;

    /// <summary>
    /// Picks are practice now (the hub: the user, "궁극기가 좀 이상한 게. 카드를 뽑아야 하는 거 아니야?"): its range's kills
    /// fill the gauge and deal cards by the mission's own rule, and they work as cards do, but they go again when practice
    /// ends or starts over, so nothing picked in the hub reaches a chapter.
    /// </summary>
    public static bool IsPractising => kept != null;

    /// <summary>From now on picks are practice; the cards held now (the campaign's) are what practice goes back to.</summary>
    public static void BeginPractice()
    {
        kept = new Dictionary<string, int>(levels);
        momentumUntil = -99f;
    }

    /// <summary>Practice starts over (a class picked in the hub): the practice cards go, the campaign's stay.</summary>
    public static void RestartPractice()
    {
        if (kept != null) Restore();
    }

    /// <summary>Practice is over (the hub left): the practice cards go, and picks count for the campaign again.</summary>
    public static void EndPractice()
    {
        if (kept == null) return;
        Restore();
        kept = null;
    }

    private static void Restore()
    {
        momentumUntil = -99f;
        bool same = levels.Count == kept.Count;
        if (same)
            foreach (var pair in levels)
                if (!kept.TryGetValue(pair.Key, out int level) || level != pair.Value) { same = false; break; }
        if (same) return;
        levels.Clear();
        foreach (var pair in kept) levels[pair.Key] = pair.Value;
        bonuses = null;
        Changed?.Invoke();
    }

    // ---- What the game's systems ask ----

    /// <summary>
    /// What the cards add to a class's number in Tables/ClassAbilities (row "Ghost", name "hunterMax"): every held card
    /// naming it, value × level. PlayerClass.Ability adds it in; it is asked many times a frame, so the sums are kept
    /// (by row, then name) and made again only when a card is picked or the cards are cleared.
    /// </summary>
    public static float AbilityBonus(string row, string name)
    {
        if (levels.Count == 0 || string.IsNullOrEmpty(row) || string.IsNullOrEmpty(name)) return 0f;
        if (bonuses == null) SumBonuses();
        return bonuses.TryGetValue(row, out var names) && names.TryGetValue(name, out float sum) ? sum : 0f;
    }

    private static void SumBonuses()
    {
        bonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        foreach (var perk in Catalog)
        {
            int level = Mathf.Min(Level(perk.Id), perk.Levels);
            int dot = string.IsNullOrEmpty(perk.Ability) ? -1 : perk.Ability.IndexOf('.');
            if (level <= 0 || dot <= 0 || dot >= perk.Ability.Length - 1) continue;
            string row = perk.Ability.Substring(0, dot), name = perk.Ability.Substring(dot + 1);
            if (!bonuses.TryGetValue(row, out var names)) bonuses[row] = names = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            names[name] = (names.TryGetValue(name, out float sum) ? sum : 0f) + perk.Value * level;
        }
    }

    /// <summary>
    /// The bullet's damage factor for this PC's shooter: head or body. A Ghost's weak-point card (약점 사냥, the user's
    /// own trade) adds to the head what it takes from the body; Momentum adds while it runs.
    /// </summary>
    public static float GunFactor(bool headshot)
    {
        float percent = Total(headshot ? HeadshotDamage : GunDamage);
        if (Has(GhostWeakpoint)) percent += headshot ? Total(GhostWeakpoint) : -Total2(GhostWeakpoint);
        return Mathf.Max(.05f, 1f + percent / 100f) * MomentumFactor;
    }

    /// <summary>The fists' damage factor (and Momentum's while it runs).</summary>
    public static float FistFactor => (1f + Total(FistDamage) / 100f) * MomentumFactor;

    /// <summary>
    /// How far this PC's own bullets get through bodies and walls (a factor on the gun's penetration): a Ghost's
    /// over-penetration card (과관통). The shooter's PC alone judges the hits; AttackTrace.Bullet asks.
    /// </summary>
    public static float PierceFactor(DamageHit attack)
        => attack.Method == DamageMethod.Bullet && Has(GhostPierce) && IsMine(attack.Source) ? 1f + Total(GhostPierce) / 100f : 1f;

    /// <summary>
    /// Heriot (사망세): the share of a kill's money a card adds on top, from the kill's own points
    /// (CombatFeedHUD.Kill, this PC's own kills only). 0 without the card.
    /// </summary>
    public static int DeathTax(int points) => points > 0 && Has(Heriot) ? Mathf.RoundToInt(points * Total(Heriot) / 100f) : 0;

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

    // ---- Momentum (기세, the user's "때릴 때마다 갱신되는 강화 효과") ----

    /// <summary>
    /// This player's hit landed on an enemy (ClassCards): Momentum, if held, runs on from now. A miss changes nothing;
    /// it only lets it run out (the user's rule: a hit is a plus, a miss is never a minus).
    /// </summary>
    public static void Struck()
    {
        if (!Has(Momentum)) return;
        momentumSeconds = Mathf.Max(.1f, PlayerClass.Ability("Cards", "momentumSeconds", 3f));
        momentumUntil = Time.time + momentumSeconds;
    }

    /// <summary>How much of Momentum is left, 0 to 1 (0: not running).</summary>
    public static float MomentumShare => Has(Momentum) && momentumUntil > Time.time ? Mathf.Clamp01((momentumUntil - Time.time) / momentumSeconds) : 0f;

    private static float MomentumFactor => MomentumShare > 0f ? 1f + Total(Momentum) / 100f : 1f;

    /// <summary>Whether <paramref name="source"/> is this PC's own player, whose perks these are.</summary>
    public static bool IsMine(GameObject source)
        => source != null && source.GetComponentInParent<PlayerMovement>() is PlayerMovement player && player.IsLocal;

    // ---- Reading the table ----

    private static string Shown(float number) => number.ToString("0.##", CultureInfo.InvariantCulture);

    // {Row.name} in a card's line: the number of Tables/ClassAbilities as it stands (cards included).
    private static string Abilities(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        var built = new StringBuilder(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf('{', at);
            int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
            if (open < 0 || close < 0) { built.Append(text, at, text.Length - at); break; }
            built.Append(text, at, open - at);
            string inner = text.Substring(open + 1, close - open - 1);
            int dot = inner.IndexOf('.');
            if (dot > 0 && dot < inner.Length - 1 && inner.IndexOf(' ') < 0)
                built.Append(Shown(PlayerClass.Ability(inner.Substring(0, dot), inner.Substring(dot + 1), 0f)));
            else built.Append(text, open, close - open + 1);
            at = close + 1;
        }
        return built.ToString();
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}
