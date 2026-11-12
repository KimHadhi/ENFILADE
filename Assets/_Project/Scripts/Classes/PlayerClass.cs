using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Stage 7, the classes (the user's design, 2026-11-11: seven perks after Killing Floor 2 and GMod's Horde, "클래스나
/// 퍼크나 용어 혼동할 수 있는데 걍 그러려니"): Knight (melee, the toughest), Assault (adrenaline, CHARGE), Ghost (sniper,
/// head hunter), Point (SMG and shotgun, the gun put away reloads itself), Détente (healer), Schießhund (bounty marks,
/// the Desperado), Support (barricades, resupply). One is picked before a run (the title screen's 클래스 tab, or the hub's
/// class wall; kept in ClassGrowth, each with its own rank) and may change between runs but never in one (MayPick); each
/// pick starts its kit afresh (Picked); a team may share one. This reads the table
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

    /// <summary>
    /// This PC's player picked a class (ClassGrowth.Choose: the hub's class wall, the title screen's 클래스 tab), with the
    /// class before and the class now. A pick is a fresh start (the user, 2026-11-12: "클래스 선택할 때 초기화 하는 방식"):
    /// ClassKit hands the class's kit as a chapter would (every stack at 0, every charge and wait ready, its guns full,
    /// the bag and syringe as they start, the last pick's barricades down), and whatever else a class keeps for the run
    /// starts again on it too (the hub's practice cards and ultimate gauge). Raised only where a pick may happen
    /// (MayPick), so never mid-run.
    /// </summary>
    public static event Action<string, string> Picked;

    /// <summary>
    /// Whether a class may be picked now: out of a run (the title screen) or in the hub. Never in a run (a chapter, a wave
    /// map: ClassGrowth.InGame), dead or alive: a pick there would hand out a fresh kit, a full bag and every charge back,
    /// for nothing.
    /// </summary>
    public static bool MayPick => !ClassGrowth.InGame || HubLobby.IsActive;

    /// <summary>ClassGrowth.Choose took a pick: everyone listening hears it (Picked).</summary>
    internal static void RaisePicked(string before, string now) => Picked?.Invoke(before, now);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        catalog = null;
        Picked = null;
        // A table saved during play is read again, the class names and roles in Texts.csv included.
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => catalog = null;

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
                    // The title and role cells name Texts rows (class.<id>.title, .role), or hold plain words.
                    Id = id, Title = Texts.Cell(Cell(row, "title", id)), Role = Texts.Cell(Cell(row, "role", "")), Primary = Cell(row, "primary", ""),
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
    /// Schießhund's own mark (<paramref name="target"/>) its bounty's debuff, and on a zombie its bounty dye
    /// splashed the dye's. AttackTrace multiplies it in with the perk cards' for this PC's shots only.
    /// </summary>
    public static float GunFactor(bool headshot, Component target = null)
    {
        var kit = ClassKit.Local;
        if (kit == null) return 1f;
        var info = Current;
        float factor = OwnsWeapon(info, kit.HeldGun) ? 1f + info.GunBonus / 100f : 1f;
        if (target != null && Is(Schiesshund) && PingMarks.IsBountyOf(target, kit.Movement))
            factor *= 1f + Ability(HoundRow, "markDamage", 25f) / 100f;
        // A Schießhund's bounty dye (DyeMarks): the zombies its own dye splashed take more from its guns, the glowing dye
        // showing the hound where to put its rounds (GrenadeRules dyeDamage). On its mark as well, it adds to the mark's.
        if (target != null && Is(Schiesshund)) factor *= DyeMarks.GunFactor(target, kit.Movement);
        // A Schießhund's duel: the foe it called out takes more again from it (HoundDuel).
        if (target != null && Is(Schiesshund) && HoundDuel.Of(kit.Movement) is HoundDuel duel && duel.Foe != null
            && duel.Foe.gameObject == target.gameObject)
            factor *= 1f + Ability(HoundRow, "duelDamage", 25f) / 100f;
        // An Assault's Danger Close card (데인저 클로즈, the call for fire right up against one's own lines): near
        // enemies take more.
        if (target != null && Is(Assault) && PlayerPerks.Has(PlayerPerks.AssaultDanger))
        {
            float reach = Ability("Assault", "dangerReach", 6f);
            if ((target.transform.position - kit.transform.position).sqrMagnitude <= reach * reach)
                factor *= 1f + PlayerPerks.Total(PlayerPerks.AssaultDanger) / 100f;
        }
        return factor * kit.DamageFactor;
    }

    /// <summary>
    /// A Schießhund's Desperado round in the head of its own mark: whatever it is, it dies (the user: "엘리트 같은 애들이
    /// 나와도 표식 찍고 리볼버로 헤드샷하면 한 방에"), the brute too (2026-11-13: "데스페라도 정도면 거구도 즉사해라 좀").
    /// ClassAbilities Schiesshund.bossShare under 1 would leave a brute standing with only that share of its whole health
    /// gone. AttackTrace asks for this PC's own bullets; anything else keeps its amount.
    /// </summary>
    public static float Finish(float amount, bool headshot, IDamageReceiver receiver, Vector3 point, Vector3 from)
    {
        var kit = ClassKit.Local;
        // Any foe: a zombie, or a marked target on the hub's range, where the Desperado is tried out (ICombatFoe).
        if (!headshot || kit == null || !kit.DesperadoLoaded || !(receiver is ICombatFoe foe) || !foe.IsAlive || !(receiver is Component target))
            return amount;
        if (!PingMarks.IsBountyOf(target, kit.Movement)) return amount;
        kit.Finishing(target, point, from);
        // Its whole health, not what this PC has of it: the host's may stand higher than a copy's. A brute's too, unless
        // the table keeps a brute to a share (bossShare under 1).
        bool firm = target.TryGetComponent(out MeleeCombatTarget melee) && melee.Steadfast;
        float share = firm ? Mathf.Clamp01(Ability(HoundRow, "bossShare", 1f)) : 1f;
        return Mathf.Max(amount, share >= 1f ? foe.MaximumHealth + 1f : foe.MaximumHealth * share);
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

    /// <summary>A factor on how long a gun takes to come out for <paramref name="who"/>: none at all in a Point's Gun Kata (ClassKit).</summary>
    public static float DrawFactor(PlayerMovement who) => InKata(who) ? 0f : 1f;

    /// <summary>A factor on the spread moving adds to <paramref name="who"/>'s shots: none in a Point's Gun Kata, shooting on the run.</summary>
    public static float MovingSpreadFactor(PlayerMovement who) => InKata(who) ? 0f : 1f;

    private static bool InKata(PlayerMovement who)
    {
        var kit = ClassKit.Local;
        return who != null && kit != null && kit.Movement == who && kit.KataShare > 0f;
    }

    /// <summary>
    /// How big a quarry a foe is to a Schießhund's bounty mark: 0 a common zombie (or the range's zombie dummy), 1 an elite
    /// (or the range's steel plate, to try the rule out on), 2 a brute or a named one. F moves the mark only onto a quarry
    /// as big or bigger than the one it hunts (PlayerPing).
    /// </summary>
    public static int Grade(Component foe)
    {
        if (foe is EnemyHealth enemy)
        {
            if (enemy.Rank == EnemyRank.Named) return 2;
            if (enemy.TryGetComponent(out ZombieType type) && type.Kind == ZombieKind.Brute) return 2;
            return IsElite(enemy) ? 1 : 0;
        }
        return foe is RangeTarget target && target.IsPlate ? 1 : 0;
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

    /// <summary>
    /// An ability's number (Tables/ClassAbilities, "Class.name"), with what this PC's player's perk cards add to it
    /// (a card naming it in Tables/Perks' ability column: PlayerPerks.AbilityBonus). Only a player of that class is ever
    /// dealt its cards, so a class's numbers grow for its own player.
    /// </summary>
    public static float Ability(string classRow, string name, float fallback)
        => (GameTables.TryGetRow(AbilityTable, classRow, out var row) ? Number(row, name, fallback) : fallback)
            + PlayerPerks.AbilityBonus(classRow, name);

    private static string Cell(IReadOnlyDictionary<string, string> row, string key, string fallback) => TableCells.Text(row, key, fallback);

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback) => TableCells.Number(row, key, fallback);
}
