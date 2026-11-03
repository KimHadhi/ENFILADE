using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>Builds the Esc menu panel: a tab per group of settings, each control bound to <see cref="GameSettings.Current"/>.</summary>
public sealed class SettingsMenuView
{
    private static readonly string[] Tabs = { "소리", "조작", "화면", "크로스헤어" };
    private const int CrosshairTab = 3;
    private static readonly string[] OffOn = { "끔", "켬" };
    private static readonly string[] HoldToggle = { "누르는 동안", "눌러서 전환" };
    // In DamageNumberMode's order.
    private static readonly string[] DamageNumberNames = { "끔", "합산", "낱개" };
    private static readonly string[] FrameLimitNames =
        GameSettings.FrameLimits.Select(limit => limit > 0 ? limit.ToString() : "무제한").ToArray();
    private static int tab; // reopening shows the last tab
    private readonly RectTransform root;
    private const float ButtonWidth = 156f;
    private readonly Action resume, leave, quit;
    private RectTransform panel, page;

    public SettingsMenuView(Transform canvas, Action resume, Action leave, Action quit)
    {
        this.resume = resume; this.leave = leave; this.quit = quit;
        root = W.Rect("Settings", canvas);
        W.Stretch(root);
        W.Fill(root, new Color(0f, 0f, 0f, .62f));
        root.gameObject.SetActive(false);
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
        panel = W.Rect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(820f, 920f);
        W.Fill(panel, new Color(.06f, .07f, .09f, .96f));
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(44, 44, 36, 36);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

        W.Size(W.Label(panel, "설정", 40f, W.Text), height: 56f);
        W.Size(W.Choice(panel, Tabs, tab, i => { tab = i; BuildPage(); }), height: 44f, flexibleWidth: 1f, flexibleHeight: 0f);
        page = W.Rect("Page", panel);
        W.Size(page, flexibleWidth: 1f, flexibleHeight: 1f);
        var pageLayout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        pageLayout.padding = new RectOffset(0, 0, 8, 0);
        pageLayout.spacing = 8f;
        pageLayout.childControlWidth = pageLayout.childControlHeight = true;
        pageLayout.childForceExpandWidth = true; pageLayout.childForceExpandHeight = false;
        BuildPage();

        var buttons = W.ButtonRow(panel);
        W.Size(W.ActionButton(buttons, "기본값", false, () => { GameSettings.ResetToDefaults(); Build(); }), ButtonWidth, 48f);
        if (!MainMenuScreen.IsOpen) W.Size(W.ActionButton(buttons, "메인 메뉴", false, leave), ButtonWidth, 48f);
        W.Size(W.ActionButton(buttons, "게임 종료", false, quit), ButtonWidth, 48f);
        W.Size(W.ActionButton(buttons, "돌아가기", true, resume), ButtonWidth, 48f);
    }

    private void BuildPage()
    {
        W.Clear(page);
        if (tab == CrosshairTab)
        {
            new CrosshairSettingsView(page);
            return;
        }
        var rows = new SettingsRows(W.ScrollArea(page));
        var s = GameSettings.Current;
        if (tab == 0) SoundTab(rows, s);
        else if (tab == 1) ControlsTab(rows, s);
        else ScreenTab(rows, s);
    }

    private static void SoundTab(SettingsRows rows, GameSettings s)
    {
        rows.Volume("전체", s.masterVolume, v => s.masterVolume = v);
        rows.Volume("전투", s.combatVolume, v => s.combatVolume = v);
        rows.Volume("이동", s.movementVolume, v => s.movementVolume = v);
        rows.Volume("목소리", s.voiceVolume, v => s.voiceVolume = v);
        rows.Volume("UI", s.uiVolume, v => s.uiVolume = v);
    }

    private static void ControlsTab(SettingsRows rows, GameSettings s)
    {
        rows.Slider("마우스 감도", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, s.mouseSensitivity,
            v => s.mouseSensitivity = v, v => v.ToString("0.00") + "×");
        rows.Choice("상하 반전", OffOn, s.invertY ? 1 : 0, i => s.invertY = i == 1);
        rows.Choice("앉기", HoldToggle, s.crouchToggle ? 1 : 0, i => s.crouchToggle = i == 1);
        rows.Choice("기울이기", HoldToggle, s.leanToggle ? 1 : 0, i => s.leanToggle = i == 1);
    }

    private static void ScreenTab(SettingsRows rows, GameSettings s)
    {
        rows.Slider("시야각", GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, s.fieldOfView,
            v => s.fieldOfView = Mathf.Round(v), v => Mathf.RoundToInt(v) + "°");
        rows.Volume("화면 흔들림", s.screenShake, v => s.screenShake = v);
        rows.Volume("HUD 흔들림", s.hudSway, v => s.hudSway = v);
        rows.Choice("HUD 그리드 배경", OffOn, s.hudBoards ? 1 : 0, i => s.hudBoards = i == 1);
        rows.Choice("데미지 숫자", DamageNumberNames, (int)s.damageNumbers, i => s.damageNumbers = (DamageNumberMode)i);
        rows.Choice("내 머리카락 보기", OffOn, s.showOwnHead ? 1 : 0, i => s.showOwnHead = i == 1);
        var bodyProfile = PlayerBodyProfile.Load();
        var skins = bodyProfile?.Skins.Where(skin => skin != null && skin.Model != null).ToArray();
        if (skins != null && skins.Length > 1 && !MainMenuScreen.IsOpen)
        {
            int selected = Array.FindIndex(skins, skin => skin.Model == bodyProfile.Model);
            rows.Choice("캐릭터 스킨", skins.Select(skin => skin.DisplayName).ToArray(), Mathf.Max(0, selected),
                i => s.characterSkin = skins[i].Id);
        }

        rows.Header("프레임");
        RectTransform limitRow = null;
        rows.Choice("수직 동기화", OffOn, s.vSync ? 1 : 0, i =>
        {
            s.vSync = i == 1;
            limitRow.gameObject.SetActive(!s.vSync); // vSync follows the monitor, so a cap means nothing
        });
        limitRow = rows.Choice("프레임 제한", FrameLimitNames, Array.IndexOf(GameSettings.FrameLimits, s.frameLimit),
            i => s.frameLimit = GameSettings.FrameLimits[i]);
        limitRow.gameObject.SetActive(!s.vSync);
    }
}
