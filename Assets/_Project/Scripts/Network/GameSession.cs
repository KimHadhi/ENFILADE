using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A room shared with other people. Whoever makes it hosts on their own PC and the others join by address. Alone, none
/// of this exists, so solo play has no network at all. Lasts across scene loads until the room ends.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour, INetworkPrefabInstanceHandler
{
    public const ushort Port = 7777;
    public const string DefaultAddress = "127.0.0.1";
    private const float SpawnSpacing = 1.5f;
    // The transport tries once a second, so this is how long a failed join takes.
    private const int ConnectAttempts = 10;
    private static GameSession current;
    private NetworkManager network;
    private UnityTransport transport;
    private GameObject playerPrefab;
    private int spawned;

    public static bool IsOnline => current != null;
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

    public static void Host(string map)
    {
        if (IsOnline) return;
        var session = Create();
        session.transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        if (!session.network.StartHost()) { session.End("방을 만들지 못했습니다. 포트 " + Port + "을 다른 프로그램이 쓰고 있는지 확인하세요."); return; }
        session.network.SceneManager.OnLoadEventCompleted += session.OnMapLoaded;
        session.network.SceneManager.OnSynchronizeComplete += session.SpawnPlayer;
        session.network.SceneManager.LoadScene(map, LoadSceneMode.Single);
    }

    public static void Join(string typed)
    {
        if (IsOnline) return;
        string address = ParseAddress(typed);
        if (address == null) { Status = "주소 형식이 맞지 않습니다. 예: 192.168.0.5"; return; }
        var session = Create();
        Status = address + "에 접속하는 중입니다...";
        session.transport.MaxConnectAttempts = ConnectAttempts;
        session.transport.SetConnectionData(address, Port);
        session.network.OnClientDisconnectCallback += session.OnDisconnected;
        if (!session.network.StartClient()) session.End("접속을 시작하지 못했습니다. 주소를 확인하세요.");
    }

    /// <summary>Leaves the room and returns to the main menu.</summary>
    public static void Leave() { if (current != null) current.End(""); }

    private static GameSession Create()
    {
        var host = new GameObject("Game Session");
        host.SetActive(false);
        var session = host.AddComponent<GameSession>();
        session.transport = host.AddComponent<UnityTransport>();
        session.network = host.AddComponent<NetworkManager>();
        session.network.NetworkConfig = new NetworkConfig { NetworkTransport = session.transport };
        session.playerPrefab = PlayerFactory.Prefab;
        host.SetActive(true);
        session.network.AddNetworkPrefab(session.playerPrefab);
        session.network.PrefabHandler.AddHandler(session.playerPrefab, session);
        current = session;
        Status = "";
        return session;
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
        End(string.IsNullOrEmpty(reason) ? "연결이 끊겼습니다. 주소와 방이 열려 있는지 확인하세요." : reason);
    }

    private void End(string message)
    {
        if (current != this) return;
        current = null;
        Status = message;
        network.Shutdown();
        Destroy(gameObject);
        if (SceneManager.GetActiveScene().name != MainMenuScreen.SceneName) SceneManager.LoadScene(MainMenuScreen.SceneName);
    }
}
