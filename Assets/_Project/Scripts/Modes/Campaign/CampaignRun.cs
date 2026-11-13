using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Multiplayer plan stage 5B: a campaign chapter played as a run of objectives. The chapter's steps (Tables/
/// CampaignSteps, rows for this scene) go one after another on the mission HUD: get somewhere, press something, hold out,
/// reach a rest stop, escape. Each step sets how hard the horde comes (CampaignHorde). The chapter opens and closes on its
/// story card (Tables/CampaignChapters, CampaignScreen); once everyone has escaped, the next chapter's scene loads, for the
/// whole room through the host (GameSession.ChangeMap).
/// The host, or the PC alone, runs it: it checks where everyone is, counts the holds, makes the enemies and tells the
/// others where the run stands (a named message, also every couple of seconds for anyone who missed one). A guest's
/// press goes to the host. The dead come back at the last rest stop reached (PlayerLife through TryCheckpoint), and a
/// wipe sends the run back to the step after it, with every fuel can back in place. A Collect step counts the cans
/// poured at its spot (CampaignCans), and [F] picks up, puts down and pours them; a step's gate opens by its progress
/// (CampaignGate). The builder (Editor/Maps/GreyboxBuilder) puts one in each chapter's
/// scene with its spots and spawn points. Numbers: Tables/Campaign.
/// Stage C-15, stealth (user, 2026-11-10: no horde until woken, calm zombies in the maps): a chapter opens quiet, and
/// so does every stretch after a rest stop. Quiet, the director sends no one; zombies stand calm along the way ahead
/// instead (CampaignHorde.PlaceCalm, the steps' own numbers by calmShare), and can be slipped past or taken from
/// behind. One of them noticing someone no longer ends it (user, 2026-11-12: "한번 들켰다고 바로 모든 놈들이 나한테 오지
/// 말고 들켜도 들킨 놈들 빠르게 싹 다 처치하면 조용히 넘어갈 수 있었으면 좋겠어"): the alarm is the woken ones' own (ZombieMind:
/// a witness draws breath before it calls, and its call wakes only those that hear it), and the quiet ends only when
/// that alarm lasts alarmSeconds, which every PC is told at the top of the screen, or when a hold or the escape begins;
/// from then on the horde comes as before. An alarm put out in time (the woken dead, or lost) leaves the stretch quiet,
/// unseen bonus and all, and every PC hears "들키지 않음".
/// Stage C-14, a finale as long as its song (the user's answer, 2026-11-10: the song plays whole, and its end is when
/// the ride leaves; whoever misses it is left behind): under a finale's song (Music.board) the hold lasts the song
/// less the board time, then the ride waits at the escape's spot for the board time and leaves as the song ends, with
/// whoever stands there. Those who don't are named on the closing card; with nobody aboard the run goes back to the
/// last rest stop. Without the song's file the steps run as they did (the hold's own seconds, everyone to the spot).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Campaign)]
public sealed partial class CampaignRun : MonoBehaviour
{
    public const string TableName = "Campaign", TableRow = "Default", DirectorRow = "Director", StartSpot = "Start";
    private const string StatusMessage = "ENFILADE.Campaign", PressMessage = "ENFILADE.CampaignPress", DoorMessage = "ENFILADE.CampaignDoor",
        CanShotMessage = "ENFILADE.CanShot";
    // How near the doorway someone must be to shut or open a rest stop's door, and a counter to shop (m).
    private const float DoorReach = 2.2f, ShopReach = 2f;
    // The run is told again this often even when nothing changed, so a guest who missed a message catches up (s).
    private const float ShareEvery = 2f;

    private struct Status : INetworkSerializeByMemcpy, System.IEquatable<Status>
    {
        public byte Step, Finished;
        /// <summary>The last rest stop's step plus one; 0 is the chapter's start.</summary>
        public byte Checkpoint;
        /// <summary>Whole seconds a hold has lasted.</summary>
        public ushort Held;
        /// <summary>Which rest-stop doors are shut, a bit each (CampaignRun.doors).</summary>
        public byte Doors;
        /// <summary>1 while the stretch is quiet: no horde until a zombie notices someone.</summary>
        public byte Quiet;
        /// <summary>How many quiet stretches the room got through unseen this chapter: each PC pays its player as it rises.</summary>
        public byte Unseen;
        /// <summary>1 while a quiet stretch has a local alarm (zombies woken by stealth still after someone).</summary>
        public byte Alarm;
        public bool Equals(Status other) => Step == other.Step && Finished == other.Finished && Checkpoint == other.Checkpoint
            && Held == other.Held && Doors == other.Doors && Quiet == other.Quiet && Unseen == other.Unseen && Alarm == other.Alarm;
    }

