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
            if (RowOf(item) != null) items.Add(item);
        }
    }

    private void ReceiveGive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Item item);
        Give(item);
    }

    private void SendAsk(ushort id)
    {
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(ushort), Allocator.Temp);
        writer.WriteValueSafe(id);
        network.CustomMessagingManager.SendNamedMessage(AskMessage, NetworkManager.ServerClientId, writer);
    }

    // A gun goes on the ground as it is, for anyone: one swapped out for another, one put down (Drop), or one of a kind
    // its taker already holds, left with what their reserve had no room for. What is in it lies there with it.
    private void Leave(in GunState gun, Vector3 at)
    {
        if (gun.Weapon == null || GunList.IdOf(gun.Weapon) == 0) return;
        GunAmmoLog.Note($"{gun.Weapon.name,-15} laid on the ground with {gun.Rounds} in it" + (gun.Spare > 0 ? $" and {gun.Spare} lying with it" : "")
            + (gun.MagazineOut ? ", its magazine out" : ""));
        var item = ItemOf(gun, at);
        if (IsHost)
        {
            item.Position = Ground(at);
            Add(item, 0f);
            return;
        }
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Item>(), Allocator.Temp);
        writer.WriteValueSafe(item);
        network.CustomMessagingManager.SendNamedMessage(LeaveMessage, NetworkManager.ServerClientId, writer);
    }
}
