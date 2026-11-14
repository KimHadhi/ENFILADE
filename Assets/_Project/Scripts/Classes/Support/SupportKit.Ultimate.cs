using UnityEngine;

// SupportKit: The ultimate's restock of the whole team. One part of the class; the rest is in SupportKit.cs and the files beside it.
public sealed partial class SupportKit
{
    // The ultimate (Restock, every card taken and a full gauge: ClassCards.Ultimate): every teammate's shield, ammo and
    // throwables full (the user's card feedback, 2026-11-14), the whole team topped up as one radio call would. This PC
    // decides who and how much (memory actor-decides): each teammate's own PC fills its player (RestockRpc), this
    // Support's own here at once. Every teammate counts wherever they stand, since it is a call over the radio rather than
    // a hand's reach. The dead get nothing; their own PC turns it away.
    private void RestockTeam()
    {
        float shield = Mathf.Max(0f, Number("ultShield", 100f));
        foreach (var other in PlayerRoster.All)
        {
            if (other == null) continue;
            if (other == movement) Restocked(other, shield, "");
            else if (other.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned) network.Restock(shield);
        }
    }

    /// <summary>
    /// A Support's ultimate reached this PC's own player (PlayerNetworkCombat.RestockRpc from the Support's PC, or the
    /// Support's own at once): their shield full, every gun full as the shop fills it (PlayerWeaponSlots.RefillAmmo: the
    /// magazine, and the reserve up to what it holds full; a Schießhund's Desperado keeps its own rounds) and their class
    /// throwable up to what is carried. The shield is the Support's number (Support.ultShield), or this player's own health when that is more,
    /// so the band over their health bar reads full whatever cards they hold. <paramref name="from"/> names the Support
    /// for the alert, empty for their own.
    /// </summary>
    public static void Restocked(PlayerMovement player, float shield, string from)
    {
        if (player == null || !player.IsLocal) return;
        var receiver = player.DamageReceiver;
        if (receiver == null || !receiver.IsAlive) return;
        if (player.TryGetComponent(out PlayerWeaponSlots slots)) slots.RefillAmmo();
        PlayerGrenades.Fill(player);
        float full = Mathf.Max(shield, receiver.MaximumHealth);
        if (full > receiver.Shield + .5f)
        {
            receiver.SetShield(full);
            AdaptiveHealthHUD.Blink(KitStyle.Ice, true);
        }
        KitSound.Play(KitSound.Cue.PickAmmo, .9f);
        AmmoHUD.Blink(KitStyle.Ice);
        if (!string.IsNullOrEmpty(from)) AlertHUD.Self(Texts.Format("class.support.ult.got", from));
    }
}
