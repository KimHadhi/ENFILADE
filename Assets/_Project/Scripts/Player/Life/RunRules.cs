using UnityEngine;

/// <summary>The words a run puts in place of the defaults (Texts.csv ids): the dead waiting, a joiner waiting, the wipe's line.</summary>
public enum RunWords { Respawn, Join, Wipe }

/// <summary>
/// The rules of the run a map is playing (a mode: waves, a tower), as the player's life, its HUD and the class cards ask
/// them. The game never names a mode: a mode is an object of its own that registers its rules here while it runs
/// (RunRules.Begin) and takes them back when it ends (RunRules.End), so a mode is added or deleted without editing the
/// rest of the game (user, 2026-11-22: "객체 지향 프로그래밍으로 알아 하게 짰으면 됐잖아").
/// </summary>
public interface IRunRules
{
    /// <summary>Someone joining now comes in watching (PlayerLife.Arrive), not alive at the start.</summary>
    bool UnderWay { get; }
    /// <summary>The dead wait with no timer until the run calls them back (CallsDeadBack).</summary>
    bool KeepsDead { get; }
    /// <summary>The dead come back now.</summary>
    bool CallsDeadBack { get; }
    /// <summary>A stretch of the run (a wave, a hall): a new number brings back what is had once a stretch (Resurrection, Phoenix). -1 for none.</summary>
    int Section { get; }
    /// <summary>Nobody can reach this player to get them up: they die instead of going down.</summary>
    bool NoOneToHelp(PlayerMovement player);
    /// <summary>Where the dead come back, when the run has such a place.</summary>
    bool TryCheckpoint(out Vector3 at);
    /// <summary>The run's own words (a Texts.csv id), or null for the defaults.</summary>
    string Words(RunWords which);
}

/// <summary>The run's rules now (IRunRules), or the defaults where no run has registered: the dead come back after
/// PlayerLife's wait at the map's spawn point, everyone can go down, there are no stretches.</summary>
public static class RunRules
{
    private static IRunRules current;

    public static void Begin(IRunRules rules) => current = rules;

    public static void End(IRunRules rules)
    {
        if (current == rules) current = null;
    }

    public static bool UnderWay => current != null && current.UnderWay;
    public static bool KeepsDead => current != null && current.KeepsDead;
    public static bool CallsDeadBack => current != null && current.CallsDeadBack;
    public static int Section => current != null ? current.Section : -1;
    public static bool NoOneToHelp(PlayerMovement player) => current != null && current.NoOneToHelp(player);

    public static bool TryCheckpoint(out Vector3 at)
    {
        at = default;
        return current != null && current.TryCheckpoint(out at);
    }

    /// <summary>The run's words for that, else <paramref name="fallback"/> (a Texts.csv id).</summary>
    public static string Words(RunWords which, string fallback) => current?.Words(which) ?? fallback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;
}
