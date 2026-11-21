using System;
using System.Collections.Generic;
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
        /// <summary>
        /// The class this row is a variant of (Classes.csv base), "" for a class of its own. A variant plays its base's
        /// kit (Is answers yes for the base), shares its growth and insignia, and takes the base's guns, role and numbers
        /// wherever its own cells are empty, the way GMod Horde's subclasses stand under their class (the user,
        /// 2026-11-13: "같은 클래스여도 예를 들어 시순트는 변형으로 처형 집행인 같은 게 … 있을 예정").
        /// </summary>
        public string Base = "";
        public string[] Weapons = Array.Empty<string>();
        public float GunBonus;
        public bool Ready;

        /// <summary>The class it plays as: its base for a variant, else itself.</summary>
        public string Family => Base.Length > 0 ? Base : Id;
        public bool IsVariant => Base.Length > 0;
    }

    private static List<Info> catalog;

    /// <summary>
    /// This PC's player picked a class (ClassGrowth.Choose: the hub's class wall, the title screen's 클래스 tab), with the
    /// class before and the class now. A pick is a fresh start (the user, 2026-11-12: "클래스 선택할 때 초기화 하는 방식"):
    /// ClassKit hands the class's kit as a chapter would (every stack at 0, every charge and wait ready, its guns full,
    /// the bag and syringe as they start, the last pick's barricades down), and whatever else a class keeps for the run
    /// starts again on it too (the hub's ultimate gauge). The cards are not part of that: a card stays in its class's
    /// deck or everyone's (PlayerPerks, CardDecks), so a pick takes the old class's out of play without throwing any
    /// away (2026-11-18; until then the hub threw its practice cards away here). Raised only where a pick may happen
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

    /// <summary>Every class and variant, in the table's order.</summary>
    public static IReadOnlyList<Info> Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            catalog = new List<Info>();
            var bonusSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in GameTables.RowIds(TableName))
            {
                if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
                if (row.Text("gunBonus", "").Trim().Length > 0) bonusSet.Add(id);
                catalog.Add(new Info
                {
                    // The title and role cells name Texts rows (class.<id>.title, .role), or hold plain words.
                    Id = id, Title = Texts.Cell(row.Text("title", id)), Role = Texts.Cell(row.Text("role", "")), Primary = row.Text("primary", ""),
                    Secondary = row.Text("secondary", ""), Base = row.Text("base", "").Trim(),
                    Weapons = row.Text("classWeapons", "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    GunBonus = row.Number("gunBonus", 0f), Ready = row.Number("ready", 0f) > .5f
                });
            }
            // A variant takes its base's cells where its own are empty. A base that is no class of its own (missing, or
            // itself a variant) leaves the row a class of its own, said once.
            foreach (var info in catalog)
            {
                if (!info.IsVariant) continue;
                var root = catalog.Find(other => string.Equals(other.Id, info.Base, StringComparison.OrdinalIgnoreCase));
                if (root == null || root.IsVariant || root == info)
                {
                    Debug.LogWarning($"[Classes] {TableName}.csv {info.Id}: base '{info.Base}'는 따로 선 클래스가 아니어서 변형이 아닌 클래스로 둡니다.");
                    info.Base = "";
                    continue;
                }
                info.Base = root.Id;
                if (info.Role.Length == 0) info.Role = root.Role;
                if (info.Primary.Length == 0) info.Primary = root.Primary;
                if (info.Secondary.Length == 0) info.Secondary = root.Secondary;
                if (info.Weapons.Length == 0) info.Weapons = root.Weapons;
                if (!bonusSet.Contains(info.Id)) info.GunBonus = root.GunBonus;
            }
            return catalog;
        }
    }

    /// <summary>The class an id plays as: a variant's base (Classes.csv base), else the id itself.</summary>
    public static string KitOf(string id)
    {
        var info = Find(id);
        return info != null ? info.Family : id ?? "";
    }

    /// <summary>A class and its variants, in the table's order, the class first; empty for an id the table hasn't.</summary>
    public static List<Info> FamilyOf(string id)
    {
        var family = new List<Info>();
        string root = KitOf(id);
        foreach (var info in Catalog)
            if (string.Equals(info.Family, root, StringComparison.OrdinalIgnoreCase))
            {
                if (info.IsVariant) family.Add(info);
                else family.Insert(0, info);
            }
        return family;
    }

    /// <summary>The class this PC's player plays (their pick, kept with its rank in ClassGrowth).</summary>
    public static string CurrentId => ClassGrowth.CurrentId;

    public static Info Current => Find(CurrentId);

    public static Info Find(string id)
    {
        foreach (var info in Catalog) if (string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) return info;
        return null;
    }

    /// <summary>
    /// Whether this PC's player plays this class: it, or a variant of it, which plays its base's kit unless code that
    /// knows the variant asks Plays.
    /// </summary>
    public static bool Is(string id)
    {
        string current = CurrentId;
        return string.Equals(current, id, StringComparison.OrdinalIgnoreCase) || string.Equals(KitOf(current), id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether this PC's player plays exactly this row (a variant's base is not it here).</summary>
    public static bool Plays(string id) => string.Equals(CurrentId, id, StringComparison.OrdinalIgnoreCase);

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
    public static float GunFactor(Component target = null)
    {
        var kit = ClassKit.Local;
        if (kit == null) return 1f;
        var info = Current;
        float factor = OwnsWeapon(info, kit.HeldGun) ? 1f + info.GunBonus / 100f : 1f;
        // A Knight's cover turned a blow on this player, the Knight holding 원호 반격: their guns hit harder a moment, any
        // class, by the share the Knight's PC sent with the cover (KnightCover.GuardedGunFactor).
        factor *= KnightCover.GuardedGunFactor;
        if (target != null && Is(Schiesshund) && PingMarks.IsBountyOf(target, kit.Movement))
            factor *= 1f + Ability(HoundRow, "markDamage", 25f) / 100f;
        // A Schießhund's bounty dye (DyeMarks): the zombies its own dye splashed take more from its guns, the glowing dye
        // showing the hound where to put its rounds (GrenadeRules dyeDamage). On its mark as well, it adds to the mark's.
        if (target != null && Is(Schiesshund)) factor *= DyeMarks.GunFactor(target, kit.Movement);
        // A Schießhund's duel: the foe it called out takes more again from it (HoundDuel).
        if (target != null && Is(Schiesshund) && HoundDuel.Of(kit.Movement) is HoundDuel duel && duel.Foe != null
            && duel.Foe.gameObject == target.gameObject)
            factor *= 1f + Ability(HoundRow, "duelDamage", 25f) / 100f;
        // An Assault's 데인저 클로즈 used to add here on near enemies; since 2026-11-14 it is a barrage called on a mark
        // (ClassCards.DangerClose, the user: "진짜 폭격 스킬처럼").
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

    /// <summary>
    /// A factor on how long a gun takes to come out for <paramref name="who"/>: none at all in a Point's Gun Kata (ClassKit),
    /// and a Schießhund's Desperado quicker with its 빠른 뽑기 card (ClassKit.DesperadoDraw).
    /// </summary>
    public static float DrawFactor(PlayerMovement who)
    {
        // Someone else's copy draws at their PC's pace, which knows their kata and their gun: it comes with their stance.
        if (who != null && !who.IsLocal && who.TryGetComponent(out PlayerNetworkCombat shown)) return shown.ShownDrawFactor;
        return InKata(who) ? 0f : ClassKit.DesperadoDraw(who);
    }

    /// <summary>
    /// A factor on the spread moving adds to <paramref name="who"/>'s shots: none in a Point's Gun Kata, shooting on the run;
    /// less in a slide, a jump or a fall with a Point's 곡예 사격 card (PointCards).
    /// </summary>
    public static float MovingSpreadFactor(PlayerMovement who) => InKata(who) ? 0f : PointCards.AcrobatSpread(who);

    /// <summary>
    /// A factor on the spread a jump or a fall adds to <paramref name="who"/>'s shots: less with a Point's 곡예 사격 card,
    /// whose point is shooting in the air (PointCards). The Gun Kata alone leaves it as it is.
    /// </summary>
    public static float AirSpreadFactor(PlayerMovement who) => PointCards.AcrobatSpread(who);

    /// <summary>
    /// A factor on all of <paramref name="who"/>'s spread: less in a Point's Gun Kata with its 탄막 춤 card (none at
    /// its top level: PointCards), else 1.
    /// </summary>
    public static float KataSpreadFactor(PlayerMovement who) => InKata(who) ? PointCards.BalletSpread() : 1f;

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
    /// again while their taunt holds; an Assault with the 선두 card less while it charges (ClassKit.LeadShare).
    /// PlayerDamageReceiver multiplies it in, on the player's own PC.
    /// </summary>
    public static float TakenFactor(PlayerMovement player)
    {
        float lead = ClassKit.LeadShare(player);
        if (!Knights(player)) return lead;
        float share = Mathf.Clamp01(Ability(KnightRow, "taken", .75f));
        if (ClassKit.Local.TauntShare > 0f) share *= Mathf.Clamp01(Ability(KnightRow, "tauntTaken", .6f));
        return share * lead;
    }

    /// <summary>
    /// A Knight's fists hit this much harder, damage and posture (PlayerMeleeCombat's punches), more again for a moment
    /// after their cover turned a blow with the 원호 반격 card (ClassCards.CoverStrikeFactor).
    /// </summary>
    public static float FistFactor(PlayerMovement player) => Knights(player) ? (1f + Ability(KnightRow, "fists", 50f) / 100f) * CoverStrike : 1f;

    /// <summary>
    /// A Knight's counter, after a dodge or a parry, hits this much harder (PlayerPerfectDefense: the memo's 흘리기), more
    /// again for a moment after their cover turned a blow with the 원호 반격 card.
    /// </summary>
    public static float CounterFactor(PlayerMovement player) => Knights(player) ? (1f + Ability(KnightRow, "counter", 50f) / 100f) * CoverStrike : 1f;

    /// <summary>
    /// A Knight's parry windows are this much longer (PlayerMeleeCombat), longer again for a moment after a counter of
    /// theirs landed with the 비껴 흘리기 card (ClassCards.FlowFactor).
    /// </summary>
    public static float ParryWindowFactor(PlayerMovement player)
        => Knights(player) ? (1f + Ability(KnightRow, "parryWindow", 50f) / 100f) * (ClassCards.Local != null ? ClassCards.Local.FlowFactor : 1f) : 1f;

    // This PC's Knight's 원호 반격 moment, 1 when none runs.
    private static float CoverStrike => ClassCards.Local != null ? ClassCards.Local.CoverStrikeFactor : 1f;

    /// <summary>A Knight's parry opens the counter as a perfect dodge does (the memo's 퍼펙트 패리; PlayerPerfectDefense).</summary>
    public static bool ParryCounters(PlayerMovement player) => Knights(player) && Ability(KnightRow, "parryCounter", 1f) > .5f;

    /// <summary>
    /// Whether this PC's player, a Knight, parries <paramref name="hit"/> though it can't be guarded (KnightTurns): asked by
    /// PlayerMeleeCombat while their parry window is open, on their own PC where the parry is judged.
    /// </summary>
    public static bool ParriesAll(PlayerMovement player, in DamageHit hit) => Knights(player) && KnightTurns(player, hit);

    /// <summary>
    /// A blow a Knight's parry turns on <paramref name="victim"/>, whatever it is (the user, 2026-11-14: "나이트면 기본적으로
    /// 패링으로 러너나 그런 패링 못 하는 공격들도 다 패링할 수 있게"): any blow anyone could guard, and with Knight.parryAll on,
    /// every other blow of a living zombie too: a runner's leap, a shambler's grab, a reaper's scythe, a ninja's stab from
    /// behind, a charger's ram. Not the bites of a zombie that holds them already: those are no blow to turn, and V throws
    /// it off (ClassCards). Not an exploder's burst either: it comes from a body already dead. The Knight's own parry
    /// (ParriesAll) and a Knight's cover over a friend (KnightCover.Covers) ask it, on the PC of the one it lands on, which
    /// judges every zombie blow on its player.
    /// </summary>
    public static bool KnightTurns(PlayerMovement victim, in DamageHit hit)
    {
        if (hit.Kind != DamageKind.Attack) return false;
        if (hit.CanBeGuarded) return true;
        if (victim == null || victim.IsHeld || Ability(KnightRow, "parryAll", 1f) <= .5f) return false;
        return hit.Source != null && hit.Source.GetComponentInParent<EnemyHealth>() is EnemyHealth zombie && zombie.IsAlive;
    }

    /// <summary>
    /// A pack zombie, whose posture a Knight's parry breaks at once (Knight.parryBreaks; the user, 2026-11-14: "기본 패링으로
    /// 좀비 처형 가능하게 해"): a shambler or a runner of no rank, or a plain enemy without a kind. A brute, an exploder, a
    /// charger, the memo's elites (ninja, candyman, breadbag, reaper) and anything ranked elite or named take a parry's
    /// usual posture damage instead.
    /// </summary>
    public static bool BreaksOnParry(MeleeCombatTarget target)
    {
        if (target == null || target.Steadfast || Ability(KnightRow, "parryBreaks", 1f) <= .5f) return false;
        if (target.TryGetComponent(out EnemyHealth enemy) && enemy.IsStandout) return false;
        return !target.TryGetComponent(out ZombieType type) || type.Kind == ZombieKind.Shambler || type.Kind == ZombieKind.Runner;
    }

    /// <summary>
    /// The posture <paramref name="player"/>'s parry takes from <paramref name="attacker"/>, from the parry's own
    /// (<paramref name="basic"/>). A Knight's wears an elite or a brute down harder (parryPosture), more again with the
    /// 무너뜨리기 card, and all of it twice while the shadow double is out (its parry falls with the Knight's); a pack
    /// zombie's it breaks outright (BreaksOnParry), so the execution is there at once. PlayerMeleeCombat applies it before
    /// the parry is told to anyone (ParryFeedback, PlayerPerfectDefense), so a break shows and plays as one.
    /// </summary>
    public static float ParryPosture(PlayerMovement player, MeleeCombatTarget attacker, float basic)
    {
        if (!Knights(player)) return basic;
        float posture = basic * (1f + Ability(KnightRow, "parryPosture", 100f) / 100f);
        if (attacker == null) return posture;
        if (BreaksOnParry(attacker)) return Mathf.Max(posture, attacker.MaximumPosture + 1f);
        posture += Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.KnightDeflect));
        var cards = ClassCards.Local;
        return cards != null && cards.ShadowOut ? posture * 2f : posture;
    }

    // This PC's own player, playing a Knight.
    private static bool Knights(PlayerMovement player)
    {
        var kit = ClassKit.Local;
        return player != null && kit != null && kit.Movement == player && Is(Knight);
    }

    /// <summary>
    /// An ability's number (Tables/ClassAbilities, "Class.name"), with what this PC's player's perk cards add to it
    /// (a card naming it in Tables/Perks' ability column: PlayerPerks.AbilityBonus). Only a player of that class is ever
    /// dealt its cards, so a class's numbers grow for its own player. Played as a variant, the variant's own row wins
    /// where it has the number ("Executioner.markSeconds" over "Schiesshund.markSeconds": RowOf the variant's id).
    /// </summary>
    public static float Ability(string classRow, string name, float fallback)
    {
        var info = Current;
        float value = info != null && info.IsVariant && string.Equals(classRow, info.Base, StringComparison.OrdinalIgnoreCase)
            ? GameTables.Number(AbilityTable, RowOf(info.Id), name, GameTables.Number(AbilityTable, classRow, name, fallback))
            : GameTables.Number(AbilityTable, classRow, name, fallback);
        return value + PlayerPerks.AbilityBonus(classRow, name);
    }

    /// <summary>A class's rows in Tables/ClassAbilities go by its id with a capital first letter (schiesshund → Schiesshund).</summary>
    public static string RowOf(string id) => string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1);
}
