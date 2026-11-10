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
        Slider(name, 0f, 1f, value, set, Percent);

    public static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

    public Slider Slider(string name, float min, float max, float value, Action<float> set, Func<float, string> format,
        bool wholeNumbers = false)
    {
        var row = W.Row(parent, name);
        TextMeshProUGUI readout = null;
        var slider = W.Slider(row, min, max, value, v =>
        {
            set(v);
            Apply();
            readout.text = format(v);
        }, wholeNumbers);
        readout = W.Label(row, format(value), 20f, W.Muted, TextAlignmentOptions.MidlineRight);
        W.Size(readout, width: 96f);
        return slider;
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
