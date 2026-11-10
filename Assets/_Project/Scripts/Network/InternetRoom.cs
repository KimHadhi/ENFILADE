using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using Unity.Services.Qos;

/// <summary>
/// The internet side of a room: Unity's lobby lists it. When the host's router opens a port by UPnP (RouterPortMap),
/// friends connect to the host straight; otherwise the nearest relay carries the traffic, so nobody needs an open port.
/// GameSession keeps running the connection itself; this only points the transport at the host or the relay.
/// </summary>
public sealed class InternetRoom : INetworkHandler
{
    public const string MapKey = "map", RegionKey = "region", LatencyKey = "relayMs", LinkKey = "link", DirectLink = "direct";
    // The game's port first, then a few more in case another PC behind the same router holds it.
    private static readonly ushort[] DirectPorts =
        { GameSession.Port, (ushort)(GameSession.Port + 1), (ushort)(GameSession.Port + 2), (ushort)(GameSession.Port + 3) };
    private readonly NetworkManager network;
    private readonly UnityTransport transport;
    private readonly Action lost;
    private ISession session;
    private RouterPortMap portMap;

    /// <summary>The room goes straight to the host, not through a relay.</summary>
    public bool Direct => portMap != null;

    /// <summary>
    /// How this room is carried, for /net (friend feedback C16): straight to the host (its router opened a port), or
    /// through a relay and which region's.
    /// </summary>
    public string LinkName
    {
        get
        {
            if (Direct) return "직접 연결(방장 공유기 포트 열림)";
            var properties = session?.Properties;
            if (properties != null && properties.TryGetValue(LinkKey, out var link) && link?.Value == DirectLink) return "직접 연결";
            string region = properties != null && properties.TryGetValue(RegionKey, out var where) ? where?.Value : null;
            return string.IsNullOrEmpty(region) ? "릴레이 경유" : "릴레이 경유(" + region + ")";
        }
    }

    public InternetRoom(NetworkManager network, UnityTransport transport, Action lost)
    {
        this.network = network;
        this.transport = transport;
        this.lost = lost;
    }

    public async Task Create(string title, string map, int maxPlayers, CancellationToken cancel)
    {
        // Both at once. Measuring the relay's regions also signs in, which the room needs either way.
        var regions = OnlineServices.MeasureRelayRegions();
        var opening = RouterPortMap.Open(DirectPorts, cancel);
        IList<IQosAnnotatedResult> measured;
        try { measured = await regions; }
        catch
        {
            (await opening)?.Close();
            throw;
        }
        portMap = await opening;
        var options = new SessionOptions
        {
            Name = title,
            MaxPlayers = maxPlayers,
            SessionProperties = new Dictionary<string, SessionProperty> { { MapKey, new SessionProperty(map) } }
        };
        try
        {
            if (portMap != null)
            {
                options.SessionProperties[LinkKey] = new SessionProperty(DirectLink);
                options = options.WithDirectNetwork("0.0.0.0", portMap.Outside.ToString(), portMap.Port);
            }
            else
            {
                var nearest = measured.FirstOrDefault();
                if (nearest != null)
                {
                    options.SessionProperties[RegionKey] = new SessionProperty(nearest.Region);
                    options.SessionProperties[LatencyKey] = new SessionProperty(nearest.AverageLatencyMs.ToString());
                }
                options = options.WithRelayNetwork(nearest?.Region);
            }
            session = await MultiplayerService.Instance.CreateSessionAsync(options.WithNetworkHandler(this), cancel);
        }
        catch
        {
            ClosePort();
            throw;
        }
    }

    public async Task Join(string id, CancellationToken cancel)
    {
        await OnlineServices.SignIn();
        session = await MultiplayerService.Instance.JoinSessionByIdAsync(id, new JoinSessionOptions().WithNetworkHandler(this), cancel);
    }

    /// <summary>Takes the room off the list if this PC hosts it, or gives up this player's place. Nothing waits for it.</summary>
    public async void Close()
    {
        ClosePort();
        var closing = session;
        session = null;
        if (closing == null) return;
        try
        {
            if (closing.IsHost) await closing.AsHost().DeleteAsync();
            else await closing.LeaveAsync();
        }
        catch (Exception e) when (OnlineServices.IsServiceError(e))
        {
            // The room is gone already, or the lobby service forgets it on its own once nobody keeps it alive.
        }
    }

    private void ClosePort()
    {
        portMap?.Close();
        portMap = null;
    }

    Task INetworkHandler.StartAsync(NetworkConfiguration configuration)
    {
        bool client = configuration.Role == NetworkRole.Client;
        if (configuration.Type == NetworkType.Direct)
        {
            // The host listens on every address of this PC; a friend connects to the router's outside address.
            var publish = configuration.DirectNetworkPublishAddress;
            string address = publish.ToFixedStringNoPort().ToString();
            if (client) transport.SetConnectionData(address, publish.Port);
            else transport.SetConnectionData(address, publish.Port, configuration.DirectNetworkListenAddress.ToFixedStringNoPort().ToString());
        }
        else transport.SetRelayServerData(configuration.RelayServerData);
        bool started = client ? network.StartClient() : network.StartHost();
        if (!started) throw new InvalidOperationException("The network did not start (" + configuration.Type + ").");
        return Task.CompletedTask;
    }

    // Also runs when a room fails to open or is being closed here, and those already have their own outcome.
    Task INetworkHandler.StopAsync()
    {
        if (session != null) lost();
        return Task.CompletedTask;
    }
}
