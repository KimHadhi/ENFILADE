using System.Collections.Generic;
using Unity.Collections;
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
public sealed class CampaignRun : MonoBehaviour
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

    // A quiet stretch begins (the chapter, or the way on from a rest stop): no horde, and calm zombies owed to the way ahead.
    private void BeginQuiet()
    {
        quiet = wasQuiet = calmDue = quietStart;
        lull = false;
        unseen = quietStart;
        alarm = wasAlarm = false;
        alarmSince = -1f;
        calmMade = 0;
    }

    // One of them has noticed someone. Quiet, the alarm is theirs and is watched from now (Alarm); with alarmSeconds 0 the
    // horde is on its way at once, as it used to be.
    private void OnWoke(ZombieMind mind, PlayerMovement who)
    {
        if (!IsHost || finished) return;
        if (quiet && alarmSeconds > 0f) { if (alarmSince < 0f) alarmSince = Time.time; return; }
        quiet = lull = unseen = false;
        noticedAt = Time.time;
    }

    // The host, quiet: a local alarm lasting alarmSeconds ends the quiet (the horde comes, the stretch is no longer unseen);
    // one put out sooner (no zombie woken by stealth still after anyone: the witnesses dead, or lost and gone searching)
    // leaves it quiet.
    private void Alarm()
    {
        if (!quiet || alarmSeconds <= 0f) { alarm = false; alarmSince = -1f; return; }
        bool up = ZombieMind.AlarmUp;
        if (up && alarmSince < 0f) alarmSince = Time.time;
        if (up && Time.time - alarmSince >= alarmSeconds)
        {
            quiet = lull = unseen = alarm = false;
            alarmSince = -1f;
            noticedAt = Time.time;
            return;
        }
        if (!up) alarmSince = -1f;
        alarm = up;
    }

    // The way ahead of a quiet stretch, up to the next hold, escape or rest stop: each step's share of calm zombies between
    // the place before it and its own.
    private void PlaceCalmAhead()
    {
        Vector3 from = players[0].transform.position;
        for (int i = step; i < steps.Count; i++)
        {
            var ahead = steps[i];
            if (ahead.Kind == CampaignStepKind.Hold || ahead.Kind == CampaignStepKind.Escape) break;
            var spot = SpotOf(ahead.Spot);
            if (spot == null) continue;
            calmMade += horde.PlaceCalm(Mathf.RoundToInt(ahead.Horde * calmShare), from, spot.position, players, calmNear);
            from = spot.position;
            if (ahead.Kind == CampaignStepKind.Checkpoint) break;
        }
    }

    // Every PC: the quiet ended because they were noticed. A hold or the escape beginning says so itself.
    private void WatchQuiet()
    {
        if (quiet == wasQuiet) return;
        var now = Current;
        bool noticed = wasQuiet && !finished && now != null && now.Kind != CampaignStepKind.Hold && now.Kind != CampaignStepKind.Escape;
        wasQuiet = quiet;
        if (noticed) AlertHUD.Danger(Texts.Get("campaign.alert.noticed"));
        else if (quiet && lull && !finished) AlertHUD.Self(Texts.Get("campaign.alert.lull"));
    }

    // Every PC: a quiet stretch's alarm put out in time, the witnesses dead or lost; the stretch stays quiet.
    private void WatchAlarm()
    {
        if (alarm == wasAlarm) return;
        bool cleared = wasAlarm && quiet && !finished;
        wasAlarm = alarm;
        if (cleared) AlertHUD.Self(Texts.Get("stealth.clear"));
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

    // Who counts: the players of this room (this PC's and the others'), not test dummies, and not the dead.
    private void GatherPlayers()
    {
        players.Clear();
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.IsInitialized && !someone.IsDead && (someone.IsLocal || someone.IsNetworkDriven))
                players.Add(someone);
    }

    // Every PC sees its own wipe (PlayerLife); the host sends the run back to after the last rest stop at once, so
    // everyone comes back there when the wipe ends.
    private void WatchForWipe()
    {
        var mine = PlayerRoster.Local;
        bool wiped = mine != null && mine.TryGetComponent(out PlayerLife life) && life.IsWiped;
        if (wiped && !wasWiped && IsHost) BackToCheckpoint();
        wasWiped = wiped;
    }

    private void Run(float dt)
    {
        if (finished)
        {
            if (!movedOn && Time.time - finishedAt >= outroSeconds) MoveOn();
            return;
        }
        var now = Current;
        if (now == null) return;
        bool scripted = now.Kind == CampaignStepKind.Hold || now.Kind == CampaignStepKind.Escape;
        // A hold or the escape is a crescendo whatever was noticed. Quiet, nobody is sent, and the pacing starts afresh
        // when it ends, as after a rest stop.
        if (scripted) quiet = calmDue = lull = false;
        Alarm();
        // The chase dies down: found once, but no zombie has had anyone in sight for lullSeconds (they shut themselves in a
        // rest stop, or slipped away), so the director stops and the zombies go looking, then calm (ZombieMind.LoseAll).
        // Found was found for good before (user, 2026-11-11 playtest).
        if (IsHost && !quiet && !scripted && lullSeconds > 0f && Time.time - noticedAt >= lullSeconds
            && Time.time - ZombieMind.LastNoticed >= lullSeconds)
        {
            quiet = lull = true;
            ZombieMind.LoseAll();
        }
        if (calmDue && players.Count > 0) { calmDue = false; PlaceCalmAhead(); }
        pacer.Pace(dt, players, scripted, quiet || Resting(), now.Horde, now.Rate, out int most, out float rate);
        pacer.Spawned(horde.Tick(dt, most, rate, players, minSpawnDistance, maxSpawnDistance, seenMinDistance));
        switch (now.Kind)
        {
            case CampaignStepKind.Reach:
                if (AnyoneAt(now)) Advance();
                break;
            case CampaignStepKind.Checkpoint:
                if (EveryoneAt(now) && DoorsShut(now.Spot)) Advance();
                break;
            case CampaignStepKind.Gather:
                if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Escape:
                if (CampaignMusic.TryFinale(now.Music, out float holdBefore, out float board))
                {
                    // The ride waits out the song and leaves as it ends, with whoever stands on it.
                    held = SongTime(now, dt, holdBefore);
                    if (held >= board) Depart(now);
                    else ShowHeld(now);
                }
                else if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Hold:
                // A manned hold (chapter 4's tug) counts only while someone stands at its spot, and pauses otherwise.
                held = now.Manned ? held + (AnyoneAt(now) ? dt : 0f) : SongTime(now, dt, 0f);
                if (held >= TotalOf(now)) Advance();
                else ShowHeld(now);
                break;
            case CampaignStepKind.Collect:
                if (fuel.Poured >= Mathf.Max(1, now.Count)) Advance();
                break;
        }
        if (!finished && Current == now) SkipAhead();
    }

    // How long a hold lasts, or a finale's escape waits: under a finale's song, by the song (CampaignMusic.TryFinale);
    // otherwise the hold's own seconds, and no time at all for an escape (0).
    private float TotalOf(CampaignStep at)
    {
        bool finale = CampaignMusic.TryFinale(at.Music, out float hold, out float board);
        return at.Kind == CampaignStepKind.Escape ? (finale ? board : 0f) : finale ? hold : at.Seconds;
    }

    // How far into its time a step is. Under a finale's song that this PC is playing, the song's own clock says, less
    // what of the song came before the step: the song runs in real time, and a slowed moment of the game must not
    // leave the ride waiting after its last note. Otherwise the game's clock.
    private float SongTime(CampaignStep now, float dt, float before)
    {
        bool bySong = !string.IsNullOrEmpty(now.Music) && string.Equals(music.Playing, now.Music, System.StringComparison.OrdinalIgnoreCase)
            && CampaignMusic.TryFinale(now.Music, out _, out _);
        return bySong ? Mathf.Max(held, music.Clock - before) : held + dt;
    }

    // The song is over and the ride goes. Whoever stands on it leaves with it and the chapter is done (ShowFinish names
    // the rest); with nobody aboard, the run goes back to the last rest stop.
    private void Depart(CampaignStep now)
    {
        foreach (var someone in players)
            if (Within(someone, now)) { Advance(); return; }
        MissedRide();
        BackToCheckpoint();
    }

    private static void MissedRide() => AlertHUD.Danger(Texts.Get("campaign.alert.ride"));

    // Through unseen: the points, the words and three bells.
    private static void ShowUnseen()
    {
        CombatFeedHUD.Award(Texts.Get("feed.score.unseen"), StealthScore.Unseen);
        AlertHUD.Self(Texts.Get("campaign.alert.unseen"));
        KitSound.Play(KitSound.Cue.Unseen, .8f);
    }

    // Friend feedback C4 ("일부러 지나친 곳 다시 가기 싫어"): a later step met first finishes every step before it, as long
    // as each step on the way is only getting somewhere, pressing something or a rest stop. A hold, a collect and the
    // escape always play out, and nothing is skipped past them. A rest stop passed on the way still becomes the place
    // the dead come back. Pressing a later button goes through Prompt.
    private void SkipAhead()
    {
        for (int later = step + 1; later < steps.Count; later++)
        {
            if (!Passable(steps[later - 1].Kind)) return;
            var ahead = steps[later];
            if (SpotOf(ahead.Spot) == null) continue;
            bool met = ahead.Kind == CampaignStepKind.Reach ? AnyoneAt(ahead)
                : ahead.Kind == CampaignStepKind.Gather ? EveryoneAt(ahead)
                : ahead.Kind == CampaignStepKind.Checkpoint && EveryoneAt(ahead) && DoorsShut(ahead.Spot);
            if (met) { AdvanceTo(later + 1); return; }
        }
    }

    private static bool Passable(CampaignStepKind kind) => kind == CampaignStepKind.Reach || kind == CampaignStepKind.Press
        || kind == CampaignStepKind.Gather || kind == CampaignStepKind.Checkpoint;

    // Every step from the current one up to this one can be passed by (SkipAhead).
    private bool PassableUpTo(int index)
    {
        if (index < step || index >= steps.Count) return false;
        for (int i = step; i < index; i++) if (!Passable(steps[i].Kind)) return false;
        return true;
    }

    // The steps passed by go into the log one after another; only the step landed on is announced.
    private void AdvanceTo(int next)
    {
        while (!finished && step < next) Advance(step + 1 >= next);
    }

    private void Advance(bool announce = true)
    {
        var done = Current;
        if (done == null) return;
        if (done.Kind == CampaignStepKind.Checkpoint) checkpoint = step;
        step++;
        held = 0f;
        songlessSent = false;
        // A whole quiet stretch got through without one zombie finding anyone: into the rest stop, or up to where a hold
        // or the escape begins. Every player is paid for it, each PC on the host's word (Status.Unseen).
        var next = Current;
        bool through = done.Kind == CampaignStepKind.Checkpoint
            || next != null && (next.Kind == CampaignStepKind.Hold || next.Kind == CampaignStepKind.Escape);
        if (IsHost && through && unseen && quiet && !lull && calmMade > 0)
        {
            unseen = false;
            unseenCount++;
            ShowUnseen();
        }
        // The way on from a rest stop starts quiet again.
        if (done.Kind == CampaignStepKind.Checkpoint && step < steps.Count) BeginQuiet();
        if (done.Kind == CampaignStepKind.Escape || step >= steps.Count) Finish();
        else ShowObjective(announce);
    }

    private void Finish()
    {
        finished = true;
        finishedAt = Time.time;
        horde.Clear();
        ShowFinish();
    }

    // C1/C2: the step's song on every PC (a finished chapter's plays on under its closing card); on the host, the
    // song's highlight sends the step's brutes. A spot for them is looked for again every half second until one does.
    private void PlayMusic()
    {
        var now = Current;
        string wanted = finished ? music.Playing : now != null ? now.Music : "";
        // Joined partway: a hold's song from as far in as the hold is, a finale's escape from past the whole hold.
        float from = now == null ? 0f : now.Kind == CampaignStepKind.Hold ? held
            : now.Kind == CampaignStepKind.Escape && CampaignMusic.TryFinale(now.Music, out float holdBefore, out _) ? holdBefore + held : 0f;
        if (music.Want(wanted, from)) brutesDue = IsHost && now != null ? now.PeakBosses : 0;
        music.Tick();
        // A hold with no song (the first chapter's shutter, whose siren is its music) sends its brutes halfway through.
        bool songless = now != null && now.Kind == CampaignStepKind.Hold && string.IsNullOrEmpty(now.Music);
        if (IsHost && songless && !songlessSent && now.PeakBosses > 0 && held >= now.Seconds * .5f)
        {
            songlessSent = true;
            brutesDue = now.PeakBosses;
        }
        if (!IsHost || finished || brutesDue <= 0 || !(music.PastPeak || songless) || Time.time < nextBruteTry) return;
        nextBruteTry = Time.time + .5f;
        brutesDue -= horde.SpawnBrutes(brutesDue, players, minSpawnDistance, maxSpawnDistance, seenMinDistance);
    }

    private void BackToCheckpoint()
    {
        if (finished) return;
        horde.Clear();
        music.Stop();
        brutesDue = 0;
        fuel.Reset();
        step = checkpoint + 1;
        held = 0f;
        songlessSent = false;
        BeginQuiet();
        // Back in the rest stop with its doors shut, not standing open on the horde (user, 2026-11-11 playtest: "죽고 나서
        // 다시 시작할 때 문이 열려 있어서 매번 식겁함"). Status shares the doors with the room.
        if (checkpoint >= 0 && checkpoint < steps.Count)
            foreach (var door in doors) if (door != null && door.Spot == steps[checkpoint].Spot) door.Closed = true;
        // The goals done since the rest stop are undone: the log starts over rather than counting them twice.
        if (tracker != null) tracker.ClearObjective();
        ShowObjective(true);
    }

    // Back to the hub for everyone, the results with them and its map table offering the next chapter (memo 46,
    // HubFlow). With no hub built, the next chapter as before; after the last one, or with the next not built yet, the
    // run just stays done.
    private void MoveOn()
    {
        movedOn = true;
        string next = chapter != null ? chapter.Next : null;
        if (HubFlow.Return(next)) return;
        if (string.IsNullOrEmpty(next) || !Application.CanStreamedLevelBeLoaded(next)) return;
        if (GameSession.IsOnline) GameSession.ChangeMap(next);
        else SceneLoad.Go(next);
    }

    private void Prompt()
    {
        if (screen == null) return;
        var now = Current;
        var mine = PlayerRoster.Local;
        string text = null;
        // A field pickup looked at has [F] first (FieldPickups).
        bool able = mine != null && mine.IsInitialized && !mine.IsDead && !mine.DamageReceiver.IsDowned && !FieldPickups.Targeting;
        bool pressed = able && InputFocus.TakesPlayInput() && interact != null && interact.WasPressedThisFrame();
        int door = able ? NearestDoor(mine) : -1;
        // One [F] at a time: a step's button (this one's, or a later one's with nothing but getting there before it, C4),
        // then a can in hand, then a door, then a can to pick up.
        int button = able && !finished && now != null ? ButtonWithin(mine) : -1;
        if (button >= 0)
        {
            var at = steps[button];
            text = string.IsNullOrEmpty(at.Prompt) ? Texts.Format("campaign.prompt.step", at.Title) : at.Prompt;
            if (pressed) Press(button);
        }
        else if (able && fuel.Any && fuel.CarriedBy(mine) >= 0) text = CanPrompt(mine, pressed);
        else if (door >= 0)
        {
            text = doors[door].Closed ? Texts.Get("campaign.prompt.door.open") : Texts.Get("campaign.prompt.door.close");
            if (pressed) ToggleDoor(door);
        }
        else if (able && NearestShop(mine) is Transform shop)
        {
            text = Texts.Get("campaign.prompt.shop");
            if (pressed) ShopMenu.Open(mine, shop.position);
        }
        else if (able && fuel.Any) text = CanPrompt(mine, pressed);
        offering = text != null;
        screen.Prompt(ShopMenu.IsOpen ? null : text);
    }

    private Transform NearestShop(PlayerMovement mine)
    {
        foreach (var shop in shops)
        {
            if (shop == null) continue;
            Vector3 apart = shop.position - mine.transform.position;
            apart.y = 0f;
            if (apart.sqrMagnitude <= ShopReach * ShopReach) return shop;
        }
        return null;
    }

    private int NearestDoor(PlayerMovement mine)
    {
        int nearest = -1;
        float best = DoorReach * DoorReach;
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null) continue;
            Vector3 apart = doors[i].Doorway - mine.transform.position;
            apart.y = 0f;
            if (apart.sqrMagnitude <= best) { best = apart.sqrMagnitude; nearest = i; }
        }
        return nearest;
    }

    private void ToggleDoor(int index)
    {
        if (IsHost) { doors[index].Closed = !doors[index].Closed; return; }
        if (network == null || !network.IsListening) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        network.CustomMessagingManager.SendNamedMessage(DoorMessage, NetworkManager.ServerClientId, writer);
    }

    // A guest worked a door: the host shuts or opens it, and everyone sees it through the status.
    private void ReceiveDoor(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte index);
        if (index < doors.Length && doors[index] != null) doors[index].Closed = !doors[index].Closed;
    }

    // Everyone sits in a rest stop with its doors shut: the one being reached now, or the last one reached.
    private bool Resting() => RestingAt(Current) || (checkpoint >= 0 && checkpoint < steps.Count && RestingAt(steps[checkpoint]));

    private bool RestingAt(CampaignStep at)
        => at != null && at.Kind == CampaignStepKind.Checkpoint && HasDoors(at.Spot) && DoorsShut(at.Spot) && EveryoneAt(at);

    private bool HasDoors(string spot)
    {
        foreach (var door in doors) if (door != null && door.Spot == spot) return true;
        return false;
    }

    // A rest stop with no doors counts as shut.
    private bool DoorsShut(string spot)
    {
        foreach (var door in doors) if (door != null && door.Spot == spot && !door.Closed) return false;
        return true;
    }

    private byte DoorBits()
    {
        int bits = 0;
        for (int i = 0; i < doors.Length && i < 8; i++) if (doors[i] != null && doors[i].Closed) bits |= 1 << i;
        return (byte)bits;
    }

    /// <summary>The `director` command: how the director paces the horde now (host or alone).</summary>
    public static string DirectorReport()
    {
        if (active == null) return "캠페인 맵이 아니에요.";
        if (!active.IsHost) return "디렉터는 방장 PC에서 돌아요. 방장이 입력해 보세요.";
        return active.pacer.Report(active.horde.Alive) + (active.Resting() ? " · 휴게소에서 쉬는 중" : "")
            + (active.quiet ? " · 조용한 구간(좀비가 알아채기 전에는 떼가 오지 않음)" : "");
    }

    // Carrying a can: pour it where the step wants it, else put it down. Hands free: pick up the can within reach.
    private string CanPrompt(PlayerMovement mine, bool pressed)
    {
        int carried = fuel.CarriedBy(mine);
        if (carried >= 0)
        {
            bool pour = MayPour(mine);
            if (pressed) CanAct(pour ? CampaignCans.Act.Pour : CampaignCans.Act.Drop, carried, mine);
            return Texts.Format("campaign.prompt.can.carry", pour ? Texts.Get("campaign.prompt.can.pour") : Texts.Get("campaign.prompt.can.drop"));
        }
        int near = fuel.NearestLying(mine.transform.position);
        if (near < 0) return null;
        if (pressed) CanAct(CampaignCans.Act.Pick, near, mine);
        return Texts.Get("campaign.prompt.can.pick");
    }

    private void CanAct(CampaignCans.Act act, int index, PlayerMovement mine, Vector3 aim = default)
    {
        if (IsHost) fuel.Apply(act, index, mine, MayPour(mine), aim);
        else CampaignCans.Send(network, act, index, aim);
    }

    // C17: carried, the left button throws the can where the view looks.
    private void ThrowCan()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.DamageReceiver.IsDowned || !InputFocus.TakesPlayInput()) return;
        // The fire key (Combat/Fire, the left button unless the key window says otherwise): the hands hold the can, not a gun.
        if (!KeyBindings.Pressed("Combat/Fire")) return;
        int carried = fuel.CarriedBy(mine);
        if (carried < 0) return;
        var view = mine.TryGetComponent(out PlayerBody body) ? body.View : null;
        CanAct(CampaignCans.Act.Throw, carried, mine, view != null ? view.forward : mine.transform.forward);
    }

    /// <summary>
    /// C17: this PC's player shot fuel can <paramref name="index"/> (CampaignCanTarget): the host sets it off; a guest
    /// tells the host.
    /// </summary>
    public static void ShootCan(int index, PlayerMovement shooter)
    {
        if (active == null || shooter == null || !shooter.IsLocal) return;
        if (active.IsHost) { active.fuel.Ignite(index, shooter); return; }
        var network = active.network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        network.CustomMessagingManager.SendNamedMessage(CanShotMessage, NetworkManager.ServerClientId, writer);
    }

    // A guest's shot reached a can: the host sets it off as theirs.
    private void ReceiveCanShot(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte index);
        PlayerMovement shooter = null;
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned && owner.OwnerClientId == sender) { shooter = someone; break; }
        fuel.Ignite(index, shooter);
    }

    // A Collect's count on the mission HUD as the cans go in, here and on every guest.
    private void ShowPoured()
    {
        var now = Current;
        if (finished || now == null || now.Kind != CampaignStepKind.Collect || tracker == null) { shownPoured = -1; return; }
        int poured = fuel.Poured;
        if (poured == shownPoured) return;
        shownPoured = poured;
        tracker.SetProgress(now.Id, Mathf.Min(poured, now.Count), Mathf.Max(1, now.Count));
    }

    // The button of the current step, or of a later one with only passable steps before it (C4), that this player
    // stands at; -1 for none.
    private int ButtonWithin(PlayerMovement mine)
    {
        for (int i = step; i < steps.Count; i++)
        {
            if (i > step && !Passable(steps[i - 1].Kind)) return -1;
            if (steps[i].Kind == CampaignStepKind.Press && Within(mine, steps[i])) return i;
        }
        return -1;
    }

    private void Press(int button)
    {
        if (IsHost) { AdvanceTo(button + 1); return; }
        if (network == null || !network.IsListening) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)button);
        network.CustomMessagingManager.SendNamedMessage(PressMessage, NetworkManager.ServerClientId, writer);
    }

    // A guest pressed a step's button: it counts if that step is still ahead with nothing but passable steps before it.
    private void ReceivePress(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte pressed);
        if (!finished && PassableUpTo(pressed) && steps[pressed].Kind == CampaignStepKind.Press) AdvanceTo(pressed + 1);
    }

    private void Share()
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        var now = new Status
        {
            Step = (byte)Mathf.Clamp(step, 0, 255), Finished = (byte)(finished ? 1 : 0),
            Checkpoint = (byte)Mathf.Clamp(checkpoint + 1, 0, 255), Held = (ushort)Mathf.Clamp(Mathf.FloorToInt(held), 0, ushort.MaxValue),
            Doors = DoorBits(), Quiet = (byte)(quiet ? lull ? 2 : 1 : 0), Unseen = unseenCount, Alarm = (byte)(alarm ? 1 : 0)
        };
        if (now.Equals(sent) && Time.unscaledTime < nextShare) return;
        sent = now;
        nextShare = Time.unscaledTime + ShareEvery;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Status>(), Allocator.Temp);
        writer.WriteValueSafe(now);
        network.CustomMessagingManager.SendNamedMessageToAll(StatusMessage, writer);
    }

    // A guest follows the host's run.
    private void ReceiveStatus(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Status status);
        if (sender != NetworkManager.ServerClientId) return;
        bool back = status.Step < step, moved = status.Step != step;
        // Sent back from a finale's escape while the chapter still runs: the ride left with nobody on it.
        if (back && !finished && Current != null && Current.Kind == CampaignStepKind.Escape && TotalOf(Current) > 0f) MissedRide();
        step = status.Step;
        checkpoint = status.Checkpoint - 1;
        held = status.Held;
        quiet = status.Quiet != 0;
        lull = status.Quiet == 2;
        alarm = status.Alarm != 0;
        // The host's count of stretches got through unseen: each one more pays this player too. The first word only
        // sets the count, so joining late pays for nothing that came before.
        if (!unseenKnown) { unseenKnown = true; unseenCount = status.Unseen; }
        else if (status.Unseen != unseenCount)
        {
            int more = (byte)(status.Unseen - unseenCount);
            unseenCount = status.Unseen;
            if (more > 0 && more < 4) for (int i = 0; i < more; i++) ShowUnseen();
        }
        for (int i = 0; i < doors.Length && i < 8; i++) if (doors[i] != null) doors[i].Closed = (status.Doors & (1 << i)) != 0;
        if (status.Finished != 0)
        {
            if (!finished) { finished = true; ShowFinish(); }
            return;
        }
        finished = false;
        if (back) music.Stop();
        if (back && tracker != null) tracker.ClearObjective();
        if (moved) ShowObjective(true);
        else if (Current != null) ShowHeld(Current);
    }

    private void ShowObjective(bool announce)
    {
        var now = Current;
        if (tracker == null || now == null) return;
        // A hold, and a finale's escape (the ride's wait), show their time passing.
        float total = now.Kind == CampaignStepKind.Hold || now.Kind == CampaignStepKind.Escape ? TotalOf(now) : 0f;
        tracker.SetObjective(total > 0f
            ? new MissionObjective(now.Id, now.Title, now.Description, MissionProgressDisplay.Percent,
                Mathf.FloorToInt(held), Mathf.CeilToInt(total), now.Done)
            : now.Kind == CampaignStepKind.Collect
            ? new MissionObjective(now.Id, now.Title, now.Description, MissionProgressDisplay.Count,
                Mathf.Min(fuel.Poured, now.Count), Mathf.Max(1, now.Count), now.Done)
            : new MissionObjective(now.Id, now.Title, now.Description, doneTitle: now.Done), announce);
        shownPoured = -1;
    }

    private void ShowHeld(CampaignStep now)
    {
        if (tracker == null || (now.Kind != CampaignStepKind.Hold && now.Kind != CampaignStepKind.Escape)) return;
        float total = TotalOf(now);
        if (total > 0f) tracker.SetProgress(now.Id, Mathf.FloorToInt(held), Mathf.CeilToInt(total));
    }

    private void ShowFinish()
    {
        // Under a finale's song the ride left as it ended: whoever wasn't on it is named on the card ("못 타면 걍 아쉬운
        // 거지": no more comes of it than that).
        leftBehind = "";
        var last = step - 1 >= 0 && step - 1 < steps.Count ? steps[step - 1] : null;
        if (last != null && last.Kind == CampaignStepKind.Escape && TotalOf(last) > 0f)
        {
            var names = new List<string>();
            foreach (var someone in players)
                if (!Within(someone, last)) names.Add(someone.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : someone.name);
            leftBehind = string.Join(", ", names);
        }
        PlayerRecords.ChapterEscaped(gameObject.scene.name);
        // The escape has its moment first; the card comes up as the banner lets go (ShowCardWhenDue).
        climb = ClassGrowth.Settle();
        // Everyone's numbers go with this PC to the hub, where the results play over the players (memo 37, MatchResults).
        MatchResults.Capture(gameObject.scene.name, chapter != null ? chapter.Title : "", chapter != null ? chapter.Next : "", climb,
            someone => last == null || last.Kind != CampaignStepKind.Escape || TotalOf(last) <= 0f || Within(someone, last));
        ChapterEndBanner.Play(chapter != null ? chapter.Title : "", leftBehind.Length > 0 ? Texts.Format("campaign.escape.left", leftBehind)
            : GameSession.IsOnline ? Texts.Get("campaign.escape.all") : Texts.Get("campaign.escape.solo"), BannerSeconds);
        cardAt = Time.unscaledTime + BannerSeconds - CardOverBanner;
        if (tracker == null) return;
        string next = chapter != null ? chapter.Next : null;
        string title = string.IsNullOrEmpty(next) ? Texts.Get("campaign.end.done")
            : Application.CanStreamedLevelBeLoaded(next) ? Texts.Get("campaign.end.next") : Texts.Get("campaign.end.unbuilt");
        tracker.SetObjective(new MissionObjective(gameObject.scene.name + ".end", title, ""));
    }

    // The closing card and, beside its results, the class's insignia, climbing if this chapter took it up a rank (6E).
    // With the hub built the card is the story alone: the results and the insignia play in the hub (ResultsShow).
    private void ShowCardWhenDue()
    {
        // The results keep up while the card is up: the last numbers on their way from the others (MatchResults).
        if (finished && Time.unscaledTime >= nextResults) { nextResults = Time.unscaledTime + .5f; MatchResults.Refresh(); }
        if (cardAt < 0f || Time.unscaledTime < cardAt) return;
        cardAt = -1f;
        float seconds = Mathf.Max(4f, outroSeconds - (BannerSeconds - CardOverBanner));
        bool hub = HubFlow.IsBuilt;
        if (chapter != null && screen != null) screen.ShowCard(chapter.Title, chapter.Outro, seconds, hub ? null : Results());
        if (climb != null && !hub) RankClimb.Play(climb, seconds);
        climb = null;
    }

    // The chapter's results under its closing story (stage 6A), two plain numbers: this chapter's kills and the money
    // held (the same one number as in play; no experience anywhere, which confused the user). In a room, each player's.
    private string Results()
    {
        const string Gold = "<color=#FFD36B>", End = "</color>";
        var text = new System.Text.StringBuilder();
        var mine = PlayerRoster.Local;
        int kills = mine != null && mine.TryGetComponent(out PlayerTally own) ? own.Kills : 0;
        text.Append(Texts.Get("campaign.results.kills")).Append("<pos=50%>").Append(kills).Append('\n');
        text.Append(Texts.Get("campaign.results.money")).Append("<pos=50%>").Append(Gold).Append(Wallet.Format(Wallet.Balance)).Append(End).Append('\n');
        if (leftBehind.Length > 0) text.Append(Texts.Get("campaign.results.left")).Append("<pos=50%>").Append(leftBehind).Append('\n');
        if (!GameSession.IsOnline) return text.ToString();
        text.Append("\n<b>").Append(Texts.Get("campaign.results.team")).Append("</b><pos=50%>").Append(Texts.Get("campaign.results.kills"))
            .Append("<pos=72%>").Append(Texts.Get("campaign.results.money")).Append('\n');
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !(someone.IsLocal || someone.IsNetworkDriven) || !someone.TryGetComponent(out PlayerTally tally)) continue;
            text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tally.Color)).Append('>').Append(tally.DisplayName).Append(End)
                .Append("<pos=50%>").Append(tally.Kills).Append("<pos=72%>").Append(Wallet.Format(tally.Score)).Append('\n');
        }
        return text.ToString();
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
