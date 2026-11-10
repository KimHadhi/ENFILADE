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
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A room shared with other people. Whoever makes it hosts on their own PC, and the others join over the local network
/// or through Unity's relay. Alone, none of this exists, so solo play has no network at all. Lasts across scene loads
/// until the room ends.
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
    private static GameSession current;
    private NetworkManager network;
    private CountingTransport transport;
    private InternetRoom internetRoom;
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
    private static void ResetStatics() { current = null; Status = ""; }

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
        var session = Create();
        session.roomMap = map; session.roomTitle = title; session.maxPlayers = maxPlayers;
        session.network.ConnectionApprovalCallback = session.Approve;
        if (internet) { session.HostOnInternet(); return; }
        session.transport.SetConnectionData(DefaultAddress, Port, "0.0.0.0");
        if (!session.network.StartHost()) { session.End("방을 만들지 못했습니다. 포트 " + Port + "을 다른 프로그램이 쓰고 있는지 확인하세요."); return; }
        session.gameObject.AddComponent<LanRoomBeacon>();
        session.OpenRoom();
    }

    public static void Join(string typed)
    {
        if (IsOnline) return;
        string address = ParseAddress(typed);
        if (address == null) { Status = "주소 형식이 맞지 않습니다. 예: 192.168.0.5"; return; }
        var session = StartJoining(address);
        session.transport.SetConnectionData(address, Port);
        if (!session.network.StartClient()) session.End("접속을 시작하지 못했습니다. 주소를 확인하세요.");
    }

    public static void JoinInternet(RoomInfo room)
    {
        if (IsOnline) return;
        StartJoining(room.title).JoinOnInternet(room.id);
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

    private static GameSession Create()
    {
        var host = new GameObject("Game Session");
        host.SetActive(false);
        var session = host.AddComponent<GameSession>();
        session.transport = host.AddComponent<CountingTransport>();
        session.transport.DisconnectTimeoutMS = SilenceLimitMs;
        // The build's log filled the default 128-packet receive queue and dropped packets (friend feedback C16), most
        // likely as a chapter loads and every enemy, corpse and player reports at once.
        session.transport.MaxPacketQueueSize = ReceiveQueue;
        session.network = host.AddComponent<NetworkManager>();
        session.network.NetworkConfig = new NetworkConfig { NetworkTransport = session.transport, ConnectionApproval = true };
        session.playerPrefab = PlayerFactory.Prefab;
        host.SetActive(true);
        session.network.AddNetworkPrefab(session.playerPrefab);
        session.network.PrefabHandler.AddHandler(session.playerPrefab, session);
        foreach (var enemy in Resources.LoadAll<NetworkObject>(EnemyPrefabs)) session.network.AddNetworkPrefab(enemy.gameObject);
        current = session;
        Status = "";
        return session;
    }

    private static GameSession StartJoining(string target)
    {
        var session = Create();
        Status = target + "에 접속하는 중입니다...";
        session.transport.MaxConnectAttempts = ConnectAttempts;
        session.network.OnClientConnectedCallback += _ => session.wasConnected = true;
        session.network.OnClientDisconnectCallback += session.OnDisconnected;
        return session;
    }

    private async void HostOnInternet()
    {
        Status = "인터넷 방을 만드는 중입니다...";
        bool opened = await EnterInternetRoom(cancel => internetRoom.Create(roomTitle, roomMap, maxPlayers, cancel),
            "방을 만들지 못했습니다. 인터넷 연결을 확인하세요.");
        if (!opened) return;
        Status = "";
        OpenRoom();
    }

    private async void JoinOnInternet(string id)
    {
        await EnterInternetRoom(cancel => internetRoom.Join(id, cancel), "방에 들어가지 못했습니다. 방이 닫혔거나 가득 찼을 수 있습니다.");
    }

    // True when the room is ready and still wanted: the player can give up while this waits on Unity's services.
    private async Task<bool> EnterInternetRoom(Func<CancellationToken, Task> enter, string failed)
    {
        internetRoom = new InternetRoom(network, transport, () => OnDisconnected(0));
        try { await enter(destroyCancellationToken); }
        catch (OperationCanceledException) { return false; }
        catch (Exception e) when (OnlineServices.IsServiceError(e))
        {
            Debug.LogWarning($"[GameSession] {e.Message}");
            End(failed);
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
        response.Reason = response.Approved ? string.Empty : "방이 가득 찼습니다.";
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
        string reason = network.DisconnectReason;
        if (string.IsNullOrEmpty(reason))
            reason = wasConnected ? "방이 닫혔거나 연결이 끊겼습니다." : "접속하지 못했습니다. 방이 열려 있는지 확인하세요.";
        End(reason);
    }

    private void End(string message)
    {
        if (current != this || ending) return;
        ending = true;
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
        if (SceneManager.GetActiveScene().name != MainMenuScreen.SceneName) SceneManager.LoadScene(MainMenuScreen.SceneName);
    }
}
