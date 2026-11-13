using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// FieldPickups: The host's side. One part of the class; the rest is in FieldPickups.cs and the files beside it.
public sealed partial class FieldPickups
{
    private void RollSpots()
    {
        foreach (var spot in FindObjectsByType<PickupSpot>(FindObjectsSortMode.None))
        {
            var roll = spot.Kind;
            int row = Pick(r => r.SpotWeight > 0f && (roll == PickupSpot.Roll.Any || (roll == PickupSpot.Roll.Gun) == (r.Kind == Kind.Gun)),
                r => r.SpotWeight);
            if (row >= 0) Add(row, spot.transform.position, 0f);
        }
    }

    private void Add(int row, Vector3 at, float life)
    {
        var item = new Item { Id = nextId++, Row = (byte)row, Position = at };
        if (nextId == 0) nextId = 1;
        items.Add(item);
        if (life > 0f) expiry[item.Id] = Time.time + life;
        changed = true;
    }

    // A real enemy's death on the host sometimes leaves something where it fell.
    private void OnDamaged(CombatDamageReport report)
    {
        if (!IsHost || !report.IsKillingBlow || !(report.Target is EnemyHealth) || random.NextDouble() >= dropChance) return;
        int row = Pick(r => r.DropWeight > 0f, r => r.DropWeight);
        if (row >= 0) Add(row, Ground(report.Target.transform.position), dropSeconds);
    }

    private void Expire()
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (!expiry.TryGetValue(items[i].Id, out float until) || Time.time < until) continue;
            expiry.Remove(items[i].Id);
            items.RemoveAt(i);
            changed = true;
        }
    }

    /// <summary>
    /// On the host: <paramref name="who"/> (client <paramref name="client"/>) takes item <paramref name="id"/> if it is
    /// still there and they are near it. First come, first served.
    /// </summary>
    private bool Take(ushort id, ulong client, PlayerMovement who)
    {
        int index = IndexOf(id);
        if (index < 0 || who == null || who.IsDead || who.IsDowned) return false;
        var item = items[index];
        float far = reach + ReachSlack;
        if ((item.Position - who.transform.position).sqrMagnitude > far * far) return false;
        items.RemoveAt(index);
        expiry.Remove(id);
        changed = true;
        if (client == Mine) Give(item.Row);
        else SendGive(client, item.Row);
        return true;
    }

    private void Share()
    {
        if (!InRoom || !network.IsServer || network.CustomMessagingManager == null) return;
        if (!changed && Time.unscaledTime < nextShare) return;
        changed = false;
        nextShare = Time.unscaledTime + shareSeconds;
        using var writer = new FastBufferWriter(sizeof(ushort) + items.Count * FastBufferWriter.GetWriteSize<Item>(), Allocator.Temp);
        writer.WriteValueSafe((ushort)items.Count);
        foreach (var item in items) writer.WriteValueSafe(item);
        network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer);
    }

    private void ReceiveAsk(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort id);
        Take(id, sender, PlayerRoster.ById(sender));
    }

    private void ReceiveLeave(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte row);
        reader.ReadValueSafe(out Vector3 at);
        if (row < rows.Count) Add(row, Ground(at), 0f);
    }

    private void SendGive(ulong client, byte row)
    {
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(row);
        network.CustomMessagingManager.SendNamedMessage(GiveMessage, client, writer);
    }
}
