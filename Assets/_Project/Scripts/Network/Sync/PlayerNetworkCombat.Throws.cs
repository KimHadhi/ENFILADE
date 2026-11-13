using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: charges, taunts and everything thrown or set down. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // An Assault sounded a CHARGE here: each other PC's player near it charges too.
    [Rpc(SendTo.NotMe)]
    private void ChargeRpc(Vector3 at)
    {
        string name = TryGetComponent(out PlayerTally tally) ? tally.DisplayName : gameObject.name;
        ClassKit.ReceiveCharge(at, Texts.Format("class.assault.charge.friend", name));
        // And everyone it took glows gold here too, as on the Assault's own PC.
        ChargeGlow.Sounded(at);
    }

    // A Knight taunted here: the host's zombies already fighting near go after them, and every PC sees and hears it.
    [Rpc(SendTo.NotMe)]
    private void TauntRpc(Vector3 at) => ClassKit.ReceiveTaunt(mover != null ? mover : GetComponent<PlayerMovement>(), at, false);

    // A stone this player threw: every other PC throws its own copy, and the host's is the one its zombies hear.
    [Rpc(SendTo.NotMe)]
    private void ThrowRpc(Vector3 from, Vector3 velocity)
    {
        if (throws != null) throws.ShowThrow(from, velocity);
    }

    // This player threw their class throwable: every other PC throws its own copy of it.
    [Rpc(SendTo.NotMe)]
    private void GrenadeRpc(byte kind, ushort id, Vector3 from, Vector3 velocity, float fuse)
    {
        if (grenades != null) grenades.ShowThrow((Throwable)kind, id, from, velocity, fuse);
    }

    // Their throwable burst there (their PC's fuse decides): every other PC's copy bursts in that spot, so the frag, the
    // flash, the smoke and the mist are in one place for everyone, and the host's zombies meet the host's.
    [Rpc(SendTo.NotMe)]
    private void GrenadeBurstRpc(byte kind, ushort id, Vector3 at)
    {
        if (grenades != null) grenades.ShowBurst((Throwable)kind, id, at);
    }

    // A Knight took a thrown knife back: every other PC's copy of it goes.
    [Rpc(SendTo.NotMe)]
    private void KnifeTakenRpc(ushort id)
    {
        if (grenades != null) grenades.ShowKnifeTaken(id);
    }

    // This player's off hand went for their throwable, threw it or put it back (G): every other PC's copy of them moves
    // its arm the same way (GrenadeArm), so the hold and the throw are seen. The throwable itself follows in GrenadeRpc.
    [Rpc(SendTo.NotMe)]
    private void GrenadeHandRpc(byte kind, byte phase)
    {
        if (grenades != null) grenades.ShowHand((Throwable)kind, (PlayerGrenades.HandPhase)phase);
    }

    // A Support set a claymore down (G): every other PC sets its copy down there; the Support's PC fires it and says
    // where in GrenadeBurstRpc, so its copies fire with it.
    [Rpc(SendTo.NotMe)]
    private void MineRpc(ushort id, Vector3 at, Quaternion facing, bool wall)
    {
        if (grenades != null) grenades.ShowMine(id, at, facing, wall);
    }

    // A knock on a wall by this player: every other PC hears it there, and the host's zombies come to look.
    [Rpc(SendTo.NotMe)]
    private void KnockRpc(Vector3 at, Vector3 normal)
    {
        if (knocks != null) knocks.ShowKnock(at, normal);
    }
}
