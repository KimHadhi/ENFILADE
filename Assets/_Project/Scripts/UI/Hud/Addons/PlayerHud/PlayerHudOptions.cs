using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The PlayerHud module's options: the player's own HUD from the HUD Mk II addon (health, armour, ammo, effects, hazards, pickup history, the damage direction indicator and the selector box drawn for our weapon slots). Saved in settings.json as GameSettings.addonPlayerHud.
    /// The field initialisers are the defaults: the addon's own convar defaults, except where the video the user gave
    /// (youtu.be/gG4QmyzPM2Q, measured frame by frame on 2026-11-22) shows its HUD set otherwise: the font, the text
    /// weight and the health, armour, stims, weapon and grenade offsets (the user: "영상처럼 HUD 만들어"). A new player
    /// starts with them and the HUD tab's reset returns to them (GameSettings.ResetToDefaults copies a fresh preset's
    /// block). Keep every field a value or a string, so JsonUtility's copy of the preset is a full one. Player-visible
    /// names go in Texts.csv.
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
        public const string VideoFont = "Hitmarker Normal";

        // ---- Main (hud_mk2_hud, scale, font, weight, sway, sway_intensity, hide) ----
        public bool enabled = true;
        public float scale = 1f;
        /// <summary>The face every text is drawn in (the panel's text box); a name AddonFonts.csv has, else the Label face.
        /// The video's: the Hitmarker Normal the addon ships, at a weight that keeps it regular (its letters measure as
        /// the regular face's; weight 2 would embolden them). The addon's own default is Unifont at 2.</summary>
        public string font = VideoFont;
        public float weight = 1f;
        public bool sway = true;
        public float swayIntensity = 2f;
        /// <summary>Health and armour show only for a while after a hit, fading out after.</summary>
        public bool hide;

        // ---- Health (health, health_x/y, health_color_*; stims, stims_x/y) ----
        // The video's bars stand at the foot of the screen's left side: the health bar's middle at (330, 980) on 1080p,
        // the armour bar under it, 630 left of and 30 above the addon's own place. The stims box goes with them (the
        // addon places it from the health position before health_x, so it needs the same offset to stay beside the bar).
        public bool health = true;
        public float healthX = -630f, healthY = -30f;
        public Color32 healthColor = new Color32(223, 255, 143, 255);
        public bool stims = true;
        public float stimsX = -630f, stimsY = -30f;

        // ---- Armour (armor, armor_x/y, armor_color_*) ----
        public bool armor = true;
        public float armorX = -630f, armorY = -30f;
        public Color32 armorColor = new Color32(255, 255, 255, 255);

        // ---- Ammo (weapon, weapon_x/y, weapon_color_*; eqp, eqp_x/y, eqp_color_*) ----
        // The video's magazine box has its middle at (1690, 998), its grenade box at (1776, 944): offsets from the
        // addon's places of (10, 26) and (14, 54).
        public bool weapon = true;
        public float weaponX = 10f, weaponY = 26f;
        public Color32 weaponColor = new Color32(255, 255, 255, 255);
        public bool eqp = true;
        public float eqpX = 14f, eqpY = 54f;
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
            weight = Range(weight, 0f, 5f, 1f);
            swayIntensity = Range(swayIntensity, 1f, 5f, 2f);
            font = string.IsNullOrWhiteSpace(font) ? VideoFont : font.Trim();
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
