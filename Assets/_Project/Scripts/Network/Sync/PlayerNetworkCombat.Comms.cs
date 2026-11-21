using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: pings, duels, callouts and faces. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // The spot the hand points at goes in the foe's own frame, so a friend's copy points at the same place on their copy of
    // the foe, wherever it stands on their screen (PlayerPoint.Pinged).
    private void OnPinged(Component foe, bool bounty, Vector3 spot)
    {
        Vector3 local = foe != null ? foe.transform.InverseTransformPoint(spot) : spot;
        if (foe != null && foe.TryGetComponent(out NetworkObject marked) && marked.IsSpawned) PingRpc(marked.NetworkObjectId, bounty, local);
        // A target on the hub's range is no network object: every PC has its own from the scene, found by where it stood.
        else if (foe is RangeTarget target) RangePingRpc(target.Home, bounty, local);
    }

    // This player pinged a target on the hub's range (a Schießhund trying its mark out): every other PC marks its own copy,
    // and this player's copy there points at it as they do.
    [Rpc(SendTo.NotMe)]
    private void RangePingRpc(Vector3 home, bool bounty, Vector3 spot)
    {
        var target = RangeTarget.Nearest(home);
        if (target == null) return;
        var pinger = mover != null ? mover : GetComponent<PlayerMovement>();
        PingMarks.Ping(target, pinger, bounty);
        PlayerPoint.Pinged(pinger, target, spot);
    }

    // This player pinged an enemy (a Schießhund's bounty mark if bounty): every other PC marks it too, and this player's
    // copy there points at the spot on it as they do.
    [Rpc(SendTo.NotMe)]
    private void PingRpc(ulong enemy, bool bounty, Vector3 spot)
    {
        if (NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(enemy, out var marked)) return;
        if (!marked.TryGetComponent(out EnemyHealth health)) return;
        var pinger = mover != null ? mover : GetComponent<PlayerMovement>();
        PingMarks.Ping(health, pinger, bounty);
        PlayerPoint.Pinged(pinger, health, spot);
    }

    // A Schießhund's duel (HoundDuel): on a zombie, every other PC keeps it too. One on a range target is practice, this
    // PC's alone.
    private void OnDueled(Component foe, float seconds, bool ace)
    {
        if (foe is EnemyHealth && foe.TryGetComponent(out NetworkObject marked) && marked.IsSpawned) DuelRpc(marked.NetworkObjectId, seconds, ace);
    }

    // This player called a duel on that zombie: this PC keeps it as theirs does, and the host's zombie goes after them.
    [Rpc(SendTo.NotMe)]
    private void DuelRpc(ulong enemy, float seconds, bool ace)
    {
        if (NetworkManager == null || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(enemy, out var marked)) return;
        if (marked.TryGetComponent(out EnemyHealth health)) HoundDuel.Begin(mover != null ? mover : GetComponent<PlayerMovement>(), health, seconds, ace);
    }

    private void OnCalled(string line, EnemyHealth target, Vector3 point, bool pins, uint seed)
    {
        ulong marked = target != null && target.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.NetworkObjectId : ulong.MaxValue;
        CalloutRpc(new Unity.Collections.FixedString64Bytes(line), marked, point, pins, seed);
    }

    // This player shouted a callout (PlayerCallouts): every other PC hears it from them, reads it over their head, and
    // pings the zombie or pins the spot it was about. The line goes by its id, not a list index, so the table's order
    // can never make two PCs read it differently.
    // Seed: the shout's, so the same take of the line plays here as on their PC (SharedRandom).
    [Rpc(SendTo.NotMe)]
    private void CalloutRpc(Unity.Collections.FixedString64Bytes line, ulong target, Vector3 point, bool pins, uint seed)
    {
        EnemyHealth enemy = null;
        if (target != ulong.MaxValue && NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(target, out var marked))
            marked.TryGetComponent(out enemy);
        var speaker = mover != null ? mover : GetComponent<PlayerMovement>();
        using (SharedRandom.Scope(seed)) PlayerCallouts.Hear(speaker, line.ToString(), enemy, point, pins);
        // A call for healing: a Détente here sees them marked, and its offer takes at once (DetenteKit).
        DetenteKit.Called(speaker, line.ToString());
    }

    // A moment of face only this player's PC knows the reason for (their ultimate, a parry, a perfect evade, a special
    // kill, an execution: PlayerFace.Shared). Everything else their face shows, every PC reads from what it already has.
    private void OnFaceShared(string mood) => FaceRpc(new Unity.Collections.FixedString32Bytes(mood));

    // That moment of face on their copy here. By the mood's id, as callouts go by theirs.
    [Rpc(SendTo.NotMe)]
    private void FaceRpc(Unity.Collections.FixedString32Bytes mood)
    {
        if (face == null && !TryGetComponent(out face)) face = gameObject.AddComponent<PlayerFace>();
        face.Show(mood.ToString());
    }
}
