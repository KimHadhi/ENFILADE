using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What a zombie knows of the players (ZombieMind). Awake is 0, so an older report reads as awake. Rousing: just woken
/// from calm, it rouses (its rouse clip on every PC) before it charges. Rising: back on its feet after a knockdown, it
/// comes to its senses the same way, but apart from Rousing, which a charger's charge wind-up also shows as (the
/// 2026-11-11 review: a charger getting up bellowed and flared for a charge that never came).
/// </summary>
public enum ZombieMood : byte { Awake, Calm, Searching, Alert, Rousing, Rising }

/// <summary>
/// Multiplayer plan stage 4C: what a zombie knows. A horde zombie (a wave's, or a plain /spawn) is awake from the start
/// and always knows where the nearest player is, as before. One that starts calm (placed in a map with startCalm, or
/// /spawn n calm) has to notice them: it stands Calm and looks round now and then, grows Alert (stops and turns to what
/// it noticed), goes Searching (walks over and looks about) and wakes when sure, then chases and fights as before, and
/// calls the calm ones nearby. Its suspicion grows from what it sees and hears. Sight: players within range and in
/// front, best straight ahead and fading toward the edge of its sight, each checked at the head, chest and hips with
/// rays, faster the closer, the more of them shows and the faster they move, slower crouched and slower still lying
/// down, sooner and from farther with their flashlight turned on it; anyone this close right before its eyes wakes it
/// at once. Hearing: every Noise (footsteps, landings, vaults, voices, shots) within its reach, half the reach through a
/// wall, louder the nearer, but none of a crouched player's small sounds; a faint one only turns its head. A stone a
/// player threw (a lure) draws the nearest that hears it over to look, and turns the others' heads, without ever
/// telling them who threw it. A blow wakes it at once. Awake but no longer seeing nor hearing its quarry, it searches
/// where it last knew them, and gives up in the end. The host thinks; other PCs show its mood, how sure it is and of
/// whom (EnemyNetwork), for the stealth HUD. Numbers: Tables/ZombieSenses. The 2026-11-11 stealth round took its
/// shape from Advanced Stealth (F:\Addons): the smooth fall-off of sight to the side, silent crouched steps, thrown
/// objects that draw one zombie while the rest turn to look.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieMind : MonoBehaviour
{
    public const string TableName = "ZombieSenses", TableRow = "Default";
    private const float ThinkEvery = .1f, ArriveDistance = 1f;
    private const string EnemyLayer = "Enemy";
    // Where a player is looked at, as shares of their capsule's height from its bottom.
    private static readonly float[] LookHeights = { .9f, .62f, .3f };

    [Tooltip("Placed in a map: it starts calm and must notice the players. Otherwise it is a horde zombie, awake from the start.")]
    [SerializeField] private bool startCalm;

    [Header("Sight")]
    [SerializeField, Min(0f), Tooltip("How far it sees, alert or searching (m).")]
    private float sightRange = 22f;
    [SerializeField, Min(0f), Tooltip("How far it sees while calm (m).")]
    private float calmSightRange = 12f;
    [SerializeField, Range(1f, 360f), Tooltip("Its full view, side to side (degrees). Inside it, it notices at full speed.")]
    private float fieldOfView = 110f;
    [SerializeField, Range(1f, 360f), Tooltip("The edge of its sight, side to side (degrees). Between the two it notices at sideShare.")]
    private float sideView = 200f;
    [SerializeField, Range(0f, 1f), Tooltip("How fast it notices at the edge of its sight, as a share.")]
    private float sideShare = .35f;
    [SerializeField, Min(0f), Tooltip("Suspicion a second from a player standing still in full view right in front (1 wakes it).")]
    private float nearRate = 2.5f;
    [SerializeField, Range(0f, 1f), Tooltip("How fast it notices at the far end of its sight, as a share of nearRate.")]
    private float farShare = .15f;
    [SerializeField, Min(0f), Tooltip("Seen this close (m) and in its full view, a player wakes it at once.")]
    private float touchRange = 2f;
    [SerializeField, Range(0f, 1f)] private float crouchShare = .55f;
    [SerializeField, Range(0f, 1f), Tooltip("Lying down: prone, on the back, crawling.")]
    private float lyingShare = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("A player standing still, as a share. Moving it rises to 1 at walking pace.")]
    private float stillShare = .6f;
    [SerializeField, Min(1f), Tooltip("A sprinting player, as a share.")]
    private float sprintShare = 1.6f;
    [SerializeField, Min(.1f), Tooltip("Speed that counts as sprinting (m/s).")]
    private float sprintSpeed = 5f;
    [SerializeField, Min(1f), Tooltip("A player whose flashlight is on and turned on it: how much faster it notices them.")]
    private float lightShare = 1.6f;
    [SerializeField, Min(1f), Tooltip("...and how much farther it sees them from, as a share of its sight.")]
    private float lightRange = 1.4f;
    [SerializeField, Range(1f, 90f), Tooltip("How far off the way a player faces their flashlight still lights it (degrees either side).")]
    private float lightAngle = 30f;

    [Header("Hearing")]
    [SerializeField, Range(0f, 2f), Tooltip("Share of a noise's reach it hears, alert or searching.")]
    private float hearShare = 1f;
    [SerializeField, Range(0f, 2f), Tooltip("Share of a noise's reach it hears while calm.")]
    private float calmHearShare = .6f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the reach left through a wall.")]
    private float wallShare = .5f;
    [SerializeField, Min(0f), Tooltip("Suspicion a noise gives right at its source; less the farther off.")]
    private float hearGain = 1.5f;
    [SerializeField, Min(0f), Tooltip("A crouched player's sounds that carry no farther than this (m: a sneaking step, crouching, a lean) don't reach it at all.")]
    private float sneakSilent = 3f;
    [SerializeField, Min(0f), Tooltip("Least suspicion a sound has to give it for it to turn its head that way a moment.")]
    private float glanceAt = .06f;
    [SerializeField, Min(0f), Tooltip("How long it looks toward a faint sound, or a stone it isn't the one to check, before it settles (s).")]
    private float glanceSeconds = 1.5f;
    [SerializeField, Min(0f), Tooltip("The one nearest a thrown stone stares at where it fell this long (s), then walks over to look. A stone never makes it surer that someone is there.")]
    private float lureStare = 1.2f;
    [SerializeField, Range(.05f, 1f), Tooltip("Going to look where a stone fell, it walks at this share of its chase speed (a search's is slower).")]
    private float lureSpeedShare = .6f;
    [SerializeField, Min(0f), Tooltip("Come to where a stone fell, how long it looks round there before it settles on that spot (s).")]
    private float lureLook = 5f;
    [SerializeField, Min(0f), Tooltip("The others that hear a stone turn and watch where it fell this long (s), then turn back.")]
    private float lureGlance = 6f;
    [SerializeField, Range(0f, 2f), Tooltip("Share of a stone's reach it hears, calm or not: a stone's clack carries as it is.")]
    private float lureHearShare = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Turned to a stone, how well it still notices things off to the side, as a share: its mind is on the stone.")]
    private float lureFocus = .35f;
    [SerializeField, Range(0f, 180f), Tooltip("Come to where a stone fell, how far it looks either way (degrees): about the stone, not back the way it came.")]
    private float lureSweep = 35f;

    [Header("Mind")]
    [SerializeField, Min(0f), Tooltip("Suspicion lost a second while it notices nothing.")]
    private float forget = .25f;
    [SerializeField, Range(0f, 1f), Tooltip("Suspicion at which it stops and turns to look.")]
    private float alertAt = .3f;
    [SerializeField, Min(0f), Tooltip("Awake, with its quarry neither seen nor heard this long (s), it goes searching where it last knew them.")]
    private float loseSeconds = 6f;
    [SerializeField, Min(0f), Tooltip("How long a search lasts before it calms down (s).")]
    private float searchSeconds = 12f;
    [SerializeField, Min(0f), Tooltip("How long it looks round once it reaches the place (s).")]
    private float lookSeconds = 3f;
    [SerializeField, Range(0f, 180f), Tooltip("Looking round where it came to search, how far it turns either way (degrees).")]
    private float searchSweep = 70f;
    [SerializeField, Range(.05f, 1f), Tooltip("Searching, it walks at this share of its chase speed.")]
    private float searchSpeedShare = .3f;
    [SerializeField, Min(0f), Tooltip("Waking, it wakes the calm ones this near (m).")]
    private float callRadius = 12f;
    [SerializeField, Min(0f), Tooltip("Alert, how fast it turns to what it noticed (degrees a second).")]
    private float turnSpeed = 240f;
    [SerializeField, Min(0f), Tooltip("Calm, about how often it turns to look elsewhere (s). 0: never.")]
    private float lookAroundEvery = 8f;
    [SerializeField, Range(0f, 180f), Tooltip("Calm, how far from the way it stood it turns to look (degrees either side).")]
    private float lookAroundAngle = 80f;
    [SerializeField, Min(0f), Tooltip("Calm, how fast it turns when it looks round (degrees a second).")]
    private float lookAroundSpeed = 40f;
    [SerializeField, Min(0f), Tooltip("Woken from calm, how long it rouses before it charges (s): its rouse clip.")]
    private float rouseSeconds = 1.1f;
    [SerializeField, Range(0f, 90f), Tooltip("How far it looks up (degrees above level from its eyes): someone higher than this "
        + "is seen only at lookUpShare. Shamblers stared up at players on ledges (user, 2026-11-11 playtest).")]
    private float lookUp = 30f;
    [SerializeField, Range(0f, 1f), Tooltip("How well it sees someone above lookUp, as a share.")]
    private float lookUpShare = .2f;
    [SerializeField, Min(0f), Tooltip("Back on its feet after a knockdown, how long it comes to its senses before it chases, "
        + "swings or grabs (s): it stands, turns to the nearest player and plays its rouse. Horde zombies too. 0: it goes at once.")]
    private float riseRouse = 1.2f;

    [Header("Busy (a calm pose: ZombieLooks.CalmPose kind)")]
    [SerializeField, Range(0f, 1f), Tooltip("Eating a body on the ground: how far it sees, as a share of its calm sight.")]
    private float eatRangeShare = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("Eating: how wide it sees, as a share of its view and the edge of its sight.")]
    private float eatViewShare = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("Eating: how far it hears, as a share.")]
    private float eatHearShare = .7f;
    [SerializeField, Range(0f, 1f), Tooltip("Eating: how fast it notices what it does see, as a share.")]
    private float eatNoticeShare = .7f;
    [SerializeField, Range(0f, 1f), Tooltip("Dozing: how far it sees, as a share of its calm sight.")]
    private float dozeRangeShare = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("Dozing: how wide it sees, as a share.")]
    private float dozeViewShare = .4f;
    [SerializeField, Range(0f, 1f), Tooltip("Dozing: how far it hears, as a share.")]
    private float dozeHearShare = .45f;
    [SerializeField, Range(0f, 1f), Tooltip("Dozing: how fast it notices what it does see, as a share.")]
    private float dozeNoticeShare = .5f;

    [Header("Stealth kill")]
    [SerializeField, Range(10f, 180f), Tooltip("Within this angle either side of its back (degrees), a player may take a zombie that isn't awake down (F, or a left click with the fists).")]
    private float takedownAngle = 70f;

    private static readonly List<ZombieMind> minds = new List<ZombieMind>();
    // Each player's gun, for its flashlight. Found once.
    private static readonly Dictionary<PlayerMovement, WeaponController> guns = new Dictionary<PlayerMovement, WeaponController>();

    /// <summary>Every zombie mind switched on, on this PC (the host's or copies), for the stealth HUD and view.</summary>
    public static IReadOnlyList<ZombieMind> All => minds;

    /// <summary>
    /// On the host or alone: a zombie that had not noticed anyone has, and who it is after. A campaign's quiet stretch
    /// ends on it (CampaignRun).
    /// </summary>
    public static event System.Action<ZombieMind, PlayerMovement> Woke;

    private EnemyHealth health;
    private EnemyNetwork network;
    private MeleeCombatTarget melee;
    private NavMeshAgent agent;
    private ZombieBody body;
    // Who it is after (or would be), and who its suspicion or a stone came from: whom the stealth HUD shows it to.
    private PlayerMovement focus, noticed;
    private Vector3 stimulus, lastKnown;
    private float suspicion, thinkLeft, lastSeen, searchStart, arrivedAt, rouseUntil, chaseSpeed = -1f;
    // A glance's end (0: not glancing), the way it stood calm, the way it looks now and when it looks elsewhere next, and
    // the way it faced on reaching a search's place.
    private float glanceUntil, postYaw, lookYaw, lookAt, arriveYaw;
    private bool horde = true, arrived;
    // On its way to (or looking round) where a stone fell, and where that was; turned to watch a stone until then.
    private bool lured;
    private Vector3 lureAt;
    private float distractedUntil;

    // Its mind on a stone: it notices what is off to the side of it far more slowly (lureFocus).
    private bool Distracted => lured || Time.time < distractedUntil;
    // On a copy: how sure the host's zombie is, in twentieths, and of whom (their client id + 1; 0 for nobody).
    private byte shownStep;
    private ulong shownWatch, resolvedWatch;
    private PlayerMovement shownWatcher;

    /// <summary>Its mood: thought on the host (or alone), shown on every other PC.</summary>
    public ZombieMood Mood { get; private set; } = ZombieMood.Awake;
    /// <summary>It knows someone is there: rousing or awake.</summary>
    public bool IsAwake => Mood == ZombieMood.Awake || Mood == ZombieMood.Rousing || Mood == ZombieMood.Rising;
    /// <summary>
    /// Awake, done rousing and on its feet: it may chase and fight (EnemyChaseAI, EnemyCombatAI, and every other mover:
    /// a runner's charge, a charger's, a climb, an exploder's walk in).
    /// </summary>
    public bool Ready => Mood == ZombieMood.Awake && !GettingUp;
    /// <summary>
    /// Its body still shows some of its calm pose (sitting, eating, crouched, lying) or is getting up out of it, as the
    /// body measures from its own weights every frame (ZombieBody.Moods, on every PC alike): until it stands it isn't
    /// Ready and goes nowhere, whatever woke it, and every mover waits on it (Ready, or ZombieElite/ZombieCharger CanAct).
    /// Shot awake it woke straight to Awake (Struck) while its body took the get-up clip's whole length, so it slid at the
    /// player still sitting or lying (user, 2026-11-12: "좀비가 앉은 채, 누운 채로 와. 모션이 굳은 채로 온다고"); later the
    /// same day, still: "calm한 좀비한테 들켰을 때 좀비가 여전히 원래 포즈 그대로 와".
    /// </summary>
    internal bool GettingUp { get; set; }
    // It was held by its getting up last frame: the frame it stands, a search it was sent on walks on.
    private bool heldUp;
    /// <summary>
    /// Not awake: calm, searching, or alert to something it hasn't made out. A takedown from behind ends it (EnemyHealth).
    /// Alert used to count as aware, so a zombie turned to look at a thrown stone could not be taken from behind.
    /// </summary>
    public bool Unaware => !IsAwake;
    /// <summary>The player it is after, awake; null for a horde zombie, which takes the nearest (EnemyTargeting).</summary>
    public PlayerMovement Target => IsAwake && !horde && Alive(focus) ? focus : null;
    /// <summary>How sure it is that someone is there, 0 to 1 (the host's own).</summary>
    public float Suspicion => suspicion;

    /// <summary>How sure it is that someone is there, 0 to 1, on every PC: the host's own, a copy's as the host shares it.</summary>
    public float ShownSuspicion => IsCopy ? shownStep / 20f : horde ? 0f : suspicion;

    /// <summary>The host's suspicion in twentieths, for its copies (EnemyNetwork).</summary>
    internal byte SuspicionStep => (byte)Mathf.Clamp(Mathf.RoundToInt(ShownSuspicion * 20f), 0, 20);

    /// <summary>
    /// The player it is noticing, going to look for or woke after, on every PC; null for nobody and for a horde zombie.
    /// The stealth HUD shows it to that player (StealthMarkersHUD).
    /// </summary>
    public PlayerMovement Watching
    {
        get
        {
            if (!IsCopy) return !horde && Alive(noticed) ? noticed : null;
            if (shownWatch == 0) return null;
            if (resolvedWatch != shownWatch || shownWatcher == null)
            {
                resolvedWatch = shownWatch;
                shownWatcher = null;
                foreach (var player in PlayerRoster.All)
                    if (player != null && player.TryGetComponent(out NetworkObject net) && net.OwnerClientId + 1 == shownWatch) { shownWatcher = player; break; }
            }
            return shownWatcher;
        }
    }

    /// <summary>Where it sees and hears from: its head, or about there without a body.</summary>
    internal Vector3 EyePoint => Eye();
    /// <summary>Its body still lives (on this PC).</summary>
    internal bool Living => health != null && health.IsAlive;
    /// <summary>How far it sees now (m): its calm sight while calm, less again while eating or dozing.</summary>
    internal float ViewRange => (Mood == ZombieMood.Calm || lured ? calmSightRange : sightRange) * BusyRange;
    /// <summary>Its full view and the edge of its sight, side to side (degrees), narrower while eating or dozing.</summary>
    internal float FrontView => fieldOfView * BusyView;
    internal float EdgeView => Mathf.Max(fieldOfView, sideView) * BusyView;

    /// <summary>
    /// What its calm pose is busy with (ZombieBody sets it on every PC while it holds the pose; Rest otherwise): eating
    /// or dozing, it sees less far and narrower, hears less and is slower to notice (eat*, doze* in ZombieSenses). The
    /// 2026-11-11 memo, item 47.
    /// </summary>
    internal CalmPoseKind PoseKind { get; set; }
    private float BusyRange => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatRangeShare : PoseKind == CalmPoseKind.Doze ? dozeRangeShare : 1f;
    private float BusyView => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatViewShare : PoseKind == CalmPoseKind.Doze ? dozeViewShare : 1f;
    private float BusyHear => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatHearShare : PoseKind == CalmPoseKind.Doze ? dozeHearShare : 1f;
    private float BusyNotice => !HoldsPose ? 1f : PoseKind == CalmPoseKind.Eat ? eatNoticeShare : PoseKind == CalmPoseKind.Doze ? dozeNoticeShare : 1f;

    /// <summary>
    /// Stage 4C-2: not awake (calm, searching or alert) and this spot behind its back, so a player standing there may take
    /// it down, an execution without breaking it first (MeleeCombatTarget.CanBeExecutedBy). Every PC judges it the same
    /// way from the mood the host shares; the host checks again when it holds the zombie for them.
    /// </summary>
    public bool CanBeTakenDownFrom(Vector3 at)
    {
        if (!Unaware) return false;
        Vector3 toward = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
        return toward.sqrMagnitude < 1e-4f || Vector3.Angle(-transform.forward, toward) <= takedownAngle;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics()
    {
        minds.Clear();
        guns.Clear();
        Woke = null;
        LastNoticed = float.NegativeInfinity;
        LastSeenAt = Vector3.zero;
    }

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        melee = GetComponent<MeleeCombatTarget>();
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<ZombieBody>();
        GameTables.Bind(TableName, TableRow, this);
    }

    private void OnEnable()
    {
        minds.Add(this);
        if (minds.Count == 1) Noise.Made += Hear;
    }

    private void OnDisable()
    {
        minds.Remove(this);
        if (minds.Count == 0) Noise.Made -= Hear;
    }

    private void Start()
    {
        // The table has set the agent by now (EnemyHealth binds it in Awake).
        if (agent != null) chaseSpeed = agent.speed;
        thinkLeft = Random.Range(0f, ThinkEvery);
        if (startCalm && !IsCopy) Calm();
    }

    private bool IsCopy => network != null && network.IsCopy;
    private bool Thinks => !IsCopy && health != null && health.IsAlive && !health.WavePaused;

    // ---- Moods -------------------------------------------------------------------------------------------------

    /// <summary>When an awake zombie last saw anyone (game time), horde or not: the campaign's lull waits on it.</summary>
    public static float LastNoticed { get; private set; } = float.NegativeInfinity;
    /// <summary>Where that was (a player's feet then): the horde zombies that skip their own look take it as theirs.</summary>
    public static Vector3 LastSeenAt { get; private set; }

    /// <summary>
    /// The chase dies down (CampaignRun's lull): every awake zombie on the host, horde ones included, loses its quarry and
    /// goes to look where it last knew them, then calms (Searching). Being found once no longer means being found for good
    /// (user, 2026-11-11 playtest: "한번 들키면 끝이고 의미 없는 거 아니야?").
    /// </summary>
    public static void LoseAll()
    {
        foreach (var mind in minds)
        {
            if (mind == null || !mind.Thinks || !mind.IsAwake) continue;
            if (mind.TryGetComponent(out ZombieGrip grip) && grip.IsHolding) continue;
            mind.horde = false;
            mind.focus = null;
            // Lost, none of them is noticing anyone in particular: no question marks over the whole crowd.
            mind.noticed = null;
            mind.suspicion = mind.alertAt;
            mind.Search(mind.lastKnown != Vector3.zero ? mind.lastKnown : mind.transform.position);
        }
    }

    /// <summary>
    /// It lost its quarry in smoke (SmokeScreen, on the host): as the chase dying down does to all of them (LoseAll), this
    /// one goes to look where it last knew them and must make them out again by sight or sound, horde zombie or not. A
    /// Ghost's smoke is how they slip a crowd off their back (user, 2026-11-12: "잠입과 어그로를 피하기 위한 연막탄").
    /// </summary>
    internal void Lose(Vector3 lastAt)
    {
        if (!Thinks || !IsAwake) return;
        if (TryGetComponent(out ZombieGrip grip) && grip.IsHolding) return;
        horde = false;
        focus = noticed = null;
        suspicion = alertAt;
        lastKnown = lastAt;
        Search(lastAt);
    }

    /// <summary>Calms it: it stands where it is and has to notice the players (a placed zombie, /spawn n calm).</summary>
    public void Calm()
    {
        if (IsCopy) return;
        horde = false;
        Mood = ZombieMood.Calm;
        suspicion = 0f;
        focus = noticed = null;
        glanceUntil = distractedUntil = 0f;
        lured = false;
        Halt();
        Post();
        lookAt = Time.time + Random.Range(1.5f, Mathf.Max(1.5f, lookAroundEvery));
    }

    /// <summary>
    /// Placed calm (CampaignHorde), it stands looking this way (degrees about up) and looks round from there.
    /// </summary>
    internal void FaceWhenCalm(float yaw)
    {
        if (IsCopy) return;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Post();
    }

    // Where it stands looking now becomes the way it looks round from.
    private void Post()
    {
        postYaw = lookYaw = transform.eulerAngles.y;
    }

    /// <summary>
    /// Awake already, it rouses again for this long: it stands, faces this player and plays its rouse on every PC, then
    /// goes on as before. A charger winding up (ZombieCharger).
    /// </summary>
    internal void Rouse(PlayerMovement at, float seconds)
    {
        if (IsCopy || Mood != ZombieMood.Awake) return;
        Mood = ZombieMood.Rousing;
        if (Alive(at)) focus = at;
        rouseUntil = Time.time + Mathf.Max(0f, seconds);
        Halt();
    }

    /// <summary>Wakes it after who (or the nearest player). Rouse: it rouses a moment first. Call: it wakes the calm near it.</summary>
    public void Wake(PlayerMovement who, bool rouse = true, bool call = true)
    {
        if (IsCopy || IsAwake) return;
        Mood = rouse && rouseSeconds > 0f ? ZombieMood.Rousing : ZombieMood.Awake;
        suspicion = 1f;
        glanceUntil = distractedUntil = 0f;
        lured = false;
        focus = Alive(who) ? who : PlayerRoster.Nearest(transform.position);
        noticed = focus;
        lastSeen = Time.time;
        if (focus != null) lastKnown = focus.transform.position;
        rouseUntil = Time.time + rouseSeconds;
        SetSpeed(1f);
        // Woken out of a pose it gets up first, from this very frame (its body takes it over from its next update).
        if (HoldsPose) GettingUp = true;
        // Rousing, it stands and faces its quarry; then EnemyChaseAI takes it.
        if (Mood == ZombieMood.Rousing || GettingUp) Halt();
        else if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
        Woke?.Invoke(this, focus);
        if (!call) return;
        foreach (var other in minds)
            if (other != this && !other.IsAwake && other.Thinks && (other.transform.position - transform.position).sqrMagnitude <= callRadius * callRadius)
                other.Wake(focus, true, false);
    }

    private void Alert(Vector3 at)
    {
        stimulus = at;
        if (Mood == ZombieMood.Alert) return;
        Mood = ZombieMood.Alert;
        Halt();
    }

    // It turns to look at something a while (a faint sound, a stone another goes to check), then settles back where it
    // stood. One already searching keeps at it, one on its way to a stone keeps to it, and a real alert stays one.
    private void Glance(Vector3 at, PlayerMovement by, float seconds)
    {
        if (seconds <= 0f || lured) return;
        if (Mood != ZombieMood.Calm && !(Mood == ZombieMood.Alert && glanceUntil > 0f)) return;
        stimulus = at;
        if (noticed == null && Alive(by)) noticed = by;
        glanceUntil = Time.time + seconds;
        if (Mood == ZombieMood.Calm) { Mood = ZombieMood.Alert; Halt(); }
    }

    // The one nearest where a stone fell: it stares that way a moment, then walks over briskly, looks round there a good
    // while and settles on that spot. A stone never makes it any surer that someone is there: it used to, which filled
    // the thrower's stealth gauge as if they had been seen and left the zombie a glimpse from waking (user, 2026-11-11:
    // "돌 던지기가 걍 좀비 의심 게이지만 높이고 별 쓸모가 없는 거 같은데"). Turned to the stone its back is to the way
    // past, and once settled it no longer stands where it stood.
    private void Investigate(Vector3 at, PlayerMovement by)
    {
        lured = true;
        lureAt = stimulus = at;
        if (Alive(by)) noticed = by;
        if (Mood == ZombieMood.Searching) { Search(at, true); return; }
        glanceUntil = Time.time + Mathf.Max(.05f, lureStare);
        if (Mood != ZombieMood.Alert) { Mood = ZombieMood.Alert; Halt(); }
    }

    // Lure: it is going to look where a stone fell (briskly, and with calm eyes: Look).
    private void Search(Vector3 at, bool lure = false)
    {
        Mood = ZombieMood.Searching;
        lured = lure;
        glanceUntil = 0f;
        stimulus = at;
        searchStart = Time.time;
        arrived = false;
        SetSpeed(lure ? lureSpeedShare : searchSpeedShare);
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        // Sent off while still getting up out of a pose (or straight out of one), it walks once it stands (Update).
        if (HoldsPose) GettingUp = true;
        agent.isStopped = GettingUp;
        agent.SetDestination(at);
    }

    // Back to standing calm. Glancing, it turns back the way it stood; after a search, where it ends up is its new post.
    private void Settle(bool newPost)
    {
        glanceUntil = 0f;
        lured = false;
        Mood = ZombieMood.Calm;
        SetSpeed(1f);
        Halt();
        if (newPost) Post();
        else lookYaw = postYaw;
        lookAt = Time.time + Mathf.Max(1f, lookAroundEvery) * Random.Range(.5f, 1f);
    }

    private void Halt()
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    private void SetSpeed(float share)
    {
        if (agent != null && chaseSpeed > 0f) agent.speed = chaseSpeed * share;
    }

    /// <summary>
    /// Its chase speed changed (its kind, or crawling: ZombieType). Searching walks a share of it, and a flinch slows
    /// from and returns to it.
    /// </summary>
    internal void SetChaseSpeed(float speed)
    {
        chaseSpeed = speed;
        if (TryGetComponent(out EnemyHitReaction reaction)) reaction.SetBaseSpeed(speed);
        SetSpeed(Mood == ZombieMood.Searching ? lured ? lureSpeedShare : searchSpeedShare : 1f);
    }

    /// <summary>A blow (EnemyHealth, where it thinks): it wakes at once, after whoever landed it.</summary>
    internal void Struck(in DamageHit hit)
    {
        if (!Thinks || IsAwake) return;
        Wake(hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null, false);
    }

    /// <summary>On another PC: the mood the host's zombie is in, how sure it is (twentieths) and of whom (client id + 1).</summary>
    internal void ShowMind(byte mood, byte step, ulong watch)
    {
        if (!IsCopy) return;
        Mood = (ZombieMood)mood;
        shownStep = step;
        shownWatch = watch;
    }

    // ---- Thinking ----------------------------------------------------------------------------------------------

    private void Update()
    {
        if (!Thinks) return;
        ComeTo();
        float dt = Time.deltaTime;
        // Held (a takedown, a grab), shoved or down, it turns to nothing: an alert one can be taken from behind now, and
        // turning in the executor's hands would pull the takedown apart.
        if (melee == null || !melee.IsStunned)
        {
            if (Mood == ZombieMood.Alert) TurnTo(stimulus, dt);
            else if (Mood == ZombieMood.Calm) LookRound(dt);
            else if (Mood == ZombieMood.Searching && arrived) Sweep(dt);
        }
        if (Mood == ZombieMood.Rousing || Mood == ZombieMood.Rising)
        {
            if (focus != null) TurnTo(focus.transform.position, dt);
            if (Time.time >= rouseUntil)
            {
                Mood = ZombieMood.Awake;
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
            }
        }
        // Getting up out of a pose it stays where it is, whatever it means to do next (a search sets off its walk at once,
        // and nothing else stops it). The frame it stands, a search walks on; the chase takes it itself (Ready).
        if (GettingUp) { Halt(); heldUp = true; }
        else if (heldUp)
        {
            heldUp = false;
            if (Mood == ZombieMood.Searching && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
        }
        if ((thinkLeft -= dt) > 0f) return;
        // Far from every player it thinks less often (ZombieLod); what it sees adds up over the longer step all the same.
        float step = ZombieLod.ThinkEvery(transform.position, ThinkEvery);
        thinkLeft += step;
        // A horde zombie needs no senses to hunt (EnemyTargeting gives it the nearest), but whether any zombie still has
        // someone in sight decides when the campaign lets the chase die down (LastNoticed, CampaignRun's lull). Once one
        // has seen someone very lately the others needn't cast their rays too (ZombieLod): they take that sighting.
        if (horde)
        {
            if (Mood != ZombieMood.Awake) return;
            if (!ZombieLod.HordeLooks(LastNoticed)) lastKnown = LastSeenAt;
            else if (Look(out _, out _) is PlayerMovement seen) { LastNoticed = Time.time; LastSeenAt = lastKnown = seen.transform.position; }
            return;
        }
        // Down, stunned or held it notices nothing new; a blow has woken it anyway.
        if (melee != null && melee.IsStunned) return;
        Think(step);
    }

    // Knocked down, it used to fight again the moment the get-up clip let it go, so one that had just risen grabbed a
    // player in the same instant (user, 2026-11-11 playtest: "완전히 일어나고 정신 차리고 잡아야지 ... 불공평함"). Now,
    // the frame its knockdown ends (the clip done, ZombieBody.Down's Stand), it rouses for riseRouse: it stands where it
    // got up, turns to the nearest player and plays its rouse on every PC, and only then chases and fights (Ready). A
    // legless crawler pushes straight into its crawl instead: the rouse is a standing clip.
    private void ComeTo()
    {
        bool down = melee != null && melee.IsKnockedDown;
        bool rose = wasDown && !down;
        wasDown = down;
        if (!rose || riseRouse <= 0f || Mood != ZombieMood.Awake) return;
        if (TryGetComponent(out ZombieType type) && type.Crawls) return;
        var at = Alive(focus) ? focus : PlayerRoster.Nearest(transform.position);
        Mood = ZombieMood.Rising;
        if (Alive(at)) focus = at;
        rouseUntil = Time.time + riseRouse;
        Halt();
    }
    private bool wasDown;

    private void Think(float dt)
    {
        var seen = Look(out float rate, out bool touching);
        if (IsAwake)
        {
            if (seen != null && (focus == null || seen == focus || !Alive(focus))) focus = seen;
            if (focus != null && seen == focus) { lastSeen = Time.time; LastSeenAt = lastKnown = focus.transform.position; LastNoticed = Time.time; }
            else if (Time.time - lastSeen > loseSeconds) { suspicion = alertAt; Search(lastKnown); }
            return;
        }
        if (seen != null)
        {
            suspicion += rate * dt;
            focus = noticed = seen;
            // Watching a stone, a glimpse doesn't turn its head: only once it is unsure of someone (alertAt) does it look
            // their way.
            if (!Distracted || suspicion >= alertAt) stimulus = seen.transform.position;
            if (touching) { Wake(seen); return; }
        }
        else suspicion = Mathf.Max(0f, suspicion - forget * dt);

        if (suspicion >= 1f) { Wake(focus); return; }
        // Sure enough to stop and stare: a glance becomes a real look, and one that made someone out forgets the stone.
        if (suspicion >= alertAt)
        {
            glanceUntil = 0f;
            if (seen != null) lured = false;
            Alert(stimulus);
            return;
        }
        if (Mood == ZombieMood.Alert)
        {
            // A glance at something it won't go to looks till its time is up and settles back; the stare at a stone ends
            // in a walk over to it.
            if (glanceUntil > 0f)
            {
                if (Time.time < glanceUntil) return;
                if (lured) Search(lureAt, true);
                else Settle(false);
                return;
            }
            Search(stimulus, lured);
            return;
        }
        if (Mood == ZombieMood.Searching) Searching();
        else if (Mood == ZombieMood.Calm && suspicion <= 0f) noticed = null;
    }

    private void Searching()
    {
        if (!arrived && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && !agent.pathPending
            && agent.remainingDistance <= ArriveDistance)
        {
            arrived = true;
            arrivedAt = Time.time;
            arriveYaw = transform.eulerAngles.y;
            Halt();
        }
        if ((arrived && Time.time - arrivedAt >= LookTime) || Time.time - searchStart >= searchSeconds + (lured ? lureLook : 0f)) Settle(true);
    }

    // How long it looks round where it came to search: longer where a stone fell.
    private float LookTime => lured ? lureLook : lookSeconds;

    private void TurnTo(Vector3 at, float dt)
    {
        Vector3 way = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
        if (way.sqrMagnitude < .01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(way), turnSpeed * dt);
    }

    // Standing calm it looks round now and then: a slow turn some way off the way it stood, and back again the next time,
    // so no stretch of the way is watched for good and gaps open and close (the stealth view's cones show it). Every
    // calm zombie used to stare one way forever, so a scattered few covered every way past (user, 2026-11-11: "얘들이
    // 각자 흩어져 있으니까 사방을 보고 있고").
    /// <summary>
    /// Its body sits, eats or crouches while calm (ZombieBody, ZombieLooks.calmPoses): it doesn't turn to look round,
    /// as a seated body spinning on the spot looks wrong. Set by ZombieBody on every PC; only the host's is read.
    /// </summary>
    internal bool HoldsPose { get; set; }

    private void LookRound(float dt)
    {
        if (HoldsPose || lookAroundEvery <= 0f || lookAroundAngle <= 0f || lookAroundSpeed <= 0f) return;
        if (Time.time >= lookAt)
        {
            lookAt = Time.time + lookAroundEvery * Random.Range(.6f, 1.4f);
            bool atPost = Mathf.Abs(Mathf.DeltaAngle(lookYaw, postYaw)) < 1f;
            lookYaw = atPost ? postYaw + Random.Range(-lookAroundAngle, lookAroundAngle) : postYaw;
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, lookYaw, 0f), lookAroundSpeed * dt);
    }

    // Come to where it searches, it looks left, then right, then back ahead over lookSeconds, not a statue's stare.
    private void Sweep(float dt)
    {
        float look = LookTime, sweep = lured ? lureSweep : searchSweep;
        if (sweep <= 0f || look <= 0f) return;
        float phase = Mathf.Clamp01((Time.time - arrivedAt) / look);
        float yaw = arriveYaw + Mathf.Sin(phase * Mathf.PI * 2f) * sweep;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, yaw, 0f), turnSpeed * .5f * dt);
    }

    // The player it notices fastest now, how fast (suspicion a second), and whether they stand right before its eyes.
    private PlayerMovement Look(out float best, out bool touching)
    {
        best = 0f;
        touching = false;
        PlayerMovement found = null;
        // Going to look at a stone it is curious, not alarmed: its calm eyes. Eating or dozing, it sees less (BusyRange).
        float sight = ViewRange;
        Vector3 eye = Eye();
        foreach (var player in PlayerRoster.All)
        {
            if (!Alive(player)) continue;
            Vector3 toward = player.transform.position - transform.position;
            bool lit = Lit(player, toward);
            // Someone standing in the dark is made out from less far and more slowly (StealthLight, the memo's item 47:
            // "어두운 곳일수록 덜 보이게"); a flashlight turned on it lights them whatever the dark.
            float light = lit ? 1f : StealthLight.Level(player);
            float range = sight * (lit ? lightRange : StealthLight.RangeShare(light));
            float distance = toward.magnitude;
            if (distance > range) continue;
            float angle = Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(toward, Vector3.up));
            float view = Angular(angle);
            if (view <= 0f) continue;
            float shown = Shown(player, eye);
            if (shown <= 0f) continue;
            // It hardly looks up: someone well above its eyes is seen at lookUpShare.
            float above = Mathf.Atan2(player.transform.position.y + 1f - eye.y, Mathf.Max(.01f, new Vector2(toward.x, toward.z).magnitude))
                * Mathf.Rad2Deg;
            float rate = nearRate * Mathf.Lerp(1f, farShare, distance / Mathf.Max(.01f, range)) * shown * Posture(player) * view
                * (above > lookUp ? lookUpShare : 1f) * (lit ? lightShare : StealthLight.SeenShare(light)) * BusyNotice;
            if (rate <= best) continue;
            best = rate;
            found = player;
            touching = distance <= touchRange && angle <= FrontView * .5f;
        }
        return found;
    }

    // How well it sees at this angle off its nose (degrees): fully straight ahead, a little less toward the edge of its
    // full view, then quickly down to sideShare, which holds across the edge of its sight and fades out at its very end.
    // It was a step from full to sideShare, so one degree either side of the line decided a great deal (Advanced
    // Stealth's peripheral glimpses, which gather suspicion much more slowly).
    private float Angular(float angle)
    {
        // Eating or dozing, both narrow (BusyView).
        float front = FrontView * .5f, edge = Mathf.Max(front + 1f, EdgeView * .5f);
        if (angle <= front) return Mathf.Lerp(1f, .85f, Mathf.Clamp01(angle / Mathf.Max(1f, front)));
        if (angle >= edge) return 0f;
        float t = (angle - front) / (edge - front);
        return Mathf.Lerp(.85f, sideShare, Mathf.SmoothStep(0f, 1f, t / .25f)) * (1f - Mathf.SmoothStep(0f, 1f, (t - .75f) / .25f))
            * (Distracted ? lureFocus : 1f);
    }

    // Their flashlight is on and turned this way: it makes them out sooner and from farther ("손전등 켜면 들킨다").
    private bool Lit(PlayerMovement player, Vector3 toward)
    {
        if (lightShare <= 1f && lightRange <= 1f) return false;
        var gun = GunOf(player);
        if (gun == null || !gun.LightOn) return false;
        Vector3 beam = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
        Vector3 back = Vector3.ProjectOnPlane(-toward, Vector3.up);
        return beam.sqrMagnitude > 1e-4f && back.sqrMagnitude > 1e-4f && Vector3.Angle(beam, back) <= lightAngle;
    }

    private static WeaponController GunOf(PlayerMovement player)
    {
        if (!guns.TryGetValue(player, out var gun) || gun == null) guns[player] = gun = player.GetComponentInChildren<WeaponController>();
        return gun;
    }

    // How much of them it can see: the share of head, chest and hips no wall hides, nor a Ghost's smoke (SmokeScreen).
    private static float Shown(PlayerMovement player, Vector3 eye)
    {
        var capsule = player.GetComponent<CharacterController>();
        Bounds bounds = capsule != null && capsule.enabled ? capsule.bounds : new Bounds(player.transform.position + Vector3.up * .9f, new Vector3(.6f, 1.8f, .6f));
        int seen = 0;
        foreach (float height in LookHeights)
        {
            Vector3 point = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * height, bounds.center.z);
            Vector3 path = point - eye;
            float length = path.magnitude;
            if ((length < .01f || !Physics.Raycast(eye, path / length, out RaycastHit hit, length, Sight, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<PlayerMovement>() == player) && !SmokeScreen.Hides(eye, point))
                seen++;
        }
        return seen / (float)LookHeights.Length;
    }

    // Crouched or lying down they are harder to make out; moving, easier, and a sprint most of all.
    private float Posture(PlayerMovement player)
    {
        float share = 1f;
        var state = player.CurrentState;
        if (player.IsProning || state == PlayerMovement.MovementState.Supine || state == PlayerMovement.MovementState.Sliding
            || state == PlayerMovement.MovementState.HeadFirstSliding || state == PlayerMovement.MovementState.SupineSliding)
            share = lyingShare;
        // A dodge made from a crouch stays low (IsCrouchStance): crouched, and no sprint for its burst of speed, so a quick
        // crouched dodge in behind one is seen as sneaking (user, 2026-11-12).
        bool low = player.IsCrouchStance;
        if (low && share > crouchShare) share = crouchShare;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        float motion = speed >= sprintSpeed && !low ? sprintShare : Mathf.Lerp(stillShare, 1f, Mathf.Clamp01(speed / 1.5f));
        return share * motion;
    }

    private Vector3 Eye()
    {
        var head = body != null && body.Model != null ? body.Head : null;
        return head != null ? head.position : transform.position + Vector3.up * .7f;
    }

    // Walls and the world hide players (maps build them on Default); other zombies don't.
    private static int Sight => sightMask != 0 ? sightMask : sightMask = ~LayerMask.GetMask(EnemyLayer);
    // Worked out once: GetMask allocates, and this was asked three times a player every think of every zombie.
    private static int sightMask;
    // Sound goes round players, corpses and what lies on the floor (layer 31): only the world muffles it.
    private static int Hearing => Sight & ~(1 << 31);

    private static bool Alive(PlayerMovement player)
        => player != null && player.IsInitialized && !player.IsDead && player.DamageReceiver != null && player.DamageReceiver.IsAlive;

    // ---- Hearing -------------------------------------------------------------------------------------------------

    private static void Hear(NoiseEvent noise)
    {
        if (noise.Lure) { HearLure(noise); return; }
        var source = noise.Source != null ? noise.Source.GetComponentInParent<PlayerMovement>() : null;
        if (source == null || !Alive(source)) return;
        foreach (var mind in minds) if (mind != null && mind.Thinks && !mind.horde) mind.Heard(noise, source);
    }

    // A thrown stone landing (PlayerThrow): every one not awake that hears it turns to watch where it fell, its mind on it
    // (lureGlance, lureFocus), and the nearest then goes over to look. It never says where the thrower is, nor wakes
    // anyone, nor marks anyone on the thrower's stealth HUD: a zombie facing the way past is turned off it, which is what
    // the stone is for (user, 2026-11-11: "좀비가 내 쪽을 바라보고 있으면 접근 조차 불가능 ... 돌로 시선을 그쪽으로
    // 자연스럽게 옮기고 싶은데"). Advanced Stealth: "The nearest guard goes to check the object; others turn to look".
    private static void HearLure(in NoiseEvent noise)
    {
        ZombieMind nearest = null;
        float best = float.MaxValue;
        foreach (var mind in minds)
        {
            if (mind == null || !mind.Thinks || mind.horde || mind.IsAwake) continue;
            if (mind.melee != null && mind.melee.IsStunned) continue;
            if (!mind.Reaches(noise, out float distance, out _)) continue;
            if (distance < best) { best = distance; nearest = mind; }
            mind.distractedUntil = Time.time + mind.lureGlance;
            mind.Glance(noise.Point, null, mind.lureGlance);
        }
        if (nearest != null) nearest.Investigate(noise.Point, null);
    }

    /// <summary>For the throw's aim (StealthView): whether this zombie, not awake, would hear this sound now.</summary>
    internal bool Hears(in NoiseEvent noise) => !IsAwake && Living && Reaches(noise, out _, out _);

    private void Heard(in NoiseEvent noise, PlayerMovement source)
    {
        // A crouched player's small sounds (a sneaking step, crouching, a lean) don't carry to it: crouched steps alerted
        // a zombie a step behind it, which then turned, saw them that close and woke (user, 2026-11-11: "앉아서 가도 금방
        // 눈치챈다"). Advanced Stealth silences crouched footsteps the same way. A dodge made from a crouch is crouched too
        // (IsCrouchStance), and its sound is a crouched one (SoundCues DodgeLow).
        if (source.IsCrouchStance && noise.Radius <= sneakSilent) return;
        if (!Reaches(noise, out float distance, out float reach)) return;
        float loudness = 1f - distance / Mathf.Max(.01f, reach);
        if (IsAwake)
        {
            // Its quarry gives itself away.
            if (source == focus) { lastSeen = Time.time; lastKnown = source.transform.position; }
            return;
        }
        if (melee != null && melee.IsStunned) return;
        float gained = hearGain * loudness;
        suspicion += gained;
        focus = noticed = source;
        stimulus = noise.Point;
        if (suspicion >= 1f) Wake(source);
        else if (suspicion >= alertAt) { glanceUntil = 0f; Alert(noise.Point); }
        // Too faint to stop for, it still turns its head that way a moment, and may see who made it.
        else if (gained >= glanceAt) Glance(noise.Point, source, glanceSeconds);
    }

    // Whether this sound reaches its ears, how far off it is and how far it carries to it (m): half as far through a wall.
    private bool Reaches(in NoiseEvent noise, out float distance, out float reach)
    {
        // Eating or dozing it hears less, a stone or a knock too (BusyHear): asleep it may sleep through one.
        reach = noise.Radius * (noise.Lure ? lureHearShare : Mood == ZombieMood.Calm ? calmHearShare : hearShare) * BusyHear;
        Vector3 ear = Eye();
        distance = Vector3.Distance(noise.Point, ear);
        if (distance > reach) return false;
        Vector3 path = ear - noise.Point;
        if (distance > .01f && Physics.Raycast(noise.Point, path / distance, distance, Hearing, QueryTriggerInteraction.Ignore))
        {
            reach *= wallShare;
            if (distance > reach) return false;
        }
        return true;
    }

    /// <summary>
    /// For the stealth view (StealthView): how far a sound of the local player's carries to a calm zombie in the open (m),
    /// and whether any zombie that isn't awake hears it now. 0 when it doesn't carry at all (a crouched player's small
    /// sounds), or with no zombie about.
    /// </summary>
    internal static float CalmReach(in NoiseEvent noise, PlayerMovement source, out bool heard)
    {
        heard = false;
        ZombieMind any = null;
        foreach (var mind in minds) if (mind != null) { any = mind; break; }
        if (any == null) return 0f;
        if (!noise.Lure && source != null && source.IsCrouchStance && noise.Radius <= any.sneakSilent) return 0f;
        foreach (var mind in minds)
        {
            if (mind == null || mind.IsAwake || mind.health == null || !mind.health.IsAlive) continue;
            if (mind.Reaches(noise, out _, out _)) { heard = true; break; }
        }
        return noise.Radius * (noise.Lure ? any.lureHearShare : any.calmHearShare);
    }
}
