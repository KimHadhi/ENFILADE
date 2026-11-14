using System.Diagnostics;
using UnityEngine;

/// <summary>
/// What the zombies' scripts cost this PC, part by part, while the console's /zai is up (ZombieCostHUD shows it and
/// switches this on). The user asked for the zombies' thinking to be made cheap (2026-11-14: "좀비 AI 계산은 어떡하지
/// 그럼? 이것도 최적화 잘 해서 내 줘"), and the first round of that (ZombieLod, 2026-11-11) was done without measuring,
/// because nothing can be run from where the code is written. So the measuring is built in: the user turns it on in a
/// horde and reads where the time goes, on the host (which runs every zombie's brain) and on a guest (which only shows
/// copies), and the next round starts from numbers. Off, each Begin is one read of a bool; on, a timed part costs two
/// reads of the clock.
/// </summary>
public static class ZombieCost
{
    /// <summary>
    /// The parts timed. Some lie inside others, and the readout takes them out of the outer one: Sight inside Mind;
    /// Target, Path and Spot inside Chase (Target inside Combat too).
    /// </summary>
    public enum Part
    {
        /// <summary>ZombieMind's frame: moods, turning, its think (with Sight inside).</summary>
        Mind,
        /// <summary>ZombieMind.Look: who it sees, the rays to each player's head, chest and hips.</summary>
        Sight,
        /// <summary>EnemyChaseAI's frame (with Target, Path and Spot inside).</summary>
        Chase,
        /// <summary>EnemyTargeting.Player: which player it is after.</summary>
        Target,
        /// <summary>Asking the walk mesh for a new path (NavMeshAgent.SetDestination). The search itself runs in the engine afterwards.</summary>
        Path,
        /// <summary>ZombieCrowd.Spot: its place round the player.</summary>
        Spot,
        /// <summary>ZombieCrowd's step back out of a player it presses into.</summary>
        Yield,
        /// <summary>EnemyCombatAI's frame: reach, windup, the blow.</summary>
        Combat,
        /// <summary>MeleeCombatTarget's frame: posture, stuns, shoves, knockdown time.</summary>
        Stance,
        /// <summary>ZombieBody's frame: the numbers it feeds its animation graph (the posing itself is the engine's).</summary>
        Body,
        /// <summary>EnemyNetwork's frame: what the host writes for its copies.</summary>
        Network
    }

    /// <summary>What is counted beside the times.</summary>
    public enum Tally
    {
        /// <summary>Sight rays cast.</summary>
        Rays,
        /// <summary>New paths asked of the walk mesh, and path turns that kept the path they had.</summary>
        Paths, PathsKept,
        /// <summary>Horde looks made, and horde looks put off because this frame's were used up.</summary>
        Looks, LooksPutOff,
        /// <summary>Turns a host's zombie took to send its position.</summary>
        Sends
    }

    public const int Parts = 11, Tallies = 6;

    private static readonly long[] ticks = new long[Parts];
    private static readonly int[] calls = new int[Parts], tallies = new int[Tallies];

    /// <summary>Measuring: the readout is up.</summary>
    public static bool On { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Switch(false);

    /// <summary>The clock as a timed part begins, or 0 while nothing is measured.</summary>
    public static long Begin() => On ? Stopwatch.GetTimestamp() : 0L;

    /// <summary>A timed part ends: what Begin gave it.</summary>
    public static void End(Part part, long began)
    {
        if (began == 0L) return;
        ticks[(int)part] += Stopwatch.GetTimestamp() - began;
        calls[(int)part]++;
    }

    public static void Count(Tally what, int by = 1)
    {
        if (On) tallies[(int)what] += by;
    }

    /// <summary>The readout comes up or goes: everything gathered so far is dropped.</summary>
    internal static void Switch(bool on)
    {
        On = on;
        System.Array.Clear(ticks, 0, Parts);
        System.Array.Clear(calls, 0, Parts);
        System.Array.Clear(tallies, 0, Tallies);
    }

    /// <summary>
    /// The readout takes what has gathered since it last did (each part's milliseconds and how often it ran, and the
    /// tallies), and the gathering starts over.
    /// </summary>
    internal static void Take(double[] milliseconds, int[] ran, int[] counted)
    {
        double each = 1000.0 / Stopwatch.Frequency;
        for (int i = 0; i < Parts; i++)
        {
            milliseconds[i] = ticks[i] * each;
            ran[i] = calls[i];
            ticks[i] = 0L;
            calls[i] = 0;
        }
        for (int i = 0; i < Tallies; i++)
        {
            counted[i] = tallies[i];
            tallies[i] = 0;
        }
    }
}
