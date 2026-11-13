using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The menus' buttons: cut-corner plates with an icon, a thin bar, then the label. Primary is the pink one with
/// a dark label and ▶▶▶ marching at its right; Secondary is a dark one with a white icon and a pink bar; Go is
/// the near-white call to start, with stripes running across it. A small index number ("01") can sit in the top-right
/// corner like a rangefinder's mark. Each has KitHover's corner brackets and lean, and a press that darkens it. Icons
/// are white LAYERLAB Game Picto Pack pictograms copied to Resources/UI/Icons, tinted here.
/// </summary>
public static class KitButtons
{
    private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => icons.Clear();

    /// <summary>An icon from Resources/UI/Icons by name (Play, Room, Servers, Settings, Profile, Character, Map, Quit, Left, Right).</summary>
    public static Texture2D Icon(string name)
    {
        if (icons.TryGetValue(name, out var icon) && icon != null) return icon;
        return icons[name] = Resources.Load<Texture2D>("UI/Icons/" + name);
    }

    public static RawImage IconImage(Transform parent, string name, float size, Color tint)
    {
        var rect = FeelUI.Rect("Icon " + name, parent);
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = Icon(name);
        image.color = tint;
        image.raycastTarget = false;
        image.enabled = image.texture != null;
        return image;
    }

    /// <summary>A see-through pink lid over <paramref name="rect"/> for KitReveal.Card to drain, cut like the rect is.</summary>
    public static Image Lid(RectTransform rect)
    {
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", rect), KitStyle.Flash);
        if (rect.TryGetComponent(out Image under) && under.sprite == KitShapes.Chamfer) KitStyle.Shape(lid);
        return lid;
    }
}
