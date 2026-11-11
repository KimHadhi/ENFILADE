using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// Labelled settings rows under one parent. Each change writes through the caller's setter, applies
/// <see cref="GameSettings"/> and then calls the optional <c>changed</c> callback.
/// </summary>
public sealed class SettingsRows
{
    private readonly Transform parent;
    private readonly Action changed;

    /// <summary>Where the rows go, for controls that are not settings rows.</summary>
    public Transform Parent => parent;

    public SettingsRows(Transform parent, Action changed = null)
    {
        this.parent = parent;
        this.changed = changed;
    }

    public TextMeshProUGUI Header(string text)
    {
        var label = W.Label(parent, text, 16f, W.Accent);
        W.Size(label, height: 38f);
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 4f;
        return label;
    }

    public Slider Volume(string name, float value, Action<float> set) =>
        Slider(name, 0f, 1f, value, set, Percent, shownScale: 100f);

    public static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

    /// <summary>
    /// A slider with its value in a box at the end of the row (<see cref="SettingsWidgets.NumberBox"/>): drag the slider or
    /// click the box and type the number. The box reads <paramref name="format"/> and is typed in the units it shows, the
    /// slider's value times <paramref name="shownScale"/> (100 for a percentage, 2 for a dot drawn at twice its size).
    /// A typed number is held to the slider's range and goes in as a drag would, through <paramref name="set"/>.
    /// </summary>
    public Slider Slider(string name, float min, float max, float value, Action<float> set, Func<float, string> format,
        bool wholeNumbers = false, float shownScale = 1f)
    {
        var row = W.Row(parent, name);
        TMP_InputField box = null;
        Slider slider = null;
        slider = W.Slider(row, min, max, value, v =>
        {
            set(v);
            Apply();
            if (!box.isFocused) box.SetTextWithoutNotify(format(v));
        }, wholeNumbers);
        float scale = Mathf.Approximately(shownScale, 0f) ? 1f : shownScale;
        box = W.NumberBox(row, () => format(slider.value), () => W.Bare(slider.value * scale), typed =>
        {
            float v = Mathf.Clamp(typed / scale, min, max);
            slider.value = wholeNumbers ? Mathf.Round(v) : v; // the slider's listener applies it and refreshes the box
        });
        return slider;
    }

    /// <summary>
    /// A row of preset numbers with a box at its end for any other (a frame cap of 75). The box shows
    /// <paramref name="label"/> of the value ("무제한" for 0); a typed 0 picks <paramref name="presets"/>' 0 when it has one,
    /// anything else is held to <paramref name="min"/>..<paramref name="max"/>. The option matching the value is lit, none
    /// for a number between presets.
    /// </summary>
    public RectTransform NumberChoice(string name, int[] presets, Func<int, string> label, int value, int min, int max,
        Action<int> set)
    {
        var row = W.Row(parent, name);
        int current = value;
        var options = new string[presets.Length];
        for (int i = 0; i < presets.Length; i++) options[i] = label(presets[i]);
        TMP_InputField box = null;
        W.Choice(row, options, Array.IndexOf(presets, value), i =>
        {
            current = presets[i];
            set(current);
            Apply();
            box.SetTextWithoutNotify(label(current));
        }, out var show);
        box = W.NumberBox(row, () => label(current), () => current.ToString(), typed =>
        {
            int number = Mathf.RoundToInt(typed);
            current = number <= 0 && Array.IndexOf(presets, 0) >= 0 ? 0 : Mathf.Clamp(number, min, max);
            show(Array.IndexOf(presets, current));
            set(current);
            Apply();
        });
        return row;
    }

    public RectTransform Choice(string name, string[] options, int selected, Action<int> set)
    {
        var row = W.Row(parent, name);
        W.Choice(row, options, selected, i =>
        {
            set(i);
            Apply();
        });
        return row;
    }

    /// <summary>A row stepping through options one at a time (<see cref="SettingsWidgets.Stepper"/>).</summary>
    public RectTransform Stepper(string name, string[] options, int selected, Action<int> set)
    {
        var row = W.Row(parent, name);
        W.Stepper(row, options, selected, i =>
        {
            set(i);
            Apply();
        });
        return row;
    }

    /// <summary>
    /// A row with a key on a button: pressed, it waits for the next key or mouse button and binds the action to it
    /// (KeyBindings; Esc keeps the old one).
    /// </summary>
    public RectTransform Key(string name, UnityEngine.InputSystem.InputAction action)
    {
        var row = W.Row(parent, name);
        TextMeshProUGUI shown = null;
        var button = W.ActionButton(row, KeyBindings.Name(action), false, () =>
        {
            if (action == null || shown == null) return;
            shown.text = Texts.Get("settings.controls.press_key");
            KeyBindings.Rebind(action, keyName =>
            {
                if (shown != null) shown.text = keyName;
                Apply();
            });
        });
        shown = button.GetComponentInChildren<TextMeshProUGUI>();
        return row;
    }

    /// <summary>A muted line of explanation under the rows above it.</summary>
    public TextMeshProUGUI Note(string text)
    {
        var label = W.Label(parent, text, 17f, W.Muted);
        label.textWrappingMode = TextWrappingModes.Normal;
        W.Size(label, height: 28f);
        return label;
    }

    private void Apply()
    {
        GameSettings.Apply();
        changed?.Invoke();
    }
}
