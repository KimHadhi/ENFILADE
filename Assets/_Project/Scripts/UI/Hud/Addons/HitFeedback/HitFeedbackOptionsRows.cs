using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using W = SettingsWidgets;

namespace HudAddons
{
    /// <summary>
    /// The HitFeedback module's rows on the settings menu's HUD tab: SettingsMenuView.HudTab calls BuildRows with the saved
    /// options (GameSettings.addonHitFeedback), and a row's change is saved and applied by the menu like every other row.
    /// Row names and notes come from Texts.csv.
    /// <para>They are the addons' own panels, in their order and wording (in Korean, never a game's name): Black Ops VI
    /// Hitmarker's "Main" (eight check boxes, four sliders at the panel's decimals, its Reset to Default) and
    /// "Achievements" (a text entry per name, its Reset to Default), then Cool Killchains' "Client" form (its caption,
    /// check box, three sliders and help line). The addon's "Restart the map to apply" line under the sizes is gone: GMod
    /// could not remake a font in place, and here a new text size draws at once. Its Main reset left the hit mark's size
    /// out; here it resets that too.</para>
    /// </summary>
    public static class HitFeedbackOptionsRows
    {
        public static void BuildRows(SettingsRows rows, HitFeedbackOptions o)
        {
            var d = new HitFeedbackOptions();
            var offOn = new[] { Texts.Get("settings.off"), Texts.Get("settings.on") };

            var main = new List<Action>();
            rows.Header(Texts.Get("settings.hitfeedback.header"));
            Toggle(rows, offOn, "settings.hitfeedback.enable", () => o.enable, v => o.enable = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.hitmark", () => o.hitmark, v => o.hitmark = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.killmark", () => o.killmark, v => o.killmark = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.score", () => o.score, v => o.score = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.achievements", () => o.achievements, v => o.achievements = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.hit_sound", () => o.hitSound, v => o.hitSound = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.kill_sound", () => o.killSound, v => o.killSound = v, main);
            Toggle(rows, offOn, "settings.hitfeedback.debug", () => o.debug, v => o.debug = v, main);
            Number(rows, "settings.hitfeedback.horizontal", -Gm.ScrW, Gm.ScrW, 0, () => o.horizontal, v => o.horizontal = v, main);
            Number(rows, "settings.hitfeedback.vertical", -Gm.ScrH, Gm.ScrH, 0, () => o.vertical, v => o.vertical = v, main);
            Number(rows, "settings.hitfeedback.mark_size", 0f, HitFeedbackOptions.MaxSize, 2, () => o.markSize, v => o.markSize = v, main);
            Number(rows, "settings.hitfeedback.text_size", 0f, HitFeedbackOptions.MaxSize, 2, () => o.textSize, v => o.textSize = v, main);
            Reset(rows, main, () =>
            {
                o.enable = d.enable; o.hitmark = d.hitmark; o.killmark = d.killmark; o.score = d.score;
                o.achievements = d.achievements; o.hitSound = d.hitSound; o.killSound = d.killSound; o.debug = d.debug;
                o.horizontal = d.horizontal; o.vertical = d.vertical; o.markSize = d.markSize; o.textSize = d.textSize;
            });

            // The Achievements panel's order: Kill Human, (Kill Alien, Destroy Device: no such enemies here), Headshot,
            // Jump Kill, Meele Kill, Burn Kill, Explosive Kill, Triple, Quadra, Penta kill.
            var names = new List<Action>();
            rows.Header(Texts.Get("settings.hitfeedback.names_header"));
            Name(rows, "settings.hitfeedback.name.eliminate", "hitfeedback.name.eliminate", () => o.nameEliminate, v => o.nameEliminate = v, names);
            Name(rows, "settings.hitfeedback.name.headshot", "hitfeedback.name.headshot", () => o.nameHeadshot, v => o.nameHeadshot = v, names);
            Name(rows, "settings.hitfeedback.name.jump", "hitfeedback.name.jump", () => o.nameJump, v => o.nameJump = v, names);
            Name(rows, "settings.hitfeedback.name.melee", "hitfeedback.name.melee", () => o.nameMelee, v => o.nameMelee = v, names);
            Name(rows, "settings.hitfeedback.name.burn", "hitfeedback.name.burn", () => o.nameBurn, v => o.nameBurn = v, names);
            Name(rows, "settings.hitfeedback.name.explosive", "hitfeedback.name.explosive", () => o.nameExplosive, v => o.nameExplosive = v, names);
            Name(rows, "settings.hitfeedback.name.triple", "hitfeedback.name.triple", () => o.nameTriple, v => o.nameTriple = v, names);
            Name(rows, "settings.hitfeedback.name.quadra", "hitfeedback.name.quadra", () => o.nameQuadra, v => o.nameQuadra = v, names);
            Name(rows, "settings.hitfeedback.name.penta", "hitfeedback.name.penta", () => o.namePenta, v => o.namePenta = v, names);
            Reset(rows, names, () =>
            {
                o.nameEliminate = d.nameEliminate; o.nameHeadshot = d.nameHeadshot; o.nameJump = d.nameJump;
                o.nameMelee = d.nameMelee; o.nameBurn = d.nameBurn; o.nameExplosive = d.nameExplosive;
                o.nameTriple = d.nameTriple; o.nameQuadra = d.nameQuadra; o.namePenta = d.namePenta;
            });

            rows.Header(Texts.Get("settings.hitfeedback.chain_header"));
            rows.Note(Texts.Get("settings.hitfeedback.chain_caption"));
            Toggle(rows, offOn, "settings.hitfeedback.skulls", () => o.skulls, v => o.skulls = v, null);
            Number(rows, "settings.hitfeedback.skull_scale", HitFeedbackOptions.MinSkullScale, HitFeedbackOptions.MaxSkullScale, 3,
                () => o.skullScale, v => o.skullScale = v, null);
            Number(rows, "settings.hitfeedback.skull_offset", HitFeedbackOptions.MinSkullOffset, HitFeedbackOptions.MaxSkullOffset, 2,
                () => o.skullOffset, v => o.skullOffset = v, null);
            Number(rows, "settings.hitfeedback.skull_max", 0f, HitFeedbackOptions.MaxSkulls, 0, () => o.skullMax,
                v => o.skullMax = Mathf.RoundToInt(v), null);
            rows.Note(Texts.Get("settings.hitfeedback.skull_max_note"));
        }

