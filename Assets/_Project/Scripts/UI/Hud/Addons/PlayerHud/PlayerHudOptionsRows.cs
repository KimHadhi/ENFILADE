using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The PlayerHud module's rows on the settings menu's HUD tab: SettingsMenuView.HudTab calls BuildRows with the saved
    /// options (GameSettings.addonPlayerHud), and a row's change is saved and applied by the menu like every other row.
    /// Row names and notes come from Texts.csv.
    /// <para>The addon's own options panel (configs.lua PopulateToolMenu: Main, Health, Armor, Ammo, Weapon Selector,
    /// Enviroment Hazards, Indicators, Pick Up History, Special Effects), in its order and its words: a checkbox is an
    /// off/on choice, a NumSlider a slider over the same range, the font's text box a text box, a colour picker four 0-255
    /// sliders (its alpha is the element's opacity). Its rows for what this game has no part for are left out (armour
    /// plates, the enemy name, the damage blur).</para>
    /// </summary>
    public static class PlayerHudOptionsRows
    {
        private const string Prefix = "settings.addon.playerhud.";

        public static void BuildRows(SettingsRows rows, PlayerHudOptions o)
        {
            rows.Header(T("title"));

            rows.Header(T("section.main"));
            Toggle(rows, T("enable"), o.enabled, v => o.enabled = v);
            Toggle(rows, T("sway"), o.sway, v => o.sway = v);
            Toggle(rows, T("hide"), o.hide, v => o.hide = v);
            rows.Slider(T("sway_intensity"), 1f, 5f, o.swayIntensity, v => o.swayIntensity = v, Two);
            rows.Slider(T("scale"), 0f, 3f, o.scale, v => o.scale = v, Two);
            Font(rows, o);
            rows.Slider(T("weight"), 0f, 5f, o.weight, v => o.weight = v, Two);

            rows.Header(T("section.health"));
            Toggle(rows, T("enable"), o.health, v => o.health = v);
            Offsets(rows, o.healthX, o.healthY, v => o.healthX = v, v => o.healthY = v);
            Colour(rows, () => o.healthColor, c => o.healthColor = c);
            rows.Header(T("section.stims"));
            Toggle(rows, T("enable"), o.stims, v => o.stims = v);
            Offsets(rows, o.stimsX, o.stimsY, v => o.stimsX = v, v => o.stimsY = v);
            rows.Note(T("stims_note"));

            rows.Header(T("section.armor"));
            Toggle(rows, T("enable"), o.armor, v => o.armor = v);
            Offsets(rows, o.armorX, o.armorY, v => o.armorX = v, v => o.armorY = v);
            Colour(rows, () => o.armorColor, c => o.armorColor = c);

            rows.Header(T("section.ammo"));
            Toggle(rows, T("enable"), o.weapon, v => o.weapon = v);
            Offsets(rows, o.weaponX, o.weaponY, v => o.weaponX = v, v => o.weaponY = v);
            Colour(rows, () => o.weaponColor, c => o.weaponColor = c);
            rows.Header(T("section.grenade"));
            Toggle(rows, T("enable"), o.eqp, v => o.eqp = v);
            Offsets(rows, o.eqpX, o.eqpY, v => o.eqpX = v, v => o.eqpY = v);
            Colour(rows, () => o.eqpColor, c => o.eqpColor = c);

            rows.Header(T("section.selector"));
            Toggle(rows, T("enable"), o.selector, v => o.selector = v);
            Offsets(rows, o.selectorX, o.selectorY, v => o.selectorX = v, v => o.selectorY = v);
            Colour(rows, () => o.selectorColor, c => o.selectorColor = c);

            rows.Header(T("section.hazards"));
            Toggle(rows, T("enable"), o.hazards, v => o.hazards = v);
            Offsets(rows, o.hazardsX, o.hazardsY, v => o.hazardsX = v, v => o.hazardsY = v);
            Colour(rows, () => o.hazardsColor, c => o.hazardsColor = c);

            rows.Header(T("section.indicators"));
            Toggle(rows, T("sense"), o.sense, v => o.sense = v);
            rows.Header(T("section.glitter"));
            Toggle(rows, T("flick_armor"), o.flickArmor, v => o.flickArmor = v);
            Toggle(rows, T("flick_damage"), o.flickDamage, v => o.flickDamage = v);
            Toggle(rows, T("flick_heal"), o.flickHeal, v => o.flickHeal = v);
            rows.Slider(T("fade"), 0f, 3f, o.flickFade, v => o.flickFade = v, Two);
            rows.Header(T("section.effect"));
            Toggle(rows, T("shake"), o.shake, v => o.shake = v);
            rows.Slider(T("intensity"), 0f, 5f, o.shakeIntensity, v => o.shakeIntensity = v, Two);
            rows.Slider(T("fade"), 0f, 3f, o.shakeFade, v => o.shakeFade = v, Two);

            rows.Header(T("section.history"));
            Toggle(rows, T("enable"), o.history, v => o.history = v);
            Offsets(rows, o.historyX, o.historyY, v => o.historyX = v, v => o.historyY = v);
            Colour(rows, () => o.historyColor, c => o.historyColor = c);

            rows.Header(T("section.sfx"));
            rows.Note(T("sfx_credit"));
            Toggle(rows, T("enable"), o.sfx, v => o.sfx = v);
            Toggle(rows, T("scanline"), o.scanline, v => o.scanline = v);
            Toggle(rows, T("aberration"), o.aberration, v => o.aberration = v);
            Toggle(rows, T("glitch"), o.glitch, v => o.glitch = v);
        }

        private static string T(string id) => Texts.Get(Prefix + id);

        private static string[] OffOn => new[] { Texts.Get("settings.off"), Texts.Get("settings.on") };

        private static string Two(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Whole(float value) => Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);

        private static void Toggle(SettingsRows rows, string name, bool value, Action<bool> set)
            => rows.Choice(name, OffOn, value ? 1 : 0, i => set(i == 1));

        // The panel's "Horizontal offset" and "Vertical offset": -ScrW()..ScrW() and -ScrH()..ScrH(), whole numbers.
        private static void Offsets(SettingsRows rows, float x, float y, Action<float> setX, Action<float> setY)
        {
            rows.Slider(T("x"), -PlayerHudOptions.OffsetX, PlayerHudOptions.OffsetX, x, setX, Whole, wholeNumbers: true);
            rows.Slider(T("y"), -PlayerHudOptions.OffsetY, PlayerHudOptions.OffsetY, y, setY, Whole, wholeNumbers: true);
        }

        // The panel's ColorPicker: red, green, blue and alpha, 0-255 each.
        private static void Colour(SettingsRows rows, Func<Color32> get, Action<Color32> set)
        {
            rows.Slider(T("color.r"), 0f, 255f, get().r, v => { var c = get(); c.r = (byte)Mathf.RoundToInt(v); set(c); }, Whole, wholeNumbers: true);
            rows.Slider(T("color.g"), 0f, 255f, get().g, v => { var c = get(); c.g = (byte)Mathf.RoundToInt(v); set(c); }, Whole, wholeNumbers: true);
            rows.Slider(T("color.b"), 0f, 255f, get().b, v => { var c = get(); c.b = (byte)Mathf.RoundToInt(v); set(c); }, Whole, wholeNumbers: true);
            rows.Slider(T("color.a"), 0f, 255f, get().a, v => { var c = get(); c.a = (byte)Mathf.RoundToInt(v); set(c); }, Whole, wholeNumbers: true);
        }

        // The panel's "Font" text box: a face name; an empty one goes back to the default (the video's).
        private static void Font(SettingsRows rows, PlayerHudOptions o)
        {
            var row = SettingsWidgets.Row(rows.Parent, T("font"));
            var field = SettingsWidgets.TextInput(row, o.font, _ => { });
            SettingsWidgets.Size(field, width: 240f, height: 34f, flexibleWidth: 0f);
            TypingLook.On(field, row.childCount > 0 ? row.GetChild(0).GetComponent<TMP_Text>() : null);
            field.onEndEdit.AddListener(text =>
            {
                o.font = string.IsNullOrWhiteSpace(text) ? PlayerHudOptions.VideoFont : text.Trim();
                field.SetTextWithoutNotify(o.font);
                GameSettings.Apply();
            });
        }
    }
}
