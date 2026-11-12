using UnityEngine;

/// <summary>
/// When a kill streak first turns into the kill feed's skull (one past KillFeed's mostNumeral), the whole game slows for a
/// moment. In a room every PC slows as the host's word of that kill reaches it (PlayerNetworkScore.KillRpc), so the world
/// stays one; alone, as the kill counts (GunFeelDirector). It runs on real time: it eases down to `scale`, holds, and eases
/// back up. Esc's pause still stops the game, and the counter slowdown and hit stops give way while it runs
/// (CombatTimeEffects lets an outside time scale take over). The screen (SlowMotionLook) and the sound (SlowMotionSound)
/// follow Depth, and VfxTimeStep keeps the effects moving every frame. Numbers: Tables/SkullSlowMotion, one row for all
/// three.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkullSlowMotion : MonoBehaviour
{
    public const string TableName = "SkullSlowMotion", TableRow = "Default";

    [SerializeField, Range(.05f, 1f), Tooltip("How fast the game runs at the slowest (1 is normal).")]
    private float scale = .2f;
    [SerializeField, Min(0f), Tooltip("Real seconds to slow down, to stay slowed, and to come back.")]
    private float easeIn = .15f, hold = 2.5f, easeOut = .6f;
    [SerializeField, Min(0f), Tooltip("Real seconds a named or elite enemy's kill stays slowed (backlog 325): the same slowdown, held briefly.")]
    private float briefHold = .5f;

    private static SkullSlowMotion instance;
    private float age = -1f, fixedBaseline = .02f, holding = 2.5f;

    /// <summary>How slowed the game is now: 0 at normal speed, 1 at the slowest.</summary>
    public static float Depth { get; private set; }
    /// <summary>Real seconds since the slowdown began; below 0 while none runs.</summary>
    public static float Age => instance != null ? instance.age : -1f;

    /// <summary>A streak's first skull: slows the game and holds it for the full time.</summary>
    public static void Begin() => Begin(false);

    /// <summary>A named or elite enemy's kill (backlog 325): the same slowdown, held only briefly. It never cuts short a
    /// longer one already running.</summary>
    public static void BeginBrief() => Begin(true);

    private static void Begin(bool brief)
    {
        if (!Application.isPlaying) return;
        Ensure();
        instance.Restart(brief ? instance.briefHold : instance.hold);
    }

    private void Restart(float holdFor)
    {
        if (age >= 0f)
        {
            // Already slowed: keep a longer hold that's still to come, otherwise carry on from how slowed it is now
            // rather than snapping back to normal speed and down again.
            if (age >= easeIn && holding - (age - easeIn) >= holdFor) return;
            age = Slowed() * easeIn;
            holding = holdFor;
            return;
        }
        // Read under any slowdown already running: CombatTimeEffects scales the physics step with the time scale.
        if (Time.timeScale > 0f) fixedBaseline = Time.fixedDeltaTime / Time.timeScale;
        age = 0f;
        holding = holdFor;
    }

    // 0 at normal speed to 1 at the slowest, before easing.
    private float Slowed()
    {
        float rise = age - easeIn - holding;
        return age < easeIn ? age / Mathf.Max(.001f, easeIn) : rise <= 0f ? 1f : Mathf.Clamp01(1f - rise / Mathf.Max(.001f, easeOut));
    }

    private void Update()
    {
        if (age < 0f || Time.timeScale <= 0f) return;
        age += Time.unscaledDeltaTime;
        if (age - easeIn - holding >= easeOut) { Stop(); return; }
        float slowed = Slowed();
        Depth = Mathf.SmoothStep(0f, 1f, slowed);
        float now = Mathf.Lerp(1f, scale, Depth);
        Time.timeScale = now;
        Time.fixedDeltaTime = fixedBaseline * now;
    }

    /// <summary>Makes the slowdown's screen and sound before the first skull, so its first frame builds nothing (CombatWarmup).
    /// Both stay idle until Begin.</summary>
    public static void Prepare()
    {
        if (Application.isPlaying) Ensure();
    }

    private static void Ensure()
    {
        if (instance != null) return;
        var host = new GameObject("Skull Slow Motion");
        instance = host.AddComponent<SkullSlowMotion>();
        GameTables.Bind(TableName, TableRow, instance, host.AddComponent<SlowMotionLook>(), host.AddComponent<SlowMotionSound>());
    }

    private void Stop()
    {
        age = -1f; Depth = 0f;
        if (Time.timeScale > 0f) Time.timeScale = 1f;
        Time.fixedDeltaTime = fixedBaseline;
    }

    private void OnDestroy()
    {
        if (age >= 0f) Stop();
        if (instance == this) instance = null;
    }
}
