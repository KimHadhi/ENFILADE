using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One rule of Tables/ExecutionRules.csv: which zombie (its posture, and whether it is unaware of anyone or broken) a player
/// may take down or execute, standing where round it (a cone about a side of it, and how near), and with which pair
/// (ExecutionPair, Tables/Executions.csv). A pair is an animation the Blender workspace made; a rule is when it is played.
/// One pair may serve several rules: the standing front pair executes a broken zombie and takes an unaware one down
/// from in front, each from its own distance.
/// </summary>
public sealed class ExecutionRule
{
    public string Id { get; internal set; }
    /// <summary>The pair it plays (a row id of Tables/Executions.csv); "" for none: the knife's own stab where the player stands.</summary>
    public string Pair { get; internal set; } = "";
    /// <summary>The posture it is for (ExecutionRules.Stand ... as ZombieBody.ExecutionPosture names them), or ExecutionRules.Any.</summary>
    public string State { get; internal set; } = ExecutionRules.Any;
    /// <summary>It is for a zombie that knows of nobody (a takedown), for one whose posture is broken (an execution), or for both.</summary>
    public bool Unaware { get; internal set; } = true;
    public bool Broken { get; internal set; } = true;
    /// <summary>
    /// Where the player stands round it: the middle of the cone and how far to either side of that, in degrees from the
    /// way it faces (0 before it, 180 behind it, + to its right).
    /// </summary>
    public float Side { get; internal set; }
    public float Cone { get; internal set; } = 180f;
    /// <summary>How near the player's eyes must be to its body (m) for F, and for the attack button with the melee slot out,
    /// which dashes in over the rest.</summary>
    public float Reach { get; internal set; }
    public float LungeReach { get; internal set; }
    /// <summary>
    /// How long after its pair begins the blade lands (real s): the pair is played as much faster as that takes
    /// (ExecutionRules.Timing). 0 or less: at the pair's own timing.
    /// </summary>
    public float KillIn { get; internal set; }
    /// <summary>
    /// How long after the blade lands the player is let go (real s), the pair's clips easing out over a body that walks
    /// on. Below 0: at the pair's end, as an execution that takes its time does.
    /// </summary>
    public float FreeAfter { get; internal set; } = -1f;
    /// <summary>The execution cinematic films it, when the player has that camera on (ThirdPersonCamera): a kill over in a moment is no shot.</summary>
    public bool Film { get; internal set; } = true;

