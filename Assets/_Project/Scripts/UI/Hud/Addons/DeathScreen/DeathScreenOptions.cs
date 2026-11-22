using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The DeathScreen module's options: dying, from the MW3 Death Screen addon (the game's death card merged into it). Saved in settings.json as GameSettings.addonDeathScreen.
    /// The field initialisers are the defaults (the addon's own convar defaults): a new player starts with them and the
    /// HUD tab's reset returns to them (GameSettings.ResetToDefaults copies a fresh preset's block). Keep every field a
    /// value or a string, so JsonUtility's copy of the preset is a full one. Player-visible names go in Texts.csv.
    /// <para>These are the addon's five client convars (mw3death_*, lua/autorun/mw3_deathscreen.lua DEFAULTS), with the
    /// ranges CreateClientConVar gives them. Since 2026-11-22 the death card is part of this screen (the user: "여기에 우리
    /// 데스 정보 보여주는 거 합치면 돼"), so <see cref="enabled"/> is the one switch for both.</para>
    /// </summary>
    [Serializable]
    public sealed class DeathScreenOptions
    {
        /// <summary>The ranges of the addon's convars (CreateClientConVar min/max) and of its panel's sliders.</summary>
        public const float MaxVolume = 2f, MaxBlur = 20f, MaxBloodDelay = 5f;

        [Tooltip("mw3death_enabled (1): the death screen at all. Off, dying looks normal: no blur, no blood, no whistle.")]
        public bool enabled = true;
        [Tooltip("mw3death_volume (1, 0-2): the whistle's volume; over 1 it is louder than the file.")]
        public float volume = 1f;
        [Tooltip("mw3death_blur (8, 0-20): the strongest blur, in pixels between the blur's samples at 1080p. 0 is none.")]
        public float blur = 8f;
        [Tooltip("mw3death_blood (1, 0-1): how strong the blood picture gets. 0 is none.")]
        public float blood = 1f;
        [Tooltip("mw3death_blood_delay (1, 0-5): seconds after death before the blood picture starts to fade in.")]
        public float bloodDelay = 1f;

        /// <summary>Keeps every field in range after settings.json is read or a row changes it (GameSettings.Clamp).</summary>
        public void Clamp()
        {
            var defaults = Defaults;
            volume = Fit(volume, MaxVolume, defaults.volume);
            blur = Fit(blur, MaxBlur, defaults.blur);
            blood = Fit(blood, 1f, defaults.blood);
            bloodDelay = Fit(bloodDelay, MaxBloodDelay, defaults.bloodDelay);
        }

        /// <summary>The panel's "Reset to defaults" (concommand mw3death_reset): every convar back to DEFAULTS.</summary>
        public void Reset()
        {
            var defaults = Defaults;
            enabled = defaults.enabled;
            volume = defaults.volume;
            blur = defaults.blur;
            blood = defaults.blood;
            bloodDelay = defaults.bloodDelay;
        }

        // The defaults, read from a fresh instance so the field initialisers stay their one place.
        private static readonly DeathScreenOptions Defaults = new DeathScreenOptions();

        // A convar's clamp: inside 0..max; a number settings.json could not hold (NaN) goes back to its default.
        private static float Fit(float value, float max, float fallback) => float.IsNaN(value) ? fallback : Mathf.Clamp(value, 0f, max);
    }
}
