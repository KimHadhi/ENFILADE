using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A Schießhund's showdown (HoundShowdown) told to every PC: a named message (ENFILADE.Showdown) a guest sends the host
/// and the host hands on to everyone else, as CardNet's effects go. The hound's PC decides it all and sends with it what
/// the others need (the user's rule that the one who acts decides, and the host only carries it out): where the two
/// stood, where the arena stands and where each is put in it, how long it lasts, and at the end how it ended. Every PC
/// then does the same with it (Act): the rift where the two stood (ArenaFx), the duel kept as the hound's PC keeps it
/// (HoundDuel), the arena made under them (HoundArena: its floor and walls hold the hound and catch a body that falls),
/// and at the end the hound's return where it left, the zombie's too when it comes back, and the crowd's roar. The PC
/// that runs the zombie also readies the arena for it and keeps it on its hound (ArenaPen); the zombie itself is moved
/// there and back by the hound's PC alone, through the zombie session's door (ZombieEffects.Move), never from here.
/// Alone nothing is sent and the same happens here.
/// </summary>
public static class ArenaNet
{
    private const string Message = "ENFILADE.Showdown";

    /// <summary>The steps of a showdown that go out: its beginning and its end.</summary>
    public enum Step : byte { Begin, End }

    /// <summary>One step of a showdown as the hound's PC decided it.</summary>
    public struct Packet : INetworkSerializeByMemcpy
    {
        public byte Step, How, Seat;
        /// <summary>Begin: the hound holds the Ace card (HoundDuel). End: the hound came back to where it left.</summary>
        public bool Ace, Back;
        /// <summary>The hound's client id; the zombie's network id (ulong.MaxValue for none).</summary>
        public ulong Hound, Foe;
        /// <summary>The pull's moment before they go, and the longest the fight lasts (s); the way the zombie faced (yaw).</summary>
        public float Pull, Seconds, FoeYaw;
        /// <summary>The arena's middle, where each stood, and where each is put in the arena.</summary>
        public Vector3 Center, HoundFrom, FoeFrom, HoundTo, FoeTo;
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

    // Every PC hears every showdown, whatever class it plays: a small listener kept for the whole run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hear()
    {
        if (ears != null) return;
        var host = new GameObject("Showdown Ears");
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

    // Hears the room's showdowns: once for each room (a new room brings a new message manager).
    private static void Listen(NetworkManager network)
    {
        var messages = network.CustomMessagingManager;
        if (messages == null || (listening == network && listeningTo == messages)) return;
        listening = network;
        listeningTo = messages;
        messages.RegisterNamedMessageHandler(Message, Receive);
    }

    /// <summary>The hound's PC: this step happens here (Act) and goes to every other PC.</summary>
    public static void Send(Packet packet, PlayerMovement hound, EnemyHealth foe)
    {
        var network = GameSession.Network;
        bool online = network != null && network.IsListening && network.CustomMessagingManager != null;
        packet.Hound = online ? network.LocalClientId : 0ul;
        packet.Foe = online && foe != null && foe.TryGetComponent(out NetworkObject marked) && marked.IsSpawned ? marked.NetworkObjectId : ulong.MaxValue;
        Act(packet, hound, foe, true);
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
        // The host hands a guest's step on to everyone but that guest, and does its share itself.
        if (network.IsServer && sender != NetworkManager.ServerClientId) HandOn(network, packet, sender);
        if (packet.Hound == network.LocalClientId) return;
        Act(packet, PlayerRoster.ById(packet.Hound), FoeOf(network, packet.Foe), false);
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

    // ---- What every PC does with a step ----

    private static void Act(in Packet packet, PlayerMovement hound, EnemyHealth foe, bool mine)
    {
        if (packet.Step == (byte)Step.Begin) Begin(packet, hound, foe, mine);
        else End(packet, hound, foe, mine);
    }

    private static void Begin(in Packet packet, PlayerMovement hound, EnemyHealth foe, bool mine)
    {
        var arena = HoundArena.At(packet.Seat, packet.Center);
        HoundArena.SetAway(hound, true);
        ArenaFx.Prewarm();
        ArenaFx.Pull(packet.HoundFrom, 1.8f, packet.Pull, mine);
        ArenaFx.Pull(packet.FoeFrom, Tall(foe), packet.Pull, false);
        if (hound == null || foe == null || !foe.IsAlive) return;
        // The duel holds in the arena as anywhere, on every PC: its foe goes after the hound alone, and so on.
        HoundDuel.Begin(hound, foe, packet.Pull + packet.Seconds, packet.Ace);
        // Where the zombie runs, the arena's walk-mesh is laid before it comes (the hound's PC moves it after the pull's
        // moment) and nobody but its hound is its quarry meanwhile.
        if (ArenaPen.Runs(foe))
            ArenaPen.Hold(foe, hound, arena, packet.FoeFrom, Quaternion.Euler(0f, packet.FoeYaw, 0f), packet.Pull + packet.Seconds);
    }

    private static void End(in Packet packet, PlayerMovement hound, EnemyHealth foe, bool mine)
    {
        HoundArena.SetAway(hound, false);
        HoundArena.Rest(packet.Seat);
        if (hound != null) HoundDuel.End(hound);
        var how = (HoundShowdown.Ending)packet.How;
        bool cheered = how != HoundShowdown.Ending.Fell;
        if (packet.Back) ArenaFx.Return(packet.HoundFrom, 1.8f, cheered, mine);
        // On time, or with the hound down, the zombie comes back as well (the hound's PC moved it back).
        if (how != HoundShowdown.Ending.Won && foe != null && foe.IsAlive) ArenaFx.Return(packet.FoeFrom, Tall(foe), false, false);
        if (cheered) ArenaFx.Cheer(packet.HoundFrom, mine);
        if (foe != null && ArenaPen.Runs(foe)) ArenaPen.Release(foe);
    }

    // How tall the zombie stands for its rift (m): a brute's is taller.
    private static float Tall(EnemyHealth foe) => foe != null ? Mathf.Clamp(CombatFoe.Top(foe).y - foe.transform.position.y, 1f, 4f) : 1.8f;
}