    public bool AnyState => string.Equals(State, ExecutionRules.Any, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// When one execution's moments fall, in real seconds from where its pair (or the knife's own stab) begins: the blade
/// lands at <see cref="Kill"/>, the player is let go at <see cref="Free"/>, the pair's clips are through at
/// <see cref="Length"/>; they play <see cref="Pace"/> times their own speed. "The player is free" and "the pair is over"
/// are two moments: until 2026-11-18 they were one, so a takedown held the player through its whole clip, a second after
/// the kill.
/// </summary>
public readonly struct ExecutionTiming
{
    public readonly float Pace, Kill, Free, Length;

    public ExecutionTiming(float pace, float kill, float free, float length)
    {
        Pace = pace; Kill = kill; Free = free; Length = length;
    }
}

/// <summary>
/// Who may be taken down or executed from where, as data (Tables/ExecutionRules.csv), in one place. Until 2026-11-18 the
/// answer was spread over three: a takedown was allowed only inside one angle about a zombie's back (ZombieSenses'
/// takedownAngle), from one distance for everything (Player.csv's Execution.reach), and the pair was picked by a 90 degree
/// split in code (ZombieBody.ExecutionState), so a takedown from in front could not exist and a new pair needed code.
/// The user, 2026-11-18: "비경계(방심) 상태의 적 정면 암살 활성화(후방보다 판정 범위를 좁게 설정해 초근접 시 가능하도록
/// 설계)". Now each rule carries its own cone and reach, the front one nearer than the back, and a mod adds a rule (and a
/// pair) as rows.
/// <see cref="Pick(IReadOnlyList{ExecutionRule}, string, bool, bool, float, float, bool)"/> is a pure function of its
/// inputs, so every rule can be checked without playing (ENFILADE → 근접 → 암살·처형 규칙 보고서 writes
/// Logs/execution-rules.txt). The player's own PC asks it (PlayerExecution through MeleeCombatTarget.ExecutionRuleFor) and
/// nobody judges again (the user's rule of 2026-11-14: the acting player's PC decides).
/// </summary>
public static class ExecutionRules
{
    public const string TableName = "ExecutionRules";
    /// <summary>The postures a zombie's body names (ZombieBody.ExecutionPosture) and the word for any of them.</summary>
    public const string Stand = "stand", Sit = "sit", Feed = "feed", Crouch = "crouch", Prone = "prone", Supine = "supine", Any = "any";

    private static readonly List<ExecutionRule> rules = new List<ExecutionRule>();
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        rules.Clear();
        loaded = false;
        GameTables.Reloaded -= Forget;
    }

    private static void Forget() => loaded = false;

    /// <summary>Every rule, in the table's order (read once, and again whenever the tables reload).</summary>
    public static IReadOnlyList<ExecutionRule> All
    {
        get
        {
            Load();
            return rules;
        }
    }

    /// <summary>The table's rule for this zombie and this place (Pick over All).</summary>
    public static ExecutionRule Pick(string state, bool unaware, bool broken, float bearing, float distance, bool lunge)
        => Pick(All, state, unaware, broken, bearing, distance, lunge);

    /// <summary>
    /// The rule that lets a player do it, or null when none does. <paramref name="state"/>: the zombie's posture.
    /// <paramref name="unaware"/>, <paramref name="broken"/>: what it is (neither: nothing can be done to it).
    /// <paramref name="bearing"/>: where the player stands round it (Bearing). <paramref name="distance"/>: from the player's
    /// eyes to its body (m); below 0 asks about the place alone, whatever the distance. <paramref name="lunge"/>: the attack
    /// button with the melee slot out, which reaches each rule's lungeReach. Of the rules that fit, one that names the
    /// posture comes before one for any posture; then the one whose cone's middle the player stands nearest; then the one
    /// written first.
    /// </summary>
    public static ExecutionRule Pick(IReadOnlyList<ExecutionRule> among, string state, bool unaware, bool broken, float bearing,
        float distance, bool lunge)
    {
        if (among == null || (!unaware && !broken)) return null;
        ExecutionRule best = null;
        bool bestNamed = false;
        float bestOff = float.PositiveInfinity;
        for (int i = 0; i < among.Count; i++)
        {
            var rule = among[i];
            if (rule == null || !((unaware && rule.Unaware) || (broken && rule.Broken))) continue;
            bool named = string.Equals(rule.State, state, StringComparison.OrdinalIgnoreCase);
            if (!named && !rule.AnyState) continue;
            float off = Math.Abs(Wrap(bearing - rule.Side));
            if (off > rule.Cone) continue;
            if (distance >= 0f && distance > (lunge ? Math.Max(rule.Reach, rule.LungeReach) : rule.Reach)) continue;
            bool better = best == null || (named && !bestNamed) || (named == bestNamed && off < bestOff);
            if (!better) continue;
            best = rule;
            bestNamed = named;
            bestOff = off;
        }
        return best;
    }

    /// <summary>
    /// Where <paramref name="toPlayer"/> (from the zombie to the player) lies round the zombie, in degrees across the ground
    /// from the way it faces: 0 straight before it, 180 straight behind, + to its right. A player on the very spot counts
    /// as behind it.
    /// </summary>
    public static float Bearing(Vector3 facing, Vector3 toPlayer)
    {
        float fx = facing.x, fz = facing.z, tx = toPlayer.x, tz = toPlayer.z;
        if (tx * tx + tz * tz < 1e-4f || fx * fx + fz * fz < 1e-8f) return 180f;
        return (float)(Math.Atan2(fz * tx - fx * tz, fx * tx + fz * tz) * (180.0 / Math.PI));
    }

    /// <summary>
    /// When an execution by this rule strikes, lets the player go and ends (a pure function, like Pick: the report lists
    /// it for every rule). <paramref name="kill"/> and <paramref name="length"/>: its pair's own kill moment and length in
    /// the clips' seconds, or the knife's own stab's when no pair plays. <paramref name="card"/>: a factor on top that
    /// makes all of it faster (a Ghost's 빠른 칼, GhostCards.TakedownPace; 1 for none). The rule's killIn sets the pace:
    /// the clips play as much faster as lands the blade then, so a pair timed again in Blender keeps the moment the
    /// player feels (the user, 2026-11-18: "전체 암살 모션을 《카운터 스트라이크》 칼 백스탭처럼 빠르고 간결하게 단축").
    /// Its freeAfter lets the player go that long after the blade, never later than the pair's end. A null rule (a copy
    /// of someone else's execution before their word comes) plays the pair as it is, to its end.
    /// </summary>
    public static ExecutionTiming Timing(ExecutionRule rule, float kill, float length, float card = 1f)
    {
        length = Math.Max(.05f, length);
        kill = Math.Min(Math.Max(0f, kill), length);
        card = Math.Max(.1f, card);
        float pace = rule != null && rule.KillIn > 0f && kill > 0f ? kill / rule.KillIn : 1f;
        pace = Math.Max(.1f, pace * card);
        float struck = kill / pace, over = length / pace;
        float free = rule != null && rule.FreeAfter >= 0f ? Math.Min(over, struck + rule.FreeAfter / card) : over;
        return new ExecutionTiming(pace, struck, Math.Max(struck, free), over);
    }

    /// <summary>An angle brought into -180 (not included) to 180 degrees.</summary>
    public static float Wrap(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        else if (degrees <= -180f) degrees += 360f;
        return degrees;
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
        rules.Clear();
        foreach (string id in GameTables.RowIds(TableName))
            if (GameTables.TryGetRow(TableName, id, out var row)) rules.Add(Read(id, row));
    }

    /// <summary>One row as a rule (the game's own reading; the report reads rows the same way).</summary>
    public static ExecutionRule Read(string id, IReadOnlyDictionary<string, string> row)
    {
        string victim = row.Text("victim", Any);
        float reach = Mathf.Max(0f, row.Number("reach", 2.1f));
        return new ExecutionRule
        {
            Id = id,
            Pair = row.Text("pair"),
            State = row.Text("state", Any),
            // "unaware" or "broken" alone; anything else (any, or a blank cell) is for both.
            Unaware = !string.Equals(victim, "broken", StringComparison.OrdinalIgnoreCase),
            Broken = !string.Equals(victim, "unaware", StringComparison.OrdinalIgnoreCase),
            Side = Wrap(row.Number("side", 0f)),
            Cone = Mathf.Clamp(row.Number("cone", 180f), 0f, 180f),
            Reach = reach,
            LungeReach = Mathf.Max(reach, row.Number("lungeReach", reach)),
            KillIn = Mathf.Max(0f, row.Number("killIn", 0f)),
            FreeAfter = row.Number("freeAfter", -1f),
            Film = row.Flag("film", true),
        };
    }
}