        // panel:CheckBox: an off / on row, as SettingsRows.Choice draws it, that a reset can light again.
        private static void Toggle(SettingsRows rows, string[] offOn, string textId, Func<bool> get, Action<bool> set, List<Action> refresh)
        {
            var row = W.Row(rows.Parent, Texts.Get(textId));
            W.Choice(row, offOn, get() ? 1 : 0, i =>
            {
                set(i == 1);
                GameSettings.Apply();
            }, out var show);
            refresh?.Add(() => show(get() ? 1 : 0));
        }

        // panel:NumSlider(min, max, decimals): the value kept to the slider's decimals, as DNumSlider rounds it.
        private static void Number(SettingsRows rows, string textId, float min, float max, int decimals, Func<float> get, Action<float> set,
            List<Action> refresh)
        {
            float step = Mathf.Pow(10f, decimals);
            string format = decimals == 0 ? "0" : "0." + new string('0', decimals);
            float Snap(float v) => Mathf.Round(v * step) / step;
            var slider = rows.Slider(Texts.Get(textId), min, max, get(), v => set(Snap(v)),
                v => Snap(v).ToString(format, CultureInfo.InvariantCulture), wholeNumbers: decimals == 0);
            // Setting the slider runs its listener (the setter, the apply, the box's text) when the value differs.
            refresh?.Add(() => slider.value = get());
        }

        // panel:TextEntry: the name as the popup shows it (the addon's own while none is typed). Typing the addon's own
        // name, or nothing, goes back to "the addon's own", so a later change of the default reaches this player too.
        private static void Name(SettingsRows rows, string textId, string nameId, Func<string> get, Action<string> set, List<Action> refresh)
        {
            var row = W.Row(rows.Parent, Texts.Get(textId));
            string Shown() => string.IsNullOrEmpty(get()) ? Texts.InEnglish(nameId) : get();
            var field = W.TextInput(row, Shown(), _ => { });
            TypingLook.On(field, row.childCount > 0 ? row.GetChild(0).GetComponent<TMP_Text>() : null);
            field.onEndEdit.AddListener(text =>
            {
                set(string.IsNullOrEmpty(text) || text == Texts.InEnglish(nameId) ? "" : text);
                GameSettings.Apply();
                field.SetTextWithoutNotify(Shown());
            });
            refresh.Add(() => field.SetTextWithoutNotify(Shown()));
        }

        // The panel's "Reset to Default" button: the panel's options back to the convars' defaults, its rows redrawn.
        private static void Reset(SettingsRows rows, List<Action> refresh, Action reset)
        {
            var buttons = W.ButtonRow(rows.Parent);
            W.ActionButton(buttons, Texts.Get("settings.hitfeedback.reset"), false, () =>
            {
                reset();
                GameSettings.Apply();
                foreach (var redraw in refresh) redraw();
            });
        }
    }
}
