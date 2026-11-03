using UnityEngine;

/// <summary>
/// When a kill streak first turns into the kill feed's skull (one past KillFeed's mostNumeral), the whole game slows for a
/// moment. In a room every PC slows as the host's word of that kill reaches it (PlayerNetworkScore.KillRpc), so the world
/// stays one; alone, as the kill counts (GunFeelDirector). It runs on real time: it eases down to `scale`, holds, and eases
/// back up. Esc's pause still stops the game, and the counter slowdown and hit stops give way while it runs
/// (CombatTimeEffects lets an outside time scale take over). Numbers: Tables/SkullSlowMotion.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkullSlowMotion : MonoBehaviour
{
    public const string TableName = "SkullSlowMotion", TableRow = "Default";

    [SerializeField, Range(.05f, 1f), Tooltip("How fast the game runs at the slowest (1 is normal).")]
    private float scale = .2f;
    [SerializeField, Min(0f), Tooltip("Real seconds to slow down, to stay slowed, and to come back.")]
    private float easeIn = .15f, hold = 2.5f, easeOut = .6f;

    private static SkullSlowMotion instance;
    private float age = -1f, fixedBaseline = .02f;

    /// <summary>A streak's first skull: slows the game, from the start again if it is already slowed.</summary>
    public static void Begin()
    {
        if (!Application.isPlaying) return;
        if (instance == null)
        {
            instance = new GameObject("Skull Slow Motion").AddComponent<SkullSlowMotion>();
            GameTables.Bind(TableName, TableRow, instance);
        }
        // Read under any slowdown already running: CombatTimeEffects scales the physics step with the time scale.
        if (instance.age < 0f && Time.timeScale > 0f) instance.fixedBaseline = Time.fixedDeltaTime / Time.timeScale;
        instance.age = 0f;
    }

    private void Update()
    {
        if (age < 0f || Time.timeScale <= 0f) return;
        age += Time.unscaledDeltaTime;
        float rise = age - easeIn - hold;
        if (rise >= easeOut) { Stop(); return; }
        float slowed = age < easeIn ? age / Mathf.Max(.001f, easeIn) : rise <= 0f ? 1f : 1f - rise / Mathf.Max(.001f, easeOut);
        float now = Mathf.Lerp(1f, scale, Mathf.SmoothStep(0f, 1f, slowed));
        Time.timeScale = now;
        Time.fixedDeltaTime = fixedBaseline * now;
    }

    private void Stop()
    {
        age = -1f;
        if (Time.timeScale > 0f) Time.timeScale = 1f;
        Time.fixedDeltaTime = fixedBaseline;
    }

    private void OnDestroy()
    {
        if (age >= 0f) Stop();
        if (instance == this) instance = null;
    }
}
