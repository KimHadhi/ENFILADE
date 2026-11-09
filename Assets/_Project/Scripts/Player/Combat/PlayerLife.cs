using UnityEngine;

/// <summary>
/// What becomes of this PC's player at zero health. While someone else is on their feet they go down instead: they crawl
/// with a pistol and bleed out unless someone gets them up (PlayerRevive). Otherwise, or once they bleed out, they die
/// and come back at the spawn point: at the next wave, or after a while where no waves run. When nobody is left standing
/// the team is wiped: a moment on the screen, then everyone back at the start and the waves from the first. Alone there
/// is nobody to come, so zero health is a wipe at once. A fatal hit (DamageHit.IsFatal) skips the ground altogether.
/// Bleeding stops while someone is getting them up, so help that arrives in time and isn't broken off always saves them.
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
    public float BleedLeft => receiver != null ? receiver.Health / (downedHealth / BleedSeconds) : 0f;
    // The bleed-out time, longer with the grit perk (PlayerPerks).
    private float BleedSeconds => Mathf.Max(.1f, bleedSeconds * PlayerPerks.BleedFactor);
    /// <summary>Who is getting this player up right now, or null.</summary>
    public PlayerMovement Rescuer { get; private set; }
    /// <summary>How far along they are (0-1).</summary>
    public float RescueProgress { get; private set; }

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
            if (other != player && other.IsInitialized && Standing(other))
            {
                receiver.GoDown(downedHealth);
                PlayerRecords.WentDown();
                return true;
            }
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
        if (IsWiped) return;
        wipeLeft = Mathf.Max(.01f, wipeSeconds);
        PlayerRecords.Wiped();
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
        FindRescuer();
        if (receiver.IsDowned && Rescuer == null) receiver.Bleed(downedHealth / BleedSeconds * dt);
        if (!receiver.IsAlive && !WaitsForWave)
        {
            respawnLeft -= dt;
            if (GameSession.IsOnline && respawnLeft <= 0f) Respawn();
        }
        WatchForWipe();
    }

    // Several at once count as the one furthest along.
    private void FindRescuer()
    {
        Rescuer = null;
        RescueProgress = 0f;
        if (!receiver.IsDowned || !GameSession.IsOnline) return;
        ulong mine = GameSession.Network.LocalClientId;
        foreach (var other in PlayerRoster.All)
        {
            if (other == player || !other.TryGetComponent(out PlayerNetworkCombat network)) continue;
            if (!network.IsReviving(mine, out float progress) || progress < RescueProgress) continue;
            Rescuer = other;
            RescueProgress = progress;
        }
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

    private void OnDied()
    {
        respawnLeft = respawnSeconds;
        PlayerRecords.Died();
    }

    private void OnWaveStarted()
    {
        if (!receiver.IsAlive && !IsWiped) Respawn();
    }

    private void Respawn()
    {
        var point = FindFirstObjectByType<PlayerSpawnPoint>();
        Vector2 scatter = GameSession.IsOnline ? Random.insideUnitCircle * SpawnScatter : Vector2.zero;
        // A campaign chapter brings them back at the last rest stop reached, not the chapter's start (CampaignRun).
        bool campaign = CampaignRun.TryCheckpoint(out Vector3 checkpoint);
        if (campaign || point != null)
            player.MoveTo((campaign ? checkpoint : point.transform.position) + new Vector3(scatter.x, 0f, scatter.y));
        receiver.RestoreHealth();
    }
}
