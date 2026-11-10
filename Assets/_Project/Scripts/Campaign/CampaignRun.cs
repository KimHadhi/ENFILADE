using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
/// (CampaignGate). The builder (Editor/World/GreyboxBuilder) puts one in each chapter's
/// scene with its spots and spawn points. Numbers: Tables/Campaign.
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
        public bool Equals(Status other) => Step == other.Step && Finished == other.Finished && Checkpoint == other.Checkpoint
            && Held == other.Held && Doors == other.Doors;
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
    [SerializeField, Tooltip("A shot fuel can's blast, its fire and its sound (C17; the builder sets them).")]
    private GameObject canBlast, canFire;
    [SerializeField] private AudioClip canBlastSound;
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

    private bool IsHost => network == null || network.IsServer;
    private CampaignStep Current => step >= 0 && step < steps.Count ? steps[step] : null;

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
        return now.Kind == CampaignStepKind.Hold && now.Seconds > 0f ? Mathf.Clamp01(active.held / now.Seconds) : 0f;
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
        fuel.Dress(canBlast, canFire, canBlastSound);
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
    }

    private void OnDestroy()
    {
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
        pacer.Pace(dt, players, scripted, Resting(), now.Horde, now.Rate, out int most, out float rate);
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
            case CampaignStepKind.Escape:
                if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Hold:
                held += dt;
                if (held >= now.Seconds) Advance();
                else ShowHeld(now);
                break;
            case CampaignStepKind.Collect:
                if (fuel.Poured >= Mathf.Max(1, now.Count)) Advance();
                break;
        }
        if (!finished && Current == now) SkipAhead();
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
        float from = now != null && now.Kind == CampaignStepKind.Hold ? held : 0f;
        if (music.Want(wanted, from)) brutesDue = IsHost && now != null ? now.PeakBosses : 0;
        music.Tick();
        if (!IsHost || finished || brutesDue <= 0 || !music.PastPeak || Time.time < nextBruteTry) return;
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
        // The goals done since the rest stop are undone: the log starts over rather than counting them twice.
        if (tracker != null) tracker.ClearObjective();
        ShowObjective(true);
    }

    // The next chapter, for everyone; after the last one, or with the next not built yet, the run just stays done.
    private void MoveOn()
    {
        movedOn = true;
        string next = chapter != null ? chapter.Next : null;
        if (string.IsNullOrEmpty(next) || !Application.CanStreamedLevelBeLoaded(next)) return;
        if (GameSession.IsOnline) GameSession.ChangeMap(next);
        else SceneManager.LoadScene(next);
    }

    private void Prompt()
    {
        if (screen == null) return;
        var now = Current;
        var mine = PlayerRoster.Local;
        string text = null;
        // A field pickup looked at has [F] first (FieldPickups).
        bool able = mine != null && mine.IsInitialized && !mine.IsDead && !mine.DamageReceiver.IsDowned && !FieldPickups.Targeting;
        bool pressed = able && TakesInput() && interact != null && interact.WasPressedThisFrame();
        int door = able ? NearestDoor(mine) : -1;
        // One [F] at a time: a step's button (this one's, or a later one's with nothing but getting there before it, C4),
        // then a can in hand, then a door, then a can to pick up.
        int button = able && !finished && now != null ? ButtonWithin(mine) : -1;
        if (button >= 0)
        {
            var at = steps[button];
            text = string.IsNullOrEmpty(at.Prompt) ? "[F] " + at.Title : at.Prompt;
            if (pressed) Press(button);
        }
        else if (able && fuel.Any && fuel.CarriedBy(mine) >= 0) text = CanPrompt(mine, pressed);
        else if (door >= 0)
        {
            text = doors[door].Closed ? "[F] 문 열기" : "[F] 문 닫기";
            if (pressed) ToggleDoor(door);
        }
        else if (able && NearestShop(mine) is Transform shop)
        {
            text = "[F] 상점 열기";
            if (pressed) ShopMenu.Open(mine, shop.position);
        }
        else if (able && fuel.Any) text = CanPrompt(mine, pressed);
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
        return active.pacer.Report(active.horde.Alive) + (active.Resting() ? " · 휴게소에서 쉬는 중" : "");
    }

    // Carrying a can: pour it where the step wants it, else put it down. Hands free: pick up the can within reach.
    private string CanPrompt(PlayerMovement mine, bool pressed)
    {
        int carried = fuel.CarriedBy(mine);
        if (carried >= 0)
        {
            bool pour = MayPour(mine);
            if (pressed) CanAct(pour ? CampaignCans.Act.Pour : CampaignCans.Act.Drop, carried, mine);
            return (pour ? "[F] 발전기에 연료 붓기" : "[F] 연료통 내려놓기") + "   [좌클릭] 던지기";
        }
        int near = fuel.NearestLying(mine.transform.position);
        if (near < 0) return null;
        if (pressed) CanAct(CampaignCans.Act.Pick, near, mine);
        return "[F] 연료통 들기";
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
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.DamageReceiver.IsDowned || !TakesInput()) return;
        if (UnityEngine.InputSystem.Mouse.current == null || !UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) return;
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
            Doors = DoorBits()
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
        step = status.Step;
        checkpoint = status.Checkpoint - 1;
        held = status.Held;
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
        else if (Current != null && Current.Kind == CampaignStepKind.Hold) ShowHeld(Current);
    }

    private void ShowObjective(bool announce)
    {
        var now = Current;
        if (tracker == null || now == null) return;
        tracker.SetObjective(now.Kind == CampaignStepKind.Hold
            ? new MissionObjective(now.Id, now.Title, now.Description, MissionProgressDisplay.Percent,
                Mathf.FloorToInt(held), Mathf.CeilToInt(now.Seconds), now.Done)
            : now.Kind == CampaignStepKind.Collect
            ? new MissionObjective(now.Id, now.Title, now.Description, MissionProgressDisplay.Count,
                Mathf.Min(fuel.Poured, now.Count), Mathf.Max(1, now.Count), now.Done)
            : new MissionObjective(now.Id, now.Title, now.Description, doneTitle: now.Done), announce);
        shownPoured = -1;
    }

    private void ShowHeld(CampaignStep now)
    {
        if (tracker != null) tracker.SetProgress(now.Id, Mathf.FloorToInt(held), Mathf.CeilToInt(now.Seconds));
    }

    private void ShowFinish()
    {
        PlayerRecords.ChapterEscaped(gameObject.scene.name);
        if (chapter != null && screen != null) screen.ShowCard(chapter.Title, chapter.Outro, outroSeconds, Results());
        // The class's insignia beside the results, climbing if this chapter took it up a rank (stage 6E).
        RankClimb.Play(ClassGrowth.Settle(), outroSeconds);
        if (tracker == null) return;
        string next = chapter != null ? chapter.Next : null;
        string title = string.IsNullOrEmpty(next) ? "캠페인 완료"
            : Application.CanStreamedLevelBeLoaded(next) ? "다음 장으로 이동합니다" : "다음 장은 아직 지어지지 않았어요";
        tracker.SetObjective(new MissionObjective(gameObject.scene.name + ".end", title, ""));
    }

    // The chapter's results under its closing story (stage 6A), two plain numbers: this chapter's kills and the money
    // held (the same one number as in play; no experience anywhere, which confused the user). In a room, each player's.
    private string Results()
    {
        const string Gold = "<color=#FFD36B>", End = "</color>";
        var text = new System.Text.StringBuilder();
        var mine = PlayerRoster.Local;
        int kills = mine != null && mine.TryGetComponent(out PlayerTally own) ? own.Kills : 0;
        text.Append("처치<pos=50%>").Append(kills).Append('\n');
        text.Append("돈<pos=50%>").Append(Gold).Append(Wallet.Format(Wallet.Balance)).Append(End).Append('\n');
        if (!GameSession.IsOnline) return text.ToString();
        text.Append("\n<b>팀</b><pos=50%>처치<pos=72%>돈\n");
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

    // As the mouse look: not while the cursor is free (chat, menus), the window is in the background or the console is open.
    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard
        && !ShopMenu.TakesKeyboard && !PerkCardScreen.TakesKeyboard;
}
