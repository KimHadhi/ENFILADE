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

    // A thing of this row of the table comes to lie there: a supply, or a gun as the map lays one out or a zombie drops
    // one: a new gun with its magazine full and its starting reserve lying with it for whoever takes it first, unless
    // the row says otherwise (the user, 2026-11-18: "길가에 배치된 총기에는 초기 탄약이 들어 있어야 함(탄약 소진 시 필드
    // 무기를 주워 쓸 수 있도록)").
    private void Add(int row, Vector3 at, float life)
    {
        var made = rows[row];
        if (made.Kind != Kind.Gun)
        {
            Add(new Item { Row = (byte)row, Position = at }, life);
            return;
        }
        var gun = GunState.New(made.Weapon);
        int rounds = made.Rounds >= 0 ? Mathf.Min(made.Rounds, made.Weapon.MagazineSize) : gun.Rounds;
        int spare = made.Spare >= 0 ? made.Spare : made.Weapon.InitialReserveAmmo;
        Add(ItemOf(gun.Holding(rounds, spare), at), life);
    }

    // The item lies there from now, under an id of its own. Put into something a player set down (a gun put down at an
    // ammo crate), it comes to lie beside that, where it shows and can be taken (FieldPickups.Lying.cs, Placement.Lie).
    private void Add(Item item, float life)
    {
        item.Position = LieClear(item);
        item.Id = nextId++;
        if (nextId == 0) nextId = 1;
        items.Add(item);
        if (life > 0f) expiry[item.Id] = dropClock + life;
        changed = true;
    }

    // A real enemy's death on the host sometimes leaves something where it fell; in a campaign assault more often
    // (Phases.csv drops), so the control after it has things to take up.
    private void OnDamaged(CombatDamageReport report)
    {
        if (!IsHost || !report.IsKillingBlow || !(report.Target is EnemyHealth) || random.NextDouble() >= dropChance * CampaignRun.DropShare) return;
        int row = Pick(r => r.DropWeight > 0f, r => r.DropWeight);
        if (row >= 0) Add(row, Ground(report.Target.transform.position), dropSeconds);
    }

    // The drops' clock stands still through a campaign assault: what the dead dropped in the fight starts running out
    // only in the control after it, when there is time to take it up (user, 2026-11-14).
    private void Expire()
    {
        if (CampaignRun.Phase != CombatPhase.Assault) dropClock += Time.deltaTime;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (!expiry.TryGetValue(items[i].Id, out float until) || dropClock < until) continue;
            expiry.Remove(items[i].Id);
            items.RemoveAt(i);
            changed = true;
        }
    }

    /// <summary>
    /// On the host: <paramref name="who"/> (client <paramref name="client"/>) takes item <paramref name="id"/> if it is
    /// still there and they are near it. First come, first served. The taker's PC is handed the item as it lay (a gun
    /// with what is in it) and does the rest: its own player's guns and rounds are its own to count.
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
        if (client == Mine) Give(item);
        else SendGive(client, item);
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

    // A guest's player put a gun down (swapped out, or stripped of what its reserve had room for): it lies there as their
    // PC says it is. The host only lays it, it judges nothing of it.
    private void ReceiveLeave(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Item item);
        if (GunList.Of(item.Gun) == null) return;
        item.Row = NoRow;
        item.Position = Ground(item.Position);
        Add(item, 0f);
    }

    private void SendGive(ulong client, Item item)
    {
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Item>(), Allocator.Temp);
        writer.WriteValueSafe(item);
        network.CustomMessagingManager.SendNamedMessage(GiveMessage, client, writer);
    }
}
