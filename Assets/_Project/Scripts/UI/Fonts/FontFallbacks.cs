using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The last links of the font chain: operating-system fonts, so whatever a player types (a name, a chat line) draws
/// instead of a box. The game's own faces cover every character of the tables (ENFILADE → 글꼴 → 글꼴 검사 proves it) and
/// all of Hangul, Latin, Greek, Cyrillic and kana, but not Chinese characters, emoji or rarer scripts, which names and
/// chat can hold. Fonts.csv Fallback lists the families to try; the ones this machine has join behind TMP Settings' own
/// fallbacks, each as a TMP DynamicOS asset (no file of ours, glyphs drawn on first use). Emoji families also join TMP's
/// emoji list, which TMP asks first for an emoji, after TMP's default sprite asset (its few colour emoji keep their
/// colour); FreeType draws the rest in one colour, the text's.
/// TMP Settings is an asset, so its lists are swapped for new ones for the session instead of edited; in the editor
/// the authored lists come back when Play stops and the runtime fonts are destroyed, so the asset never saves one.
/// </summary>
public static class FontFallbacks
{
    private static bool installed;
    private static List<TMP_FontAsset> authoredFallbacks;
    private static List<TMP_Asset> authoredEmoji;
    private static readonly List<TMP_FontAsset> added = new List<TMP_FontAsset>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        installed = false; authoredFallbacks = null; authoredEmoji = null; added.Clear();
    }

    /// <summary>
    /// Add the installed families of these ';'-separated lists to TMP's fallbacks, once per session. Families this
    /// machine lacks are skipped quietly: the list names fonts of several systems.
    /// </summary>
    public static void Install(string families, string emojiFamilies, int pointSize)
    {
        if (installed) return;
        installed = true;
        // A server or a test run in batch mode draws no text.
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        HashSet<string> present;
        try { present = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase); }
        catch (Exception) { return; }
        pointSize = Mathf.Clamp(pointSize, 16, 128);

        var chain = new List<TMP_FontAsset>();
        if (TMP_Settings.fallbackFontAssets != null) chain.AddRange(TMP_Settings.fallbackFontAssets);
        var emoji = new List<TMP_Asset>();
        if (TMP_Settings.defaultSpriteAsset != null) emoji.Add(TMP_Settings.defaultSpriteAsset);
        if (TMP_Settings.emojiFallbackTextAssets != null)
            foreach (var asset in TMP_Settings.emojiFallbackTextAssets)
                if (asset != null && !emoji.Contains(asset)) emoji.Add(asset);
        int before = added.Count;

        foreach (var family in Split(families))
        {
            var font = Create(family, present, pointSize);
            if (font != null) chain.Add(font);
        }
        foreach (var family in Split(emojiFamilies))
        {
            var font = Create(family, present, pointSize);
            if (font == null) continue;
            emoji.Add(font);
            // A bold or italic emoji skips the emoji list (TMP asks it with the text's weight, and these fonts have no
            // weight table), then finds the font here on its plain retry.
            chain.Add(font);
        }
        if (added.Count == before) return;

        authoredFallbacks = TMP_Settings.fallbackFontAssets;
        authoredEmoji = TMP_Settings.emojiFallbackTextAssets;
        TMP_Settings.fallbackFontAssets = chain;
        TMP_Settings.emojiFallbackTextAssets = emoji;
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += Restore;
#endif
    }

    private static TMP_FontAsset Create(string family, HashSet<string> present, int pointSize)
    {
        if (!present.Contains(family)) return null;
        foreach (var font in added)
            if (font != null && font.name == family + " (OS)") return null;
        TMP_FontAsset created;
        try { created = TMP_FontAsset.CreateFontAsset(family, "Regular", pointSize); }
        catch (Exception) { return null; }
        if (created == null) return null;
        created.name = family + " (OS)";
        created.hideFlags = HideFlags.DontSave;
        added.Add(created);
        return created;
    }

    private static IEnumerable<string> Split(string list)
    {
        if (string.IsNullOrWhiteSpace(list)) yield break;
        foreach (var part in list.Split(';'))
        {
            string name = part.Trim();
            if (name.Length > 0) yield return name;
        }
    }

#if UNITY_EDITOR
    private static void Restore(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingPlayMode) return;
        EditorApplication.playModeStateChanged -= Restore;
        if (authoredFallbacks != null) TMP_Settings.fallbackFontAssets = authoredFallbacks;
        if (authoredEmoji != null) TMP_Settings.emojiFallbackTextAssets = authoredEmoji;
        foreach (var font in added)
        {
            if (font == null) continue;
            if (font.atlasTextures != null)
                foreach (var texture in font.atlasTextures)
                    if (texture != null) UnityEngine.Object.Destroy(texture);
            if (font.material != null) UnityEngine.Object.Destroy(font.material);
            UnityEngine.Object.Destroy(font);
        }
        added.Clear();
        authoredFallbacks = null; authoredEmoji = null;
    }
#endif
}
