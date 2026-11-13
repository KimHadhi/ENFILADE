using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The ENFILADE tower, run (the user, 2026-11-12: "타워형 맵 … 1~4명 … 각자 열린 방을 통과하며 층을 오르게 돼. 중간에 휴식하는
/// 방에선 모두가 만날 수 있고 … 마지막 10층까지 완료하게 되면 ENFILADED!!"). A trial tower in the spirit of a gacha game's
/// daily quest or Dungeon Fighter's tower: four lanes of rooms climbing side by side behind armoured glass
/// (TowerLayout), the day's program in them (TowerPlan).
/// A stretch begins in a hall: four scanners rise from its floor by the lanes' doors (TowerScanner). The first palm on one
/// ([F] held) starts a count for everyone; whoever holds a scanner when it runs out has its lane's door open, and anyone
/// without a scanner may follow someone through an open door. Doors shut behind them (TowerDoor). In each room the fight
/// begins as they come in and its door shuts: a swarm of weak ones from the holding pens, or one champion. The last one
/// down opens the way up; the stretch's last room leads to the next hall, where everyone meets again (a shop, supplies)
/// and the scanners rise for the next stretch. Floors 1-3, 4-6, 7-9, then floor 10 alone; the tower's last kill freezes on
/// ENFILADED!! (EnfiladedFinale), the money is paid and everyone goes back to the hub with the results.
/// Death: alone in a lane nobody can reach you, so you don't go down, you die (TryGoDown, NoOneToHelp); the dead wait
/// (KeepsDead) and come back in the next hall once the lanes are through. A lane with nobody left alive fails. A wipe, or
/// every lane failing, sends the stretch back to its hall's scanners.
/// The host, or the PC alone, runs it and tells the others where it stands (a named message, also every couple of seconds
/// for anyone who missed one); a guest's palm goes to the host. Every PC opens its own doors, raises its own scanners,
/// draws its HUD and pays its own player. Numbers: Tables/Tower (row Default) and Tables/TowerFloors.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Campaign)]
public sealed partial class TowerRun : MonoBehaviour
{
    public const string TableName = "Tower", TableRow = "Default", SceneName = "EnfiladeTower";
    private const string StatusMessage = "ENFILADE.Tower", PalmMessage = "ENFILADE.TowerPalm";
    // The run is told again this often when nothing changed (s), and every second while something counts.
    private const float ShareEvery = 2f, ShareCounting = 1f;
    // A guest's palm is told again this often while it holds (s), forgotten once this old, and reached a little farther
    // than this PC's own to allow for lag (m).
    private const float PalmEvery = .4f, PalmStale = 1.2f, PalmSlack = 1f;
    // A shop counter answers within this (m); a room's last ones, none of them down this long (s), are taken away.
    private const float ShopReach = 2f, StuckSeconds = 120f;
    private const string FirstTodayKey = "tower.clearedOn";

    public enum Phase : byte { Gate, Boarding, Lanes, Gather, Finale }
    public enum LaneState : byte { Shut, Boarding, Climbing, Fighting, Done, Failed, Vacant }

    private struct Status : INetworkSerializeByMemcpy, System.IEquatable<Status>
    {
        public int Seed;
        public byte Phase, Segment, Opened, Resets, Counting;
        /// <summary>Tenths of a second left on what counts (the scanners, the doors, the gathering).</summary>
        public ushort Count;
        /// <summary>Who holds each scanner: their client id plus one, 0 for nobody.</summary>
        public ulong Hand0, Hand1, Hand2, Hand3;
        public byte State0, State1, State2, State3, Floor0, Floor1, Floor2, Floor3, Left0, Left1, Left2, Left3;

        // The count runs down every frame; it goes out on its own clock, so it stays out of what counts as a change.
        public bool Equals(Status other) => Seed == other.Seed && Phase == other.Phase && Segment == other.Segment
            && Opened == other.Opened && Resets == other.Resets && Counting == other.Counting
            && Hand0 == other.Hand0 && Hand1 == other.Hand1 && Hand2 == other.Hand2 && Hand3 == other.Hand3
            && State0 == other.State0 && State1 == other.State1 && State2 == other.State2 && State3 == other.State3
            && Floor0 == other.Floor0 && Floor1 == other.Floor1 && Floor2 == other.Floor2 && Floor3 == other.Floor3
            && Left0 == other.Left0 && Left1 == other.Left1 && Left2 == other.Left2 && Left3 == other.Left3;
    }

    // A lane's fight as the host keeps it.
    private sealed class Lane
    {
        public readonly List<EnemyHealth> Alive = new List<EnemyHealth>();
        public readonly Queue<ZombieKind> Due = new Queue<ZombieKind>();
        public bool Champion;
        public int Players = 1, Spot;
        public float Toughness = 1f, NextSpawn, Changed;

        public void Reset()
        {
            foreach (var enemy in Alive) Remove(enemy);
            Alive.Clear();
            Due.Clear();
            Champion = false;
            Spot = 0;
        }
    }

    [SerializeField, Tooltip("What the rooms' zombies are made of.")]
    private EnemyHealth enemyPrefab;
    [SerializeField, Min(1f), Tooltip("The scanners' count from the first palm (s).")]
    private float countSeconds = 10f;
    [SerializeField, Min(0f), Tooltip("Everyone with a palm on a scanner: the count drops to this if it is longer (s).")]
    private float everyoneSeconds = 3f;
    [SerializeField, Min(1f), Tooltip("How long the opened doors wait for everyone to go through (s).")]
    private float boardingSeconds = 8f;
    [SerializeField, Min(.5f), Tooltip("How near a scanner's plate someone must stand to hold it (m).")]
    private float palmReach = 1.6f;
    [SerializeField, Min(0f), Tooltip("How far into a room someone must come before its fight begins and its door shuts behind them (m).")]
    private float enterPast = 2f;
    [SerializeField, Min(1), Tooltip("The most of one room's swarm about at once.")]
    private int mostAlive = 7;
    [SerializeField, Min(.05f), Tooltip("A swarm comes out one every this often (s).")]
    private float spawnEvery = .55f;
    [SerializeField, Min(0f), Tooltip("Each player past the first in a lane makes its swarms bigger by this share of one player's.")]
    private float crowdPerPlayer = .5f;
    [SerializeField, Min(0f), Tooltip("Each player past the first in a lane makes its champions tougher by this share.")]
    private float championPerPlayer = .6f;
    [SerializeField, Min(5f), Tooltip("Someone still not in the next hall this long after the lanes are through is brought there (s).")]
    private float gatherSeconds = 45f;
    [SerializeField, Min(0f), Tooltip("How long the opening card stays (s).")]
    private float introSeconds = 8f;
    [SerializeField, Min(1f), Tooltip("How long ENFILADED!! holds the frozen frame (s).")]
    private float finaleSeconds = 3.4f;
    [SerializeField, Min(1f), Tooltip("How long the closing card stays before everyone goes back to the hub (s).")]
    private float outroSeconds = 9f;
    [SerializeField, Min(0), Tooltip("Money for each player once the tower is through.")]
    private int clearBonus = 1500;
    [SerializeField, Min(0), Tooltip("And this much more the first time it is through today on this PC.")]
    private int firstTodayBonus = 1500;
    [SerializeField, Tooltip("The last floor's song (Tables/Music); empty for none.")]
    private string finaleSong = "TowerFinale";

    private static TowerRun active;
    private readonly List<PlayerMovement> players = new List<PlayerMovement>();
    private readonly Lane[] lanes = { new Lane(), new Lane(), new Lane(), new Lane() };
    private readonly Dictionary<ulong, (int lane, float at)> palms = new Dictionary<ulong, (int, float)>();
    // Where the run stands: the host's, and a guest's copy of it.
    private TowerPlan plan;
    private int seed, segment, opened, resets;
    private Phase phase;
    private float count;
    private bool counting;
    private readonly ulong[] hands = new ulong[TowerLayout.Lanes];
    private readonly LaneState[] states = new LaneState[TowerLayout.Lanes];
    private readonly int[] floors = new int[TowerLayout.Lanes], lefts = new int[TowerLayout.Lanes];
    // What this PC last showed, for what changed: alerts, payments, sounds.
    private Phase shownPhase = Phase.Finale;
    private int shownSegment = -1, shownResets, shownSecond = -1, shownSeed = -1;
    private bool shownCounting, started, finished, movedOn, wasWiped, listening, offering, fetched;
    private readonly LaneState[] shownStates = new LaneState[TowerLayout.Lanes];
    private readonly int[] shownFloors = new int[TowerLayout.Lanes];
    // The objective on the mission HUD, and how many the fight being shown started with.
    private string shownObjective;
    private int fightTotal, shownLeft = -1;
    private TowerDoor[] doors = new TowerDoor[0];
    private TowerScanner[] scanners = new TowerScanner[0];
    private TowerSign[] signs = new TowerSign[0];
    private TowerHUD hud;
    private CampaignScreen screen;
    private CampaignMusic music;
    private MissionTracker tracker;
    private NetworkManager network;
    private InputAction interact;
    private Status sent;
    private int palm = -1, sentPalm = -2;
    private float nextShare, nextPalm, startedAt, finaleAt, cardAt = -1f, nextResults;
    private ClassGrowth.Climb climb;

    private bool IsHost => network == null || network.IsServer;
    private ulong MyId => network != null ? network.LocalClientId + 1UL : 1UL;

    /// <summary>The tower's [F] has something on offer now (a scanner, a shop): F does that, never a ping (PlayerPing).</summary>
    public static bool Offering => active != null && active.offering;

    /// <summary>Where the dead come back in the tower: the hall of the stretch under way, or the next one once its lanes
    /// are through. False outside the tower.</summary>
    public static bool TryCheckpoint(out Vector3 position)
    {
        position = default;
        if (active == null) return false;
        position = TowerLayout.HallSpot(active.CheckpointSegment);
        return true;
    }

    /// <summary>The dead wait while the lanes are being climbed: they come back in the next hall once those are through.</summary>
    public static bool KeepsDead => active != null && (active.phase == Phase.Boarding || active.phase == Phase.Lanes);

    /// <summary>The dead come back now, not after the usual wait: the lanes are through (or the stretch starts over).</summary>
    public static bool CallsDeadBack => active != null && (active.phase == Phase.Gather || active.phase == Phase.Gate);

    /// <summary>
    /// Nobody could get this player up where they are: they are in a lane while the lanes run, with nobody else standing
    /// in it, so zero health is death, not the ground (PlayerLife.TryGoDown).
    /// </summary>
    public static bool NoOneToHelp(PlayerMovement who)
    {
        if (active == null || who == null || (active.phase != Phase.Boarding && active.phase != Phase.Lanes)) return false;
        int lane = TowerLayout.LaneAt(who.transform.position, active.segment);
        if (lane < 0) return false;
        foreach (var other in PlayerRoster.All)
            if (other != null && other != who && Counted(other) && !other.IsDead && !other.IsDowned
                && TowerLayout.LaneAt(other.transform.position, active.segment) == lane)
                return false;
        return true;
    }

    /// <summary>Today's program: the date as a number (20261112), the same seed for everyone today.</summary>
    public static int TodaySeed()
    {
        var today = System.DateTime.Now;
        return today.Year * 10000 + today.Month * 100 + today.Day;
    }

    private int CheckpointSegment => phase == Phase.Gather ? Mathf.Min(segment + 1, TowerLayout.Segments - 1) : segment;

    private void Awake()
    {
        active = this;
        GameTables.Bind(TableName, TableRow, this);
        music = new CampaignMusic(gameObject);
        for (int i = 0; i < TowerLayout.Lanes; i++) shownStates[i] = LaneState.Shut;
    }

    private void Start()
    {
        tracker = FindFirstObjectByType<MissionTracker>();
        screen = gameObject.AddComponent<CampaignScreen>();
        hud = TowerHUD.Make(transform);
        interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact", false) : null;
        doors = FindObjectsByType<TowerDoor>(FindObjectsSortMode.None);
        scanners = FindObjectsByType<TowerScanner>(FindObjectsSortMode.None);
        signs = FindObjectsByType<TowerSign>(FindObjectsSortMode.None);
        startedAt = Time.time;
        Listen();
        if (IsHost)
        {
            seed = TodaySeed();
            plan = new TowerPlan(seed, this);
            BeginGate();
        }
        // A line of the story per line of tower.card.intro (Enter in the 글자 편집기), then the day.
        var lines = new List<string>(Texts.Lines(Texts.Get("tower.card.intro"))) { DayText(IsHost ? seed : TodaySeed()) };
        screen.ShowCard(Texts.Get("tower.card.title"), lines.ToArray(), introSeconds);
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
        foreach (var lane in lanes) lane.Reset();
        if (!listening || network == null || network.CustomMessagingManager == null) return;
        network.CustomMessagingManager.UnregisterNamedMessageHandler(network.IsServer ? PalmMessage : StatusMessage);
    }

    private void Listen()
    {
        network = GameSession.Network;
        if (network == null || network.CustomMessagingManager == null) return;
        listening = true;
        if (network.IsServer) network.CustomMessagingManager.RegisterNamedMessageHandler(PalmMessage, ReceivePalm);
        else network.CustomMessagingManager.RegisterNamedMessageHandler(StatusMessage, ReceiveStatus);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        GatherPlayers();
        WatchForWipe();
        ReadPalm();
        if (IsHost) Run(dt);
        else if (counting) count = Mathf.Max(0f, count - dt);
        Present();
        if (IsHost) Share();
    }

    // Who counts: the players of this room (this PC's and the others'), not test dummies, and not the dead.
    private void GatherPlayers()
    {
        players.Clear();
        foreach (var someone in PlayerRoster.All)
            if (someone != null && Counted(someone) && !someone.IsDead) players.Add(someone);
    }

    private static bool Counted(PlayerMovement someone) => someone.IsInitialized && (someone.IsLocal || someone.IsNetworkDriven);

    // Every PC sees its own wipe (PlayerLife); the host sends the stretch back to its hall's scanners at once, so everyone
    // comes back there when the wipe ends.
    private void WatchForWipe()
    {
        var mine = PlayerRoster.Local;
        bool wiped = mine != null && mine.TryGetComponent(out PlayerLife life) && life.IsWiped;
        if (wiped && !wasWiped && IsHost && phase != Phase.Finale) StartOver();
        wasWiped = wiped;
    }
}
