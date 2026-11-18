using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A room shared with other people. Whoever makes it hosts on their own PC, and the others join over the local network,
/// straight over the internet when the two routers let them, or through Unity's relay (InternetRoom). Alone, none of
/// this exists, so solo play has no network at all. Lasts across scene loads until the room ends.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour, INetworkPrefabInstanceHandler
{
    public const ushort Port = 7777;
    public const string DefaultAddress = "127.0.0.1";
    private const float SpawnSpacing = 1.5f;
    // The transport tries once a second, so this is how long a failed join takes.
    private const int ConnectAttempts = 10;
    // How long a silent connection lasts when a PC crashes or loses its network, without any goodbye (Network.csv
    // silenceLimit, s). It has to outlast the longest frame a PC makes while in a room: the first frame of a scene builds
    // every player's body, guns and cloth at once and took 9.2 to 21.5 s here (Logs/loads.txt, Logs/hitches.txt), and a PC
    // sends nothing during it. At 10 s the other end let go of a friend whose load ran longer than that.
    private static int SilenceLimitMs => Mathf.RoundToInt(Mathf.Clamp(NetSmoothing.Number("silenceLimit", 30f), 5f, 120f) * 1000f);
    // A room lost while in it is gone back into by way of the title screen; if that screen takes longer than this to
    // come up (s), the try is dropped rather than made at some later visit there.
    private const float ComeBackWithin = 60f;
    // Packets the transport may hold waiting each frame (UnityTransport's default is 128).
    private const int ReceiveQueue = 512;
    // Every enemy lives here, so all PCs know the same network prefabs before the first one spawns.
    private const string EnemyPrefabs = "Enemies";
    // How Netcode's own description of a disconnect begins, when the host sent none.
    private const string NetcodeOwnReason = "[Disconnect Event]";
    private static GameSession current;
    // A room to join once more: one whose straight connection was found but never carried the game (through the relay
    // this time), or one this PC was in and lost without a word (comingBack: by the room again, or by the address it
    // was joined at, from the title screen as any join). The join under way is such a return (returning): lost again
    // before it gets in, it is not tried a second time.
    private static RoomInfo again;
    private static string againAddress;
    private static bool comingBack, returning;
    private static UnityEngine.Events.UnityAction<Scene, LoadSceneMode> whenMenuIsUp;
    private NetworkManager network;
    // An internet room's host takes friends two ways at once (DualTransport): `transport` is then the relay's half.
    private CountingTransport transport, direct;
    private DualTransport dual;
    private InternetRoom internetRoom;
    private RoomInfo joining;
    // The address this PC joined at (the LAN, or typed), for coming back; the room's own scene load on this PC while it
    // runs (Netcode reads it), which an ending waits out.
    private string joinedAddress;
    private NetworkSceneManager watched;
    private AsyncOperation roomLoad;
    private GameObject playerPrefab;
    private int spawned, maxPlayers;
    private bool wasConnected, ending;
    private string roomTitle, roomMap;
    private readonly string roomId = Guid.NewGuid().ToString("N");

    public static bool IsOnline => current != null;
    /// <summary>The room's network, or null outside a room.</summary>
    public static NetworkManager Network => current != null ? current.network : null;
    /// <summary>In someone else's room, where the host runs the enemies and the waves.</summary>
    public static bool IsGuest => current != null && !current.network.IsServer;
    /// <summary>What the main menu shows about the room: joining, or why the last one ended.</summary>
    public static string Status { get; private set; } = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null; again = null; againAddress = null; comingBack = returning = false; Status = "";
        if (whenMenuIsUp != null) SceneManager.sceneLoaded -= whenMenuIsUp;
        whenMenuIsUp = null;
    }

    /// <summary>This PC's addresses a friend can join, on the local network or a virtual LAN.</summary>
    public static IEnumerable<string> LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(card => card.OperationalStatus == OperationalStatus.Up && card.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(card => card.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(address => address.Address.ToString());

    /// <summary>The IPv4 address that was typed, or null. A bare "123" would otherwise be tried as a host name.</summary>
    public static string ParseAddress(string typed)
    {
        string text = (typed ?? string.Empty).Trim();
        if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return DefaultAddress;
        var parts = text.Split('.');
        return parts.Length == 4 && parts.All(part => byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)) ? text : null;
    }

    /// <summary>How the room is carried (/net, friend feedback C16): an internet room's link, or a LAN or typed address's direct one.</summary>
    public static string LinkName => current == null ? "" : current.internetRoom != null ? current.internetRoom.LinkName : "직접 연결(LAN·주소)";

    /// <summary>The friend with this Netcode client id reached this PC's internet room straight, not through the relay.</summary>
    public static bool CameDirect(ulong client) => current != null && current.dual != null && current.dual.IsDirect(client);

    /// <summary>The room this PC hosts, as the server list shows it, or null once it is closing.</summary>
    public static RoomInfo DescribeRoom() => current == null || current.ending ? null : new RoomInfo
    {
        id = current.roomId, title = current.roomTitle, map = current.roomMap,
        players = current.network.ConnectedClients.Count, maxPlayers = current.maxPlayers
    };

    /// <summary>Opens a room. A room is either on the internet or on the local network: the transport can't do both.</summary>
    public static void Host(string map, string title, int maxPlayers, bool internet)
    {
        if (IsOnline) return;
        var session = Create(internet && InternetRoom.DirectAllowed);
        session.roomMap = map; session.roomTitle = title; session.maxPlayers = maxPlayers;
        session.network.ConnectionApprovalCallback = session.Approve;
        if (internet) { session.HostOnInternet(); return; }
        session.transport.SetConnectionData(DefaultAddress, Port, "0.0.0.0");
        if (!session.network.StartHost()) { session.End(Texts.Format("net.host.portbusy", Port)); return; }
        session.gameObject.AddComponent<LanRoomBeacon>();
        session.OpenRoom();
    }

    public static void Join(string typed)
    {
        if (IsOnline) return;
        string address = ParseAddress(typed);
        if (address == null) { Status = Texts.Get("net.join.badaddress"); return; }
        var session = StartJoining(address);
        session.joinedAddress = address;
        session.transport.SetConnectionData(address, Port);
        if (!session.network.StartClient()) session.End(Texts.Get("net.join.nostart"));
    }

    public static void JoinInternet(RoomInfo room)
    {
        if (IsOnline) return;
        var session = StartJoining(room.title);
        session.joining = room;
        session.JoinOnInternet(room.id);
    }

    /// <summary>Leaves the room and returns to the main menu.</summary>
    public static void Leave() { if (current != null) current.End(""); }

    /// <summary>
    /// The host takes the whole room to another map (the campaign's next chapter, CampaignRun). Everyone's player goes
    /// with the old scene and is made again at the new map's start, as when the room opened. False if it can't.
    /// </summary>
    public static bool ChangeMap(string map)
    {
        if (current == null || current.ending || !current.network.IsServer || !Application.CanStreamedLevelBeLoaded(map)) return false;
        // Only a load that started changes the room: one refused (another load still running) leaves the map's name and
        // the players' places as they are.
        if (current.network.SceneManager.LoadScene(map, LoadSceneMode.Single) != SceneEventProgressStatus.Started) return false;
        current.roomMap = map;
        current.spawned = 0;
        return true;
    }

    // `twoWays` is an internet room's host, who takes friends straight and through the relay at once.
    private static GameSession Create(bool twoWays = false)
    {
        var host = new GameObject("Game Session");
        host.SetActive(false);
        var session = host.AddComponent<GameSession>();
        session.transport = AddTransport(host);
        NetworkTransport used = session.transport;
        if (twoWays)
        {
            session.direct = AddTransport(host);
            session.dual = host.AddComponent<DualTransport>();
            session.dual.Use(session.transport, session.direct);
            used = session.dual;
        }
        session.network = host.AddComponent<NetworkManager>();
        session.network.NetworkConfig = new NetworkConfig { NetworkTransport = used, ConnectionApproval = true, TickRate = TickRate };
        // Position comes in TickRate times a second; the copies show it this far behind to start with (BufferTicks), and
        // then as far as the updates' real lateness asks (NetSmoothing, Network.csv smoothing).
        NetSmoothing.Begin(BufferTicks);
        session.playerPrefab = PlayerFactory.Prefab;
        host.SetActive(true);
        session.network.AddNetworkPrefab(session.playerPrefab);
        session.network.PrefabHandler.AddHandler(session.playerPrefab, session);
        foreach (var enemy in Resources.LoadAll<NetworkObject>(EnemyPrefabs)) session.network.AddNetworkPrefab(enemy.gameObject);
        current = session;
        Status = "";
        return session;
    }

    // Netcode's tick: how often whatever changed goes out to the others, a teammate's look and position included
    // (PlayerNetworkPose sends every tick). Netcode's own default is 30; the user asked whether it could be 120 (2026-11-11
    // playtest: "팀원 30hz로 보내나. 이거 120hz까지 늘릴 수 있나"), so it is Network.csv's tickRate, 120. Zombies' positions
    // go out as often, which /net shows; every PC in a room must use the same number or joining fails.
    private static uint TickRate => (uint)Mathf.Clamp(Mathf.RoundToInt(NetSmoothing.Number("tickRate", 120f)), 10, 240);

    // A copy is drawn a little in the past so the next position has always come in by the time it is needed: two ticks
    // was Netcode's own buffer at 30 a second (66 ms), which at 120 would leave 17 ms for every bump in the connection.
    // Network.csv's bufferSeconds keeps it about that long in time instead, as extra ticks on top of Netcode's two.
    private static int BufferTicks => Mathf.Max(0, Mathf.RoundToInt(NetSmoothing.Number("bufferSeconds", .05f) * TickRate) - 2);

    private static CountingTransport AddTransport(GameObject host)
    {
        var transport = host.AddComponent<CountingTransport>();
        transport.DisconnectTimeoutMS = SilenceLimitMs;
        // The build's log filled the default 128-packet receive queue and dropped packets (friend feedback C16), most
        // likely as a chapter loads and every enemy, corpse and player reports at once.
        // That queue was sized at 30 ticks a second; packets come about once a ticking frame, so it grows with the tick
        // (2048 at 120: the 2026-11-11 review).
        transport.MaxPacketQueueSize = Mathf.Max(ReceiveQueue, Mathf.CeilToInt(ReceiveQueue * TickRate / 30f));
        return transport;
    }

    private static GameSession StartJoining(string target)
    {
        var session = Create();
        Status = Texts.Format("net.joining", target);
        session.transport.MaxConnectAttempts = ConnectAttempts;
        // In: a return after a loss has succeeded, so a later loss may be come back from again.
        session.network.OnClientConnectedCallback += _ => { session.wasConnected = true; returning = false; };
        session.network.OnClientDisconnectCallback += session.OnDisconnected;
        return session;
    }

    // The room's own scene loads on this PC are kept while they run (OnRoomScene): an ending waits one out.
    private void Update()
    {
        var scenes = network != null && network.IsListening ? network.SceneManager : null;
        if (scenes == watched) return;
        if (watched != null) watched.OnSceneEvent -= OnRoomScene;
        watched = scenes;
        if (watched != null) watched.OnSceneEvent += OnRoomScene;
    }

    private void OnRoomScene(SceneEvent change)
    {
        if (change == null || network == null || change.ClientId != network.LocalClientId) return;
        if (change.SceneEventType == SceneEventType.Load && change.AsyncOperation != null) roomLoad = change.AsyncOperation;
    }

    private async void HostOnInternet()
    {
        Status = Texts.Get("net.host.creating");
        bool opened = await EnterInternetRoom(cancel => internetRoom.Create(roomTitle, roomMap, maxPlayers, cancel),
            Texts.Get("net.host.failed"));
        if (!opened) return;
        Status = "";
        OpenRoom();
    }

    private async void JoinOnInternet(string id)
    {
        await EnterInternetRoom(cancel => internetRoom.Join(id, cancel), Texts.Get("net.join.internetfailed"));
    }

    // True when the room is ready and still wanted: the player can give up while this waits on Unity's services.
    private async Task<bool> EnterInternetRoom(Func<CancellationToken, Task> enter, string failed)
    {
        internetRoom = new InternetRoom(network, transport, direct, dual, () => OnDisconnected(0));
        try { await enter(destroyCancellationToken); }
        catch (OperationCanceledException) { return false; }
        catch (Exception e) when (OnlineServices.IsServiceError(e) || e is InternetRoom.StartFailed)
        {
            Debug.LogWarning($"[GameSession] {e.Message}");
            End(failed, true);
            return false;
        }
        // Anything else going wrong on the way in must still end the room: left half open it would say "making the room"
        // for ever and refuse every new one.
        catch (Exception e)
        {
            Debug.LogException(e);
            End(failed, true);
            return false;
        }
        if (ending) internetRoom.Close();
        return !ending;
    }

    private void OpenRoom()
    {
        wasConnected = true;
        network.OnClientDisconnectCallback += OnGuestGone;
        network.SceneManager.OnLoadEventCompleted += OnMapLoaded;
        network.SceneManager.OnSynchronizeComplete += SpawnPlayer;
        network.SceneManager.LoadScene(roomMap, LoadSceneMode.Single);
    }

    private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = network.ConnectedClients.Count < maxPlayers;
        response.Reason = response.Approved ? string.Empty : Texts.Get("net.full");
    }

    private void OnMapLoaded(string scene, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        foreach (ulong client in completed) SpawnPlayer(client);
    }

    private void SpawnPlayer(ulong client)
    {
        if (!network.IsServer || !network.ConnectedClients.TryGetValue(client, out var info) || info.PlayerObject != null) return;
        var point = FindFirstObjectByType<PlayerSpawnPoint>();
        Vector3 position = point != null ? point.transform.position : Vector3.zero;
        Quaternion rotation = point != null ? point.transform.rotation : Quaternion.identity;
        // Side by side from the start point, and round again after a room's worth: friends who leave and come back
        // would otherwise be placed further and further along, in the end past the floor.
        position += rotation * Vector3.right * (SpawnSpacing * (spawned++ % Mathf.Max(1, maxPlayers)));
        network.SpawnManager.InstantiateAndSpawn(playerPrefab.GetComponent<NetworkObject>(), client,
            destroyWithScene: true, isPlayerObject: true, position: position, rotation: rotation);
    }

    NetworkObject INetworkPrefabInstanceHandler.Instantiate(ulong owner, Vector3 position, Quaternion rotation)
    {
        var player = owner == network.LocalClientId ? PlayerFactory.CreateLocal(position, rotation)
            : PlayerFactory.CreateRemote(position, rotation, null, true);
        return player.GetComponent<NetworkObject>();
    }

    void INetworkPrefabInstanceHandler.Destroy(NetworkObject player) => Destroy(player.gameObject);

    // The host's log of a friend going, with how long this PC's own last frame took: a frame longer than the silence
    // limit is this PC having been the silent one (Player.log in a build).
    private void OnGuestGone(ulong client)
    {
        if (network == null || !network.IsServer || client == network.LocalClientId) return;
        Debug.Log($"[GameSession] client {client} left or was lost in '{SceneManager.GetActiveScene().name}'; this PC's last frame took {Time.unscaledDeltaTime:0.0} s");
    }

    private void OnDisconnected(ulong client)
    {
        // Only what the host itself said counts (the room is full, the host closed it). Without that Netcode fills in a
        // technical line of its own, which is no use to a player; and a host that said something was reached, so the way
        // there works.
        string reason = network.DisconnectReason;
        bool told = !string.IsNullOrEmpty(reason) && !reason.StartsWith(NetcodeOwnReason, StringComparison.Ordinal);
        Debug.Log($"[GameSession] disconnected in '{SceneManager.GetActiveScene().name}': {(told ? "the host said '" + reason + "'" : "no word from the host")}, "
            + $"was in {wasConnected}, a room load in flight {roomLoad != null && !roomLoad.isDone}, this PC's last frame took {Time.unscaledDeltaTime:0.0} s");
        if (!told) reason = wasConnected ? Texts.Get("net.lost") : Texts.Get("net.join.failed");
        End(reason, !told);
    }

    // `failed` is an ending nobody asked for, as opposed to leaving.
    private void End(string message, bool failed = false)
    {
        if (current != this || ending) return;
        ending = true;
        bool server = network.IsServer;
        // The straight way answered the probe and then didn't carry the game: the same room once more, and this time
        // InternetRoom goes through the relay. Never when the player was already in, or gave up.
        if (failed && !wasConnected && joining != null && internetRoom != null && internetRoom.GiveUpDirect())
        {
            again = joining;
            message = Texts.Get("net.join.relay");
        }
        // In the room and lost without a word from the host (a host closing its room says so, below): back into the same
        // room once. A long load is the usual cause (SilenceLimitMs; the relay's own limit is ten seconds whatever this
        // game sets), and the room is still there. It used to leave the friend alone in whatever scene came up (user,
        // 2026-11-18: "게스트가 함께 허브로 모이지 않고 개인 허브로 튕기는").
        else if (failed && wasConnected && !server && !returning && (joining != null || !string.IsNullOrEmpty(joinedAddress)))
        {
            again = joining;
            againAddress = joining == null ? joinedAddress : null;
            comingBack = returning = true;
            message = Texts.Get("net.rejoining");
        }
        else returning = false;
        Status = message;
        // A host closing its room tells the others so: their PCs then know it was closed, not lost, and go to the title
        // screen with that word instead of trying to come back.
        if (server && !failed)
            foreach (ulong client in new List<ulong>(network.ConnectedClientsIds))
                if (client != network.LocalClientId) network.DisconnectClient(client, Texts.Get("net.closed"));
        internetRoom?.Close();
        network.Shutdown();
        StartCoroutine(FinishEnding());
    }

    // The goodbye to the others goes out when the shutdown runs, so nothing is destroyed before it has.
    private IEnumerator FinishEnding()
    {
        yield return new WaitWhile(() => network.ShutdownInProgress);
        // A scene the room was loading on this PC wakes first, with the room still counted as this PC's: let go of
        // before that, the scene woke as a game alone (PlayerSpawnPoint made a player), and the way out started beside a
        // load still in flight.
        var load = roomLoad;
        while (load != null && !load.isDone) yield return null;
        current = null;
        Destroy(gameObject);
        var retry = again;
        string address = againAddress;
        bool back = comingBack;
        again = null;
        againAddress = null;
        comingBack = false;
        bool atMenu = SceneManager.GetActiveScene().name == MainMenuScreen.SceneName;
        if (back && !atMenu)
        {
            // From the title screen, the way any join is made; the room's scene is let go of first.
            if (!ComeBackFromMenu(retry, address)) returning = false;
        }
        else if (retry != null) JoinInternet(retry);
        else if (address != null) Join(address);
        else if (!atMenu) SceneLoad.Go(MainMenuScreen.SceneName);
    }

    // To the title screen, and into the room again as it comes up. False when the screen can't be gone to now.
    private static bool ComeBackFromMenu(RoomInfo room, string address)
    {
        if (whenMenuIsUp != null) SceneManager.sceneLoaded -= whenMenuIsUp;
        float until = Time.realtimeSinceStartup + ComeBackWithin;
        whenMenuIsUp = (scene, mode) =>
        {
            if (scene.name != MainMenuScreen.SceneName) return;
            SceneManager.sceneLoaded -= whenMenuIsUp;
            whenMenuIsUp = null;
            // Not if the player was quicker and is in a room already, or the screen took too long to come.
            if (IsOnline || Time.realtimeSinceStartup > until) { returning = false; return; }
            if (room != null) JoinInternet(room);
            else Join(address);
        };
        SceneManager.sceneLoaded += whenMenuIsUp;
        if (SceneLoad.Go(MainMenuScreen.SceneName)) return true;
        SceneManager.sceneLoaded -= whenMenuIsUp;
        whenMenuIsUp = null;
        return false;
    }

    // Play mode stopped or the game quit without the room ending: the router's port and the sockets' threads must not
    // outlive it.
    private void OnDestroy() => internetRoom?.ReleaseLocal();
}
