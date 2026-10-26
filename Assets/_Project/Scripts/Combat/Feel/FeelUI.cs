using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small helpers shared by the runtime-built feedback HUDs.</summary>
public static class FeelUI
{
    private static readonly Color DarkOutline = new Color(0f, 0f, 0f, .85f);
    private static readonly Dictionary<TMP_FontAsset, Material> shadowed = new Dictionary<TMP_FontAsset, Material>();

    public static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    /// <summary>Overlay canvas scaled like the rest of the combat HUD (1920x1080 reference).</summary>
    public static void SetupOverlay(GameObject host, int order)
    {
        FeelAssets.GroupRuntimeRoot(host, "Screen");
        var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
        var scaler = host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
    }

    /// <summary>Bold outlined label in the project's Korean prompt font.</summary>
    public static TextMeshProUGUI Label(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        var text = Create(name, parent, size, alignment, CombatUIAssets.ResolveFont(null));
        var outlined = CombatUIAssets.Outlined(text.font, DarkOutline, .22f);
        if (outlined != null) text.fontSharedMaterial = outlined;
        text.fontStyle = FontStyles.Bold;
        return text;
    }

    /// <summary>Score/kill-feed label: GunFeel Score Font (heavy sans) with a soft drop shadow instead of an outline.</summary>
    public static TextMeshProUGUI ScoreLabel(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        var font = GunFeelSettings.Instance.scoreFont;
        var text = Create(name, parent, size, alignment, font != null ? font : CombatUIAssets.ResolveFont(null));
        var material = Shadowed(text.font);
        if (material != null) text.fontSharedMaterial = material;
        return text;
    }

    private static TextMeshProUGUI Create(string name, Transform parent, float size, TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Material Shadowed(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;
        if (shadowed.TryGetValue(font, out var material) && material != null) return material;
        material = new Material(font.material) { name = font.name + " Shadow (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        if (material.HasProperty(ShaderUtilities.ID_UnderlayColor)) material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, .7f));
        if (material.HasProperty(ShaderUtilities.ID_UnderlayOffsetX)) material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, .35f);
        if (material.HasProperty(ShaderUtilities.ID_UnderlayOffsetY)) material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.45f);
        if (material.HasProperty(ShaderUtilities.ID_UnderlaySoftness)) material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .45f);
        if (material.HasProperty(ShaderUtilities.ID_UnderlayDilate)) material.SetFloat(ShaderUtilities.ID_UnderlayDilate, .15f);
        shadowed[font] = material;
        return material;
    }
}
