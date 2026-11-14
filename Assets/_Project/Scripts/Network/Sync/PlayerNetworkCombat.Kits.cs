using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: the class kits over the network, barricades, crates, resupply and the bounty. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // A Support's barricade (or wire, crate, light) went up here on its own PC, with the health that PC gave it (the
    // fortify card raises it there): every other PC puts its copy up as told, the host too, which never checks the spot
    // again (the user's rule of 2026-11-14, memory actor-decides: the acting player's PC decides, the host applies).
    [Rpc(SendTo.NotMe)]
    private void BarricadeRaisedRpc(ushort id, Vector3 at, float yaw, float health)
        => Barricade.Place(id, at, yaw, mover != null ? mover : GetComponent<PlayerMovement>(), health);

    // A Support's barricade came down, taken down by its own PC (its oldest past its most for a new one, by that PC's own
    // count, or a class pick): every other PC takes its copy down. It is sent before the new one's BarricadeRaisedRpc,
    // so the copies never stand one past the most.
    [Rpc(SendTo.NotMe)]
    private void CollapseRpc(ushort id) => Barricade.Collapse(id);

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
        => SupportKit.Receive(mover != null ? mover : GetComponent<PlayerMovement>(), magazines, overfill, syringe, NameOfClient(rpc.Receive.SenderClientId));

    /// <summary>
    /// This PC's Support threw a supply pack at <paramref name="friend"/> (SupportKit, the throw card) from
    /// <paramref name="from"/>, landing in <paramref name="seconds"/>, with the hand resupply's numbers as its PC has them:
    /// every other PC flies the same pack, and the friend's own fills their guns when it lands there.
    /// </summary>
    public void SendSupplyThrow(PlayerMovement friend, Vector3 from, float seconds, int magazines, float overfill, float syringe)
    {
        if (IsSpawned && friend != null && friend.TryGetComponent(out NetworkObject target) && target.IsSpawned)
            SupplyThrowRpc(target.NetworkObjectId, from, seconds, magazines, overfill, syringe);
    }

    /// <summary>
    /// A Support's ultimate for this copy's player (SupportKit), with the Support's shield number: their own PC fills
    /// their shield, every gun and their throwables.
    /// </summary>
    public void Restock(float shield) { if (IsSpawned) RestockRpc(shield); }

    // A Support (this copy's player) threw a supply pack at a friend: this PC flies its own copy of it, from the hand to
    // that friend, and if the friend is this PC's player, takes what is in it when it lands (SupportKit.Tossed).
    [Rpc(SendTo.NotMe)]
    private void SupplyThrowRpc(ulong friend, Vector3 from, float seconds, int magazines, float overfill, float syringe)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(friend, out NetworkObject found) || !found.TryGetComponent(out PlayerMovement target))
            return;
        SupportKit.Tossed(mover != null ? mover : GetComponent<PlayerMovement>(), target, from, seconds, magazines, overfill, syringe);
    }

    // A Support's ultimate reached this player: their own PC (this one) fills their shield to the Support's number, every
    // gun and the throwables (SupportKit.Restocked).
    [Rpc(SendTo.Owner)]
    private void RestockRpc(float shield, RpcParams rpc = default)
        => SupportKit.Restocked(mover != null ? mover : GetComponent<PlayerMovement>(), shield, NameOfClient(rpc.Receive.SenderClientId));

    // The name of the player a client plays (an RPC's sender), or "".
    private static string NameOfClient(ulong client) => PlayerTally.NameOf(PlayerRoster.ById(client));

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
