using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The class cards' effects seen on every PC (Docs/ClassCards.md): what a card does is judged on its player's own PC
/// (ClassCards), and how it looks goes out to the others here, a named message (ENFILADE.CardFx) a guest sends the host
/// and the host hands on to everyone else, as MoneyDrops does. The sender has already drawn it; the others draw it
/// where it happened (CardFx). Alone nothing is sent. Hits and stuns travel the usual ways (EnemyNetwork); only the
/// look comes this way.
/// </summary>
public static class CardNet
{
    private const string Message = "ENFILADE.CardFx";

    private struct Packet : INetworkSerializeByMemcpy
    {
        public byte Kind;
        public Vector3 At, Direction;
        public float Value;
        public ulong From;
    }

    private static NetworkManager listening;
    private static readonly List<ulong> others = new List<ulong>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => listening = null;

    /// <summary>
    /// Draws <paramref name="kind"/> here and tells the other PCs to draw it too: <paramref name="at"/> where,
    /// <paramref name="direction"/> which way it goes or faces, <paramref name="value"/> its size or length (CardFx).
    /// </summary>
    public static void Show(CardFx.Kind kind, Vector3 at, Vector3 direction, float value)
    {
        CardFx.Play(kind, at, direction, value, true);
        var network = GameSession.Network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        Listen();
        var packet = new Packet { Kind = (byte)kind, At = at, Direction = direction, Value = value, From = network.LocalClientId };
        if (network.IsServer) HandOn(network, packet, network.LocalClientId);
        else
        {
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Packet>(), Allocator.Temp);
            writer.WriteValueSafe(packet);
            network.CustomMessagingManager.SendNamedMessage(Message, NetworkManager.ServerClientId, writer);
        }
    }

    /// <summary>Starts hearing the others' card effects (ClassCards, on this PC's own player, once the room is up).</summary>
    public static void Listen()
    {
        var network = GameSession.Network;
        if (network == null || network.CustomMessagingManager == null || listening == network) return;
        listening = network;
        network.CustomMessagingManager.RegisterNamedMessageHandler(Message, Receive);
    }

    /// <summary>Stops hearing them (this PC's player is going).</summary>
    public static void Stop()
    {
        if (listening == null) return;
        if (listening.CustomMessagingManager != null) listening.CustomMessagingManager.UnregisterNamedMessageHandler(Message);
        listening = null;
    }

    private static void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Packet packet);
        var network = GameSession.Network;
        if (network == null) return;
        // The host hands a guest's effect on to everyone but that guest, and draws it itself.
        if (network.IsServer && sender != NetworkManager.ServerClientId) HandOn(network, packet, sender);
        if (packet.From == network.LocalClientId) return;
        CardFx.Play((CardFx.Kind)packet.Kind, packet.At, packet.Direction, packet.Value, false);
    }

    // On the host: to every client but the one it came from (and not to itself).
    private static void HandOn(NetworkManager network, in Packet packet, ulong from)
    {
        others.Clear();
        foreach (ulong id in network.ConnectedClientsIds)
            if (id != from && id != network.LocalClientId) others.Add(id);
        if (others.Count == 0) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Packet>(), Allocator.Temp);
        writer.WriteValueSafe(packet);
        network.CustomMessagingManager.SendNamedMessage(Message, others, writer);
    }
}
