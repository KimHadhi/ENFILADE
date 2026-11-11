using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// Builds the Esc menu panel in the menus' kit look (2026-11-09): "설정" tilted at the top left, a slanted plate per
/// group of settings down the left (the chosen one dark with a thin pink edge and a pink bar, as the lobby's ways to
/// play), the group's controls at the right, each bound to <see cref="GameSettings.Current"/>, and the buttons along the
/// bottom ("이 탭 기본값" at the left, 돌아가기 in pink at the right). The tabs grow in together as it opens (one after
/// another from the top read as slow; user, 2026-11-10). The tabs follow
/// <see cref="SettingsGroup"/>'s order, so "이 탭 기본값" resets the group of the tab on show.
/// </summary>
public sealed class SettingsMenuView
{
    // In SettingsGroup's order: the ids of the tabs' names in Texts.
    private static readonly string[] TabIds =
    {
        "settings.tab.sound", "settings.tab.controls", "settings.tab.screen", "settings.tab.hud", "settings.tab.crosshair",
        "settings.tab.character",
    };
    // The panel (px at 1080p), its margins, the tab column and where the page starts.
    private const float PanelWidth = 1360f, PanelHeight = 860f, Margin = 48f, TabsTop = 132f, TabWidth = 270f, TabHeight = 54f,
        TabGap = 10f, TabSlant = 14f, PageLeft = Margin + TabWidth + 52f, PageWidth = PanelWidth - PageLeft - Margin,
        PageHeight = PanelHeight - TabsTop - 112f;
    private static readonly Color TabClear = KitStyle.With(KitStyle.Ice, .05f), TabDark = KitStyle.With(KitStyle.Night, .92f),
        TabGrey = KitStyle.With(KitStyle.Ice, .35f);
    // Built when asked rather than held in static fields, so reloaded or translated words show at the next build.
    private static string[] OffOn => new[] { Texts.Get("settings.off"), Texts.Get("settings.on") };
    private static string[] HoldToggle => new[] { Texts.Get("settings.hold"), Texts.Get("settings.toggle") };
    // In DamageNumberMode's order.
    private static string[] DamageNumberNames => new[]
    {
        Texts.Get("settings.off"), Texts.Get("settings.hud.damage_numbers.total"), Texts.Get("settings.hud.damage_numbers.each"),
    };
    // In WindowMode's order.
    private static string[] WindowModeNames => new[]
    {
        Texts.Get("settings.screen.window_mode.fullscreen"), Texts.Get("settings.screen.window_mode.borderless"),
        Texts.Get("settings.screen.window_mode.windowed"),
    };
    private static string[] FrameLimitNames =>
        GameSettings.FrameLimits.Select(limit => limit > 0 ? limit.ToString() : Texts.Get("settings.screen.frame_limit.none")).ToArray();
    private static int tab; // reopening shows the last tab
    private readonly RectTransform root;
    private const float ButtonWidth = 156f;
    private readonly Action resume, leave, quit;
    private RectTransform panel, page;
    private readonly List<(Image edge, Image plate, Image bar, TMP_Text label)> tabPlates = new List<(Image, Image, Image, TMP_Text)>();

    public SettingsMenuView(Transform canvas, Action resume, Action leave, Action quit)
    {
        this.resume = resume; this.leave = leave; this.quit = quit;
        root = W.Rect("Settings", canvas);
        W.Stretch(root);
        W.Fill(root, new Color(0f, 0f, 0f, .62f));
        root.gameObject.SetActive(false);
        // Another monitor offers other resolutions: list them once the window is there.
        DisplaySettings.Moved += () =>
        {
            if (root != null && root.gameObject.activeSelf && page != null && (SettingsGroup)tab == SettingsGroup.Screen) BuildPage();
        };
    }

    /// <summary>Showing rebuilds the panel, so it always reflects the saved values.</summary>
    public void Show(bool visible)
    {
        if (visible) Build();
        root.gameObject.SetActive(visible);
    }

