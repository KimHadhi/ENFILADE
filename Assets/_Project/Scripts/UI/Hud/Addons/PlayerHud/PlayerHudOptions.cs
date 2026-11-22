using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The PlayerHud module's options: the player's own HUD from the HUD Mk II addon (health, armour, ammo, effects, hazards, pickup history, the damage direction indicator and the selector box drawn for our weapon slots). Saved in settings.json as GameSettings.addonPlayerHud.
    /// The field initialisers are the defaults (the addon's own convar defaults): a new player starts with them and the
    /// HUD tab's reset returns to them (GameSettings.ResetToDefaults copies a fresh preset's block). Keep every field a
    /// value or a string, so JsonUtility's copy of the preset is a full one. Player-visible names go in Texts.csv.
    /// <para>One field per convar of the addon's configs.lua that its options panel shows and its code reads, named after
    /// the convar (hud_mk2_&lt;name&gt;). Left out, each for a reason given in Docs/Features/PlayerHud.md: the armour plates'
    /// three (our game carries no plates), the enemy name's one (the user picked the Black Ops 6 bars for enemies), the
    /// damage blur's three (Unity has no motion blur amount to add to), and the ones nothing in the addon reads
    /// (health_wave, abl*, indicator, flick, distort, distort_mult).</para>
    /// </summary>
    [Serializable]
    public sealed class PlayerHudOptions
    {
        // Offsets reach a screen's width or height each way, as the panel's sliders do (-ScrW()..ScrW()); a wider
        // screen's units are kept with room to spare.
        public const float OffsetX = 1920f, OffsetY = 1080f;

        // ---- Main (hud_mk2_hud, scale, font, weight, sway, sway_intensity, hide) ----
        public bool enabled = true;
        public float scale = 1f;
        /// <summary>The face every text is drawn in (the panel's text box); a name AddonFonts.csv has, else the Label face.</summary>
        public string font = "Unifont";
        public float weight = 2f;
        public bool sway = true;
        public float swayIntensity = 2f;
        /// <summary>Health and armour show only for a while after a hit, fading out after.</summary>
        public bool hide;

        // ---- Health (health, health_x/y, health_color_*; stims, stims_x/y) ----
        public bool health = true;
        public float healthX, healthY;
        public Color32 healthColor = new Color32(223, 255, 143, 255);
        public bool stims = true;
        public float stimsX, stimsY;

        // ---- Armour (armor, armor_x/y, armor_color_*) ----
        public bool armor = true;
        public float armorX, armorY;
        public Color32 armorColor = new Color32(255, 255, 255, 255);

        // ---- Ammo (weapon, weapon_x/y, weapon_color_*; eqp, eqp_x/y, eqp_color_*) ----
        public bool weapon = true;
        public float weaponX, weaponY;
        public Color32 weaponColor = new Color32(255, 255, 255, 255);
        public bool eqp = true;
        public float eqpX, eqpY;
        public Color32 eqpColor = new Color32(255, 255, 255, 255);

        // ---- Weapon selector (selector, selector_x/y, selector_color_*): drawn for our weapon slots ----
        public bool selector = true;
        public float selectorX, selectorY;
        public Color32 selectorColor = new Color32(255, 255, 255, 255);

        // ---- Environment hazards (hazards, hazards_x/y, hazards_color_*) ----
        public bool hazards = true;
        public float hazardsX, hazardsY;
        public Color32 hazardsColor = new Color32(255, 0, 0, 255);

        // ---- Indicators (sense; flick_armor, flick_damage, flick_heal, flick_fade; shake, shake_intensity, shake_fade) ----
        public bool sense = true;
        public bool flickArmor = true, flickDamage = true, flickHeal = true;
        public float flickFade = .3f;
        public bool shake = true;
        public float shakeIntensity = 1f, shakeFade = .1f;

        // ---- Pickup history (history, history_x/y, history_color_*) ----
        public bool history = true;
        public float historyX, historyY;
        public Color32 historyColor = new Color32(255, 255, 255, 255);

        // ---- Special effects (sfx, scanline, abberation, glitch) ----
        public bool sfx = true, scanline = true, aberration = true, glitch = true;

        /// <summary>Keeps every field in range after settings.json is read or a row changes it (GameSettings.Clamp): the
        /// panel's slider ranges.</summary>
        public void Clamp()
        {
            scale = Range(scale, 0f, 3f, 1f);
            weight = Range(weight, 0f, 5f, 2f);
            swayIntensity = Range(swayIntensity, 1f, 5f, 2f);
            font = string.IsNullOrWhiteSpace(font) ? "Unifont" : font.Trim();
            flickFade = Range(flickFade, 0f, 3f, .3f);
            shakeIntensity = Range(shakeIntensity, 0f, 5f, 1f);
            shakeFade = Range(shakeFade, 0f, 3f, .1f);
            healthX = Offset(healthX, OffsetX); healthY = Offset(healthY, OffsetY);
            stimsX = Offset(stimsX, OffsetX); stimsY = Offset(stimsY, OffsetY);
            armorX = Offset(armorX, OffsetX); armorY = Offset(armorY, OffsetY);
            weaponX = Offset(weaponX, OffsetX); weaponY = Offset(weaponY, OffsetY);
            eqpX = Offset(eqpX, OffsetX); eqpY = Offset(eqpY, OffsetY);
            selectorX = Offset(selectorX, OffsetX); selectorY = Offset(selectorY, OffsetY);
            hazardsX = Offset(hazardsX, OffsetX); hazardsY = Offset(hazardsY, OffsetY);
            historyX = Offset(historyX, OffsetX); historyY = Offset(historyY, OffsetY);
        }

        private static float Range(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        private static float Offset(float value, float reach) => Range(value, -reach * 2f, reach * 2f, 0f);
    }
}
