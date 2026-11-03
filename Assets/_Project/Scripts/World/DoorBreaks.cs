using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Doors break on every PC. Whoever breaks one breaks it at once on their own PC and tells the host; the host breaks it
/// and tells everyone, and tells anyone who joins later which doors are already down. A door is found by where it
/// stands, since scene objects carry no network id. With no session, a door just breaks where it is.
/// </summary>
internal static class DoorBreaks
{
    private const string Message = "ENFILADE.Door";
    // How far a door may stand from where another PC says it is and still be that door (m).
    private const float Match = .5f;
    private static CustomMessagingManager listening;

    private struct Report : INetworkSerializeByMemcpy
    {
        public Vector3 Door, Point;
        public float Speed;
        public byte Quiet;
    }

    /// <summary>Hooks this PC up to the door messages of the session it is in, once a session.</summary>
    internal static void Listen()
    {
        var network = GameSession.Network;
        var messages = network != null ? network.CustomMessagingManager : null;
        if (messages == null || messages == listening) return;
        listening = messages;
        messages.RegisterNamedMessageHandler(Message, Receive);
        if (network.IsServer && network.SceneManager != null) network.SceneManager.OnSynchronizeComplete += SendBroken;
    }

    /// <summary>A door broke on this PC.</summary>
    internal static void Tell(BreakableDoor door, Vector3 point, float speed)
    {
        var network = GameSession.Network;
        if (network == null || !network.IsListening) return;
        Listen();
        using var writer = Write(new Report { Door = door.transform.position, Point = point, Speed = speed });
        if (network.IsServer) network.CustomMessagingManager.SendNamedMessageToAll(Message, writer);
        else network.CustomMessagingManager.SendNamedMessage(Message, NetworkManager.ServerClientId, writer);
    }

    private static void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Report report);
        var door = Find(report.Door);
        if (door != null) door.Shatter(report.Point, report.Speed, report.Quiet != 0);
        // The host passes on what a guest broke.
        var network = GameSession.Network;
        if (network == null || !network.IsServer || sender == NetworkManager.ServerClientId) return;
        using var writer = Write(report);
        network.CustomMessagingManager.SendNamedMessageToAll(Message, writer);
    }

    // Someone joined: which doors are already down, without the noise.
    private static void SendBroken(ulong client)
    {
        var network = GameSession.Network;
        if (network == null) return;
        foreach (var door in BreakableDoor.All)
        {
            if (door == null || !door.IsBroken) continue;
            using var writer = Write(new Report { Door = door.transform.position, Point = door.transform.position, Quiet = 1 });
            network.CustomMessagingManager.SendNamedMessage(Message, client, writer);
        }
    }

    private static BreakableDoor Find(Vector3 position)
    {
        BreakableDoor nearest = null;
        float best = Match * Match;
        foreach (var door in BreakableDoor.All)
        {
            if (door == null) continue;
            float distance = (door.transform.position - position).sqrMagnitude;
            if (distance < best) { best = distance; nearest = door; }
        }
        return nearest;
    }

    private static FastBufferWriter Write(in Report report)
    {
        var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Report>(), Allocator.Temp);
        writer.WriteValueSafe(report);
        return writer;
    }
}