    // A fixed size, so switching tabs never moves the panel; long tabs scroll.
    private void Build()
    {
        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        tabPlates.Clear();
        panel = W.Rect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        W.Panel(panel);
        var reveal = panel.gameObject.AddComponent<KitReveal>();
        reveal.Hide();

        var title = KitStyle.TopLeft("Title", panel, new Vector2(Margin, 34f), new Vector2(400f, 64f));
        title.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(title, Texts.Get("settings.title"), 48f, TextAlignmentOptions.MidlineLeft);
        var hint = KitStyle.TopLeft("Hint", panel, new Vector2(PanelWidth - Margin - 300f, 44f), new Vector2(300f, 30f));
        KitStyle.Flat(hint, Texts.Get("settings.hint.close"), 16f, TextAlignmentOptions.MidlineRight, KitStyle.Soft).fontStyle = FontStyles.Normal;

        // All at the same moment, not one after another.
        const float TabsAt = .05f;
        for (int i = 0; i < TabIds.Length; i++)
        {
            var plate = Tab(i);
            var lid = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
            KitSkew.On(lid, TabSlant);
            reveal.Card(plate, lid, TabsAt);
        }
        PaintTabs();

        page = KitStyle.TopLeft("Page", panel, new Vector2(PageLeft, TabsTop), new Vector2(PageWidth, PageHeight));
        var pageLayout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        pageLayout.spacing = 8f;
        pageLayout.childControlWidth = pageLayout.childControlHeight = true;
        pageLayout.childForceExpandWidth = true; pageLayout.childForceExpandHeight = false;
        BuildPage();

        // The buttons: this tab's defaults at the left, then leaving, then back to the game in pink.
        var line = KitStyle.Bar("Line", panel, new Vector2(Margin, PanelHeight - 100f), new Vector2(PanelWidth - Margin * 2f, 1f),
            KitStyle.With(KitStyle.Ice, .1f));
        line.raycastTarget = false;
        var buttons = KitStyle.TopLeft("Buttons", panel, new Vector2(Margin, PanelHeight - 82f), new Vector2(PanelWidth - Margin * 2f, 50f));
        var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        W.Size(W.ActionButton(buttons, Texts.Get("settings.defaults"), false, () => { GameSettings.ResetToDefaults((SettingsGroup)tab); Build(); }),
            ButtonWidth + 20f, 46f);
        W.Size(W.Rect("Gap", buttons), flexibleWidth: 1f);
        if (!MainMenuScreen.IsOpen) W.Size(W.ActionButton(buttons, Texts.Get("settings.main_menu"), false, leave), ButtonWidth, 46f);
        W.Size(W.ActionButton(buttons, Texts.Get("settings.quit"), false, quit), ButtonWidth, 46f);
        W.Size(W.ActionButton(buttons, Texts.Get("settings.resume"), true, resume), ButtonWidth + 20f, 46f);
        reveal.Open();
    }

