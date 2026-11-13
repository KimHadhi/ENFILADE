using UnityEngine;

// SupportKit: Resupply. One part of the class; the rest is in SupportKit.cs and the files beside it.
public sealed partial class SupportKit
{
    private void Give(PlayerMovement friend)
    {
        if (Supply < GiveCost - .001f)
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.support.low"));
            return;
        }
        supply -= GiveCost;
        lastGive = Time.time;
        int magazines = Mathf.Max(1, Mathf.RoundToInt(Number("resupplyMagazines", 2f)));
        float overfill = Mathf.Max(0f, Number("resupplyOverfill", 25f)), syringe = Mathf.Max(0f, Number("resupplySyringe", 25f));
        if (friend.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned) network.Resupply(magazines, overfill, syringe);
        // Handing a friend ammo is the Support's fight: it fills the gauge (UltimateCharge.AwardHelp).
        UltimateCharge.AwardHelp(UltimateCharge.Help.Resupplied);
        KitSound.Play(KitSound.Cue.Supply, .8f);
        AlertHUD.Self(Texts.Format("class.support.gave", NameOf(friend)));
    }

    /// <summary>
    /// This PC's own player was resupplied by a friend's Support (PlayerNetworkCombat): each of their guns gets that many
    /// magazines more in reserve, up to its starting reserve and <paramref name="overfill"/> per cent past it (Fully
    /// Loaded), and a Détente's syringe gets <paramref name="syringe"/> more. A Schießhund's Desperado is left alone: its
    /// bounties alone load it.
    /// </summary>
    public static void Receive(PlayerMovement player, int magazines, float overfill, float syringe, string from)
    {
        if (player == null || !player.TryGetComponent(out PlayerWeaponSlots slots)) return;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            if (data == null || data.Category != WeaponCategory.Ranged || slots.KeepsOwnRounds(i)) continue;
            var gun = slots.GetRuntime(i);
            int cap = Mathf.CeilToInt(data.InitialReserveAmmo * (1f + Mathf.Max(0f, overfill) / 100f));
            int room = cap - gun.ReserveAmmo;
            if (room > 0) gun.AddReserve(Mathf.Min(room, data.MagazineSize * Mathf.Max(1, magazines)));
        }
        slots.NotifyAmmo();
        if (syringe > 0f && DetenteKit.Local != null) DetenteKit.Local.Refill(syringe);
        // The class's throwables too (GrenadeRules resupply), as the walking shop hands out magazines; an ammo crate's
        // take comes through here as well (ClassKit.Crate).
        PlayerGrenades.Resupplied(player);
        KitSound.Play(KitSound.Cue.PickAmmo, .9f);
        AmmoHUD.Blink(KitStyle.Ice);
        AlertHUD.Self(Texts.Format("class.support.got", from));
    }
}
