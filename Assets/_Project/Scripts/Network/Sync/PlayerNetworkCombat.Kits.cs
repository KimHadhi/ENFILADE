using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: the class kits over the network, barricades, crates, resupply and the bounty. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // A Support's barricade went up here (on the host or alone, by its own PC; for a client, by the host as it asked):
    // every other PC puts its copy up, and the Support's own PC, a client's, hears its hammer (SupportKit.Placed).
    [Rpc(SendTo.NotMe)]
    private void BarricadeRpc(ushort id, Vector3 at, float yaw)
    {
        Barricade.Place(id, at, yaw, mover != null ? mover : GetComponent<PlayerMovement>());
        if (IsOwner && support != null) support.Placed(id);
    }

    // A Support's barricade came down (taken down by its PC for a new one or a class pick, or by the host for a client's
    // new one past their most): every other PC takes its copy down.
    [Rpc(SendTo.NotMe)]
    private void CollapseRpc(ushort id) => Barricade.Collapse(id);

    // On the host: a client's Support asks for a barricade (SupportKit, AskBarricade). The host's world is the true one,
    // so the spot is checked again there (Barricade.Judge); allowed, the owner's oldest past their most come down and it
    // goes up, here and on every other PC, the asker's too. Else the asker's PC is told why and gives the bag back.
    [Rpc(SendTo.Server)]
    private void BuildBarricadeRpc(ushort id, Vector3 at, float yaw, byte most)
    {
        var owner = mover != null ? mover : GetComponent<PlayerMovement>();
        var fit = Barricade.Judge(at, yaw, owner, Barricade.KindOf(id));
        if (fit != Barricade.Fit.Ok)
        {
            BarricadeRefusedRpc(id, (byte)fit);
            return;
        }
        taken.Clear();
        Barricade.Raise(id, at, yaw, owner, most, taken);
        foreach (ushort old in taken) CollapseRpc(old);
        BarricadeRpc(id, at, yaw);
    }

    // The host turned this player's barricade down: their own PC (this one) gives the bag back and says why.
    [Rpc(SendTo.Owner)]
    private void BarricadeRefusedRpc(ushort id, byte fit)
    {
        if (support != null) support.Refused(id, (Barricade.Fit)fit);
    }

    // The barricades a client's new one took down on the host, for the others to be told (BuildBarricadeRpc).
    private readonly System.Collections.Generic.List<ushort> taken = new System.Collections.Generic.List<ushort>();

    // The host's barricade's health, for every client's copy.
    [Rpc(SendTo.NotServer)]
    private void BarricadeStateRpc(ushort id, float health) => Barricade.Show(id, health);

    // A client's zombie blow on its copy of a barricade: the host's takes it.
    [Rpc(SendTo.Server)]
    private void BarricadeHitRpc(ushort id, float damage) => Barricade.Hurt(id, damage);

    // A client's player took from an ammo crate: the host's loses a take, and tells everyone its stock as wear does.
    [Rpc(SendTo.Server)]
    private void TakeCrateRpc(ushort id) => Barricade.Take(id);

    // A client's Support mended a barricade: the host's takes it, and tells everyone its health as wear does.
    [Rpc(SendTo.Server)]
    private void BarricadeMendRpc(ushort id, float amount) => Barricade.Mend(id, amount);

    // A friend's Support resupplied this player: their own PC (this one) fills the guns.
    [Rpc(SendTo.Owner)]
    private void ResupplyRpc(int magazines, float overfill, float syringe, RpcParams rpc = default)
    {
        string from = "";
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.TryGetComponent(out NetworkObject network) && network.OwnerClientId == rpc.Receive.SenderClientId)
            {
                from = someone.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : someone.name;
                break;
            }
        SupportKit.Receive(mover != null ? mover : GetComponent<PlayerMovement>(), magazines, overfill, syringe, from);
    }

    // A Schießhund claimed a bounty here (the Desperado's own claim when grand): every other PC sees it burst in the world
    // (BountyBurst) and hears it, the softer the further off.
    [Rpc(SendTo.NotMe)]
    private void BountyRpc(Vector3 head, Vector3 from, Vector3 ground, bool grand)
    {
        BountyBurst.Play(head, from, ground, grand);
        var local = PlayerRoster.Local;
        float volume = local != null ? Mathf.Clamp01(1f - Vector3.Distance(local.transform.position, head) / 60f) : .5f;
        KitSound.Play(KitSound.Cue.Bounty, volume * (grand ? .9f : .5f));
    }
}
