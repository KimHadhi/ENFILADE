using UnityEngine;

/// <summary>
/// What becomes of this PC's player at zero health. While someone else is on their feet they go down instead: they crawl
/// with a pistol and bleed out unless someone gets them up (PlayerRevive). Otherwise, or once they bleed out, they die
/// and come back at the spawn point: at the next wave, or after a while where no waves run. When nobody is left standing
/// the team is wiped: a moment on the screen, then everyone back at the start and the waves from the first. Alone there
/// is nobody to come, so zero health is a wipe at once. A fatal hit (DamageHit.IsFatal) skips the ground altogether.
/// Numbers: Tables/Downed.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLife : MonoBehaviour
{
    public const string TableName = "Downed", TableRow = "Default";
    // Reports of everyone's health take a moment to catch up after a wipe, so a second one can't follow straight on it.
    private const float WipeGrace = 2f, SpawnScatter = 1.5f;

    [SerializeField, Min(.1f), Tooltip("How long someone lasts on the ground with nobody hitting them (s).")]
    private float bleedSeconds = 30f;
    [SerializeField, Min(1f), Tooltip("Health on the ground. It runs down over bleedSeconds, and blows take it faster.")]
    private float downedHealth = 100f;
    [SerializeField, Range(.01f, 1f), Tooltip("Share of full health someone gets up with.")]
    private float reviveHealthShare = .3f;
    [SerializeField, Min(0f), Tooltip("Where no waves run: how long the dead wait before coming back (s).")]
    private float respawnSeconds = 20f;
    [SerializeField, Min(0f), Tooltip("How long the wipe stays on the screen before everyone is back (s).")]
    private float wipeSeconds = 4f;

    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private WaveManager waves;
    private float wipeLeft, respawnLeft, nextWipeCheck;

    public bool IsWiped => wipeLeft > 0f;
    /// <summary>Waves bring the dead back when they start; otherwise a timer does.</summary>
    public bool WaitsForWave => waves != null && waves.IsRunning;
    public float RespawnLeft => respawnLeft;
    public float BleedLeft => receiver != null ? receiver.Health / (downedHealth / bleedSeconds) : 0f;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        receiver = GetComponent<PlayerDamageReceiver>();
    }

    private void Start()
    {
        waves = FindFirstObjectByType<WaveManager>();
        if (waves != null) waves.WaveStarted += OnWaveStarted;
        receiver.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (waves != null) waves.WaveStarted -= OnWaveStarted;
        if (receiver != null) receiver.Died -= OnDied;
    }

    /// <summary>Health ran out: true if they go down, false if there is nobody left to get them up.</summary>
    internal bool TryGoDown()
    {
        if (!GameSession.IsOnline || receiver.IsDowned) return false;
        foreach (var other in PlayerRoster.All)
            if (other != player && other.IsInitialized && Standing(other)) { receiver.GoDown(downedHealth); return true; }
        return false;
    }

    /// <summary>Someone beside them finished getting them up.</summary>
    internal void Revived()
    {
        if (receiver.IsDowned) receiver.GetUp(receiver.MaximumHealth * reviveHealthShare);
    }

    /// <summary>The team is wiped, on every PC.</summary>
    internal void BeginWipe()
    {
        if (!IsWiped) wipeLeft = Mathf.Max(.01f, wipeSeconds);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (IsWiped)
        {
            wipeLeft -= dt;
            if (!IsWiped) FinishWipe();
            return;
        }
        if (receiver.IsDowned) receiver.Bleed(downedHealth / Mathf.Max(.1f, bleedSeconds) * dt);
        if (!receiver.IsAlive && !WaitsForWave)
        {
            respawnLeft -= dt;
            if (GameSession.IsOnline && respawnLeft <= 0f) Respawn();
        }
        WatchForWipe();
    }

    // Alone only this player counts. In a room the host looks after it, and tells everyone.
    private void WatchForWipe()
    {
        if (GameSession.IsGuest || Time.time < nextWipeCheck) return;
        if (!GameSession.IsOnline)
        {
            if (!receiver.IsAlive) BeginWipe();
            return;
        }
        foreach (var someone in PlayerRoster.All)
            if (someone.IsInitialized && Standing(someone)) return;
        if (TryGetComponent(out PlayerNetworkCombat network)) network.AnnounceWipe();
    }

    private static bool Standing(PlayerMovement someone) => someone.DamageReceiver.IsAlive && !someone.DamageReceiver.IsDowned;

    private void FinishWipe()
    {
        nextWipeCheck = Time.time + WipeGrace;
        Respawn();
        if (!GameSession.IsGuest && waves != null && waves.IsRunning) waves.RestartFromFirst();
    }

    private void OnDied() => respawnLeft = respawnSeconds;

    private void OnWaveStarted()
    {
        if (!receiver.IsAlive && !IsWiped) Respawn();
    }

    private void Respawn()
    {
        var point = FindFirstObjectByType<PlayerSpawnPoint>();
        Vector2 scatter = GameSession.IsOnline ? Random.insideUnitCircle * SpawnScatter : Vector2.zero;
        if (point != null) player.MoveTo(point.transform.position + new Vector3(scatter.x, 0f, scatter.y));
        receiver.RestoreHealth();
    }
}
