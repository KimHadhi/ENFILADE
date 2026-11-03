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
        var label = W.Label(parent, text, 20f, W.Accent);
        W.Size(label, height: 40f);
        label.alignment = TextAlignmentOptions.BottomLeft;
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

    private void Apply()
    {
        GameSettings.Apply();
        changed?.Invoke();
    }
}
