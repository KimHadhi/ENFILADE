using UnityEngine;

/// <summary>
/// When a kill streak first turns into the kill feed's skull (one past KillFeed's mostNumeral), the whole game slows for a
/// moment. In a room every PC slows as the host's word of that kill reaches it (PlayerNetworkScore.KillRpc), so the world
/// stays one; alone, as the kill counts (GunFeelDirector). It runs on real time: it eases down to `scale`, holds, and eases
/// back up. It is a hold on WorldTime: Esc's pause still stops the game and the slowdown carries on after it, and beside
/// the counter's slow motion or a hit stop the world runs at the slower of the two. The screen (SlowMotionLook) and the
/// sound (SlowMotionSound) follow Depth, and VfxTimeStep keeps the effects moving every frame. As a streak's slowdown is
/// set off, each player's guns come up with full magazines (fillMagazines; the brief one only with briefFillMagazines).
/// Numbers: Tables/SkullSlowMotion, one row for all three.
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
    [SerializeField, Tooltip("As a streak's slowdown begins, every player's guns come up with full magazines, reserves untouched.")]
    private bool fillMagazines = true;
    [SerializeField, Tooltip("The brief slowdown of a named or elite kill fills the magazines too.")]
    private bool briefFillMagazines;

    private static SkullSlowMotion instance;
    private float age = -1f, holding = 2.5f;

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
        bool fills = brief ? instance.briefFillMagazines : instance.fillMagazines;
        GunAmmoLog.Note("slow motion set off (" + (brief ? "a named or elite kill, brief" : "a streak's skull")
            + (fills ? "): magazines fill" : "): magazines left as they are"));
        if (fills) FillMagazines();
    }

    // What the slowdown gives as it is set off (the user, 2026-11-18: "발동 즉시 모든 인원의 주무기 및 보조무기 현재
    // 장탄수만 최대치로 충전 (예비 탄약은 충전 제외)"): the magazines of the guns each player holds, full at once. Every PC
    // sets the slowdown off from the same word (the host's KillRpc; alone, the kill itself), and a player's rounds are
    // counted on their own PC, so each PC fills its own player's and everyone ends up filled with nothing more sent.
    // One set off while another still runs fills again: it was set off again.
    private static void FillMagazines()
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out PlayerWeaponSlots slots)) slots.TopUpMagazines();
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
        if (age < 0f || GamePause.IsPaused) return;
        age += Time.unscaledDeltaTime;
        if (age - easeIn - holding >= easeOut) { Stop(); return; }
        Depth = Mathf.SmoothStep(0f, 1f, Slowed());
        WorldTime.Hold(this, Mathf.Lerp(1f, scale, Depth));
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
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Systems, "Combat");
        instance = host.AddComponent<SkullSlowMotion>();
        GameTables.Bind(TableName, TableRow, instance, host.AddComponent<SlowMotionLook>(), host.AddComponent<SlowMotionSound>());
    }

    private void Stop()
    {
        age = -1f; Depth = 0f;
        WorldTime.Release(this);
    }

    private void OnDestroy()
    {
        if (age >= 0f) Stop();
        if (instance == this) instance = null;
    }
}