    // One slanted tab plate down the left; pressing it shows its group.
    private RectTransform Tab(int index)
    {
        string name = Texts.Get(TabIds[index]);
        var plate = KitStyle.TopLeft("Tab " + name, panel, new Vector2(Margin + (index % 2) * 8f, TabsTop + index * (TabHeight + TabGap)),
            new Vector2(TabWidth, TabHeight));
        KitStyle.Fill(plate, Color.clear).raycastTarget = true;
        var edgeRect = KitStyle.Stretch("Edge", plate);
        edgeRect.sizeDelta = new Vector2(2f, 2f);
        var edge = KitStyle.Fill(edgeRect, Color.clear);
        KitSkew.On(edge, TabSlant);
        var fill = KitStyle.Fill(KitStyle.Stretch("Plate", plate), TabClear);
        KitSkew.On(fill, TabSlant);
        var bar = KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, TabHeight), TabGrey);
        KitSkew.On(bar, TabSlant);
        var label = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(26f, 0f), new Vector2(TabWidth - 40f, TabHeight)), name, 21f,
            TextAlignmentOptions.MidlineLeft);
        label.fontStyle = FontStyles.Normal;
        tabPlates.Add((edge, fill, bar, label));
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            if (tab == index) return;
            tab = index;
            PaintTabs();
            BuildPage();
        });
        plate.gameObject.AddComponent<KitHover>();
        return plate;
    }

    private void PaintTabs()
    {
        for (int i = 0; i < tabPlates.Count; i++)
        {
            bool on = i == tab;
            var (edge, plate, bar, label) = tabPlates[i];
            edge.color = KitStyle.With(KitStyle.Accent, on ? .7f : 0f);
            plate.color = on ? TabDark : TabClear;
            bar.color = on ? KitStyle.Accent : TabGrey;
            label.color = on ? KitStyle.Text : KitStyle.Soft;
        }
    }

    private void BuildPage()
    {
        W.Clear(page);
        var group = (SettingsGroup)tab;
        if (group == SettingsGroup.Crosshair)
        {
            new CrosshairSettingsView(page);
            return;
        }
        if (group == SettingsGroup.Character)
        {
            new CharacterSkinView(page, PageWidth, true);
            return;
        }
        var rows = new SettingsRows(W.ScrollArea(page));
        var s = GameSettings.Current;
        if (group == SettingsGroup.Sound) SoundTab(rows, s);
        else if (group == SettingsGroup.Controls) ControlsTab(rows, s);
        else if (group == SettingsGroup.Screen) ScreenTab(rows, s);
        else HudTab(rows, s);
    }

    private static void SoundTab(SettingsRows rows, GameSettings s)
    {
        rows.Volume(Texts.Get("settings.sound.master"), s.masterVolume, v => s.masterVolume = v);
        rows.Volume(Texts.Get("settings.sound.combat"), s.combatVolume, v => s.combatVolume = v);
        rows.Volume(Texts.Get("settings.sound.movement"), s.movementVolume, v => s.movementVolume = v);
        rows.Volume(Texts.Get("settings.sound.voice"), s.voiceVolume, v => s.voiceVolume = v);
        rows.Volume(Texts.Get("settings.sound.ui"), s.uiVolume, v => s.uiVolume = v);
        rows.Volume(Texts.Get("settings.sound.music"), s.musicVolume, v => s.musicVolume = v);
        rows.Volume(Texts.Get("settings.sound.ambience"), s.ambienceVolume, v => s.ambienceVolume = v);
    }

    private static void ControlsTab(SettingsRows rows, GameSettings s)
    {
        rows.Slider(Texts.Get("settings.controls.sensitivity"), GameSettings.MinSensitivity, GameSettings.MaxSensitivity, s.mouseSensitivity,
            v => s.mouseSensitivity = v, v => v.ToString("0.00") + "×");
        rows.Choice(Texts.Get("settings.controls.invert_y"), OffOn, s.invertY ? 1 : 0, i => s.invertY = i == 1);
        rows.Choice(Texts.Get("settings.controls.crouch"), HoldToggle, s.crouchToggle ? 1 : 0, i => s.crouchToggle = i == 1);
        rows.Choice(Texts.Get("settings.controls.lean"), HoldToggle, s.leanToggle ? 1 : 0, i => s.leanToggle = i == 1);
        // The class key (stage 7): the wheel click unless picked otherwise.
        rows.Key(Texts.Get("settings.controls.class_key"),
            UnityEngine.InputSystem.InputSystem.actions != null ? UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/ClassAbility", false) : null);
        // The kick key (porting bundle 9, Mighty Foot Engaged): the mouse's back side button unless picked otherwise.
        rows.Key(Texts.Get("settings.controls.kick_key"),
            UnityEngine.InputSystem.InputSystem.actions != null ? UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/Kick", false) : null);
        // A dual pistol's left gun alone (AkimboGuns): the right button unless picked otherwise.
        rows.Key(Texts.Get("settings.controls.alt_fire"),
            UnityEngine.InputSystem.InputSystem.actions != null ? UnityEngine.InputSystem.InputSystem.actions.FindAction("Combat/AltFire", false) : null);
    }

    private static void ScreenTab(SettingsRows rows, GameSettings s)
    {
        rows.Header(Texts.Get("settings.screen.display"));
        if (!DisplaySettings.Applies) rows.Note(Texts.Get("settings.screen.editor_note"));
        var monitors = new List<DisplayInfo>(DisplaySettings.Monitors());
        if (monitors.Count > 0)
            rows.Stepper(Texts.Get("settings.screen.monitor"), monitors.Select(DisplaySettings.MonitorLabel).ToArray(), DisplaySettings.ChosenMonitor(), i =>
            {
                s.monitorName = monitors[i].name;
                s.monitorIndex = i;
                s.resolutionWidth = s.resolutionHeight = 0; // the new monitor's own, until another is picked there
            });
        rows.Choice(Texts.Get("settings.screen.window_mode"), WindowModeNames, (int)s.windowMode, i => s.windowMode = (WindowMode)i);
        ResolutionRow(rows, s);

        rows.Header(Texts.Get("settings.screen.frame"));
        RectTransform limitRow = null;
        rows.Choice(Texts.Get("settings.screen.vsync"), OffOn, s.vSync ? 1 : 0, i =>
        {
            s.vSync = i == 1;
            limitRow.gameObject.SetActive(!s.vSync); // vSync follows the monitor, so a cap means nothing
        });
        limitRow = rows.Choice(Texts.Get("settings.screen.frame_limit"), FrameLimitNames, Array.IndexOf(GameSettings.FrameLimits, s.frameLimit),
            i => s.frameLimit = GameSettings.FrameLimits[i]);
        limitRow.gameObject.SetActive(!s.vSync);

        rows.Header(Texts.Get("settings.screen.view"));
        rows.Slider(Texts.Get("settings.screen.fov"), GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, s.fieldOfView,
            v => s.fieldOfView = Mathf.Round(v), v => Mathf.RoundToInt(v) + "°");
        rows.Volume(Texts.Get("settings.screen.shake"), s.screenShake, v => s.screenShake = v);
        rows.Choice(Texts.Get("settings.screen.own_head"), OffOn, s.showOwnHead ? 1 : 0, i => s.showOwnHead = i == 1);
    }

    // The first option is the monitor's own resolution (saved as 0 x 0), so it stays right on another monitor.
    private static void ResolutionRow(SettingsRows rows, GameSettings s)
    {
        var sizes = DisplaySettings.Resolutions();
        var saved = new Vector2Int(s.resolutionWidth, s.resolutionHeight);
        if (saved.x > 0 && !sizes.Contains(saved)) sizes.Insert(0, saved); // chosen on a monitor this one does not match
        var options = new string[sizes.Count + 1];
        options[0] = Texts.Format("settings.screen.resolution.native", DisplaySettings.ResolutionLabel(DisplaySettings.Native()));
        for (int i = 0; i < sizes.Count; i++) options[i + 1] = DisplaySettings.ResolutionLabel(sizes[i]);
        int selected = saved.x > 0 ? sizes.IndexOf(saved) + 1 : 0;
        rows.Stepper(Texts.Get("settings.screen.resolution"), options, selected, i =>
        {
            var size = i > 0 ? sizes[i - 1] : Vector2Int.zero;
            s.resolutionWidth = size.x;
            s.resolutionHeight = size.y;
        });
    }

    private static void HudTab(SettingsRows rows, GameSettings s)
    {
        rows.Volume(Texts.Get("settings.hud.sway"), s.hudSway, v => s.hudSway = v);
        rows.Slider(Texts.Get("settings.hud.tilt"), 0f, GameSettings.MaxHudTilt, s.hudTilt, v => s.hudTilt = Mathf.Round(v * 20f) / 20f,
            v => SettingsRows.Percent(Mathf.Round(v * 20f) / 20f));
        rows.Slider(Texts.Get("settings.hud.angle"), 0f, GameSettings.MaxHudAngle, s.hudAngle, v => s.hudAngle = Mathf.Round(v),
            v => Mathf.RoundToInt(v) + "°");
        rows.Choice(Texts.Get("settings.hud.follow_lean"), OffOn, s.hudFollowsLean ? 1 : 0, i => s.hudFollowsLean = i == 1);
        rows.Choice(Texts.Get("settings.hud.boards"), OffOn, s.hudBoards ? 1 : 0, i => s.hudBoards = i == 1);
        rows.Choice(Texts.Get("settings.hud.damage_numbers"), DamageNumberNames, (int)s.damageNumbers, i => s.damageNumbers = (DamageNumberMode)i);
    }
}
