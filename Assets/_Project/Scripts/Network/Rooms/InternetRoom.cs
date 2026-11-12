using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using Unity.Services.Qos;
using UnityEngine;

/// <summary>
/// The internet side of a room: Unity's lobby lists it, and every room has a place on the nearest relay, which always
/// works. On top of that the host and each friend try to reach each other straight (user, 2026-11-10: a neighbour's
/// room showed 100 ms because it went through the Tokyo relay; direct first, the relay only as the fallback):
///   - The host opens a PunchSocket on the game's port, learns what it looks like from outside (the router's forwarded
///     port when UPnP gives one, RouterPortMap; a STUN server's answer otherwise) and writes that into the room, readable
///     by its members only. Its transport takes friends both ways at once (DualTransport).
///   - A friend opens a PunchSocket too and joins the room with their own outside address attached, which makes the
///     host open its router for them. Then the friend probes the host's addresses for a few seconds. An answer means the
///     game's transport connects straight, from the very port that was probed; no answer means the relay, as before.
/// So a failed attempt costs a friend those few seconds and nothing else, and one that passed the probe but still
/// didn't connect is tried once more through the relay (GameSession). Network.csv switches all of it off (direct = 0).
/// GameSession keeps running the connection itself; this only decides which way the transport goes.
/// </summary>
public sealed class InternetRoom : INetworkHandler
{
    public const string MapKey = "map", RegionKey = "region", LatencyKey = "relayMs";
    // Where the host can be reached straight, for the room's members; and where a friend will probe from, for the host.
    private const string DirectKey = "direct", EndpointKey = "ep", Layout = "1", Nothing = "-";
    private const string Table = "Network", Column = "value", Loopback = "127.0.0.1";
    private const string DefaultStunServers = "stun.l.google.com:19302 stun.cloudflare.com:3478";
    // How long looking a server's name up and waiting for its answer may hold up opening or joining a room.
    private const int ResolveMs = 1000, StunMs = 1200;
    // After a probe was answered, answers to the probes sent just before it are still on their way: they must find this
    // socket, not the game's transport on the same port.
    private const int SettleMs = 200;
    // A way the probe has just proven connects at once or not at all (the transport tries once a second).
    private const int DirectAttempts = 4;
    private const int MostCandidates = 4, LeaveWaitMs = 3000;
    private const uint HomeMask = 0xFFFFFF00;
    // The game's port first, then a few more in case another PC behind the same router holds it.
    private static readonly ushort[] DirectPorts =
        { GameSession.Port, (ushort)(GameSession.Port + 1), (ushort)(GameSession.Port + 2), (ushort)(GameSession.Port + 3) };
    // Rooms where a straight connection was found and then failed: the next join there goes through the relay.
    private static readonly HashSet<string> relayOnly = new HashSet<string>();
    // The room being left: the lobby keeps one room per player, so the next one waits for it.
    private static Task leaving = Task.CompletedTask;

