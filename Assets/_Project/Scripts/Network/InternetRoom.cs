using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;

/// <summary>
/// The internet side of a room: Unity's lobby lists it and its relay carries the traffic, so nobody needs an open port.
/// GameSession keeps running the connection itself; this only points the transport at the relay.
/// </summary>
public sealed class InternetRoom : INetworkHandler
{
    public const string MapKey = "map", RegionKey = "region", LatencyKey = "relayMs";
    private readonly NetworkManager network;
    private readonly UnityTransport transport;
    private readonly Action lost;
    private ISession session;

    public InternetRoom(NetworkManager network, UnityTransport transport, Action lost)
    {
        this.network = network;
        this.transport = transport;
        this.lost = lost;
    }

    public async Task Create(string title, string map, int maxPlayers, CancellationToken cancel)
    {
        var nearest = (await OnlineServices.MeasureRelayRegions()).FirstOrDefault();
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
        session = await MultiplayerService.Instance.CreateSessionAsync(options.WithRelayNetwork(nearest?.Region).WithNetworkHandler(this), cancel);
    }

    public async Task Join(string id, CancellationToken cancel)
    {
        await OnlineServices.SignIn();
        session = await MultiplayerService.Instance.JoinSessionByIdAsync(id, new JoinSessionOptions().WithNetworkHandler(this), cancel);
    }

    /// <summary>Takes the room off the list if this PC hosts it, or gives up this player's place. Nothing waits for it.</summary>
    public async void Close()
    {
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

    Task INetworkHandler.StartAsync(NetworkConfiguration configuration)
    {
        transport.SetRelayServerData(configuration.RelayServerData);
        bool started = configuration.Role == NetworkRole.Client ? network.StartClient() : network.StartHost();
        if (!started) throw new InvalidOperationException("The network did not start on the relay.");
        return Task.CompletedTask;
    }

    // Also runs when a room fails to open or is being closed here, and those already have their own outcome.
    Task INetworkHandler.StopAsync()
    {
        if (session != null) lost();
        return Task.CompletedTask;
    }
}
