using UnityEngine;

/// <summary>
/// Marks a menu built in the HUD's look (HudMenu): every widget SettingsWidgets and SettingsRows make under it wears that
/// instead of the title's kit look. On the settings canvas, so the settings, the key window, the colour board and the HUD
/// settings all match without being told; a narrow panel under it carries one of its own with a shorter name column.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudMenuHost : MonoBehaviour
{
    /// <summary>The width of a row's name, and a row's height (px).</summary>
    public float LabelWidth = 320f, RowHeight = 46f;
    /// <summary>The width of a value's box at the end of a row (px).</summary>
    public float BoxWidth = 104f;

    /// <summary>The look's host over <paramref name="at"/>, or null in the title's kit.</summary>
    public static HudMenuHost Of(Transform at) => at != null ? at.GetComponentInParent<HudMenuHost>(true) : null;
}
