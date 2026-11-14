using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a room of the ENFILADE tower holds: many weak ones, or one strong one (the user: "약한 놈 여럿 또는 강한 놈 하나").</summary>
public enum TowerTrial : byte { Swarm, Champion }

/// <summary>
/// A floor's rule for the day (the map gimmick card "오늘의 층 규칙", only from parts the game had: the user's note "발상은
/// 좋은데 만들기 귀찮겠다"): its rooms' lights off (Blackout), its swarms lying face down (Sleeping), alarm wires across its rooms
/// (Wire). TowerRun.Rules plays them.
/// </summary>
public enum TowerRule : byte { None, Blackout, Sleeping, Wire }

/// <summary>
/// The day's program of the ENFILADE tower: what each room of each lane holds. The user likened the tower to a gacha
/// game's daily quest or Dungeon Fighter's tower of trials, so the program changes with the date: the seed is the day
/// (TowerRun.TodaySeed), the host's, shared with the room, and every PC works out the same plan from it.
/// Floors come from Tables/TowerFloors (rows F1-F10): whether its rooms hold a swarm or a champion (mode: alt takes turns,
/// so two lanes side by side never hold the same kind of fight and each lane's floors change from one to the other;
/// swarm or champion makes every lane the same), how big a swarm is and what is in it, which champions the day picks
/// from, how much tougher than usual they are, and what getting through pays. A room shared by more players holds more
/// (TowerRun: crowdPerPlayer, championPerPlayer).
/// </summary>
public sealed class TowerPlan
{
    public const string TableName = "TowerFloors";

    /// <summary>One row of Tables/TowerFloors.</summary>
    [Serializable]
    public sealed class Floor
    {
        [Tooltip("alt: the lanes take turns, swarm beside champion; swarm or champion: every lane alike.")]
        public string mode = "alt";
        [Tooltip("How many weak ones a swarm room holds for one player.")]
        public int swarm = 6;
        [Tooltip("The share of a swarm that are runners (the rest shamblers).")]
        public float runners;
        [Tooltip("Exploders in a swarm, on top of it.")]
        public int exploders;
        [Tooltip("Chargers in a swarm, on top of it.")]
        public int chargers;
        [Tooltip("The champions a champion room picks from (ZombieKind names, ';' between).")]
        public string champions = "Ninja";
        [Tooltip("A swarm's health against usual.")]
        public float toughness = 1f;
        [Tooltip("A champion's health against usual.")]
        public float championToughness = 1f;
        [Tooltip("Money for each player of the lane once the room is got through.")]
        public int reward = 100;
    }

    private readonly Floor[] floors = new Floor[TowerLayout.Floors];
    // Each floor's champions, parsed once.
    private readonly ZombieKind[][] picks = new ZombieKind[TowerLayout.Floors][];

    public int Seed { get; }

    /// <summary>The plan for <paramref name="seed"/>, its rows kept bound to <paramref name="owner"/> for live table edits.</summary>
    public TowerPlan(int seed, UnityEngine.Object owner)
    {
        Seed = seed;
        for (int i = 0; i < floors.Length; i++)
        {
            floors[i] = new Floor();
            if (owner != null) GameTables.BindNested(TableName, "F" + (i + 1), owner, floors[i]);
        }
    }

    /// <summary>Floor 1-10's row.</summary>
    public Floor Of(int floor) => floors[Mathf.Clamp(floor, 1, TowerLayout.Floors) - 1];

    /// <summary>
    /// The day's stealth lane (the map gimmick card "잠입 레인", after PAYDAY 2's Shadow Raid): on floors 1-9 its rooms hold
    /// their swarm calm, spread through the room, to be got through unseen (TowerRun.Rules). The last floor is a fight in
    /// every lane.
    /// </summary>
    public int StealthLane => (int)(Mix(Seed, 0x5EA1) % TowerLayout.Lanes);

    /// <summary>Floor 1-10's rule today: one floor of each stretch of three has one, the day picking which and what; the
    /// last floor none.</summary>
    public TowerRule RuleOf(int floor)
    {
        if (floor < 1 || floor >= TowerLayout.Floors) return TowerRule.None;
        int stretch = (floor - 1) / 3;
        uint roll = Mix(Seed, 0x7A11 + stretch);
        if (floor != stretch * 3 + 1 + (int)(roll % 3)) return TowerRule.None;
        return (TowerRule)(1 + (int)(roll / 3 % 3));
    }

    /// <summary>Whether lane <paramref name="lane"/>'s room on floor 1-10 holds its swarm laid out calm (the stealth lane,
    /// a sleeping floor's swarm rooms).</summary>
    public bool Quiet(int floor, int lane)
        => floor >= 1 && floor < TowerLayout.Floors && (lane == StealthLane || RuleOf(floor) == TowerRule.Sleeping && TrialOf(floor, lane) == TowerTrial.Swarm);

