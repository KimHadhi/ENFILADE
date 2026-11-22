using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// Builds the Esc menu panel in the HUD's look (HudMenu; the user, 2026-11-22: "설정창도 리디자인해서 시인성, 가독성,
/// 통일성 좋게"; it was the menus' pink kit): a dark side with the tabs, the chosen one lit white, and leaving at its foot;
/// at the right the tab's name with "이 탭 기본값", its settings in cards under headings, each bound to
/// <see cref="GameSettings.Current"/>, and 돌아가기 at the foot. The tabs follow <see cref="SettingsGroup"/>'s order, so
/// "이 탭 기본값" resets the group of the tab on show. HUD has no page here: its tab opens the HUD settings on the HUD itself
/// (HudSettingsView).
/// There is no 캐릭터 tab (user, 2026-11-12: "설정에서 캐릭터 바꾸는 거 이제 없애도 될 듯"): the character, its colours,
/// clothes and body are chosen in the title screen's wardrobe, and in the hub at the stage's mirror (HubDressing).
/// SettingsGroup.Character stays as the reset group for those settings.
/// </summary>
public sealed class SettingsMenuView
{
    // In SettingsGroup's order: the ids of the tabs' names in Texts. SettingsGroup.Character, the last, has no tab.
    private static readonly string[] TabIds =
    {
        "settings.tab.sound", "settings.tab.controls", "settings.tab.screen", "settings.tab.camera", "settings.tab.hud",
        "settings.tab.crosshair",
    };
    // The panel (px at 1080p), its side, the page beside it.
    private const float PanelWidth = 1480f, PanelHeight = 900f, SideWidth = 270f, TabsTop = 112f, TabHeight = 52f, TabGap = 6f, SideInset = 16f,
        PageLeft = SideWidth + 34f, PageWidth = PanelWidth - PageLeft - 30f, PageTop = 112f, PageHeight = PanelHeight - PageTop - 96f;
    // Built when asked rather than held in static fields, so reloaded or translated words show at the next build.
    private static string[] HoldToggle => new[] { Texts.Get("settings.hold"), Texts.Get("settings.toggle") };
    // In WindowMode's order.
    private static string[] WindowModeNames => new[]
    {
        Texts.Get("settings.screen.window_mode.fullscreen"), Texts.Get("settings.screen.window_mode.borderless"),
        Texts.Get("settings.screen.window_mode.windowed"),
    };
    private static string FrameLimitName(int limit) => limit > 0 ? limit.ToString() : Texts.Get("settings.screen.frame_limit.none");
    // In TextureDetail's order.
    private static string[] TextureDetailNames => new[]
    {
        Texts.Get("settings.screen.texture_quality.full"), Texts.Get("settings.screen.texture_quality.high"),
        Texts.Get("settings.screen.texture_quality.medium"), Texts.Get("settings.screen.texture_quality.low"),
    };
    private static int tab; // reopening shows the last tab
    private readonly RectTransform root;
    private static readonly Color Dim = new Color(0f, 0f, 0f, .8f);
    private readonly Action resume, leave, quit;
    private readonly Image dim;
    private RectTransform panel, page;
    private TextMeshProUGUI pageTitle;
    private readonly List<HudMenu.Entry> tabs = new List<HudMenu.Entry>();

    public SettingsMenuView(Transform canvas, Action resume, Action leave, Action quit)
    {
        this.resume = resume; this.leave = leave; this.quit = quit;
        root = W.Rect("Settings", canvas);
        KitStyle.Stretch(root);
        dim = W.Fill(root, Dim);
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
        else
        {
            KeyBindingsView.CloseIfOpen();
            HudSettingsView.CloseIfOpen();
        }
        root.gameObject.SetActive(visible);
    }