    [SerializeField, Tooltip("What the hordes are made of.")]
    private EnemyHealth enemyPrefab;
    [SerializeField, Tooltip("The named spots the steps go to (CampaignSteps.spot); 'Start' is where the dead come back before any rest stop.")]
    private Transform[] spots;
    [SerializeField, Tooltip("Where enemies may come from.")]
    private Transform[] spawns;
    [SerializeField, Tooltip("The fuel cans a Collect step wants, where they lie at the start (CampaignCans).")]
    private Transform[] cans;
    [SerializeField, Tooltip("The rest stops' doors (CampaignDoor), at most eight.")]
    private CampaignDoor[] doors;
    [SerializeField, Tooltip("Where the rest stops' shop counters stand (ShopMenu opens there).")]
    private Transform[] shops;
    [SerializeField, Tooltip("A shot fuel can's blast sound (C17; the builder sets it). The blast and its fire are drawn by CanBlastEffect.")]
    private AudioClip canBlastSound;
    [SerializeField, Min(0f), Tooltip("Enemies never appear nearer than this to anyone (m).")]
    private float minSpawnDistance = 15f;
    [SerializeField, Min(0f), Tooltip("Nor farther than this from the player they head for (m).")]
    private float maxSpawnDistance = 60f;
    [SerializeField, Min(0f), Tooltip("A spawn point in someone's sight is used only this far away, and only when no hidden one will do (m).")]
    private float seenMinDistance = 30f;
    [SerializeField, Tooltip("How the director paces the hordes (Tables/Campaign, row Director).")]
    private CampaignDirector.Tuning director = new CampaignDirector.Tuning();
    [SerializeField, Min(0f), Tooltip("How long the opening story card stays (s).")]
    private float introSeconds = 9f;
    [SerializeField, Min(0f), Tooltip("How long the closing card stays before the next chapter loads (s).")]
    private float outroSeconds = 10f;
    [SerializeField, Tooltip("A chapter, and every stretch after a rest stop, opens quiet: no horde until a zombie notices someone or a hold begins. Off: the horde comes from the first moment, as it used to.")]
    private bool quietStart = true;
    [SerializeField, Min(0f), Tooltip("Found, then no zombie has seen anyone this long (s): the chase dies down. The director stops "
        + "and every zombie goes to look where it last knew them, then calms. 0: once found, found for good.")]
    private float lullSeconds = 20f;
    [SerializeField, Min(0f), Tooltip("Quiet, zombies woken by stealth may be after someone this long (s) and the stretch stays quiet; past it, the horde "
        + "comes. Kill the witnesses or lose them sooner and nobody else comes. 0: the first one woken brings the horde, as it used to.")]
    private float alarmSeconds = 20f;
    [SerializeField, Min(0f), Tooltip("Zombies set calm along the way of a quiet stretch: each step's horde number times this.")]
    private float calmShare = 1f;
    [SerializeField, Min(0f), Tooltip("No calm zombie is set nearer than this to a player (m).")]
    private float calmNear = 14f;

    private static CampaignRun active;
    private readonly Dictionary<string, Transform> spotsByName = new Dictionary<string, Transform>();
    private readonly List<PlayerMovement> players = new List<PlayerMovement>();
    private readonly HashSet<string> reported = new HashSet<string>();
    private List<CampaignStep> steps = new List<CampaignStep>();
    private CampaignChapter chapter;
    private CampaignHorde horde;
    private CampaignDirector pacer;
    private CampaignCans fuel;
    private CampaignMusic music;
    private CampaignScreen screen;
    private MissionTracker tracker;
    private NetworkManager network;
    private InputAction interact;
    private Status sent;
    private int step, checkpoint = -1, shownPoured = -1, brutesDue;
    private float held, finishedAt, nextShare, nextBruteTry;
    private bool finished, movedOn, wasWiped, listening;
    // The stretch is quiet (the host's word, shared); this PC last saw it so; the host still owes the stretch its calm zombies.
    private bool quiet, wasQuiet, calmDue;
    // The quiet came back after they were found (a lull, not the stretch's own start), and when they were last found.
    private bool lull;
    // The stealth round's reward (StealthScore.Unseen): this stretch hasn't been found yet, how many calm zombies it was
    // given, how many stretches went unseen this chapter, and whether a guest has heard the host's count yet.
    private bool unseen, unseenKnown;
    private int calmMade;
    private byte unseenCount;
    private float noticedAt = float.NegativeInfinity;
    // A quiet stretch's local alarm (ZombieMind.AlarmUp): since when (the host's; -1 none), and up now and last seen so on
    // this PC (the host's word, shared).
    private float alarmSince = -1f;
    private bool alarm, wasAlarm;
    // A hold with no song has sent its brutes; who the ride left behind, for the closing card.
    private bool songlessSent;
    private string leftBehind = "";
    // The closing card waits for the escape's banner (ChapterEndBanner): when it comes up (real s; negative, none due),
    // and what the game did to the class, for the insignia beside it.
    private const float BannerSeconds = 2.8f, CardOverBanner = .5f;
    private float cardAt = -1f, nextResults;
    private ClassGrowth.Climb climb;