    /// <summary>A rule's name as the signs and alerts say it.</summary>
    public static string RuleName(TowerRule rule) => rule switch
    {
        TowerRule.Blackout => Texts.Get("tower.rule.blackout"),
        TowerRule.Sleeping => Texts.Get("tower.rule.sleeping"),
        TowerRule.Wire => Texts.Get("tower.rule.wire"),
        _ => ""
    };

    // A spread of the day and a salt: the same on every PC.
    private static uint Mix(int seed, int salt)
    {
        uint x = (uint)seed * 2654435761u ^ (uint)salt * 40503u;
        x ^= x >> 13;
        x *= 0x5bd1e995u;
        x ^= x >> 15;
        return x;
    }

    /// <summary>What lane <paramref name="lane"/>'s room on floor 1-10 holds.</summary>
    public TowerTrial TrialOf(int floor, int lane)
    {
        string mode = Of(floor).mode ?? "";
        if (mode.Equals("champion", StringComparison.OrdinalIgnoreCase)) return TowerTrial.Champion;
        if (mode.Equals("swarm", StringComparison.OrdinalIgnoreCase)) return TowerTrial.Swarm;
        // Taking turns: the day decides which lanes start with a swarm.
        return (lane + floor + (Seed & 1)) % 2 == 0 ? TowerTrial.Swarm : TowerTrial.Champion;
    }

    /// <summary>The champion of lane <paramref name="lane"/>'s room on floor 1-10 (its first pick for a swarm room). The
    /// day turns the floor's list, and two champion rooms of one floor take different ones while the list has enough.</summary>
    public ZombieKind ChampionOf(int floor, int lane)
    {
        var list = Picks(floor);
        int rank = 0;
        for (int other = 0; other < lane; other++) if (TrialOf(floor, other) == TowerTrial.Champion) rank++;
        int start = (Hash(Seed, floor, 0) & int.MaxValue) % list.Length;
        return list[(start + rank) % list.Length];
    }

    /// <summary>
    /// What a swarm room lets out, in the order it comes, for <paramref name="players"/> players and the table's
    /// <paramref name="crowdPerPlayer"/>: shamblers and runners by the floor's share, and its exploders and chargers.
    /// </summary>
    public List<ZombieKind> SwarmOf(int floor, int lane, int players, float crowdPerPlayer)
    {
        var row = Of(floor);
        int total = Mathf.Max(1, Mathf.RoundToInt(row.swarm * (1f + Mathf.Max(0f, crowdPerPlayer) * Mathf.Max(0, players - 1))));
        int runners = Mathf.Clamp(Mathf.RoundToInt(total * Mathf.Clamp01(row.runners)), 0, total);
        var kinds = new List<ZombieKind>(total + row.exploders + row.chargers);
        for (int i = 0; i < total; i++) kinds.Add(i < runners ? ZombieKind.Runner : ZombieKind.Shambler);
        for (int i = 0; i < row.exploders; i++) kinds.Add(ZombieKind.Exploder);
        for (int i = 0; i < row.chargers; i++) kinds.Add(ZombieKind.Charger);
        // The day's order, the same on every PC: the first out are shamblers, so a room never opens on a runner's leap.
        var random = new System.Random(Hash(Seed, floor, lane + 1));
        for (int i = kinds.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }
        int first = kinds.IndexOf(ZombieKind.Shambler);
        if (first > 0) (kinds[0], kinds[first]) = (kinds[first], kinds[0]);
        return kinds;
    }

    /// <summary>The words for a room on the door signs and the HUD: 떼 or the champion's name.</summary>
    public string Label(int floor, int lane)
    {
        string trial = Quiet(floor, lane) && lane == StealthLane ? Texts.Get("tower.trial.stealth")
            : TrialOf(floor, lane) == TowerTrial.Swarm ? Texts.Get("tower.trial.swarm") : Texts.Get(ZombieType.NameId(ChampionOf(floor, lane)));
        var rule = RuleOf(floor);
        return rule == TowerRule.None ? trial : trial + " (" + RuleName(rule) + ")";
    }

    private ZombieKind[] Picks(int floor)
    {
        int index = Mathf.Clamp(floor, 1, TowerLayout.Floors) - 1;
        if (picks[index] != null) return picks[index];
        var found = new List<ZombieKind>();
        foreach (string name in (Of(floor).champions ?? "").Split(';'))
            if (Enum.TryParse(name.Trim(), true, out ZombieKind kind) && Enum.IsDefined(typeof(ZombieKind), kind) && !found.Contains(kind))
                found.Add(kind);
        if (found.Count == 0) found.Add(ZombieKind.Charger);
        return picks[index] = found.ToArray();
    }

    // A small mix of the day and where in the tower, the same on every PC and every run of the game.
    private static int Hash(int seed, int floor, int lane)
    {
        unchecked
        {
            int h = seed * 73856093 ^ floor * 19349663 ^ lane * 83492791;
            h ^= h >> 13;
            h *= 1274126177;
            return h ^ (h >> 16);
        }
    }
}
