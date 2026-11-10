using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The transport of an internet room's host: two transports as one, so friends who reached the host straight (hole
/// punching, PunchSocket) and friends who came through Unity's relay play in the same room (user, 2026-11-10: direct
/// first, the relay only as the fallback). Netcode takes one transport, and Unity's is either a relay's or a direct one.
/// Each friend is on exactly one of the two for as long as they stay, and their id tells which: the direct transport's
/// ids carry a bit that no connection id of the transport uses (its top byte is a driver number, zero outside a
/// multi-driver setup).
///
/// The relay's transport is given the room's NetworkManager exactly as it would be alone, so the relay path behaves as it
/// always has. The direct one runs without it: it would look its own ids up in the NetworkManager, which knows them only
/// with the bit set, and would send nothing. On its own it packs unreliable messages into larger packets (up to the
/// fragmenting size, not one MTU), which costs a little only under packet loss.
/// </summary>
[DisallowMultipleComponent]
public sealed class DualTransport : NetworkTransport
{
    private const ulong DirectBit = 1UL << 63;
    private CountingTransport relay, direct;
    private NetworkManager manager;

    /// <summary>The direct transport is listening, so friends can come straight.</summary>
    public bool DirectListening { get; private set; }
    public long BytesSent => relay.BytesSent + direct.BytesSent;
    public long BytesReceived => relay.BytesReceived + direct.BytesReceived;

    // A server's own id, as Unity's transport has it on a server.
    public override ulong ServerClientId => 0;

    /// <summary>Which two transports this one stands for. Both sit on the same object and are driven only from here.</summary>
    public void Use(CountingTransport relay, CountingTransport direct)
    {
        this.relay = relay;
        this.direct = direct;
    }

    /// <summary>The friend with this Netcode client id came straight, not through the relay.</summary>
    public bool IsDirect(ulong clientId) => (TransportId(clientId) & DirectBit) != 0;

    public override void Initialize(NetworkManager networkManager = null)
    {
        manager = networkManager;
        relay.Initialize(networkManager);
        direct.Initialize(null);
        relay.OnTransportEvent -= FromRelay;
        relay.OnTransportEvent += FromRelay;
        direct.OnTransportEvent -= FromDirect;
        direct.OnTransportEvent += FromDirect;
    }

    public override bool StartServer()
    {
        if (!relay.StartServer()) return false;
        // Without it the room still opens, through the relay only.
        DirectListening = direct.StartServer();
        return true;
    }

    // Only a host has two ways in; a friend uses one plain transport.
    public override bool StartClient() => false;

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        if ((clientId & DirectBit) != 0) direct.Send(clientId & ~DirectBit, payload, networkDelivery);
        else relay.Send(clientId, payload, networkDelivery);
    }

    // Events are passed on as the two transports raise them (OnEarlyUpdate), the way Unity's own transport works.
    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        clientId = default;
        payload = default;
        receiveTime = default;
        return NetworkEvent.Nothing;
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if ((clientId & DirectBit) != 0) direct.DisconnectRemoteClient(clientId & ~DirectBit);
        else relay.DisconnectRemoteClient(clientId);
    }

    public override void DisconnectLocalClient() { }

    // Asked with Netcode's client id (/net) or with this transport's own, as Unity's transport allows.
    public override ulong GetCurrentRtt(ulong clientId)
    {
        ulong id = TransportId(clientId);
        return (id & DirectBit) != 0 ? direct.GetCurrentRtt(id & ~DirectBit) : relay.GetCurrentRtt(id);
    }

    public override void Shutdown()
    {
        relay.Shutdown();
        direct.Shutdown();
        DirectListening = false;
    }

    protected override void OnEarlyUpdate()
    {
        relay.EarlyUpdate();
        direct.EarlyUpdate();
    }

    protected override void OnPostLateUpdate()
    {
        relay.PostLateUpdate();
        direct.PostLateUpdate();
    }

    // Netcode's client id as this transport's id, or the number itself when Netcode doesn't know it as a client.
    private ulong TransportId(ulong clientId)
    {
        if (manager == null) return clientId;
        ulong id = manager.GetTransportIdFromClientId(clientId);
        return id == ulong.MaxValue ? clientId : id;
    }

    private void FromRelay(NetworkEvent kind, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        => Pass(relay, kind, clientId, payload, receiveTime);

    private void FromDirect(NetworkEvent kind, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        => Pass(direct, kind, clientId | DirectBit, payload, receiveTime);

    private void Pass(CountingTransport from, NetworkEvent kind, ulong clientId, ArraySegment<byte> payload, float receiveTime)
    {
        // Netcode reads why a friend left from its own transport, which is this one.
        if (kind == NetworkEvent.Disconnect) SetDisconnectEvent(from.DisconnectEvent, from.DisconnectEventMessage);
        InvokeOnTransportEvent(kind, clientId, payload, receiveTime);
    }
}
