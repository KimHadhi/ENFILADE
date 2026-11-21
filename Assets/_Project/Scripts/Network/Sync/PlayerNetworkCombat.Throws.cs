using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: charges, taunts and everything thrown or set down. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // An Assault sounded a CHARGE here, lasting and reaching as its PC reckoned it (its 돌격 나팔 card lengthens it): each
    // other PC's player near it charges too, for those seconds and not what this PC's own table would give.
    [Rpc(SendTo.NotMe)]
    private void ChargeRpc(Vector3 at, float seconds, float reach)
    {
        string name = TryGetComponent(out PlayerTally tally) ? tally.DisplayName : gameObject.name;
        ClassKit.ReceiveCharge(at, Texts.Format("class.assault.charge.friend", name), seconds, reach);
        // And everyone it took glows gold here too, as on the Assault's own PC.
        ChargeGlow.Sounded(at, seconds, reach);
    }

    // A Knight taunted here: the host's zombies already fighting near go after them, and every PC sees and hears it.
    [Rpc(SendTo.NotMe)]
    private void TauntRpc(Vector3 at) => ClassKit.ReceiveTaunt(mover != null ? mover : GetComponent<PlayerMovement>(), at, false);

    // A stone this player threw: every other PC throws its copy, which follows theirs (PropNet).
    [Rpc(SendTo.NotMe)]
    private void ThrowRpc(Vector3 from, Vector3 velocity, ushort id)
    {
        if (throws != null) throws.ShowThrow(from, velocity, id);
    }

    // Their stone first landed there: its lure, on every other PC (the host's zombies hear it there).
    [Rpc(SendTo.NotMe)]
    private void StoneLureRpc(Vector3 at)
    {
        if (throws != null) throws.ShowLure(at);
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

    // A Knight's thrown knife went in (a zombie's bone, a wall) or came to lie on the floor: every other PC's copy goes there.
    [Rpc(SendTo.NotMe)]
    private void KnifeLandedRpc(ushort id, BodyPoint at, bool lying)
    {
        if (grenades != null) grenades.ShowKnifeLanded(id, at, lying);
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
    // where in GrenadeBurstRpc (or, with others near it, MineChainRpc), so its copies fire with it.
    // With it go the numbers of the Support's own that came back off for it, past their most: their PC counted them
    // (Deployables), with their cards, and every other PC takes those copies off as told.
    [Rpc(SendTo.NotMe)]
    private void MineRpc(ushort id, Vector3 at, Quaternion facing, bool wall, ushort[] retired)
    {
        if (grenades != null) grenades.ShowMine(id, at, facing, wall, retired);
    }

    // Their claymore went off with others set down near it, anyone's (ClaymoreChain: their PC gathered them, each named
    // by its owner and number): every other PC sets its copies of the same ones off together, and each one's hits land
    // on its own owner's PC.
    [Rpc(SendTo.NotMe)]
    private void MineChainRpc(ulong[] owners, ushort[] ids, Vector3 middle)
    {
        if (grenades != null) grenades.ShowChain(owners, ids, middle);
    }
}