    // A fixed size, so switching tabs never moves the panel; long tabs scroll.
    private void Build()
    {
        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        tabs.Clear();
        // A tab remembered from before it went (the 캐릭터 tab) reopens on the first.
        if (tab < 0 || tab >= TabIds.Length || tab == (int)SettingsGroup.Hud) tab = 0;
        dim.color = Dim;
        panel = W.Rect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        W.Panel(panel);

        // The side: the menu's name, the tabs, leaving at the foot.
        var side = KitStyle.TopLeft("Side", panel, new Vector2(1f, 1f), new Vector2(SideWidth, PanelHeight - 2f));
        HudMenu.Box(side, HudMenu.Side, false, 9f);
        var title = W.Label(KitStyle.TopLeft("Title", side, new Vector2(SideInset + 14f, 30f), new Vector2(SideWidth - 40f, 56f)),
            Texts.Get("settings.title"), 38f, HudMenu.Text);
        KitStyle.Stretch(title.rectTransform);
        title.fontStyle = FontStyles.Bold;
        for (int i = 0; i < TabIds.Length; i++)
        {
            int index = i;
            var rect = KitStyle.TopLeft("Tab " + TabIds[i], side, new Vector2(SideInset, TabsTop + i * (TabHeight + TabGap)),
                new Vector2(SideWidth - SideInset * 2f, TabHeight));
            string name = Texts.Get(TabIds[i]);
            tabs.Add(new HudMenu.Entry(rect, i == (int)SettingsGroup.Hud ? name + "  >" : name, 21f, () => Choose(index)));
        }
        float foot = PanelHeight - SideInset - 46f;
        SideButton(side, foot, "settings.quit", quit);
        if (!MainMenuScreen.IsOpen) SideButton(side, foot - 52f, "settings.main_menu", leave);

        // The page: the tab's name and its defaults over it, 돌아가기 under it.
        pageTitle = W.Label(KitStyle.TopLeft("Page Title", panel, new Vector2(PageLeft + 6f, 34f), new Vector2(600f, 48f)), "", 32f, HudMenu.Text);
        KitStyle.Stretch(pageTitle.rectTransform);
        pageTitle.fontStyle = FontStyles.Bold;
        var defaults = KitStyle.TopLeft("Defaults", panel, new Vector2(PageLeft + PageWidth - 170f, 36f), new Vector2(170f, 42f));
        Filled(W.ActionButton(defaults, Texts.Get("settings.defaults"), false, () =>
        {
            GameSettings.ResetToDefaults((SettingsGroup)tab);
            BuildPage();
        }));
        KitStyle.Bar("Line", panel, new Vector2(PageLeft, 96f), new Vector2(PageWidth, 1f), HudMenu.Line);

        page = KitStyle.TopLeft("Page", panel, new Vector2(PageLeft, PageTop), new Vector2(PageWidth, PageHeight));
        var pageLayout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        pageLayout.spacing = 10f;
        pageLayout.childControlWidth = pageLayout.childControlHeight = true;
        pageLayout.childForceExpandWidth = true; pageLayout.childForceExpandHeight = false;
        BuildPage();

        var back = KitStyle.TopLeft("Back", panel, new Vector2(PageLeft + PageWidth - 220f, PanelHeight - 72f), new Vector2(220f, 48f));
        Filled(W.ActionButton(back, Texts.Get("settings.resume"), true, resume));
        var hint = W.Label(KitStyle.TopLeft("Hint", panel, new Vector2(PageLeft + 6f, PanelHeight - 72f), new Vector2(400f, 48f)),
            Texts.Get("settings.hint.close"), 16f, HudMenu.Muted);
        KitStyle.Stretch(hint.rectTransform);
    }

    private static void SideButton(RectTransform side, float top, string id, Action clicked)
        => Filled(W.ActionButton(KitStyle.TopLeft(id, side, new Vector2(SideInset, top), new Vector2(SideWidth - SideInset * 2f, 44f)), Texts.Get(id), false, clicked));

    private static void Filled(Button button) => KitStyle.Stretch((RectTransform)button.transform);

    private void Choose(int index)
    {
        if (index == (int)SettingsGroup.Hud)
        {
            OpenHud();
            return;
        }
        if (tab == index) return;
        tab = index;
        BuildPage();
    }

    // The HUD is set on the HUD itself (HudSettingsView), so the window steps aside for it and comes back as it was.
    private void OpenHud()
    {
        panel.gameObject.SetActive(false);
        dim.color = Color.clear;
        HudSettingsView.Open(root, () =>
        {
            dim.color = Dim;
            panel.gameObject.SetActive(true);
            BuildPage();
        });
    }

