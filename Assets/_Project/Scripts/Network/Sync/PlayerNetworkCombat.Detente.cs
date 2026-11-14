using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: the Détente's treatment seconds, adrenaline shot and ultimate between PCs (the card feedback of
// 2026-11-14). One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it. The Détente's PC
// decides each and sends its own numbers with it (memory actor-decides): its cards are its own player's, so the PC that
// receives one only applies what it was told and never reads its own player's cards for it.
public sealed partial class PlayerNetworkCombat
{
    /// <summary>
    /// A Détente began treating this copy's player by hand for <paramref name="seconds"/>, as its own PC makes them (the
    /// 손에 밴 붕대 card shortens them there): their own PC says so, with those seconds.
    /// </summary>
    public void TreatmentBegun(PlayerMovement detente, float seconds)
    {
        if (IsSpawned && detente != null && detente.TryGetComponent(out NetworkObject from)) TreatBeganRpc(from.NetworkObjectId, seconds);
    }

    /// <summary>
    /// A Détente's adrenaline shot went into this copy's player (DetenteKit, the 아드레날린 주사 card), with that Détente's
    /// numbers: their own PC gives them the pace and gun damage for the seconds (DetenteKit.Boosted).
    /// </summary>
    public void SendAdrenaline(float seconds, float speed, float damage) { if (IsSpawned) AdrenalineRpc(seconds, speed, damage); }

    /// <summary>
    /// On this Détente's own player: its ultimate went off with disinfectant clouds at <paramref name="spots"/>
    /// (DetenteKit.SecondLife): every other PC draws the same clouds and gets its own player up, or fills their health.
    /// </summary>
    public void SendSecondLife(Vector3[] spots, float radius, float seconds)
    {
        if (IsSpawned && spots != null && spots.Length > 0) SecondLifeRpc(spots, radius, seconds);
    }

    // A Détente began treating this player (whose PC this is) for so long.
    [Rpc(SendTo.Owner)]
    private void TreatBeganRpc(ulong detente, float seconds)
    {
        var from = PlayerOf(detente);
        if (from != null && TryGetComponent(out DetenteKit kit)) kit.Begun(from, seconds);
    }

    // A Détente's adrenaline shot reached this player (whose PC this is): their pace and guns, for its seconds.
    [Rpc(SendTo.Owner)]
    private void AdrenalineRpc(float seconds, float speed, float damage, RpcParams rpc = default)
    {
        if (TryGetComponent(out DetenteKit kit)) kit.Boosted(seconds, speed, damage, PlayerRoster.ById(rpc.Receive.SenderClientId));
    }

    // This Détente's (this copy's player's) ultimate: every other PC draws its clouds and looks after its own player.
    [Rpc(SendTo.NotMe)]
    private void SecondLifeRpc(Vector3[] spots, float radius, float seconds)
        => DetenteKit.SecondLifeReached(mover != null ? mover : GetComponent<PlayerMovement>(), spots, radius, seconds);
}
