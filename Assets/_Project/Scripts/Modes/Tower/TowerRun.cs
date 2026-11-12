using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
public sealed class TowerRun : MonoBehaviour
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

    /// <summary>This scene is the tower.</summary>
    public static bool IsActive => active != null;
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

    // ---- The host ----

    private void Run(float dt)
    {
        if (plan == null) return;
        switch (phase)
        {
            case Phase.Gate: RunGate(dt); break;
            case Phase.Boarding: RunBoarding(dt); break;
            case Phase.Lanes: RunLanes(); break;
            case Phase.Gather: RunGather(dt); break;
            case Phase.Finale:
                if (!movedOn && Time.time - finaleAt >= finaleSeconds + outroSeconds) MoveOn();
                break;
        }
    }

    private void BeginGate()
    {
        phase = Phase.Gate;
        counting = false;
        count = countSeconds;
        opened = 0;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            hands[k] = 0;
            states[k] = LaneState.Shut;
            floors[k] = lefts[k] = 0;
            lanes[k].Reset();
        }
    }

    // The scanners: the count starts with the first palm and stops if every palm comes off; at its end the held lanes open.
    private void RunGate(float dt)
    {
        ResolveHands();
        bool any = false;
        foreach (ulong hand in hands) any |= hand != 0;
        if (!any)
        {
            counting = false;
            count = countSeconds;
            return;
        }
        if (!counting) { counting = true; count = countSeconds; }
        count -= dt;
        if (EveryoneHolds() && count > everyoneSeconds) count = everyoneSeconds;
        if (count <= 0f) OpenLanes();
    }

    // Who holds which scanner: a palm that came off lets go, and a palm on a free one takes it (first come keeps it).
    private void ResolveHands()
    {
        for (int k = 0; k < TowerLayout.Lanes; k++)
            if (hands[k] != 0 && PalmOf(hands[k]) != k) hands[k] = 0;
        TakeHand(MyId, palm);
        foreach (var pair in palms) TakeHand(pair.Key + 1UL, PalmOf(pair.Key + 1UL));
    }

    private void TakeHand(ulong id, int lane)
    {
        if (lane < 0 || lane >= TowerLayout.Lanes || hands[lane] != 0) return;
        foreach (ulong hand in hands) if (hand == id) return;
        hands[lane] = id;
    }

    // The scanner a player's palm is on: this PC's own, or a guest's word if fresh and they really stand there.
    private int PalmOf(ulong id)
    {
        if (id == MyId) return palm;
        if (!palms.TryGetValue(id - 1UL, out var word) || Time.unscaledTime - word.at > PalmStale || word.lane < 0) return -1;
        var who = PlayerById(id);
        var scanner = ScannerOf(segment, word.lane);
        if (who == null || who.IsDead || who.IsDowned || scanner == null) return -1;
        return Flat(who.transform.position - scanner.Palm) <= palmReach + PalmSlack ? word.lane : -1;
    }

    private bool EveryoneHolds()
    {
        if (players.Count == 0) return false;
        foreach (var someone in players)
        {
            ulong id = IdOf(someone);
            bool holds = false;
            foreach (ulong hand in hands) holds |= hand != 0 && hand == id;
            if (!holds) return false;
        }
        return true;
    }

    private void OpenLanes()
    {
        opened = 0;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if (hands[k] == 0) continue;
            opened |= 1 << k;
            states[k] = LaneState.Boarding;
            floors[k] = lefts[k] = 0;
        }
        phase = Phase.Boarding;
        counting = true;
        count = boardingSeconds;
    }

    // The doors stand open until everyone is through one or the time is up; then they shut and the lanes begin.
    private void RunBoarding(float dt)
    {
        count -= dt;
        bool allIn = players.Count > 0;
        foreach (var someone in players)
        {
            int lane = TowerLayout.LaneAt(someone.transform.position, segment);
            if (lane < 0 || (opened & (1 << lane)) == 0) { allIn = false; break; }
        }
        if (count > 0f && !allIn) return;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if ((opened & (1 << k)) == 0) continue;
            int inside = PlayersIn(k);
            states[k] = inside > 0 ? LaneState.Climbing : LaneState.Vacant;
            lanes[k].Players = Mathf.Max(1, inside);
        }
        phase = Phase.Lanes;
        counting = false;
        count = 0f;
    }

    private int PlayersIn(int lane)
    {
        int inside = 0;
        foreach (var someone in players) if (TowerLayout.LaneAt(someone.transform.position, segment) == lane) inside++;
        return inside;
    }

    private void RunLanes()
    {
        bool open = false, through = false;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if (states[k] == LaneState.Climbing || states[k] == LaneState.Fighting)
            {
                TickLane(k);
                if (states[k] == LaneState.Climbing || states[k] == LaneState.Fighting) open = true;
            }
            if (states[k] == LaneState.Done) through = true;
        }
        if (open) return;
        // Every lane is through or out. Through, the next hall (or ENFILADED!! on the tower's last floor, on the frame of
        // its last kill); every one out with someone still standing in the hall, the stretch starts over.
        if (!through) StartOver();
        else if (TowerLayout.IsLastSegment(segment)) BeginFinale();
        else
        {
            phase = Phase.Gather;
            counting = true;
            count = gatherSeconds;
        }
    }

    private void TickLane(int k)
    {
        var lane = lanes[k];
        int level = TowerLayout.FirstLevel(segment) + floors[k];
        if (PlayersIn(k) == 0) { Fail(k); return; }
        if (states[k] == LaneState.Climbing)
        {
            foreach (var someone in players)
                if (TowerLayout.InRoom(someone.transform.position, level, k, enterPast)) { BeginRoom(k, level); break; }
            return;
        }
        lane.Alive.RemoveAll(enemy => enemy == null || !enemy.IsAlive);
        Release(k, level);
        int left = Mathf.Clamp(lane.Due.Count + lane.Alive.Count, 0, 255);
        if (left != lefts[k]) lane.Changed = Time.time;
        lefts[k] = left;
        // The last few out and none of them down for this long: one stuck out of reach can't hold the lane for ever.
        if (lane.Due.Count == 0 && lane.Alive.Count > 0 && Time.time - lane.Changed > StuckSeconds) { lane.Reset(); lefts[k] = 0; }
        if (lane.Due.Count > 0 || lane.Alive.Count > 0) return;
        // The room is through: on to the next floor, or the stretch is done for this lane.
        if (floors[k] + 1 < TowerLayout.FloorsIn(segment)) { floors[k]++; states[k] = LaneState.Climbing; }
        else states[k] = LaneState.Done;
    }

    // Someone came into the room: its door shuts behind them (TowerDoor waits while anyone is in the doorway) and the
    // room's trial is let out.
    private void BeginRoom(int k, int level)
    {
        var lane = lanes[k];
        lane.Reset();
        int floor = TowerLayout.FloorOf(level);
        var row = plan.Of(floor);
        lane.Champion = plan.TrialOf(floor, k) == TowerTrial.Champion;
        if (lane.Champion)
        {
            lane.Due.Enqueue(plan.ChampionOf(floor, k));
            lane.Toughness = row.championToughness * (1f + championPerPlayer * (lane.Players - 1));
        }
        else
        {
            foreach (var kind in plan.SwarmOf(floor, k, lane.Players, crowdPerPlayer)) lane.Due.Enqueue(kind);
            lane.Toughness = row.toughness;
        }
        lane.NextSpawn = Time.time + .5f;
        lane.Changed = Time.time;
        states[k] = LaneState.Fighting;
        lefts[k] = lane.Due.Count;
    }

    // One out of the pens at a time, as long as the room doesn't hold its most already.
    private void Release(int k, int level)
    {
        var lane = lanes[k];
        if (lane.Due.Count == 0 || lane.Alive.Count >= mostAlive || Time.time < lane.NextSpawn || enemyPrefab == null) return;
        lane.NextSpawn = Time.time + spawnEvery;
        Vector3 wanted = lane.Champion ? TowerLayout.ChampionAt(level, k) : TowerLayout.PenAt(level, k, lane.Spot++ % 6);
        if (!NavMesh.SamplePosition(wanted + Vector3.up * .3f, out NavMeshHit ground, 2.5f, NavMesh.AllAreas)) return;
        var kind = lane.Due.Dequeue();
        var container = RuntimeSceneHierarchy.GetGroup(gameObject.scene, RuntimeSceneHierarchy.Enemies, "Tower");
        var facing = Quaternion.LookRotation(TowerLayout.EntersFromEast(level) ? Vector3.right : Vector3.left, Vector3.up);
        var enemy = Instantiate(enemyPrefab, ground.position, facing, container);
        // Set up before it goes out to the room, so its copies are born what it is.
        if (enemy.TryGetComponent(out ZombieType type))
        {
            type.Set(kind);
            type.Toughen(lane.Toughness);
        }
        if (lane.Champion) enemy.SetRank(EnemyRank.Elite);
        int stretch = segment;
        if (enemy.TryGetComponent(out EnemyTargeting targeting))
            targeting.Pen = someone => someone != null && TowerLayout.LaneAt(someone.transform.position, stretch) == k;
        EnemyNetwork.SpawnInRoom(enemy);
        lane.Alive.Add(enemy);
    }

    // Nobody left alive in it: the lane is out, and what was left in its room goes.
    private void Fail(int k)
    {
        lanes[k].Reset();
        states[k] = LaneState.Failed;
        lefts[k] = 0;
    }

    private static void Remove(EnemyHealth enemy)
    {
        if (enemy == null) return;
        enemy.gameObject.SetActive(false);
        Destroy(enemy.gameObject);
    }

    // The lanes are through: everyone in the next hall (the dead come back there, the rest walk up or are brought) and the
    // scanners rise for the next stretch.
    private void RunGather(float dt)
    {
        count = Mathf.Max(0f, count - dt);
        int next = segment + 1;
        if (players.Count == 0) return;
        foreach (var someone in players) if (!TowerLayout.InHall(someone.transform.position, next)) return;
        segment = next;
        BeginGate();
    }

    // A wipe, or every lane out: back to this stretch's scanners, with nothing left in its rooms.
    private void StartOver()
    {
        resets = (resets + 1) & 255;
        music.Stop();
        BeginGate();
    }

    private void BeginFinale()
    {
        phase = Phase.Finale;
        finaleAt = Time.time;
        counting = false;
        foreach (var lane in lanes) lane.Reset();
    }

    // Back to the hub with the results, the map table keeping the tower chosen. With no hub, the tower again.
    private void MoveOn()
    {
        movedOn = true;
        string scene = gameObject.scene.name;
        if (HubFlow.Return(scene)) return;
        if (GameSession.IsOnline) GameSession.ChangeMap(scene);
        else SceneLoad.Go(scene);
    }

    // ---- Palms ----

    // This PC's own palm: [F] held at a risen scanner within reach while the hall is choosing. A guest tells the host.
    private void ReadPalm()
    {
        int was = palm;
        palm = -1;
        var mine = PlayerRoster.Local;
        bool able = plan != null && phase == Phase.Gate && mine != null && mine.IsInitialized && !mine.IsDead && !mine.IsDowned
            && TakesInput() && !FieldPickups.Targeting;
        if (able && KeyBindings.Held("Player/Interact")) palm = NearestScanner(mine);
        if (palm >= 0 && palm != was) KitSound.Play(KitSound.Cue.TowerScan, .8f);
        if (IsHost || network == null || !network.IsListening) return;
        if (palm == sentPalm && (palm < 0 || Time.unscaledTime < nextPalm)) return;
        sentPalm = palm;
        nextPalm = Time.unscaledTime + PalmEvery;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)(palm < 0 ? 255 : palm));
        network.CustomMessagingManager.SendNamedMessage(PalmMessage, NetworkManager.ServerClientId, writer);
    }

    private void ReceivePalm(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte lane);
        palms[sender] = (lane == 255 ? -1 : lane, Time.unscaledTime);
    }

    // The lane of the risen scanner of this stretch nearest this player's palm reach, or -1.
    private int NearestScanner(PlayerMovement mine)
    {
        int nearest = -1;
        float best = palmReach;
        foreach (var scanner in scanners)
        {
            if (scanner == null || scanner.Segment != segment || !scanner.Ready) continue;
            Vector3 apart = mine.transform.position - scanner.Palm;
            if (apart.y < -2.2f || apart.y > 1f) continue;
            float distance = Flat(apart);
            if (distance <= best) { best = distance; nearest = scanner.Lane; }
        }
        return nearest;
    }

    private TowerScanner ScannerOf(int stretch, int lane)
    {
        foreach (var scanner in scanners) if (scanner != null && scanner.Segment == stretch && scanner.Lane == lane) return scanner;
        return null;
    }

    private static float Flat(Vector3 apart) { apart.y = 0f; return apart.magnitude; }

    private ulong IdOf(PlayerMovement someone)
    {
        if (someone == null) return 0UL;
        if (someone.IsLocal) return MyId;
        return someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned ? owner.OwnerClientId + 1UL : 0UL;
    }

    private PlayerMovement PlayerById(ulong id)
    {
        if (id == 0UL) return null;
        foreach (var someone in PlayerRoster.All) if (someone != null && Counted(someone) && IdOf(someone) == id) return someone;
        return null;
    }

    private string NameOf(ulong id)
    {
        var who = PlayerById(id);
        string name = who != null && who.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : "";
        return string.IsNullOrEmpty(name) ? "?" : name;
    }

    // ---- Sharing ----

    private void Share()
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        var now = new Status
        {
            Seed = seed, Phase = (byte)phase, Segment = (byte)segment, Opened = (byte)opened, Resets = (byte)resets,
            Counting = (byte)(counting ? 1 : 0), Count = (ushort)Mathf.Clamp(Mathf.CeilToInt(count * 10f), 0, ushort.MaxValue),
            Hand0 = hands[0], Hand1 = hands[1], Hand2 = hands[2], Hand3 = hands[3],
            State0 = (byte)states[0], State1 = (byte)states[1], State2 = (byte)states[2], State3 = (byte)states[3],
            Floor0 = (byte)floors[0], Floor1 = (byte)floors[1], Floor2 = (byte)floors[2], Floor3 = (byte)floors[3],
            Left0 = (byte)lefts[0], Left1 = (byte)lefts[1], Left2 = (byte)lefts[2], Left3 = (byte)lefts[3]
        };
        if (now.Equals(sent) && Time.unscaledTime < nextShare) return;
        sent = now;
        nextShare = Time.unscaledTime + (counting ? ShareCounting : ShareEvery);
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Status>(), Allocator.Temp);
        writer.WriteValueSafe(now);
        network.CustomMessagingManager.SendNamedMessageToAll(StatusMessage, writer);
    }

    // A guest follows the host's run.
    private void ReceiveStatus(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Status status);
        if (sender != NetworkManager.ServerClientId) return;
        if (plan == null || plan.Seed != status.Seed) plan = new TowerPlan(status.Seed, this);
        seed = status.Seed;
        phase = (Phase)status.Phase;
        segment = Mathf.Clamp(status.Segment, 0, TowerLayout.Segments - 1);
        opened = status.Opened;
        resets = status.Resets;
        counting = status.Counting != 0;
        count = status.Count / 10f;
        hands[0] = status.Hand0; hands[1] = status.Hand1; hands[2] = status.Hand2; hands[3] = status.Hand3;
        states[0] = (LaneState)status.State0; states[1] = (LaneState)status.State1;
        states[2] = (LaneState)status.State2; states[3] = (LaneState)status.State3;
        floors[0] = status.Floor0; floors[1] = status.Floor1; floors[2] = status.Floor2; floors[3] = status.Floor3;
        lefts[0] = status.Left0; lefts[1] = status.Left1; lefts[2] = status.Left2; lefts[3] = status.Left3;
    }

    // ---- Every PC ----

    private void Present()
    {
        if (plan == null) { offering = false; return; }
        if (shownSeed != seed) { shownSeed = seed; WriteSigns(); }
        var mine = PlayerRoster.Local;
        int myLane = mine != null ? TowerLayout.LaneAt(mine.transform.position, segment) : -1;
        ShowDoors();
        ShowScanners();
        Announce(mine, myLane);
        ShowHud(mine, myLane);
        ShowObjective(mine, myLane);
        Prompt(mine);
        Fetch(mine);
        PlayMusic();
        ShowCardWhenDue();
    }

    private void ShowDoors()
    {
        foreach (var door in doors) if (door != null) door.Open = WantsOpen(door);
    }

    private bool WantsOpen(TowerDoor door)
    {
        int k = door.Lane;
        if (k < 0 || k >= TowerLayout.Lanes) return false;
        bool chosen = (opened & (1 << k)) != 0;
        switch (door.Kind)
        {
            case TowerDoorKind.Departure:
                return phase == Phase.Boarding && chosen && door.Level == TowerLayout.HallLevel(segment);
            case TowerDoorKind.Entry:
            {
                int index = door.Level - TowerLayout.FirstLevel(segment);
                if (index < 0 || index >= TowerLayout.FloorsIn(segment)) return false;
                // Open on the way up, and through the fight until everyone of the lane still alive is in the room: the
                // first one in doesn't shut a slower lane-mate out on the stairs.
                return (phase == Phase.Boarding && chosen && index == 0)
                       || (phase == Phase.Lanes && floors[k] == index
                           && (states[k] == LaneState.Climbing || (states[k] == LaneState.Fighting && SomeoneOutside(k, door.Level))));
            }
            case TowerDoorKind.Exit:
            {
                int index = door.Level - TowerLayout.FirstLevel(segment);
                if (index < 0 || index >= TowerLayout.FloorsIn(segment) || (phase != Phase.Lanes && phase != Phase.Gather)) return false;
                return floors[k] > index || (states[k] == LaneState.Done && floors[k] == index);
            }
            case TowerDoorKind.Arrival:
                return (phase == Phase.Lanes || phase == Phase.Gather) && states[k] == LaneState.Done
                       && !TowerLayout.IsLastSegment(segment) && door.Level == TowerLayout.HallLevel(segment + 1);
        }
        return false;
    }

    // Someone of the lane, alive, is not yet in its room on this level (on the stairs, or in the doorway).
    private bool SomeoneOutside(int k, int level)
    {
        foreach (var someone in players)
            if (TowerLayout.LaneAt(someone.transform.position, segment) == k && !TowerLayout.InRoom(someone.transform.position, level, k, .6f))
                return true;
        return false;
    }

    private void ShowScanners()
    {
        ulong me = MyId;
        foreach (var scanner in scanners)
        {
            if (scanner == null) continue;
            int k = scanner.Lane;
            bool here = scanner.Segment == segment && k >= 0 && k < TowerLayout.Lanes;
            bool chosen = here && (opened & (1 << k)) != 0;
            scanner.Raised = here && (phase == Phase.Gate || (phase == Phase.Boarding && chosen));
            if (!here) { scanner.Show(TowerScanner.Look.Down); continue; }
            scanner.Show(phase != Phase.Gate ? (chosen ? TowerScanner.Look.Chosen : TowerScanner.Look.Free)
                : hands[k] == 0 ? TowerScanner.Look.Free : hands[k] == me ? TowerScanner.Look.Mine : TowerScanner.Look.Held);
        }
    }

    // Each lane's letter over its door, and what its rooms hold in each hall's stretch.
    private void WriteSigns()
    {
        foreach (var sign in signs)
        {
            if (sign == null) continue;
            int stretch = Mathf.Clamp(sign.Segment, 0, TowerLayout.Segments - 1);
            var trials = new List<string>();
            for (int i = 0; i < TowerLayout.FloorsIn(stretch); i++) trials.Add(plan.Label(TowerLayout.FloorNumber(stretch, i), sign.Lane));
            int first = TowerLayout.FloorNumber(stretch, 0), last = TowerLayout.FloorNumber(stretch, TowerLayout.FloorsIn(stretch) - 1);
            string floorsText = first == last ? Texts.Format("tower.sign.floor", first) : Texts.Format("tower.sign.floors", first, last);
            sign.Write("<size=280%><b>" + TowerHUD.Letter(sign.Lane) + "</b></size>\n<color=#FF4F78>" + floorsText + "</color>\n"
                       + string.Join(" · ", trials));
        }
    }

    // What changed since the last frame: sounds, alerts, and this player's pay for a floor got through.
    private void Announce(PlayerMovement mine, int myLane)
    {
        if (phase != shownPhase)
        {
            if (phase == Phase.Gate && started) { AlertHUD.Self(Texts.Get("tower.alert.scanners")); KitSound.Play(KitSound.Cue.TowerRise, .8f); }
            if (phase == Phase.Gate && !started) KitSound.Play(KitSound.Cue.TowerRise, .6f);
            if (phase == Phase.Boarding) AlertHUD.Self(Texts.Get("tower.alert.open"));
            if (phase == Phase.Gather) AlertHUD.Self(Texts.Get("tower.alert.gather"));
            if (phase == Phase.Finale) Finish();
        }
        if (resets != shownResets) { shownResets = resets; if (started) AlertHUD.Danger(Texts.Get("tower.alert.reset")); }
        if (phase == Phase.Gate && counting && !shownCounting)
        {
            ulong first = 0UL;
            foreach (ulong hand in hands) if (hand != 0) { first = hand; break; }
            AlertHUD.Self(Texts.Format("tower.alert.count", NameOf(first), Mathf.CeilToInt(countSeconds)));
        }
        shownCounting = counting && (phase == Phase.Gate || phase == Phase.Boarding);
        // A tick for each second of a count, louder for the last three.
        int second = shownCounting ? Mathf.CeilToInt(count) : -1;
        if (second != shownSecond && second > 0) KitSound.Play(KitSound.Cue.TowerTick, second <= 3 ? 1f : .55f);
        shownSecond = second;
        // Lanes getting through floors and going out, in the stretch that was already showing (not on this PC's first look,
        // so joining late pays for nothing; a stretch's last room is paid even when the stretch ends on that same frame).
        if (started && shownSegment == segment && (phase == Phase.Lanes || phase == Phase.Gather || phase == Phase.Finale))
            for (int k = 0; k < TowerLayout.Lanes; k++)
            {
                for (int index = shownFloors[k]; index < floors[k]; index++) Through(k, index, mine, myLane);
                if (states[k] == LaneState.Done && shownStates[k] != LaneState.Done) Through(k, floors[k], mine, myLane);
                if (states[k] == LaneState.Failed && shownStates[k] != LaneState.Failed)
                    AlertHUD.Danger(Texts.Format("tower.alert.failed", TowerHUD.Letter(k)));
            }
        for (int k = 0; k < TowerLayout.Lanes; k++) { shownStates[k] = states[k]; shownFloors[k] = floors[k]; }
        shownPhase = phase;
        shownSegment = segment;
        started = true;
    }

    // Lane k got through floor index <index> of the stretch: its own players are paid (each PC its own), the rest told.
    private void Through(int k, int index, PlayerMovement mine, int myLane)
    {
        int floor = TowerLayout.FloorNumber(segment, index);
        bool mineToo = mine != null && !mine.IsDead && myLane == k;
        if (!mineToo)
        {
            AlertHUD.Team(Texts.Format("tower.alert.cleared", TowerHUD.Letter(k), floor), null, KitStyle.Accent);
            return;
        }
        KitSound.Play(KitSound.Cue.TowerClear, .9f);
        CombatFeedHUD.Award(Texts.Format("tower.feed.floor", floor), Mathf.Max(0, plan.Of(floor).reward));
    }

    private void ShowHud(PlayerMovement mine, int myLane)
    {
        if (hud == null) return;
        bool countShown = counting && (phase == Phase.Gate || phase == Phase.Boarding);
        hud.Count(countShown, count, phase == Phase.Boarding ? Texts.Get("tower.hud.board") : Texts.Get("tower.hud.count"));
        bool lanesShown = phase != Phase.Finale;
        hud.ShowLanes(lanesShown);
        if (!lanesShown) return;
        ulong me = MyId;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            string letter = TowerHUD.Letter(k);
            bool chosen = (opened & (1 << k)) != 0;
            string words;
            bool faded = false, own;
            if (phase == Phase.Gate)
            {
                words = hands[k] != 0 ? Texts.Format("tower.hud.lane.held", letter, NameOf(hands[k])) : Texts.Format("tower.hud.lane.free", letter);
                faded = hands[k] == 0;
                own = hands[k] == me;
            }
            else
            {
                int floor = TowerLayout.FloorNumber(segment, Mathf.Clamp(floors[k], 0, TowerLayout.FloorsIn(segment) - 1));
                switch (states[k])
                {
                    case LaneState.Boarding: words = Texts.Format("tower.hud.lane.in", letter); break;
                    case LaneState.Climbing: words = Texts.Format("tower.hud.lane.climb", letter, floor); break;
                    case LaneState.Fighting: words = Texts.Format("tower.hud.lane.fight", letter, floor, lefts[k]); break;
                    case LaneState.Done: words = Texts.Format("tower.hud.lane.done", letter); break;
                    case LaneState.Failed: words = Texts.Format("tower.hud.lane.failed", letter); faded = true; break;
                    default: words = Texts.Format("tower.hud.lane.none", letter); faded = true; break;
                }
                own = chosen && myLane == k;
            }
            hud.Lane(k, words, own, faded);
        }
    }

    // The objective on the mission HUD, for where this player is.
    private void ShowObjective(PlayerMovement mine, int myLane)
    {
        if (tracker == null || phase == Phase.Finale) return;
        string id, title, about;
        string done = null;
        bool fight = false;
        int floor = 0;
        if (phase == Phase.Gate)
        {
            id = "tower.gate." + segment;
            title = Texts.Get(counting ? "tower.objective.gate_count" : "tower.objective.gate");
            about = Texts.Get("tower.objective.gate_desc");
        }
        else if (phase == Phase.Boarding)
        {
            bool inside = myLane >= 0 && (opened & (1 << myLane)) != 0;
            id = inside ? "tower.board.in." + segment : "tower.board." + segment;
            title = Texts.Get(inside ? "tower.objective.climb_first" : "tower.objective.board");
            about = Texts.Get(inside ? "tower.objective.climb_desc" : "tower.objective.board_desc");
        }
        else if (phase == Phase.Gather)
        {
            id = "tower.gather." + segment;
            title = Texts.Get("tower.objective.gather");
            about = Texts.Get("tower.objective.up_desc");
        }
        else if (mine != null && mine.IsDead)
        {
            id = "tower.dead." + segment;
            title = Texts.Get("tower.objective.dead");
            about = Texts.Get("tower.objective.dead_desc");
        }
        else if (myLane >= 0 && (states[myLane] == LaneState.Climbing || states[myLane] == LaneState.Fighting))
        {
            floor = TowerLayout.FloorNumber(segment, floors[myLane]);
            fight = states[myLane] == LaneState.Fighting;
            id = (fight ? "tower.fight." : "tower.climb.") + floor;
            title = fight ? Texts.Format("tower.objective.fight", floor, plan.Label(floor, myLane)) : Texts.Format("tower.objective.climb", floor);
            about = Texts.Get(fight ? "tower.objective.fight_desc" : "tower.objective.climb_desc");
            if (fight) done = Texts.Format("tower.done.floor", floor);
        }
        else if (myLane >= 0 && states[myLane] == LaneState.Done)
        {
            bool last = TowerLayout.IsLastSegment(segment);
            id = "tower.up." + segment;
            title = Texts.Get(last ? "tower.objective.last" : "tower.objective.up");
            about = Texts.Get(last ? "tower.objective.last_desc" : "tower.objective.up_desc");
        }
        else
        {
            id = "tower.wait." + segment;
            title = Texts.Get("tower.objective.wait_lanes");
            about = Texts.Get("tower.objective.wait_desc");
        }
        int left = fight ? lefts[myLane] : 0;
        if (id + title != shownObjective)
        {
            shownObjective = id + title;
            fightTotal = left;
            shownLeft = left;
            // The way up between rooms is said quietly; a room's fight, the scanners and the halls sound the objective's alarm.
            bool announce = !id.StartsWith("tower.climb.", System.StringComparison.Ordinal);
            tracker.SetObjective(fight
                ? new MissionObjective(id, title, about, MissionProgressDisplay.Count, 0, Mathf.Max(1, fightTotal), done)
                : new MissionObjective(id, title, about, doneTitle: done), announce);
            return;
        }
        if (!fight || left == shownLeft) return;
        shownLeft = left;
        fightTotal = Mathf.Max(fightTotal, left);
        tracker.SetProgress(id, Mathf.Max(0, fightTotal - left), Mathf.Max(1, fightTotal));
    }

    // [F]: a scanner in the hall that is choosing, else a shop counter.
    private void Prompt(PlayerMovement mine)
    {
        if (screen == null) return;
        string text = null;
        bool able = mine != null && mine.IsInitialized && !mine.IsDead && !mine.IsDowned && !FieldPickups.Targeting && phase != Phase.Finale;
        if (able && phase == Phase.Gate)
        {
            int near = NearestScanner(mine);
            if (near >= 0)
                text = hands[near] == MyId ? Texts.Get("tower.prompt.holding")
                    : hands[near] != 0 && palm != near ? Texts.Format("tower.prompt.taken", NameOf(hands[near]))
                    : palm == near ? Texts.Get("tower.prompt.holding") : Texts.Get("tower.prompt.palm");
        }
        if (text == null && able && NearShop(mine, out Vector3 shop))
        {
            text = Texts.Get("campaign.prompt.shop");
            if (TakesInput() && interact != null && interact.WasPressedThisFrame()) ShopMenu.Open(mine, shop);
        }
        offering = text != null;
        screen.Prompt(ShopMenu.IsOpen ? null : text);
    }

    private static bool NearShop(PlayerMovement mine, out Vector3 shop)
    {
        for (int stretch = 0; stretch < TowerLayout.Segments; stretch++)
        {
            shop = TowerLayout.ShopAt(stretch);
            Vector3 apart = mine.transform.position - shop;
            if (Mathf.Abs(apart.y) < 1.5f && Flat(apart) <= ShopReach) return true;
        }
        shop = default;
        return false;
    }

    // Once the lanes are through, this PC's player, if alive and in no lane (left in the hall below, or outside), is
    // brought to the next hall; and anyone still not there when the gathering's time is up.
    private void Fetch(PlayerMovement mine)
    {
        if (phase != Phase.Gather) { fetched = false; return; }
        if (mine == null || !mine.IsInitialized || mine.IsDead || fetched || TowerLayout.IsLastSegment(segment)) return;
        int next = segment + 1;
        if (TowerLayout.InHall(mine.transform.position, next)) return;
        bool astray = TowerLayout.LaneAt(mine.transform.position, segment) < 0;
        if (!astray && (!counting || count > 0f)) return;
        fetched = true;
        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        mine.MoveTo(TowerLayout.HallSpot(next) + new Vector3(scatter.x, 0f, scatter.y));
    }

    // The last floor plays its song while its lanes are climbed and under the finale.
    private void PlayMusic()
    {
        bool last = TowerLayout.IsLastSegment(segment) && (phase == Phase.Lanes || phase == Phase.Finale);
        music.Want(last ? finaleSong : "", 0f);
        music.Tick();
    }

    // ---- The end ----

    // The tower is through, on every PC: ENFILADED!! over the frozen moment, the money, the results kept for the hub,
    // the class's experience; the closing card after the banner.
    private void Finish()
    {
        if (finished) return;
        finished = true;
        float seconds = Time.time - startedAt;
        string clock = Mathf.FloorToInt(seconds / 60f).ToString("00") + ":" + Mathf.FloorToInt(seconds % 60f).ToString("00");
        EnfiladedFinale.Play(Texts.Get("tower.finale.heading"), Texts.Format("tower.finale.under", DayText(seed), clock), finaleSeconds);
        CombatFeedHUD.Award(Texts.Get("tower.feed.clear"), clearBonus);
        string today = seed.ToString();
        if (PlayerPrefs.GetString(FirstTodayKey, "") != today)
        {
            PlayerPrefs.SetString(FirstTodayKey, today);
            PlayerPrefs.Save();
            CombatFeedHUD.Award(Texts.Get("tower.feed.today"), firstTodayBonus);
        }
        PlayerRecords.ChapterEscaped(gameObject.scene.name);
        climb = ClassGrowth.Settle();
        MatchResults.Capture(gameObject.scene.name, Texts.Get("tower.card.title"), gameObject.scene.name, climb, _ => true);
        cardAt = Time.unscaledTime + finaleSeconds - .3f;
        if (tracker != null) tracker.SetObjective(new MissionObjective("tower.end", Texts.Get("tower.objective.done"), ""));
    }

    private void ShowCardWhenDue()
    {
        if (finished && Time.unscaledTime >= nextResults) { nextResults = Time.unscaledTime + .5f; MatchResults.Refresh(); }
        if (cardAt < 0f || Time.unscaledTime < cardAt) return;
        cardAt = -1f;
        bool hub = HubFlow.IsBuilt;
        float seconds = Mathf.Max(4f, outroSeconds);
        if (screen != null) screen.ShowCard(Texts.Get("tower.card.title"), Texts.Lines(Texts.Get("tower.card.outro")), seconds, hub ? null : Results());
        if (climb != null && !hub) RankClimb.Play(climb, seconds);
        climb = null;
    }

    // With no hub to show them, the results under the closing card: this player's kills and money.
    private static string Results()
    {
        var mine = PlayerRoster.Local;
        int kills = mine != null && mine.TryGetComponent(out PlayerTally own) ? own.Kills : 0;
        return Texts.Get("campaign.results.kills") + "<pos=50%>" + kills + "\n" + Texts.Get("campaign.results.money") + "<pos=50%><color=#FFD36B>"
               + Wallet.Format(Wallet.Balance) + "</color>\n";
    }

    // "11월 12일 훈련" from the day's seed.
    private static string DayText(int day) => Texts.Format("tower.day", day / 100 % 100, day % 100);

    // As the mouse look: not while the cursor is free (chat, menus), the window is in the background or the console is open.
    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard
        && !ShopMenu.TakesKeyboard && !PerkCardScreen.TakesKeyboard;
}
