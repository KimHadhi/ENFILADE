using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A Ghost's cards told to every PC (Docs/Features/ClassCards.md, the Ghost): a named message (ENFILADE.Ghost) a guest sends
/// the host and the host hands on to everyone else, as CardNet and ArenaNet go. The Ghost's PC decides each one and sends
/// with it what the others need (the user's rule of 2026-11-14: the one who acts decides, the others only carry it out):
/// <list type="bullet">
/// <item>Mark: its 조용한 표적 card marked this zombie, for so long: a small white mark over its head (GhostMarks).</item>
/// <item>Phantom: its ultimate went off here: for so long every zombie this near it shows through walls (GhostSight).</item>
/// <item>Chain: its 암살 연쇄 card took it from here to there: the gust along the way (GhostFx). Its body's jump is its own
/// NetworkTransform's, and the takedown it goes into travels as every takedown does.</item>
/// </list>
/// Alone nothing is sent and the same happens here.
/// </summary>
public static class GhostNet
{
    private const string Message = "ENFILADE.Ghost";

    /// <summary>What a Ghost's PC tells the others.</summary>
    public enum Step : byte { Mark, Phantom, Chain }

    /// <summary>One step as the Ghost's PC decided it.</summary>
    public struct Packet : INetworkSerializeByMemcpy
    {
        public byte Step;
        /// <summary>The Ghost's client id; the zombie's network id (ulong.MaxValue for none). Send fills both in.</summary>
        public ulong Ghost, Foe;
        /// <summary>How long it lasts (s), and how far it reaches (m).</summary>
        public float Seconds, Reach;
        /// <summary>Where it went off; a chain's step went from From to To.</summary>
        public Vector3 From, To;
    }

    private static NetworkManager listening;
    private static CustomMessagingManager listeningTo;
    private static Ears ears;
    private static readonly List<ulong> others = new List<ulong>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        listening = null;
        listeningTo = null;
        ears = null;
    }

    // Every PC hears every Ghost, whatever class it plays: a small listener kept for the whole run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hear()
    {
        if (ears != null) return;
        var host = new GameObject("Ghost Ears");
        RuntimeSceneHierarchy.Keep(host);
        ears = host.AddComponent<Ears>();
    }

    private sealed class Ears : MonoBehaviour
    {
        private void Update()
        {
            var network = GameSession.Network;
            if (network != null && network.IsListening) Listen(network);
        }
    }

    // Hears the room's Ghosts: once for each room (a new room brings a new message manager).
    private static void Listen(NetworkManager network)
    {
        var messages = network.CustomMessagingManager;
        if (messages == null || (listening == network && listeningTo == messages)) return;
        listening = network;
        listeningTo = messages;
        messages.RegisterNamedMessageHandler(Message, Receive);
    }

    /// <summary>The Ghost's PC: this happens here (Act) and goes to every other PC.</summary>
    public static void Send(Packet packet, PlayerMovement ghost, EnemyHealth foe)
    {
        var network = GameSession.Network;
        bool online = network != null && network.IsListening && network.CustomMessagingManager != null;
        packet.Ghost = online ? network.LocalClientId : 0ul;
        packet.Foe = online && foe != null && foe.TryGetComponent(out NetworkObject marked) && marked.IsSpawned ? marked.NetworkObjectId : ulong.MaxValue;
        Act(packet, ghost, foe, true);
        if (!online) return;
        Listen(network);
        if (network.IsServer) HandOn(network, packet, network.LocalClientId);
        else
        {
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Packet>(), Allocator.Temp);
            writer.WriteValueSafe(packet);
            network.CustomMessagingManager.SendNamedMessage(Message, NetworkManager.ServerClientId, writer);
        }
    }

    private static void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Packet packet);
        var network = GameSession.Network;
        if (network == null) return;
        // The host hands a guest's word on to everyone but that guest, and does its share itself.
        if (network.IsServer && sender != NetworkManager.ServerClientId) HandOn(network, packet, sender);
        if (packet.Ghost == network.LocalClientId) return;
        Act(packet, PlayerRoster.ById(packet.Ghost), FoeOf(network, packet.Foe), false);
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

    private static EnemyHealth FoeOf(NetworkManager network, ulong id)
    {
        if (id == ulong.MaxValue || network.SpawnManager == null || !network.SpawnManager.SpawnedObjects.TryGetValue(id, out var found) || found == null)
            return null;
        return found.GetComponent<EnemyHealth>();
    }

    // ---- What every PC does with it ----

    private static void Act(in Packet packet, PlayerMovement ghost, EnemyHealth foe, bool mine)
    {
        switch ((Step)packet.Step)
        {
            case Step.Mark:
                if (foe != null) GhostMarks.Show(foe, ghost, packet.Seconds);
                break;
            case Step.Phantom:
                GhostSight.Phantom(ghost, packet.From, packet.Seconds, packet.Reach);
                break;
            case Step.Chain:
                GhostFx.Streak(packet.From, packet.To, mine);
                break;
        }
    }
}
