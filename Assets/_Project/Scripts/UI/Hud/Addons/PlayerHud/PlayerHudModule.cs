namespace HudAddons
{
    /// <summary>
    /// The PlayerHud module: the player's own HUD from the HUD Mk II addon (health, armour, ammo, effects, hazards, pickup history, the damage direction indicator and the selector box drawn for our weapon slots), drawn on the shared layer (Overlay, Painter, HudFont, Pictures, Sound, Smooth).
    /// Boot.Install calls Install when the local player's HUD comes up, again for every new local player, so it must be
    /// safe to call more than once. Hits, heals and the weapon slots' activity it hears for itself (PlayerDamageReceiver,
    /// PlayerWeaponSlots events); pickups come in through the calls below, which FieldPickups makes (the addon's
    /// HUDAmmoPickedUp, HUDItemPickedUp and HUDWeaponPickedUp hooks). Each does nothing before Install.
    /// </summary>
    public static class PlayerHudModule
    {
        public static void Install() => PlayerHud.Install();

        /// <summary>
        /// The addon HUD is the player's HUD now (its Enable option, on by default): our own pieces it has an element for
        /// (the health and shield bar, the ammo display, the weapon slots, the throwable plate, the pickup lines, the damage
        /// direction arcs and the hit shake) stay away meanwhile, so the player never sees two of one thing (the user,
        /// 2026-11-22: "애드온을 넣으라고"). Switched off in the options, ours come back.
        /// </summary>
        public static bool On => UnityEngine.Application.isPlaying && GameSettings.Current.addonPlayerHud is { enabled: true };

        /// <summary>HUDAmmoPickedUp: rounds of the ammunition this gun takes went into the reserve (an ammo pile, a gun of
        /// a kind already held taken for its rounds).</summary>
        public static void AmmoPicked(WeaponData gun, int rounds)
        {
            if (gun == null || rounds <= 0) return;
            PlayerHud.Instance.OrNull()?.PickedAmmo(gun, rounds);
        }

        /// <summary>HUDAmmoPickedUp("Grenade"): this many of the class throwable were taken (an ammo pile's).</summary>
        public static void ThrowablesPicked(int count)
        {
            if (count <= 0) return;
            PlayerHud.Instance.OrNull()?.PickedThrowables(count);
        }

        /// <summary>HUDItemPickedUp: one thing taken, by the name the pickup shows (a med kit, a shield, money).</summary>
        public static void ItemPicked(string name) => PlayerHud.Instance.OrNull()?.PickedThing(name);

        /// <summary>HUDWeaponPickedUp: a gun taken into a slot.</summary>
        public static void WeaponPicked(WeaponData gun)
        {
            if (gun == null) return;
            PlayerHud.Instance.OrNull()?.PickedThing(gun.DisplayName);
        }
    }
}
