using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The EnemyBars module's options: the enemies' health bars from the Black Ops 6 Zombies Healthbars addon. Saved in settings.json as GameSettings.addonEnemyBars.
    /// The field initialisers are the defaults (the addon's own convar defaults): a new player starts with them and the
    /// HUD tab's reset returns to them (GameSettings.ResetToDefaults copies a fresh preset's block). Keep every field a
    /// value or a string, so JsonUtility's copy of the preset is a full one. Player-visible names go in Texts.csv.
    /// <para>Every convar of cl_bo6healthbar.lua has its field here; the addon has no options panel of its own.</para>
    /// </summary>
    [Serializable]
    public sealed class EnemyBarsOptions
    {
        /// <summary>A Garry's Mod unit in metres (Source's 1 unit = 0.75 inch): the max distance convar counts in units.</summary>
        public const float MetresPerUnit = .01905f;
        /// <summary>froze_bo6hbar_max_distance's default, 3000 units.</summary>
        public const float DefaultMaxDistance = 3000f * MetresPerUnit;
        /// <summary>The convar's own upper bound, 1000000 units.</summary>
        public const float MostMaxDistance = 1000000f * MetresPerUnit;

        /// <summary>
        /// The bars are drawn: froze_bo6hbar_enabled (the server's switch, replicated) and froze_bo6hbar_enabled_cl (each
        /// player's), both 1. The addon draws only when both are on; our game has no host-wide HUD switch, so the two are
        /// one player's switch here (merged, as the brief asks of overlapping parts).
        /// </summary>
        public bool enabled = true;

        /// <summary>froze_bo6hbar_enabled_name_cl, 1: the enemy's name under its bar.</summary>
        public bool name = true;

        /// <summary>froze_bo6hbar_enabled_name_shadow_cl, 1: the blurred black shadow behind the name.</summary>
        public bool nameShadow = true;

        /// <summary>froze_bo6hbar_enabled_dmg_numbers_cl, 0: the damage total right of the bar.</summary>
        public bool damageNumbers;

        /// <summary>froze_bo6hbar_alt_bg_texture, 0: the bar's ground is bg_alt_1.png instead of the flat grey.</summary>
        public bool altBackground;

        /// <summary>
        /// froze_bo6hbar_max_distance, 3000 units = 57.15 m: only an enemy this close shows its bar under the crosshair, and
        /// a hit counts as "damage nearby" (the addon's GET_DAMAGED) when it landed this close to whoever dealt it.
        /// Metres here, as everything in our world is; the convar's 0-1000000 units bound kept.
        /// </summary>
        public float maxDistance = DefaultMaxDistance;

        /// <summary>
        /// The addon's extra boss list (froze_bo6hbar_add_boss / froze_bo6hbar_remove_boss, kept in data/bo6hbar/bosses.json):
        /// zombie kinds (ZombieKind names, ';' between them) that wear the boss icon besides the elite and named ranks.
        /// Empty, as the addon starts with no file. Set by the console commands (EnemyBarsModule.AddBoss / RemoveBoss).
        /// </summary>
        public string bossKinds = "";

        /// <summary>Keeps every field in range after settings.json is read or a row changes it (GameSettings.Clamp).</summary>
        public void Clamp()
        {
            if (float.IsNaN(maxDistance) || float.IsInfinity(maxDistance)) maxDistance = DefaultMaxDistance;
            maxDistance = Mathf.Clamp(maxDistance, 0f, MostMaxDistance);
            bossKinds ??= "";
        }
    }
}
