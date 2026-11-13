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
    // How long a silent connection lasts when a PC crashes or loses its network, without any goodbye.
    private const int SilenceLimitMs = 10000;
    // Packets the transport may hold waiting each frame (UnityTransport's default is 128).
    private const int ReceiveQueue = 512;
    // Every enemy lives here, so all PCs know the same network prefabs before the first one spawns.
    private const string EnemyPrefabs = "Enemies";
    // How Netcode's own description of a disconnect begins, when the host sent none.
    private const string NetcodeOwnReason = "[Disconnect Event]";
    private static GameSession current;
    // A room whose straight connection was found but never carried the game: joined once more, through the relay.
    private static RoomInfo again;
    private NetworkManager network;
    // An internet room's host takes friends two ways at once (DualTransport): `transport` is then the relay's half.
    private CountingTransport transport, direct;
    private DualTransport dual;
    private InternetRoom internetRoom;
    private RoomInfo joining;
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
    private static void ResetStatics() { current = null; again = null; Status = ""; }

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
        current.roomMap = map;
        current.spawned = 0;
        return current.network.SceneManager.LoadScene(map, LoadSceneMode.Single) == SceneEventProgressStatus.Started;
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
    private static uint TickRate => (uint)Mathf.Clamp(Mathf.RoundToInt(TableNumber("tickRate", 120f)), 10, 240);

    // A copy is drawn a little in the past so the next position has always come in by the time it is needed: two ticks
    // was Netcode's own buffer at 30 a second (66 ms), which at 120 would leave 17 ms for every bump in the connection.
    // Network.csv's bufferSeconds keeps it about that long in time instead, as extra ticks on top of Netcode's two.
    private static int BufferTicks => Mathf.Max(0, Mathf.RoundToInt(TableNumber("bufferSeconds", .05f) * TickRate) - 2);

    private static float TableNumber(string row, float fallback)
        => GameTables.TryGetRow("Network", row, out var cells) && cells.TryGetValue("value", out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

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
        session.network.OnClientConnectedCallback += _ => session.wasConnected = true;
        session.network.OnClientDisconnectCallback += session.OnDisconnected;
        return session;
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
        if (ending) internetRoom.Close();
        return !ending;
    }

    private void OpenRoom()
    {
        wasConnected = true;
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
        position += rotation * Vector3.right * (SpawnSpacing * spawned++);
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

    private void OnDisconnected(ulong client)
    {
        // Only what the host itself said counts (the room is full). Without that Netcode fills in a technical line of
        // its own, which is no use to a player; and a host that said something was reached, so the way there works.
        string reason = network.DisconnectReason;
        bool told = !string.IsNullOrEmpty(reason) && !reason.StartsWith(NetcodeOwnReason, StringComparison.Ordinal);
        if (!told) reason = wasConnected ? Texts.Get("net.lost") : Texts.Get("net.join.failed");
        End(reason, !told);
    }

    // `failed` is an ending nobody asked for, as opposed to leaving.
    private void End(string message, bool failed = false)
    {
        if (current != this || ending) return;
        ending = true;
        // The straight way answered the probe and then didn't carry the game: the same room once more, and this time
        // InternetRoom goes through the relay. Never when the player was already in, or gave up.
        if (failed && !wasConnected && joining != null && internetRoom != null && internetRoom.GiveUpDirect())
        {
            again = joining;
            message = Texts.Get("net.join.relay");
        }
        Status = message;
        internetRoom?.Close();
        network.Shutdown();
        StartCoroutine(FinishEnding());
    }

    // The goodbye to the others goes out when the shutdown runs, so nothing is destroyed before it has.
    private IEnumerator FinishEnding()
    {
        yield return new WaitWhile(() => network.ShutdownInProgress);
        current = null;
        Destroy(gameObject);
        var retry = again;
        again = null;
        if (retry != null) JoinInternet(retry);
        else if (SceneManager.GetActiveScene().name != MainMenuScreen.SceneName) SceneLoad.Go(MainMenuScreen.SceneName);
    }

    // Play mode stopped or the game quit without the room ending: the router's port and the sockets' threads must not
    // outlive it.
    private void OnDestroy() => internetRoom?.ReleaseLocal();
}
