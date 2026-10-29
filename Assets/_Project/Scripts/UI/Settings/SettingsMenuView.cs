using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>Builds the Esc menu panel and binds each control to <see cref="GameSettings.Current"/>.</summary>
public sealed class SettingsMenuView
{
    private static readonly string[] OffOn = { "끔", "켬" };
    private static readonly string[] HoldToggle = { "누르는 동안", "눌러서 전환" };
    private readonly RectTransform root;
    private readonly Action resume, quit;
    private RectTransform panel;

    public SettingsMenuView(Transform canvas, Action resume, Action quit)
    {
        this.resume = resume; this.quit = quit;
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

    private void Build()
    {
        if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
        panel = W.Rect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(760f, 0f);
        W.Fill(panel, new Color(.06f, .07f, .09f, .96f));
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(44, 44, 36, 36);
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        W.Size(W.Label(panel, "설정", 40f, W.Text), height: 56f);
        var s = GameSettings.Current;

        Header("소리");
        Volume("전체", s.masterVolume, v => s.masterVolume = v);
        Volume("전투", s.combatVolume, v => s.combatVolume = v);
        Volume("이동", s.movementVolume, v => s.movementVolume = v);
        Volume("목소리", s.voiceVolume, v => s.voiceVolume = v);
        Volume("UI", s.uiVolume, v => s.uiVolume = v);

        Header("조작");
        Slider("마우스 감도", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, s.mouseSensitivity,
            v => s.mouseSensitivity = v, v => v.ToString("0.00") + "×");
        Choice("상하 반전", OffOn, s.invertY ? 1 : 0, i => s.invertY = i == 1);
        Choice("앉기", HoldToggle, s.crouchToggle ? 1 : 0, i => s.crouchToggle = i == 1);
        Choice("기울이기", HoldToggle, s.leanToggle ? 1 : 0, i => s.leanToggle = i == 1);

        Header("화면");
        Slider("시야각", GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, s.fieldOfView,
            v => s.fieldOfView = Mathf.Round(v), v => Mathf.RoundToInt(v) + "°");
        Volume("화면 흔들림", s.screenShake, v => s.screenShake = v);
        Choice("내 머리카락 보기", OffOn, s.showOwnHead ? 1 : 0, i => s.showOwnHead = i == 1);

        var buttons = W.Rect("Buttons", panel);
        var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12f; row.childAlignment = TextAnchor.MiddleRight;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.padding = new RectOffset(0, 0, 20, 0);
        W.ActionButton(buttons, "기본값", false, () => { GameSettings.ResetToDefaults(); Build(); });
        W.ActionButton(buttons, "게임 종료", false, quit);
        W.ActionButton(buttons, "돌아가기", true, resume);
    }

    private void Header(string text)
    {
        var label = W.Label(panel, text, 20f, W.Accent);
        W.Size(label, height: 40f);
        label.alignment = TextAlignmentOptions.BottomLeft;
    }

    private void Volume(string name, float value, Action<float> set) =>
        Slider(name, 0f, 1f, value, set, v => Mathf.RoundToInt(v * 100f) + "%");

    private void Slider(string name, float min, float max, float value, Action<float> set, Func<float, string> format)
    {
        var row = W.Row(panel, name);
        TextMeshProUGUI readout = null;
        W.Slider(row, min, max, value, v =>
        {
            set(v);
            GameSettings.Apply();
            readout.text = format(v);
        });
        readout = W.Label(row, format(value), 20f, W.Muted, TextAlignmentOptions.MidlineRight);
        W.Size(readout, width: 76f);
    }

    private void Choice(string name, string[] options, int selected, Action<int> set)
    {
        var row = W.Row(panel, name);
        W.Choice(row, options, selected, i =>
        {
            set(i);
            GameSettings.Apply();
        });
    }
}
