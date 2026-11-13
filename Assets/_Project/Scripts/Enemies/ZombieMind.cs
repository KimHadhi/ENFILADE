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
public sealed partial class ZombieMind : MonoBehaviour
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
    [SerializeField, Min(0f), Tooltip("Its call, a roar, carries this far (m): heard as any sound is (one calm hears calmHearShare of it, a wall halves it), and whoever hears it wakes.")]
    private float callRadius = 20f;
    [SerializeField, Min(0f), Tooltip("Woken on its own, it draws breath this long before it calls the others (s): killed, held, stunned or knocked down meanwhile, nobody else is told. 0: it calls at once.")]
    private float callTell = 1.2f;
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
    /// counts its alarm from it (CampaignRun: the horde comes only if the alarm lasts).
    /// </summary>
    public static event System.Action<ZombieMind, PlayerMovement> Woke;
    /// <summary>On the host or alone: a zombie called the others (its roar went out), and whom it was after.</summary>
    public static event System.Action<ZombieMind, PlayerMovement> Called;

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
    // Drawing breath to call the others and when it calls (the host's); on a copy, the host's word and when it came.
    private bool calling, shownCalling;
    private float callAt, shownCallSince;

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
}
