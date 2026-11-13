using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: hurt, friendly fire, healing, cover, treatment and coming back. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // shielded: their shield took all of it, so others see its sparks and hear its crack, not blood (Apex Legends' shields).
    [Rpc(SendTo.NotMe)]
    private void HurtRpc(Vector3 point, Vector3 direction, bool fall, bool shielded)
    {
        if (shielded)
        {
            if (fall) return;
            FeelAudio.PlayAt(FeelSound.ShieldHit, null, point, .8f);
            ImpactVfx.ShieldSpark(point, direction, ShieldFeedback.WorldColor);
            return;
        }
        CombatHitAudio.PlayOtherPlayerHit(point, fall);
        if (!fall) ImpactVfx.Blood(point, direction, false, false, false, receiver);
    }

    // Taken on the owner's PC like any blow, from the hitter's copy there, so the hurt shows where it came from.
    [Rpc(SendTo.Owner)]
    private void FriendlyHitRpc(long id, float amount, Vector3 point, Vector3 direction, Vector3 origin, DamageMethod method,
        bool canBeGuarded, bool critical, bool pierced, RpcParams rpc = default)
    {
        if (!FriendlyFire.Enabled || receiver == null) return;
        GameObject hitter = null;
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.TryGetComponent(out NetworkObject network) && network.OwnerClientId == rpc.Receive.SenderClientId)
            { hitter = someone.gameObject; break; }
        receiver.ReceiveHit(new DamageHit(id, amount, DamageKind.Attack, point, direction, hitter, canBeGuarded,
            method: method, origin: origin, isCritical: critical, pierced: pierced));
    }

    [Rpc(SendTo.Owner)]
    private void HealRpc(float amount)
    {
        if (receiver == null || !receiver.IsAlive || receiver.IsDowned) return;
        receiver.Heal(amount);
        KitSound.Play(KitSound.Cue.PickHeal, .7f);
    }

    // This Knight's (whose PC this is) cover turned a blow on that friend: it steps in.
    [Rpc(SendTo.Owner)]
    private void CoveredRpc(ulong ally, Vector3 attacker)
    {
        var friend = PlayerOf(ally);
        if (friend != null && TryGetComponent(out KnightCover cover)) cover.Covered(friend, attacker);
    }

    // This Knight opened a cover on that friend: every other PC keeps it, the friend's to judge their blows.
    [Rpc(SendTo.NotMe)]
    private void CoverRpc(ulong ally, float seconds)
    {
        var friend = PlayerOf(ally);
        if (friend != null) KnightCover.Open(mover != null ? mover : GetComponent<PlayerMovement>(), friend, seconds);
    }

    // This Knight stepped in: the streak for everyone else.
    [Rpc(SendTo.NotMe)]
    private void CoverStepRpc(Vector3 from, Vector3 to) => CoverStreak.Show(from, to, gameObject);

    private void OnCoverOpened(PlayerMovement ally, float seconds)
    {
        if (ally != null && ally.TryGetComponent(out NetworkObject friend) && friend.IsSpawned) CoverRpc(friend.NetworkObjectId, seconds);
    }

    private void OnCoverStepped(Vector3 from, Vector3 to) => CoverStepRpc(from, to);

    // A Détente offered this player (whose PC this is) a treatment by hand.
    [Rpc(SendTo.Owner)]
    private void OfferTreatmentRpc(ulong detente)
    {
        var from = PlayerOf(detente);
        if (from != null && TryGetComponent(out DetenteKit kit)) kit.Offered(from);
    }

    // The patient took this Détente's (whose PC this is) offer.
    [Rpc(SendTo.Owner)]
    private void AcceptTreatmentRpc(ulong patient)
    {
        var from = PlayerOf(patient);
        if (from != null && TryGetComponent(out DetenteKit kit)) kit.Accepted(from);
    }

    [Rpc(SendTo.Owner)]
    private void TreatmentBegunRpc(ulong detente)
    {
        var from = PlayerOf(detente);
        if (from != null && TryGetComponent(out DetenteKit kit)) kit.Begun(from);
    }

    [Rpc(SendTo.Owner)]
    private void HealOverTimeRpc(float seconds)
    {
        if (TryGetComponent(out DetenteKit kit)) kit.Regen(seconds);
    }

    // A Détente's syringe or bandage, for the others to see in their hand (0 a dose, 1 a treatment for so long, 2 one
    // called off).
    [Rpc(SendTo.NotMe)]
    private void DetenteLookRpc(byte look, float seconds)
    {
        var who = mover != null ? mover : GetComponent<PlayerMovement>();
        if (look == 0) DetenteLooks.Syringe(who);
        else if (look == 1) DetenteLooks.Bandage(who, seconds);
        else DetenteLooks.StopBandage(who);
    }

    // A Support's mending tool in hand (0 none, 1 hammer, 2 torch), for the others to see.
    private void OnToolHeld(byte tool) => ToolRpc(tool);

    [Rpc(SendTo.NotMe)]
    private void ToolRpc(byte tool) => RepairLooks.Hold(mover != null ? mover : GetComponent<PlayerMovement>(), (RepairLooks.Tool)tool);

    private void OnInjected() => DetenteLookRpc(0, 0f);

    private void OnTreatBegan(float seconds) => DetenteLookRpc(1, seconds);

    private void OnTreatStopped() => DetenteLookRpc(2, 0f);

    // A player in the room by their network object, or null.
    private PlayerMovement PlayerOf(ulong id)
        => NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var found) && found.TryGetComponent(out PlayerMovement player)
            ? player : null;

    [Rpc(SendTo.Owner)]
    private void ResurrectRpc(float share)
    {
        if (TryGetComponent(out PlayerLife life)) life.Resurrect(share);
    }

    [Rpc(SendTo.Owner)]
    private void ReviveRpc()
    {
        if (TryGetComponent(out PlayerLife life)) life.Revived();
    }

    [Rpc(SendTo.Everyone)]
    private void WipeRpc()
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out PlayerLife life)) life.BeginWipe();
    }
}