    private void BuildPage()
    {
        for (int i = 0; i < tabs.Count; i++) tabs[i].Show(i == tab);
        pageTitle.text = Texts.Get(TabIds[tab]);
        W.Clear(page);
        var group = (SettingsGroup)tab;
        if (group == SettingsGroup.Crosshair)
        {
            new CrosshairSettingsView(page);
            return;
        }
        var rows = new SettingsRows(W.ScrollArea(page));
        var s = GameSettings.Current;
        if (group == SettingsGroup.Sound) SoundTab(rows, s);
        else if (group == SettingsGroup.Controls) ControlsTab(rows, s);
        else if (group == SettingsGroup.Screen) ScreenTab(rows, s);
        else CameraTab(rows, s);
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

    private void ControlsTab(SettingsRows rows, GameSettings s)
    {
        // Every key the game reads, in a window of its own (KeyBindingsView; user, 2026-11-12: "키 설정 창을 아예 따로
        // 만들었으면 좋겠음"). The class, knock and callout keys that had rows here are lines there.
        rows.Header(Texts.Get("settings.controls.header.keys"));
        var keys = W.Row(rows.Parent, Texts.Get("settings.controls.keys"));
        W.Size(W.ActionButton(keys, Texts.Get("settings.controls.keys_open"), false, () =>
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            KeyBindingsView.Open(root, BuildPage);
        }), 250f, 40f);
        int moved = KeyBindings.ChangedCount();
        W.Size(W.Label(keys, moved > 0 ? Texts.Format("settings.controls.keys_changed", moved) : Texts.Get("settings.controls.keys_default"), 17f,
            moved > 0 ? HudMenu.Text : HudMenu.Muted), flexibleWidth: 1f);
        rows.Header(Texts.Get("settings.controls.header.mouse"));
        rows.Slider(Texts.Get("settings.controls.sensitivity"), GameSettings.MinSensitivity, GameSettings.MaxSensitivity, s.mouseSensitivity,
            v => s.mouseSensitivity = v, v => v.ToString("0.00") + "×");
        // The mouse through each magnification the game's scopes have (ScopeLook reads them from GunParts.csv, so a mod's
        // sight of a new power gets its row): 100% = a target crosses the screen as fast as it does unaimed.
        foreach (float power in ScopeLook.Magnifications)
            rows.Slider(Texts.Format("settings.controls.scope_sensitivity", power.ToString("0.#")), ScopeLook.Least, ScopeLook.Most, ScopeLook.Of(s, power),
                v => ScopeLook.Set(s, power, v), v => Mathf.RoundToInt(v * 100f) + "%", shownScale: 100f);
        if (ScopeLook.Magnifications.Count > 0) rows.Note(Texts.Get("settings.controls.scope_sensitivity.note"));
        rows.Toggle(Texts.Get("settings.controls.invert_y"), s.invertY, on => s.invertY = on);
        rows.Header(Texts.Get("settings.controls.header.moves"));
        rows.Choice(Texts.Get("settings.controls.crouch"), HoldToggle, s.crouchToggle ? 1 : 0, i => s.crouchToggle = i == 1);
        rows.Choice(Texts.Get("settings.controls.lean"), HoldToggle, s.leanToggle ? 1 : 0, i => s.leanToggle = i == 1);
        rows.Toggle(Texts.Get("settings.controls.auto_reload"), s.autoReload, on => s.autoReload = on);
        rows.Header(Texts.Get("settings.controls.header.callouts"));
        // Callouts (memo 4, G&B's wheel): how the wheel's key says (the key itself is in the key window).
        rows.Toggle(Texts.Get("settings.controls.callout_release"), s.calloutOnRelease, on => s.calloutOnRelease = on);
        rows.Toggle(Texts.Get("settings.controls.callout_bubbles"), s.calloutBubbles, on => s.calloutBubbles = on);
        rows.Toggle(Texts.Get("settings.controls.callout_auto"), s.calloutAuto, on => s.calloutAuto = on);
        rows.Toggle(Texts.Get("settings.controls.callout_feed"), s.calloutFeed, on => s.calloutFeed = on);
        rows.Header(Texts.Get("settings.controls.header.party"));
        // Friends' pokes (a held F's fingertip, SkinPoke): off, theirs stop short of this character on every PC.
        rows.Toggle(Texts.Get("settings.controls.let_poke"), s.letOthersPoke, on => s.letOthersPoke = on);
        // Avatar mods passed round a room (ModShare): off, this PC neither fetches friends' nor offers its own.
        rows.Toggle(Texts.Get("settings.controls.share_mods"), s.shareAvatarMods, on => s.shareAvatarMods = on);
    }

