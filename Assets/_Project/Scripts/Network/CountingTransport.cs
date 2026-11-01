using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// The game's transport, counting the game data through it so /net can show what a room costs. The transport's and the
/// relay's own packet headers are not in these numbers.
/// </summary>
public sealed class CountingTransport : UnityTransport
{
    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }

    public override void Initialize(NetworkManager networkManager = null)
    {
        base.Initialize(networkManager);
        OnTransportEvent -= CountReceived;
        OnTransportEvent += CountReceived;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        BytesSent += payload.Count;
        base.Send(clientId, payload, networkDelivery);
    }

    private void CountReceived(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
    {
        if (eventType == NetworkEvent.Data) BytesReceived += payload.Count;
    }
}
