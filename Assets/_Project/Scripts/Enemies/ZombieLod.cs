using UnityEngine;

/// <summary>
/// What a crowd costs when most of it is far from every player: the 2026-11-11 memo's item 40 ("최적화, 컬링"), the
/// zombies' share (the scene and its drawing are the maps' own: CameraCulling). Done without measuring, so each part
/// has its own number in Tables/ZombieLod and `enabled 0` puts everything back as it was.
/// - Thinking (ZombieMind): one far from every player (beyond `far`) thinks every `thinkFar` seconds instead of every
///   0.1; what it sees still adds up at the same rate a second, and sounds reach it the moment they are made.
/// - Chasing (EnemyChaseAI): it looks for a new path every `chaseFar` seconds instead of every 0.15.
/// - A horde's look round (ZombieMind): an awake horde zombie only looks for players to tell the campaign someone is
///   still seen; once one has seen anyone in the last `hordeLookGap` seconds, the rest needn't cast their rays. While
///   nobody has (the players are behind walls), the whole horde together looks `hordeLooks` times a second and no
///   more: each takes its turn the more seldom the bigger the horde is, so the rays cast don't grow with the crowd.
/// - Paths (EnemyChaseAI): a zombie keeps the path it has while it still leads to where its quarry is. It asked the
///   walk mesh for a new one every 0.15 s whether or not anyone had moved: a horde round players holding a room asked
///   for hundreds of paths a second that came out the same, and the walk mesh works a set number of steps a frame, so
///   paths that mattered waited behind them. Now it asks again once its goal has moved `pathShift` of the way there (at
///   least `pathShiftLeast` m: one far off needs no new path for a step of theirs, one beside them does), and after
///   `pathKeep` seconds whatever happened; a way that stops short of them (they stand where it can't walk) is tried
///   again every `pathRetry` seconds, not every turn, since that search is the dearest of all: it runs through
///   everything it can reach before it gives up. Each zombie's turn also falls on its own frame from the start.
/// - Its body: nothing here. 0.8.93 posed a far zombie on screen every third frame on a manual graph clock (animFar,
///   animEvery), and that froze every zombie that went on it for good once it came near or moved at all (Unity does not
///   schedule a graph switched back off Manual, ZombieBody.GuardGraph says more): the frozen zombies the user reported
///   three times. Taken out (0.8.317), the body is posed every frame it is seen. Off screen the Animator skips it as ever
///   (the pack's CullUpdateTransforms). Never put a playing graph on a manual clock to save time.
/// How many corpses lie about is ZombieCorpse.csv's (mostCorpses, mostDolls).
/// </summary>
public static class ZombieLod
{
    public const string TableName = "ZombieLod", TableRow = "Default";

    private static bool read, listening, on = true;
    private static float far = 40f, thinkFar = .3f, chaseFar = .6f, hordeLookGap = .25f;
    private static float hordeLooks = 60f, pathShift = .05f, pathShiftLeast = .25f, pathRetry = .75f, pathKeep = 2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => read = listening = false;

    private static void Forget() => read = false;

    /// <summary>How far the nearest living player is (m); far beyond anything with none.</summary>
    public static float Nearest(Vector3 at)
    {
        float best = float.MaxValue;
        foreach (var player in PlayerRoster.All)
        {
            if (player == null || player.IsDead) continue;
            best = Mathf.Min(best, (player.transform.position - at).sqrMagnitude);
        }
        return best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
    }

    /// <summary>How often a zombie here thinks (s): its usual, or thinkFar when far from everyone.</summary>
    public static float ThinkEvery(Vector3 at, float usual)
    {
        Read();
        return on && thinkFar > usual && Nearest(at) > far ? thinkFar : usual;
    }

    /// <summary>How often a zombie here looks for a new path (s): its usual, or chaseFar when far from everyone.</summary>
    public static float ChaseEvery(Vector3 at, float usual)
    {
        Read();
        return on && chaseFar > usual && Nearest(at) > far ? chaseFar : usual;
    }

    /// <summary>An awake horde zombie should look round itself now: nobody has seen a player very lately.</summary>
    public static bool HordeLooks(float lastNoticed)
    {
        Read();
        return !on || hordeLookGap <= 0f || Time.time - lastNoticed >= hordeLookGap;
    }

    /// <summary>
    /// How long a horde zombie waits between its own looks while nobody has seen a player lately (s), in a horde of
    /// this many: the horde's looks a second shared out, never oftener than its usual think.
    /// </summary>
    public static float HordeLookEvery(int horde, float usual)
    {
        Read();
        return on && hordeLooks > 0f ? Mathf.Max(usual, horde / hordeLooks) : usual;
    }

    /// <summary>
    /// How far a chase's goal may have moved from where its path leads before a new path is asked for (m), for a goal
    /// this far away. 0: a new path every turn, as it was.
    /// </summary>
    public static float PathShift(float away)
    {
        Read();
        return on && pathShift > 0f ? Mathf.Max(pathShiftLeast, away * pathShift) : 0f;
    }

    /// <summary>How often a way that stops short of its goal is looked for again (s).</summary>
    public static float PathRetry { get { Read(); return on ? Mathf.Max(0f, pathRetry) : 0f; } }

    /// <summary>A path is asked for again after this long whatever happened (s).</summary>
    public static float PathKeep { get { Read(); return on ? Mathf.Max(.1f, pathKeep) : 0f; } }

    private static void Read()
    {
        if (!listening) { listening = true; GameTables.Reloaded += Forget; }
        if (read) return;
        read = true;
        if (!GameTables.TryGetRow(TableName, TableRow, out var row)) return;
        on = row.Number("enabled", 1f) > 0f;
        far = row.Number("far", far);
        thinkFar = row.Number("thinkFar", thinkFar);
        chaseFar = row.Number("chaseFar", chaseFar);
        hordeLookGap = row.Number("hordeLookGap", hordeLookGap);
        hordeLooks = row.Number("hordeLooks", hordeLooks);
        pathShift = row.Number("pathShift", pathShift);
        pathShiftLeast = row.Number("pathShiftLeast", pathShiftLeast);
        pathRetry = row.Number("pathRetry", pathRetry);
        pathKeep = row.Number("pathKeep", pathKeep);
    }
}