    private bool IsHost => network == null || network.IsServer;
    private CampaignStep Current => step >= 0 && step < steps.Count ? steps[step] : null;
    // Its [F] has something on offer this frame (a step's button, a can, a door, a shop counter).
    private bool offering;

    /// <summary>The chapter's [F] has something on offer now: F does that, never a ping (PlayerPing).</summary>
    public static bool Offering => active != null && active.offering;

    /// <summary>Where the dead come back in a campaign chapter: the last rest stop reached, or the chapter's start.
    /// False outside a chapter, or when the chapter has no such spot.</summary>
    public static bool TryCheckpoint(out Vector3 position)
    {
        position = default;
        if (active == null) return false;
        var spot = active.SpotOf(active.checkpoint >= 0 && active.checkpoint < active.steps.Count ? active.steps[active.checkpoint].Spot : StartSpot);
        if (spot == null) return false;
        position = spot.position;
        return true;
    }

    /// <summary>
    /// Where the step under way wants the players: its spot, how near counts as there (m) and what kind of step it is,
    /// for the HUD's guide (ObjectiveGuideHUD). False outside a chapter, once the chapter is over, and for a step whose
    /// spot the scene lacks.
    /// </summary>
    public static bool TryObjective(out Transform spot, out float radius, out CampaignStepKind kind)
    {
        spot = null; radius = 0f; kind = default;
        if (active == null || active.finished) return false;
        var now = active.Current;
        // Not SpotOf: a missing spot was reported once already, and this asks every frame.
        if (now == null || string.IsNullOrEmpty(now.Spot) || !active.spotsByName.TryGetValue(now.Spot, out spot) || spot == null) return false;
        radius = now.Radius;
        kind = now.Kind;
        return true;
    }

    /// <summary>How far a step has come: 0 before it, 1 once it is done (or outside a chapter, or for an unknown step),
    /// and while it runs a hold's share of its time (0 for other kinds). What CampaignGate opens by.</summary>
    public static float ProgressOf(string stepId)
    {
        if (active == null) return 1f;
        int index = active.IndexOf(stepId);
        if (index < 0 || active.finished || active.step > index) return 1f;
        if (active.step < index) return 0f;
        var now = active.steps[index];
        float total = now.Kind == CampaignStepKind.Hold ? active.TotalOf(now) : 0f;
        return total > 0f ? Mathf.Clamp01(active.held / total) : 0f;
    }

    /// <summary>
    /// A chapter's assault is under way, a hold or a finale's escape: how far into it and how long it is (s), for the
    /// objective bar's assault look (AssaultBar), which plays it like a song.
    /// </summary>
    public static bool TryAssault(out float elapsed, out float length)
    {
        elapsed = length = 0f;
        if (active == null || active.finished) return false;
        var now = active.Current;
        if (now == null || (now.Kind != CampaignStepKind.Hold && now.Kind != CampaignStepKind.Escape)) return false;
        length = active.TotalOf(now);
        if (length <= 0f) return false;
        elapsed = Mathf.Min(active.held, length);
        return true;
    }

    /// <summary>The run is at this step now.</summary>
    public static bool IsRunning(string stepId)
        => active != null && !active.finished && active.Current != null && active.IndexOf(stepId) == active.step;

    private int IndexOf(string stepId)
        => steps.FindIndex(candidate => string.Equals(candidate.Id, stepId, System.StringComparison.OrdinalIgnoreCase));

