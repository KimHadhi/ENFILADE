using System;
using System.Globalization;
using UnityEngine;
using W = SettingsWidgets;

namespace HudAddons
{
    /// <summary>
    /// The DeathScreen module's rows on the settings menu's HUD tab: SettingsMenuView.HudTab calls BuildRows with the saved
    /// options (GameSettings.addonDeathScreen), and a row's change is saved and applied by the menu like every other row.
    /// Row names and notes come from Texts.csv.
    /// <para>The addon's own panel (buildSettings in lua/autorun/mw3_deathscreen.lua: Q menu > Utilities, or
    /// mw3death_menu) in its order and wording: its help line, the Enable checkbox and its note, four sliders with the
    /// convars' ranges and the panel's decimals (the slider keeps that many, as DNumSlider does), then Preview and Reset to
    /// defaults. Preview runs the game's /death, which shows this screen with our card on it.</para>
    /// </summary>
    public static class DeathScreenOptionsRows
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static void BuildRows(SettingsRows rows, DeathScreenOptions o)
        {
            if (rows == null || o == null) return;
            rows.Header(Texts.Get("settings.hud.death.header"));
            rows.Note(Texts.Get("settings.hud.death.help"));

            // panel:CheckBox("Enable the death screen", "mw3death_enabled"), shown as the menu's off/on choice.
            var enableRow = W.Row(rows.Parent, Texts.Get("settings.hud.death.enabled"));
            W.Choice(enableRow, new[] { Texts.Get("settings.off"), Texts.Get("settings.on") }, o.enabled ? 1 : 0, i =>
            {
                o.enabled = i == 1;
                GameSettings.Apply();
            }, out Action<int> showEnabled);
            rows.Note(Texts.Get("settings.hud.death.enabled_note"));

            // panel:NumSlider(label, convar, min, max, decimals).
            var volume = rows.Slider(Texts.Get("settings.hud.death.volume"), 0f, DeathScreenOptions.MaxVolume, o.volume,
                v => o.volume = Round(v, 2), v => Round(v, 2).ToString("0.00", Invariant));
            var blur = rows.Slider(Texts.Get("settings.hud.death.blur"), 0f, DeathScreenOptions.MaxBlur, o.blur,
                v => o.blur = Round(v, 0), v => Round(v, 0).ToString("0", Invariant), wholeNumbers: true);
            var blood = rows.Slider(Texts.Get("settings.hud.death.blood"), 0f, 1f, o.blood,
                v => o.blood = Round(v, 2), v => Round(v, 2).ToString("0.00", Invariant));
            var bloodDelay = rows.Slider(Texts.Get("settings.hud.death.blood_delay"), 0f, DeathScreenOptions.MaxBloodDelay, o.bloodDelay,
                v => o.bloodDelay = Round(v, 1), v => Round(v, 1).ToString("0.0", Invariant));

            var buttons = W.ButtonRow(rows.Parent);
            // panel:Button("Preview", "mw3death_preview"): the screen once without dying. Our /death does that with the
            // card on it (global::DeathScreen.Command); it needs a player in a game, alive, as the addon's does.
            W.Size(W.ActionButton(buttons, Texts.Get("settings.hud.death.preview"), false, () => global::DeathScreen.Command("")), 160f, 34f);
            // panel:Button("Reset to defaults", "mw3death_reset"): every convar back to its default. The rows are then
            // moved to them: a slider that moves calls its own listener, which refreshes its number box.
            W.Size(W.ActionButton(buttons, Texts.Get("settings.hud.death.reset"), false, () =>
            {
                o.Reset();
                showEnabled(o.enabled ? 1 : 0);
                volume.value = o.volume;
                blur.value = o.blur;
                blood.value = o.blood;
                bloodDelay.value = o.bloodDelay;
                GameSettings.Apply();
            }), 160f, 34f);
        }

        // math.Round(value, decimals), as the panel's slider keeps its value.
        private static float Round(float value, int decimals)
        {
            float scale = Mathf.Pow(10f, decimals);
            return Mathf.Round(value * scale) / scale;
        }
    }
}
