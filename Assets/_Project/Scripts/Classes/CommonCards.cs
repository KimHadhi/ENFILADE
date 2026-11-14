using UnityEngine;

/// <summary>
/// Everyone's cards of the 2026-11-14 card feedback that only change a number another system already asks for: the ammo
/// pouch (탄약 주머니, PlayerPerks.ReserveFactor: the cap every fill-up asks through PlayerWeaponSlots.ReserveCap), the
/// scavenger (청소부, PlayerPerks.ScavengeFactor: FieldPickups' ammo piles) and the grenade pouch (수류탄 주머니,
/// PlayerPerks.ExtraThrowables: PlayerGrenades.Carry). What is left here is the moment one is picked: the room it adds comes
/// filled, as a new level of 단단한 몸 heals by what it added (PerkCardScreen.ApplyHealth), so the card shows the moment it
/// is taken rather than at the next pickup (the user's rule: a pick is a plus you can see). The phoenix (불사조) is
/// PhoenixRise's.
/// </summary>
public static class CommonCards
{
    // What the pouches held when last looked at, so a new level hands over only what it added.
    private static float pouchSeen;
    private static int nadesSeen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pouchSeen = 0f;
        nadesSeen = 0;
    }

    // PlayerPerks drops its listeners at SubsystemRegistration, which runs before this.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Listen()
    {
        PlayerPerks.Changed -= OnChanged;
        PlayerPerks.Changed += OnChanged;
    }

    // A card went up a level, or the cards were cleared or put back after practice: only a rise hands anything over.
    private static void OnChanged()
    {
        float pouch = PlayerPerks.Total(PlayerPerks.AmmoPouch);
        int nades = PlayerPerks.ExtraThrowables;
        var player = PlayerRoster.Local;
        if (player != null)
        {
            if (pouch > pouchSeen + .001f) FillPouch(player, pouch - pouchSeen);
            if (nades > nadesSeen) PlayerGrenades.Refill(player, nades - nadesSeen);
        }
        pouchSeen = pouch;
        nadesSeen = nades;
    }

    // Each gun's reserve grows by what the pouch added to its cap; the Desperado is loaded by bounties alone.
    private static void FillPouch(PlayerMovement player, float addedPercent)
    {
        if (!player.TryGetComponent(out PlayerWeaponSlots slots)) return;
        bool any = false;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            if (data == null || data.Category != WeaponCategory.Ranged || slots.KeepsOwnRounds(i)) continue;
            int added = Mathf.RoundToInt(data.InitialReserveAmmo * addedPercent / 100f);
            if (added <= 0) continue;
            slots.GetRuntime(i).AddReserve(added);
            any = true;
        }
        if (!any) return;
        slots.NotifyAmmo();
        AmmoHUD.Blink(KitStyle.Ice);
    }
}