    private void Awake()
    {
        active = this;
        GameTables.Bind(TableName, TableRow, this);
        GameTables.BindNested(TableName, DirectorRow, this, director);
        pacer = new CampaignDirector(director);
        if (doors == null) doors = new CampaignDoor[0];
        if (shops == null) shops = new Transform[0];
        if (spots != null) foreach (var spot in spots) if (spot != null) spotsByName[spot.name] = spot;
        string scene = gameObject.scene.name;
        steps = CampaignStep.For(scene);
        chapter = CampaignChapter.For(scene);
        horde = new CampaignHorde(enemyPrefab, spawns, gameObject.scene);
        fuel = new CampaignCans(cans);
        fuel.Dress(canBlastSound);
        music = new CampaignMusic(gameObject);
        if (steps.Count == 0) Debug.LogWarning("[Campaign] Tables/CampaignSteps has no steps for " + scene + ".", this);
    }

    private void Start()
    {
        tracker = FindFirstObjectByType<MissionTracker>();
        screen = gameObject.AddComponent<CampaignScreen>();
        interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact", false) : null;
        Listen();
        if (chapter != null) screen.ShowCard(chapter.Title, chapter.Intro, introSeconds);
        ShowObjective(true);
        ZombieMind.Woke += OnWoke;
        if (IsHost) BeginQuiet();
    }

    private void OnDestroy()
    {
        ZombieMind.Woke -= OnWoke;
        if (active == this) active = null;
        if (!listening || network == null || network.CustomMessagingManager == null) return;
        var messages = network.CustomMessagingManager;
        messages.UnregisterNamedMessageHandler(network.IsServer ? PressMessage : StatusMessage);
        messages.UnregisterNamedMessageHandler(network.IsServer ? CampaignCans.AskMessage : CampaignCans.StateMessage);
        if (network.IsServer) { messages.UnregisterNamedMessageHandler(DoorMessage); messages.UnregisterNamedMessageHandler(CanShotMessage); }
    }

    private void Listen()
    {
        network = GameSession.Network;
        if (network == null || network.CustomMessagingManager == null) return;
        listening = true;
        var messages = network.CustomMessagingManager;
        if (network.IsServer)
        {
            messages.RegisterNamedMessageHandler(PressMessage, ReceivePress);
            messages.RegisterNamedMessageHandler(CampaignCans.AskMessage, (sender, reader) => fuel.ReceiveAsk(sender, reader, MayPour));
            messages.RegisterNamedMessageHandler(DoorMessage, ReceiveDoor);
            messages.RegisterNamedMessageHandler(CanShotMessage, ReceiveCanShot);
        }
        else
        {
            messages.RegisterNamedMessageHandler(StatusMessage, ReceiveStatus);
            messages.RegisterNamedMessageHandler(CampaignCans.StateMessage, (sender, reader) => fuel.ReceiveState(reader));
        }
    }

    // Fuel goes in only while a Collect step wants it, and only at its spot.
    private bool MayPour(PlayerMovement who)
    {
        var now = Current;
        return !finished && now != null && now.Kind == CampaignStepKind.Collect && Within(who, now);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        GatherPlayers();
        WatchForWipe();
        if (IsHost) { fuel.Tick(dt); Run(dt); }
        WatchQuiet();
        WatchAlarm();
        ShowCardWhenDue();
        PlayMusic();
        ThrowCan();
        Prompt();
        ShowPoured();
        if (IsHost) { Share(); fuel.Share(network); }
    }

    private void LateUpdate()
    {
        var mine = PlayerRoster.Local;
        fuel.Place(mine, mine != null && mine.TryGetComponent(out PlayerBody body) ? body.View : null);
    }

    private bool AnyoneAt(CampaignStep at)
    {
        if (SpotOf(at.Spot) == null) return true;
        foreach (var someone in players) if (Within(someone, at)) return true;
        return false;
    }

    private bool EveryoneAt(CampaignStep at)
    {
        if (SpotOf(at.Spot) == null) return true;
        if (players.Count == 0) return false;
        foreach (var someone in players) if (!Within(someone, at)) return false;
        return true;
    }

    private bool Within(PlayerMovement someone, CampaignStep at)
    {
        var spot = SpotOf(at.Spot);
        return spot != null && (someone.transform.position - spot.position).sqrMagnitude <= at.Radius * at.Radius;
    }

    // A step whose spot the scene lacks is skipped (said once), so a typo can't stall the run.
    private Transform SpotOf(string name)
    {
        if (!string.IsNullOrEmpty(name) && spotsByName.TryGetValue(name, out var spot) && spot != null) return spot;
        if (reported.Add(name ?? "")) Debug.LogWarning("[Campaign] the scene has no spot named '" + name + "' under 진행 자리.", this);
        return null;
    }
}