    private readonly NetworkManager network;
    private readonly CountingTransport transport, direct;
    private readonly DualTransport dual;
    private readonly Action lost;
    private readonly SynchronizationContext main = SynchronizationContext.Current;
    private readonly HashSet<string> greeted = new HashSet<string>();
    private ISession session;
    private RouterPortMap portMap;
    private PunchSocket punch;
    private NetworkConfiguration waiting;
    private IPEndPoint hostAt;
    private ulong token;
    private int maxPlayers, bindPort;
    private bool closed, joined;
    private string roomId, note;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        relayOnly.Clear();
        leaving = Task.CompletedTask;
    }

    /// <summary>Rooms try a straight connection before the relay (Network.csv, direct).</summary>
    public static bool DirectAllowed => Number("direct", 1f) > .5f;

    /// <summary>
    /// How this room is carried, for /net (friend feedback C16): straight to the host and how, or through which region's
    /// relay and why not straight. On the host, how many friends came each way.
    /// </summary>
    public string LinkName
    {
        get
        {
            var properties = session?.Properties;
            string region = properties != null && properties.TryGetValue(RegionKey, out var where) ? where?.Value : null;
            string relay = string.IsNullOrEmpty(region) ? "릴레이" : "릴레이(" + region + ")";
            if (hostAt != null) return IsHomeNetwork(hostAt.Address) ? "직접 연결(같은 공유기)" : "직접 연결(홀 펀칭)";
            if (dual == null || !network.IsServer) return relay + " 경유" + (string.IsNullOrEmpty(note) ? "" : " · " + note);
            if (punch == null) return relay + " · 직접 연결 받지 못함";
            int straight = 0, friends = 0;
            foreach (ulong client in network.ConnectedClientsIds)
            {
                if (client == network.LocalClientId) continue;
                friends++;
                if (dual.IsDirect(client)) straight++;
            }
            string how = portMap != null ? "공유기 포트 열림" : punch.Outside == null ? "바깥 주소 모름" : !punch.Reachable ? "공유기가 주소를 매번 바꿈" : "홀 펀칭";
            return "직접 " + straight + "명 · " + relay + " " + (friends - straight) + "명 (" + how + ")";
        }
    }

    /// <summary>`direct` is the second transport of a host that takes friends both ways (with `dual`); null otherwise.</summary>
    public InternetRoom(NetworkManager network, CountingTransport transport, CountingTransport direct, DualTransport dual, Action lost)
    {
        this.network = network;
        this.transport = transport;
        this.direct = direct;
        this.dual = dual;
        this.lost = lost;
    }

    public async Task Create(string title, string map, int maxPlayers, CancellationToken cancel)
    {
        this.maxPlayers = maxPlayers;
        await Task.WhenAny(leaving, Task.Delay(LeaveWaitMs));
        // All at once. Measuring the relay's regions also signs in, which the room needs either way.
        var regions = OnlineServices.MeasureRelayRegions();
        var opening = dual != null ? RouterPortMap.Open(DirectPorts, cancel) : Task.FromResult<RouterPortMap>(null);
        var servers = dual != null ? PunchSocket.Resolve(StunServers, ResolveMs) : null;
        try
        {
            IList<IQosAnnotatedResult> measured;
            try { measured = await regions; }
            catch
            {
                (await opening)?.Close();
                throw;
            }
            portMap = await opening;
            if (dual != null) await OpenToFriends(await servers);
            Still(cancel);
            var nearest = measured.FirstOrDefault();
            var options = new SessionOptions
            {
                Name = title,
                MaxPlayers = maxPlayers,
                SessionProperties = new Dictionary<string, SessionProperty> { { MapKey, new SessionProperty(map) } }
            };
            if (nearest != null)
            {
                options.SessionProperties[RegionKey] = new SessionProperty(nearest.Region);
                options.SessionProperties[LatencyKey] = new SessionProperty(nearest.AverageLatencyMs.ToString());
            }
            bool told = punch != null;
            if (told) options.SessionProperties[DirectKey] = new SessionProperty(Advert(), VisibilityPropertyOptions.Member);
            session = await MultiplayerService.Instance.CreateSessionAsync(options.WithRelayNetwork(nearest?.Region).WithNetworkHandler(this), cancel);
            roomId = session.Id;
            // The game's listener for straight connections didn't start, so the room must stop promising one.
            if (told && punch == null) Advertise();
            Watch();
        }
        catch
        {
            ReleaseLocal();
            Close();
            throw;
        }
    }

    public async Task Join(string id, CancellationToken cancel)
    {
        roomId = id;
        await Task.WhenAny(leaving, Task.Delay(LeaveWaitMs));
        try
        {
            var signIn = OnlineServices.SignIn();
            bool straight = DirectAllowed && !relayOnly.Contains(id);
            if (!DirectAllowed) note = "직접 연결 꺼짐";
            else if (!straight) note = "직접 연결이 실패했던 방";
            var looking = straight ? LookOutside() : Task.CompletedTask;
            await signIn;
            await looking;
            Still(cancel);
            var options = new JoinSessionOptions();
            if (punch != null)
                options.PlayerProperties = new Dictionary<string, PlayerProperty> { { EndpointKey, new PlayerProperty(Whereabouts(), VisibilityPropertyOptions.Member) } };
            session = await MultiplayerService.Instance.JoinSessionByIdAsync(id, options.WithNetworkHandler(this), cancel);
            // The join has already handed over the relay's data (StartAsync), which waits: the host's address can only be
            // read now that the room is joined, and the relay drops a place nobody takes within ten seconds.
            await FindHost(cancel);
            joined = true;
            if (waiting != null) StartFriend();
        }
        catch
        {
            ReleaseLocal();
            // Joined, and then given up or failed: the place in the room must not stay taken, or the lobby refuses the
            // next room.
            Close();
            throw;
        }
    }

    /// <summary>
    /// A friend's straight connection never carried the game. True when this room was being joined that way, and from
    /// then on joining it goes through the relay.
    /// </summary>
    public bool GiveUpDirect()
    {
        if (hostAt == null || string.IsNullOrEmpty(roomId)) return false;
        relayOnly.Add(roomId);
        return true;
    }

    /// <summary>
    /// Takes the room off the list if this PC hosts it, or gives up this player's place. Nothing waits for it. The socket
    /// friends came straight through stays until ReleaseLocal: the game's goodbye to them still has to go out through it.
    /// </summary>
    public void Close()
    {
        closed = true;
        var closing = session;
        session = null;
        if (closing != null) leaving = Leave(closing);
    }

    /// <summary>
    /// Gives back what this PC holds for the room (the router's port, the socket and its threads), without telling the
    /// lobby. GameSession calls it once the game's own shutdown is through.
    /// </summary>
    public void ReleaseLocal()
    {
        portMap?.Close();
        portMap = null;
        punch?.Dispose();
        punch = null;
    }

    private static async Task Leave(ISession closing)
    {
        try
        {
            if (closing.IsHost) await closing.AsHost().DeleteAsync();
            else await closing.LeaveAsync();
        }
        catch (Exception e) when (OnlineServices.IsServiceError(e))
        {
            // The room is gone already, or the lobby service forgets it on its own once nobody keeps it alive.
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // The player gave up, or the room was closed, while this waited on something.
    private void Still(CancellationToken cancel)
    {
        if (closed) throw new OperationCanceledException();
        cancel.ThrowIfCancellationRequested();
    }

    // The host's socket for straight connections, on the port the router forwards when it does.
    private async Task OpenToFriends(List<IPEndPoint> servers)
    {
        if (portMap != null)
        {
            punch = PunchSocket.Open(new[] { (int)portMap.Port });
            if (punch != null) punch.Assume(new IPEndPoint(portMap.Outside, portMap.Port));
            else
            {
                portMap.Close();
                portMap = null;
            }
        }
        if (punch == null)
        {
            punch = PunchSocket.Open(DirectPorts.Select(port => (int)port).Concat(new[] { 0 }));
            if (punch != null) await punch.Discover(servers, StunMs);
        }
        token = PunchSocket.NewToken();
    }

    // A friend's socket, and what it looks like from outside.
    private async Task LookOutside()
    {
        var servers = await PunchSocket.Resolve(StunServers, ResolveMs);
        if (closed) return;
        punch = PunchSocket.Open(new[] { 0 });
        if (punch != null) await punch.Discover(servers, StunMs);
        else note = "소켓을 열지 못함";
    }

    // What the host writes into the room: the room's token, the outside address when others can aim at it, and this PC's
    // addresses on the home network for a friend behind the same router.
    private string Advert()
    {
        var outside = punch.Reachable ? punch.Outside : null;
        return Layout + "|" + token.ToString("x16") + "|" + (outside != null ? outside.ToString() : Nothing) + "|" + HomeAddresses();
    }

    // What a friend attaches to their place in the room.
    private string Whereabouts() => Layout + "|" + (punch.Outside != null ? punch.Outside.ToString() : Nothing) + "|" + HomeAddresses();

    private string HomeAddresses()
    {
        string all = string.Join(",", GameSession.LocalAddresses().Take(MostCandidates).Select(address => address + ":" + punch.Port));
        return all.Length > 0 ? all : Nothing;
    }

    // Every address worth trying. The home network's are tried only when the two PCs can be behind the same router,
    // where the outside address often can't be reached from inside: they show the same outside address (or one of the
    // two is unknown), and the address is on a network this PC is on as well. On somebody else's home network the same
    // numbers belong to other machines.
    private static List<IPEndPoint> Candidates(string outside, string home, IPEndPoint mine)
    {
        var all = new List<IPEndPoint>();
        var theirs = PunchSocket.Parse(outside);
        if (theirs != null) all.Add(theirs);
        if (theirs != null && mine != null && !theirs.Address.Equals(mine.Address)) return all;
        var links = Links();
        foreach (string text in (home ?? "").Split(','))
        {
            var address = PunchSocket.Parse(text);
            if (address == null || all.Count >= MostCandidates || all.Contains(address)) continue;
            uint number = AsNumber(address.Address);
            if (links.Any(link => (number & link.mask) == (link.address & link.mask))) all.Add(address);
        }
        return all;
    }

    // This PC's addresses with the size of the network each is on.
    private static List<(uint address, uint mask)> Links()
    {
        var links = new List<(uint address, uint mask)>();
        try
        {
            foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (card.OperationalStatus != OperationalStatus.Up || card.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var unicast in card.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    uint mask = 0;
                    // Not every platform tells; a home network is nearly always this size.
                    try { if (unicast.IPv4Mask != null) mask = AsNumber(unicast.IPv4Mask); }
                    catch (Exception e) when (e is NotImplementedException || e is NotSupportedException || e is PlatformNotSupportedException) { }
                    links.Add((AsNumber(unicast.Address), mask != 0 ? mask : HomeMask));
                }
            }
        }
        catch (NetworkInformationException) { }
        return links;
    }

    private static uint AsNumber(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b.Length == 4 ? (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]) : 0;
    }

    // The host's side: keeps the room's word about where the host is, and opens the router for whoever joins.
    private void Watch()
    {
        var socket = punch;
        if (socket == null) return;
        session.Changed += Greet;
        session.PlayerPropertiesChanged += Greet;
        session.PlayerJoined += _ => Greet();
        // Raised on the socket's own thread.
        socket.OutsideChanged += () => main?.Post(_ => Advertise(), null);
        Greet();
    }

    // Opens this PC's router toward every friend who told where they will probe from. A friend is greeted once per
    // address they told.
    private void Greet()
    {
        var room = session;
        var socket = punch;
        if (room == null || socket == null) return;
        foreach (var player in room.Players)
        {
            if (player?.Properties == null || !player.Properties.TryGetValue(EndpointKey, out var told)) continue;
            string[] parts = (told?.Value ?? "").Split('|');
            if (parts.Length != 3 || parts[0] != Layout) continue;
            lock (greeted)
                if (!greeted.Add(player.Id + told.Value)) continue;
            foreach (var address in Candidates(parts[1], parts[2], socket.Outside)) socket.Punch(address);
        }
    }

    private async void Advertise()
    {
        var room = session;
        if (room == null || !room.IsHost) return;
        try
        {
            var host = room.AsHost();
            host.SetProperty(DirectKey, new SessionProperty(punch != null ? Advert() : Nothing, VisibilityPropertyOptions.Member));
            await host.SavePropertiesAsync();
        }
        catch (Exception e) when (OnlineServices.IsServiceError(e))
        {
            // Friends who join meanwhile probe an address that no longer answers, and come through the relay.
        }
    }

    // A friend's side: probes where the room says the host is. Leaves hostAt set when the host answered.
    private async Task FindHost(CancellationToken cancel)
    {
        if (punch == null) return;
        var properties = session.Properties;
        string[] parts = (properties != null && properties.TryGetValue(DirectKey, out var told) ? told?.Value ?? "" : "").Split('|');
        ulong roomToken = 0;
        var candidates = parts.Length == 4 && parts[0] == Layout && ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out roomToken)
            ? Candidates(parts[2], parts[3], punch.Outside) : new List<IPEndPoint>();
        IPEndPoint found = null;
        if (candidates.Count == 0) note = "방장이 직접 주소를 내놓지 않음";
        else
        {
            found = await punch.Probe(roomToken, candidates, Mathf.RoundToInt(Mathf.Clamp(Number("punchSeconds", 2.5f), .5f, 6f) * 1000f));
            if (found == null) note = punch != null && punch.Outside == null ? "응답 없음(내 바깥 주소도 모름)" : "방장이 응답하지 않음";
            else await Task.Delay(SettleMs);
            // The room's other members have no business knowing where this PC is once the host has heard it.
            Forget();
        }
        Still(cancel);
        if (found != null && punch != null)
        {
            hostAt = found;
            bindPort = punch.Release();
        }
        else punch?.Dispose();
        punch = null;
    }

    private async void Forget()
    {
        var room = session;
        if (room?.CurrentPlayer == null) return;
        try
        {
            room.CurrentPlayer.SetProperty(EndpointKey, new PlayerProperty(Nothing, VisibilityPropertyOptions.Member));
            await room.SaveCurrentPlayerDataAsync();
        }
        catch (Exception e) when (OnlineServices.IsServiceError(e))
        {
            // It stays readable by the room's members until this player leaves.
        }
    }

    Task INetworkHandler.StartAsync(NetworkConfiguration configuration)
    {
        if (configuration.Role == NetworkRole.Client)
        {
            // Kept until Join has looked for a straight way; started at once if the room only got its network after that.
            waiting = configuration;
            if (joined) StartFriend();
            return Task.CompletedTask;
        }
        Aim(configuration, false);
        int listener = 0;
        if (dual != null)
        {
            // The listener for straight connections is on this PC only: the PunchSocket stands in front of it.
            listener = FreePort();
            direct.SetConnectionData(Loopback, (ushort)listener, Loopback);
        }
        if (!network.StartHost()) throw new StartFailed("The network did not start (" + configuration.Type + ").");
        if (punch == null) return Task.CompletedTask;
        if (dual != null && dual.DirectListening) punch.Serve(token, new IPEndPoint(IPAddress.Loopback, listener), Mathf.Max(8, maxPlayers * 2));
        else
        {
            punch.Dispose();
            punch = null;
        }
        return Task.CompletedTask;
    }

    private void StartFriend()
    {
        var configuration = waiting;
        waiting = null;
        if (hostAt != null)
        {
            transport.ConnectionData.ClientBindPort = (ushort)bindPort;
            transport.SetConnectionData(hostAt.Address.ToString(), (ushort)hostAt.Port);
            transport.MaxConnectAttempts = DirectAttempts;
            // Lives and ends with the room's object.
            if (configuration.Type == NetworkType.Relay) RelayKeeper.Hold(network.gameObject, configuration.RelayServerData);
        }
        else Aim(configuration, true);
        if (!network.StartClient()) throw new StartFailed("The network did not start (" + (hostAt != null ? "straight" : configuration.Type.ToString()) + ").");
    }

    // Points the transport where the lobby says the room is: its relay, or an address when a room was opened that way.
    private void Aim(NetworkConfiguration configuration, bool client)
    {
        if (configuration.Type != NetworkType.Direct)
        {
            transport.SetRelayServerData(configuration.RelayServerData);
            return;
        }
        var publish = configuration.DirectNetworkPublishAddress;
        string address = publish.ToFixedStringNoPort().ToString();
        if (client) transport.SetConnectionData(address, publish.Port);
        else transport.SetConnectionData(address, publish.Port, configuration.DirectNetworkListenAddress.ToFixedStringNoPort().ToString());
    }

    // Also runs when a room fails to open or is being closed here, and those already have their own outcome. A friend
    // connected straight doesn't hang on the lobby's word either: their own connection tells when the room is gone.
    Task INetworkHandler.StopAsync()
    {
        if (session != null && hostAt == null) lost();
        return Task.CompletedTask;
    }

    // A port on this PC nobody uses right now.
    private static int FreePort()
    {
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.LocalEndPoint).Port;
        }
    }

    private static bool IsHomeNetwork(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }

    /// <summary>The game's network didn't start on the way that was chosen.</summary>
    public sealed class StartFailed : Exception
    {
        public StartFailed(string message) : base(message) { }
    }

    private static string StunServers => Setting("stunServers", DefaultStunServers);

    private static string Setting(string id, string fallback)
        => GameTables.TryGetRow(Table, id, out var row) && row.TryGetValue(Column, out string text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;

    private static float Number(string id, float fallback)
        => float.TryParse(Setting(id, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
