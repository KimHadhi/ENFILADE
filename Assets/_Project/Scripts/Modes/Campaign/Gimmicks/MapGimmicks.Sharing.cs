using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// MapGimmicks: the run's seed and the switches' states between the PCs. One part of the class; the rest is in
// MapGimmicks.cs and the files beside it.
public sealed partial class MapGimmicks
{
    private const string StateMessage = "ENFILADE.Gimmick", AllMessage = "ENFILADE.Gimmicks";
    // Every state goes out again this often even when nothing changed, for a guest who joined late or missed a word (s).
    private const float ShareEvery = 2f;
    // A guest's own change is kept from the host's older snapshots this long while its word is on the way (real s).
    private const float HoldBackSeconds = 1.5f;

    private ushort seed;
    private float nextShare;
    private float[] heldBack = new float[0];
    private NetworkManager listened;

    /// <summary>
    /// The run's seed: the host's pick as the chapter starts, shared with everyone; 0 until a guest has heard it. It decides
    /// the map's variants and where its items lie, the same on every PC.
    /// </summary>
    internal static ushort Seed => active != null ? active.seed : (ushort)0;

    /// <summary>
    /// A number from 0 to 1 for this run and this <paramref name="salt"/>, the same on every PC once the seed is known
    /// (GimmickVariant, GimmickItem, the sleeping crowds' places).
    /// </summary>
    internal static float Roll(int salt) => Mix(Seed, (uint)salt);

    internal static float Mix(ushort runSeed, uint salt)
    {
        unchecked
        {
            uint hash = runSeed * 0x9E3779B9u ^ (salt * 0x85EBCA6Bu + 0x27D4EB2Fu);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (hash & 0xFFFFFF) / 16777216f;
        }
    }

    private void Listen()
    {
        var network = GameSession.Network;
        if (network == null || network.CustomMessagingManager == null) return;
        listened = network;
        var messages = network.CustomMessagingManager;
        if (network.IsServer) messages.RegisterNamedMessageHandler(StateMessage, ReceiveAsk);
        else
        {
            messages.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            messages.RegisterNamedMessageHandler(AllMessage, ReceiveAll);
        }
    }

    private void StopListening()
    {
        if (listened == null || listened.CustomMessagingManager == null) return;
        var messages = listened.CustomMessagingManager;
        messages.UnregisterNamedMessageHandler(StateMessage);
        if (!listened.IsServer) messages.UnregisterNamedMessageHandler(AllMessage);
        listened = null;
    }

    // The host, as the chapter starts: this run's seed, and the map laid out by it.
    private void PickSeed()
    {
        seed = (ushort)Random.Range(1, 65536);
        Seeded();
    }

    // The seed is known on this PC: the variants and the items' places follow it.
    private void Seeded()
    {
        GimmickVariant.ApplyAll(seed);
        foreach (var each in switches) if (each != null) each.Seed(seed);
    }

    // The host: everything again now and then (a change itself goes out at once: SendState).
    private void Share()
    {
        var network = GameSession.Network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        if (Time.unscaledTime < nextShare) return;
        nextShare = Time.unscaledTime + ShareEvery;
        using var writer = new FastBufferWriter(sizeof(ushort) * 2 + states.Length, Allocator.Temp);
        writer.WriteValueSafe(seed);
        writer.WriteValueSafe((ushort)states.Length);
        for (int i = 0; i < states.Length; i++) writer.WriteByteSafe(states[i]);
        network.CustomMessagingManager.SendNamedMessageToAll(AllMessage, writer);
    }

    // The host: one switch changed, to everyone.
    private void SendState(int index, byte state)
    {
        var network = GameSession.Network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = Write(index, state);
        network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer);
    }

    // A guest worked a switch: to the host, and the host's older word kept off it a moment.
    private void Ask(int index, byte state)
    {
        heldBack[index] = Time.unscaledTime + HoldBackSeconds;
        var network = GameSession.Network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = Write(index, state);
        network.CustomMessagingManager.SendNamedMessage(StateMessage, NetworkManager.ServerClientId, writer);
    }

    private static FastBufferWriter Write(int index, byte state)
    {
        var writer = new FastBufferWriter(sizeof(ushort) + sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((ushort)index);
        writer.WriteValueSafe(state);
        return writer;
    }

    // The host hears a guest's change: taken as it is unless the switch turns it down (a tie: GimmickSwitch.Accepts), and
    // passed on to everyone either way, so the guest who asked learns how it stands.
    private void ReceiveAsk(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort index);
        reader.ReadValueSafe(out byte state);
        if (index >= switches.Length) return;
        var target = switches[index];
        if (target != null && target.Accepts(states[index], state)) Apply(index, state, true);
        SendState(index, states[index]);
    }

    // A guest hears one change from the host.
    private void ReceiveState(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort index);
        reader.ReadValueSafe(out byte state);
        if (sender != NetworkManager.ServerClientId || index >= switches.Length) return;
        heldBack[index] = 0f;
        Apply(index, state, true);
    }

    // A guest hears everything: the seed the first time, then each state that differs, quietly (it is catching up).
    private void ReceiveAll(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out ushort hostSeed);
        reader.ReadValueSafe(out ushort count);
        if (seed == 0 && hostSeed != 0)
        {
            seed = hostSeed;
            Seeded();
        }
        for (int i = 0; i < count; i++)
        {
            reader.ReadByteSafe(out byte state);
            if (i >= states.Length || Time.unscaledTime < heldBack[i]) continue;
            Apply(i, state, false);
        }
    }
}
