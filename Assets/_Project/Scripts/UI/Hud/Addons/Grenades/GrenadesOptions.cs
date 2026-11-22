using System;

namespace HudAddons
{
    /// <summary>
    /// The Grenades module's options: one incoming-grenade system from the MW Quick Grenade and Grenade Indicator addon (cook ring, incoming direction) and Arctic's Grenade Indicators (triangle, hologram). Saved in settings.json as GameSettings.addonGrenades.
    /// The field initialisers are the defaults (the addon's own convar defaults): a new player starts with them and the
    /// HUD tab's reset returns to them (GameSettings.ResetToDefaults copies a fresh preset's block). Keep every field a
    /// value or a string, so JsonUtility's copy of the preset is a full one. Player-visible names go in Texts.csv.
    /// <para>What the addons' other options are in this game, and why they have no field here: MW's "Camera animation
    /// intensity" (cl_qg_camera_intensity) moved the view by the addon's viewmodel camera bone, and our throw is our own
    /// arm's clips with no such bone; its key (cl_qg_bind) is G in the key bindings window; its server panel (remove the
    /// HL2 grenade, remove the sprite and trail, fuse time, damage multiplier) is the throwables' gameplay
    /// (Tables/Grenades/Grenades.csv).</para>
    /// </summary>
    [Serializable]
    public sealed class GrenadesOptions
    {
        /// <summary>cl_qg_indicator 1 ("Grenade indicator"): the incoming frag's picture and arrow round the screen's middle.</summary>
        public bool indicator = true;

        /// <summary>cl_qg_fuse_indicator 1 ("Fuse time indicator"): the fuse ring round the crosshair while one cooks.</summary>
        public bool fuseIndicator = true;

        /// <summary>
        /// Arctic's menu (grenadeindicator_menu): the class names the player adds to what counts as a grenade, empty by
        /// default, kept in grenadeindicators.json. Every thrown grenade of ours is already in its default list; what our
        /// game has outside it that this module can see is the Support's claymore set down, so that is the one entry the
        /// list can take. Off is the default (an empty list); the HUD tab's reset is the menu's "RESET TO DEFAULT SETTINGS".
        /// </summary>
        public bool listClaymores;

        /// <summary>Keeps every field in range after settings.json is read or a row changes it (GameSettings.Clamp).</summary>
        public void Clamp()
        {
            // Only switches: nothing can be out of range.
        }
    }
}