    private static void ScreenTab(SettingsRows rows, GameSettings s)
    {
        rows.Header(Texts.Get("settings.screen.display"));
        var languages = Texts.LanguageNames.ToArray();
        int selected = Array.FindIndex(languages, l => string.Equals(l.Key, s.textLanguage, StringComparison.OrdinalIgnoreCase));
        if (selected < 0) selected = Array.FindIndex(languages, l => l.Key == Texts.Language);
        rows.Stepper(Texts.Get("settings.screen.language"), languages.Select(l => l.Value).ToArray(), Mathf.Max(0, selected),
            i =>
            {
                s.textLanguage = languages[i].Key;
                GameSettings.Save();
            });
        rows.Note(Texts.Get("settings.screen.language.restart"));

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
        rows.Toggle(Texts.Get("settings.screen.vsync"), s.vSync, on =>
        {
            s.vSync = on;
            limitRow.gameObject.SetActive(!s.vSync); // vSync follows the monitor, so a cap means nothing
        });
        // The presets, or any cap typed in the row's box (0 for none).
        limitRow = rows.NumberChoice(Texts.Get("settings.screen.frame_limit"), GameSettings.FrameLimits, FrameLimitName, s.frameLimit,
            GameSettings.MinFrameLimit, GameSettings.MaxFrameLimit, limit => s.frameLimit = limit);
        limitRow.gameObject.SetActive(!s.vSync);

        rows.Header(Texts.Get("settings.screen.graphics"));
        rows.Choice(Texts.Get("settings.screen.texture_quality"), TextureDetailNames, (int)s.textureDetail, i => s.textureDetail = (TextureDetail)i);
        rows.Note(Texts.Get("settings.screen.texture_quality_note"));

        rows.Header(Texts.Get("settings.screen.view"));
        rows.Slider(Texts.Get("settings.screen.fov"), GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, s.fieldOfView,
            v => s.fieldOfView = Mathf.Round(v), v => Mathf.RoundToInt(v) + "°");
        rows.Volume(Texts.Get("settings.screen.shake"), s.screenShake, v => s.screenShake = v);
        // How much of the body's own sway the view follows; the head bends to the view either way (HeadReach).
        rows.Volume(Texts.Get("settings.screen.body_sway"), s.bodySway, v => s.bodySway = v);
        rows.Toggle(Texts.Get("settings.screen.own_head"), s.showOwnHead, on => s.showOwnHead = on);
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

    // Rookie mode's switch and, while it is on, a switch per move (ThirdPersonCamera); then the execution cinematic.
    private static void CameraTab(SettingsRows rows, GameSettings s)
    {
        rows.Header(Texts.Get("settings.camera.rookie_header"));
        var moves = new List<RectTransform>();
        rows.Toggle(Texts.Get("settings.camera.rookie"), s.rookieMode, on =>
        {
            s.rookieMode = on;
            foreach (var move in moves) move.gameObject.SetActive(s.rookieMode); // the moves only count with it on
        });
        rows.Note(Texts.Get("settings.camera.rookie_note"));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.vault"), s.rookieVault, on => s.rookieVault = on));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.ladder"), s.rookieLadder, on => s.rookieLadder = on));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.landing_roll"), s.rookieLandingRoll, on => s.rookieLandingRoll = on));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.dodge"), s.rookieDodge, on => s.rookieDodge = on));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.zipline"), s.rookieZipline, on => s.rookieZipline = on));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.slide"), s.rookieSlide, on => s.rookieSlide = on));
        moves.Add(rows.Toggle(Texts.Get("settings.camera.rookie.bars"), s.rookieBars, on => s.rookieBars = on));
        foreach (var move in moves) move.gameObject.SetActive(s.rookieMode);

        rows.Header(Texts.Get("settings.camera.cinematic_header"));
        rows.Toggle(Texts.Get("settings.camera.execution"), s.executionCinematic, on => s.executionCinematic = on);
        rows.Note(Texts.Get("settings.camera.execution_note"));
    }
}
