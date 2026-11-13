using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// FieldPickups: A guest's side. One part of the class; the rest is in FieldPickups.cs and the files beside it.
public sealed partial class FieldPickups
{
    private void ReceiveState(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort count);
        items.Clear();
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out Item item);
            if (item.Row < rows.Count) items.Add(item);
        }
    }

    private void ReceiveGive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte row);
        Give(row);
    }

    private void SendAsk(ushort id)
    {
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(ushort), Allocator.Temp);
        writer.WriteValueSafe(id);
        network.CustomMessagingManager.SendNamedMessage(AskMessage, NetworkManager.ServerClientId, writer);
    }

    // A gun swapped out goes back on the floor for anyone.
    private void Leave(int row, Vector3 at)
    {
        if (row < 0) return;
        if (IsHost) { Add(row, Ground(at), 0f); return; }
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte) + FastBufferWriter.GetWriteSize<Vector3>(), Allocator.Temp);
        writer.WriteValueSafe((byte)row);
        writer.WriteValueSafe(at);
        network.CustomMessagingManager.SendNamedMessage(LeaveMessage, NetworkManager.ServerClientId, writer);
    }
}
