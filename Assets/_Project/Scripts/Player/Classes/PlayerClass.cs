using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Stage 7, the classes (the user's design, 2026-11-11: seven perks after Killing Floor 2 and GMod's Horde, "클래스나
/// 퍼크나 용어 혼동할 수 있는데 걍 그러려니"): Knight (melee, the toughest), Assault (adrenaline, CHARGE), Ghost (sniper,
/// head hunter), Point (SMG and shotgun, the gun put away reloads itself), Détente (healer), Schießhund (bounty marks,
/// the Desperado), Support (barricades, resupply). One is picked before a run (the title screen's 클래스 tab; kept in
/// ClassGrowth, each with its own rank) and may change between runs; a team may share one. This reads the table
/// (Tables/Classes: names, starting guns, the class's own guns and their bonus, whether its abilities are built yet)
/// and answers what this PC's player's class does to their shots and their pace (ClassKit keeps the live state).
/// Numbers of the abilities: Tables/ClassAbilities.
/// </summary>
public static class PlayerClass
{
    public const string TableName = ClassGrowth.ClassTable, AbilityTable = "ClassAbilities";
    public const string Knight = "knight", Assault = "assault", Ghost = "ghost", Point = "point", Detente = "detente",
        Schiesshund = "schiesshund", Support = "support";
    /// <summary>The rows of Tables/ClassAbilities ("Ghost.hunterMax").</summary>
    internal const string KnightRow = "Knight", GhostRow = "Ghost", HoundRow = "Schiesshund";

    /// <summary>One class in the table.</summary>
    public sealed class Info
    {
        public string Id, Title, Role, Primary, Secondary;
        public string[] Weapons = Array.Empty<string>();
        public float GunBonus;
        public bool Ready;
    }

    private static List<Info> catalog;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => catalog = null;

    /// <summary>Every class, in the table's order.</summary>
    public static IReadOnlyList<Info> Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            catalog = new List<Info>();
            foreach (string id in GameTables.RowIds(TableName))
            {
                if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
                catalog.Add(new Info
                {
                    Id = id, Title = Cell(row, "title", id), Role = Cell(row, "role", ""), Primary = Cell(row, "primary", ""),
                    Secondary = Cell(row, "secondary", ""),
                    Weapons = Cell(row, "classWeapons", "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    GunBonus = Number(row, "gunBonus", 0f), Ready = Number(row, "ready", 0f) > .5f
                });
            }
            return catalog;
        }
    }

    /// <summary>The class this PC's player plays (their pick, kept with its rank in ClassGrowth).</summary>
    public static string CurrentId => ClassGrowth.CurrentId;

    public static Info Current => Find(CurrentId);

    public static Info Find(string id)
    {
        foreach (var info in Catalog) if (string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) return info;
        return null;
    }

    public static bool Is(string id) => string.Equals(CurrentId, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this gun is one of the class's own (it hits harder in their hands: gunBonus).</summary>
    public static bool OwnsWeapon(Info info, WeaponData gun)
    {
        if (info == null || gun == null) return false;
        foreach (string name in info.Weapons) if (string.Equals(name, gun.name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// The bullet's damage factor from this PC's player's class: its own guns' bonus, an Assault's adrenaline, a CHARGE
    /// they are in, a Ghost's Head Hunter and Deadeye, a Schießhund's Return Back and Desperado round, and on the
    /// Schießhund's own mark (<paramref name="target"/>) its bounty's debuff. AttackTrace multiplies it in with the perk
    /// cards' for this PC's shots only.
    /// </summary>
    public static float GunFactor(bool headshot, Component target = null)
    {
        var kit = ClassKit.Local;
        if (kit == null) return 1f;
        var info = Current;
        float factor = OwnsWeapon(info, kit.HeldGun) ? 1f + info.GunBonus / 100f : 1f;
        if (target != null && Is(Schiesshund) && PingMarks.IsBountyOf(target, kit.Movement))
            factor *= 1f + Ability(HoundRow, "markDamage", 25f) / 100f;
        return factor * kit.DamageFactor;
    }

    /// <summary>
    /// A Schießhund's Desperado round in the head of its own mark: whatever it is, it dies (the user: "엘리트 같은 애들이
    /// 나와도 표식 찍고 리볼버로 헤드샷하면 한 방에"), but for a brute, the boss made for a whole team's fire, which loses
    /// bossShare of its whole health, as an execution takes a share of it. AttackTrace asks for this PC's own bullets;
    /// anything else keeps its amount.
    /// </summary>
    public static float Finish(float amount, bool headshot, IDamageReceiver receiver, Vector3 point, Vector3 from)
    {
        var kit = ClassKit.Local;
        if (!headshot || kit == null || !kit.DesperadoLoaded || !(receiver is EnemyHealth enemy) || !enemy.IsAlive) return amount;
        if (!PingMarks.IsBountyOf(enemy, kit.Movement)) return amount;
        kit.Finishing(enemy, point, from);
        // Its whole health, not what this PC has of it: the host's may stand higher than a copy's.
        bool firm = enemy.TryGetComponent(out MeleeCombatTarget melee) && melee.Steadfast;
        return Mathf.Max(amount, firm ? enemy.MaximumHealth * Mathf.Clamp01(Ability(HoundRow, "bossShare", .4f)) : enemy.MaximumHealth + 1f);
    }

    /// <summary>This PC's player's pace from their class now (adrenaline, a CHARGE): LocomotionState multiplies it in.</summary>
    public static float MoveFactor(PlayerMovement player)
    {
        var kit = ClassKit.Local;
        return kit != null && player != null && player.IsLocal && kit.Movement == player ? kit.SpeedFactor : 1f;
    }

    /// <summary>
    /// An elite to the classes (a Ghost's class gun staggers it): ranked elite or named, or one of the elite kinds, a
    /// brute, an exploder or a charger, however it was ranked.
    /// </summary>
    public static bool IsElite(EnemyHealth enemy)
    {
        if (enemy == null) return false;
        if (enemy.IsStandout) return true;
        if (!enemy.TryGetComponent(out ZombieType type)) return false;
        return type.Kind == ZombieKind.Brute || type.Kind == ZombieKind.Exploder || type.Kind == ZombieKind.Charger;
    }

    // ---- The Knight: what this PC's own player takes, and what their fists, parries and counters do ----

    /// <summary>
    /// The share of an enemy's blow this PC's player takes from their class: a Knight less (the toughest class), and less
    /// again while their taunt holds. PlayerDamageReceiver multiplies it in, on the player's own PC.
    /// </summary>
    public static float TakenFactor(PlayerMovement player)
    {
        if (!Knights(player)) return 1f;
        float share = Mathf.Clamp01(Ability(KnightRow, "taken", .75f));
        if (ClassKit.Local.TauntShare > 0f) share *= Mathf.Clamp01(Ability(KnightRow, "tauntTaken", .6f));
        return share;
    }

    /// <summary>A Knight's fists hit this much harder, damage and posture (PlayerMeleeCombat's punches).</summary>
    public static float FistFactor(PlayerMovement player) => Knights(player) ? 1f + Ability(KnightRow, "fists", 50f) / 100f : 1f;

    /// <summary>A Knight's counter, after a dodge or a parry, hits this much harder (PlayerPerfectDefense: the memo's 흘리기).</summary>
    public static float CounterFactor(PlayerMovement player) => Knights(player) ? 1f + Ability(KnightRow, "counter", 50f) / 100f : 1f;

    /// <summary>A Knight's parry windows are this much longer (PlayerMeleeCombat).</summary>
    public static float ParryWindowFactor(PlayerMovement player) => Knights(player) ? 1f + Ability(KnightRow, "parryWindow", 50f) / 100f : 1f;

    /// <summary>A Knight's parry wears the attacker's posture down this much more (PlayerMeleeCombat).</summary>
    public static float ParryPostureFactor(PlayerMovement player) => Knights(player) ? 1f + Ability(KnightRow, "parryPosture", 100f) / 100f : 1f;

    /// <summary>A Knight's parry opens the counter as a perfect dodge does (the memo's 퍼펙트 패리; PlayerPerfectDefense).</summary>
    public static bool ParryCounters(PlayerMovement player) => Knights(player) && Ability(KnightRow, "parryCounter", 1f) > .5f;

    // This PC's own player, playing a Knight.
    private static bool Knights(PlayerMovement player)
    {
        var kit = ClassKit.Local;
        return player != null && kit != null && kit.Movement == player && Is(Knight);
    }

    /// <summary>An ability's number (Tables/ClassAbilities, "Class.name").</summary>
    public static float Ability(string classRow, string name, float fallback)
        => GameTables.TryGetRow(AbilityTable, classRow, out var row) ? Number(row, name, fallback) : fallback;

    private static string Cell(IReadOnlyDictionary<string, string> row, string key, string fallback)
        => row.TryGetValue(key, out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}
