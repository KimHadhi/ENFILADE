using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: hurt, friendly fire, healing, cover, treatment, coming back and being shoved free of a pin. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    // shielded: their shield took all of it, so others see its sparks and hear its crack, not blood (Apex Legends' shields).
    // Seed: the hurt's look and sound, drawn the same on every PC (SharedRandom).
    [Rpc(SendTo.NotMe)]
    private void HurtRpc(Vector3 point, Vector3 direction, bool fall, bool shielded, uint seed)
    {
        using (SharedRandom.Scope(seed)) ShowHurt(point, direction, fall, shielded);
    }

    private void ShowHurt(Vector3 point, Vector3 direction, bool fall, bool shielded)
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

    // This Knight opened a cover on that friend: every other PC keeps it, the friend's to judge their blows. What the
    // Knight's 원호 반격 gives the friend's guns comes with it (the Knight's PC's numbers), so the friend's PC can give it
    // the moment the cover turns a blow there, with no word back and forth.
    [Rpc(SendTo.NotMe)]
    private void CoverRpc(ulong ally, float seconds, float gunBonus, float gunSeconds)
    {
        var friend = PlayerOf(ally);
        if (friend != null) KnightCover.Open(mover != null ? mover : GetComponent<PlayerMovement>(), friend, seconds, gunBonus, gunSeconds);
    }

    // This Knight stepped in: the streak for everyone else.
    [Rpc(SendTo.NotMe)]
    private void CoverStepRpc(Vector3 from, Vector3 to) => CoverStreak.Show(from, to, gameObject);

    private void OnCoverOpened(PlayerMovement ally, float seconds)
    {
        if (ally != null && ally.TryGetComponent(out NetworkObject friend) && friend.IsSpawned)
            CoverRpc(friend.NetworkObjectId, seconds, cover != null ? cover.AllyGunBonus : 0f, cover != null ? cover.AllyGunSeconds : 0f);
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

    // The treatment's start, with the Détente's own seconds: TreatBeganRpc (PlayerNetworkCombat.Detente.cs).

    [Rpc(SendTo.Owner)]
    private void HealOverTimeRpc(float seconds)
    {
        if (TryGetComponent(out DetenteKit kit)) kit.Regen(seconds);
    }

    // A Détente's syringe or bandage, for the others to see in their hand (0 a dose, 1 a treatment for so long, 2 one
    // called off), and whom it is for (their network object; any other number: the Détente itself), so a friend's dose
    // is aimed at that friend on every PC and a dose into oneself goes into one's own thigh (InjectArm).
    [Rpc(SendTo.NotMe)]
    private void DetenteLookRpc(byte look, float seconds, ulong patient)
    {
        var who = mover != null ? mover : GetComponent<PlayerMovement>();
        var into = PlayerOf(patient);
        if (look == 0) DetenteLooks.Syringe(who, into != null ? into : who);
        else if (look == 1) DetenteLooks.Bandage(who, seconds, into);
        else DetenteLooks.StopBandage(who);
    }

    // A Support's mending tool in hand (0 none, 1 hammer, 2 torch), for the others to see.
    private void OnToolHeld(byte tool) => ToolRpc(tool);

    [Rpc(SendTo.NotMe)]
    private void ToolRpc(byte tool) => RepairLooks.Hold(mover != null ? mover : GetComponent<PlayerMovement>(), (RepairLooks.Tool)tool);

    private void OnInjected(PlayerMovement patient) => DetenteLookRpc(0, 0f, IdOf(patient));

    private void OnTreatBegan(float seconds, PlayerMovement patient) => DetenteLookRpc(1, seconds, IdOf(patient));

    private void OnTreatStopped() => DetenteLookRpc(2, 0f, NoPlayer);

    // A player's network object, to name them to the other PCs; NoPlayer for none (and for one this room doesn't know).
    private const ulong NoPlayer = ulong.MaxValue;

    private static ulong IdOf(PlayerMovement player)
        => player != null && player.TryGetComponent(out NetworkObject found) && found.IsSpawned ? found.NetworkObjectId : NoPlayer;

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

    // A teammate's shove reached the runner pinning this player (whose PC this is, and holds the pin): it lets go.
    [Rpc(SendTo.Owner)]
    private void ShoveFreeRpc(float knockdown)
    {
        if (TryGetComponent(out PlayerHold hold)) hold.ShovedFree(knockdown);
    }

    [Rpc(SendTo.Everyone)]
    private void WipeRpc()
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out PlayerLife life)) life.BeginWipe();
    }
}
