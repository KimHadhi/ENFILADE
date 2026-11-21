using UnityEngine;

/// <summary>
/// What becomes of this PC's player at zero health. While someone else is on their feet they go down instead: they crawl
/// with a pistol and bleed out unless someone gets them up (PlayerRevive). Otherwise, or once they bleed out, they die
/// and come back at the spawn point after a while, or where and when the run says (RunRules). When nobody is left
/// standing the team is wiped: a moment on the screen, then everyone back at the start. Alone there
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
    [SerializeField, Min(0f), Tooltip("How long the dead wait before coming back where the run doesn't keep them (s).")]
    private float respawnSeconds = 20f;
    [SerializeField, Min(0f), Tooltip("How long the wipe stays on the screen before everyone is back (s).")]
    private float wipeSeconds = 4f;

    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private float wipeLeft, respawnLeft, nextWipeCheck;

    public bool IsWiped => wipeLeft > 0f;
    /// <summary>Seconds until the wipe ends and everyone is back (0 when there is none): the death screen counts it down.</summary>
    public float WipeLeft => Mathf.Max(0f, wipeLeft);
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
        receiver.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (receiver != null) receiver.Died -= OnDied;
    }

    /// <summary>
    /// A run someone can come into the middle of is under way in this scene (RunRules). The hub has none, and whoever
    /// joins there is simply there. The host asks as it makes a newcomer's player (GameSession).
    /// </summary>
    internal static bool RunUnderWay => RunRules.UnderWay;

    /// <summary>
    /// This PC's player came into a run already under way (the host said so as it made them: GameSession,
    /// PlayerNetworkPose). They are not set down alive at the map's start in the middle of someone else's fight: they
    /// stand out of it (PlayerDamageReceiver.StandOut), watch the others (PlayerSpectate) and come in the way the dead
    /// come back, by the same clock and at the same place (Respawn): after the wait, when the run calls them back, or
    /// with everyone after a wipe. One rule for everyone who is not in the fight, so a
    /// change to how the dead return changes this with it. No death is counted: they never fell.
    /// User, 2026-11-18: "난입은 언제든지 할 수 있게 하고 싶습니다. 관전이 있으니까요. 중간에 들어가도 세이브포인트까지
    /// 관전하다 바로 같이 할 수 있게끔 말입니다." Until then a newcomer stood alive and alone at the map's start.
    /// </summary>
    internal void Arrive()
    {
        if (receiver == null) receiver = GetComponent<PlayerDamageReceiver>();
        receiver.StandOut();
        respawnLeft = respawnSeconds;
        PlayRecord.Line(JoinRecord, JoinHeading, $"ARRIVED into a run under way: out of the fight, watching; comes in after {respawnSeconds:0} s, or when the run calls the dead back");
    }

    /// <summary>Logs/joins.txt (editor and development builds): what a join into a run under way did, on the host and on the joiner's PC.</summary>
    internal const string JoinRecord = "joins",
        JoinHeading = "joining a room: HOST the host making a newcomer's player, ARRIVED this PC's player coming in waiting, CAME IN its joining the fight";

    /// <summary>Health ran out: true if they go down, false if there is nobody left to get them up.</summary>
    internal bool TryGoDown()
    {
        // Where the run says nobody can reach them: no ground, as when playing alone (RunRules).
        if (!GameSession.IsOnline || receiver.IsDowned || RunRules.NoOneToHelp(player)) return false;
        foreach (var other in PlayerRoster.All)
            if (other != player && other.IsInitialized && Standing(other))
            {
                receiver.GoDown(downedHealth);
                PlayerRecords.WentDown();
                return true;
            }
        return false;
    }

    /// <summary>
    /// A Détente brought them back from the dead where they fell (DetenteKit's Resurrection), with this share of their
    /// health, as a respawn does but in place.
    /// </summary>
    internal void Resurrect(float share)
    {
        if (receiver.IsAlive || IsWiped) return;
        receiver.RestoreHealth();
        receiver.GetUp(receiver.MaximumHealth * Mathf.Clamp(share, .05f, 1f));
        if (TryGetComponent(out PlayerWeaponSlots slots)) slots.RefillAmmo();
        AlertHUD.Self(Texts.Get("class.detente.raised_self"));
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
        // A run may keep the dead until it calls them back (RunRules).
        if (!receiver.IsAlive && !RunRules.KeepsDead)
        {
            respawnLeft -= dt;
            if (GameSession.IsOnline && (respawnLeft <= 0f || RunRules.CallsDeadBack)) Respawn();
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
    }

    private void OnDied()
    {
        respawnLeft = respawnSeconds;
        PlayerRecords.Died();
    }

    private void Respawn()
    {
        var point = FindFirstObjectByType<PlayerSpawnPoint>();
        Vector2 scatter = GameSession.IsOnline ? Random.insideUnitCircle * SpawnScatter : Vector2.zero;
        // A run brings them back at its own place (RunRules), else the map's spawn point.
        bool hall = RunRules.TryCheckpoint(out Vector3 checkpoint);
        if (hall || point != null)
            player.MoveTo((hall ? checkpoint : point.transform.position) + new Vector3(scatter.x, 0f, scatter.y));
        if (receiver.IsWaiting)
            PlayRecord.Line(JoinRecord, JoinHeading, $"CAME IN at {player.transform.position} ({(hall ? "the run's save point" : "the map's start")})");
        receiver.RestoreHealth();
        // Back with full magazines and reserves, as at the start (user, 2026-11-11 playtest: "탄창 보충이 안 돼 있음").
        if (TryGetComponent(out PlayerWeaponSlots slots)) slots.RefillAmmo();
    }
}
