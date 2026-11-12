#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps dynamic font glyphs out of the project files. A dynamic TMP font asset draws glyphs into its atlas as text
/// needs them, in the editor too, and Unity would write that atlas (megabytes of texture text) into the .asset on every
/// save, so git saw the fonts change whenever anyone played (KINEMATION's Roboto went in with its glyphs in 0.7.35).
/// A save in edit mode now goes through with the glyphs cleared first, so a setting someone changed (fallbacks, the
/// weight table, multi-atlas) persists while the file holds no glyph data; the open texts draw their glyphs again at
/// once. A save while playing leaves the fonts out: their atlases hold the session's glyphs (FontWarmup filled them)
/// and clearing them would blank the screen; the first save after Play writes them, cleared.
/// Until 2026-11-12 every save left dynamic fonts out, so a settings change was dropped without a word unless the menu
/// below was used.
/// </summary>
public class DynamicFontSaveProcessor : UnityEditor.AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        var kept = new List<string>(paths.Length);
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        foreach (string path in paths)
        {
            var font = DynamicFontAt(path);
            if (font != null)
            {
                if (playing) continue;
                if (HoldsGlyphs(font)) font.ClearFontAssetData(setAtlasSizeToZero: true);
            }
            kept.Add(path);
        }
        return kept.ToArray();
    }

    /// <summary>The dynamic TMP font asset at this path, or null for anything else.</summary>
    internal static TMP_FontAsset DynamicFontAt(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return null;
        var type = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (type == null || !typeof(TMP_FontAsset).IsAssignableFrom(type)) return null;
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        return font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic ? font : null;
    }

    /// <summary>Whether a font asset holds drawn glyphs: table entries, a grown atlas or extra atlas pages.</summary>
    internal static bool HoldsGlyphs(TMP_FontAsset font)
    {
        if (font.glyphTable != null && font.glyphTable.Count > 0) return true;
        if (font.characterTable != null && font.characterTable.Count > 0) return true;
        var textures = font.atlasTextures;
        return textures != null && (textures.Length > 1 || (textures.Length == 1 && textures[0] != null && textures[0].width > 1));
    }

    /// <summary>Empty every dynamic font's atlas now and save the fonts (a save does this by itself; this is for all at once).</summary>
    [MenuItem("Tools/Fonts/동적 글꼴 비우고 저장")]
    public static void CleanAndSaveDynamicFonts()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[Fonts] Play 중에는 동적 글꼴을 비우지 않습니다. Play를 멈춘 뒤 다시 누르세요.");
            return;
        }
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var font = DynamicFontAt(AssetDatabase.GUIDToAssetPath(guid));
            if (font == null || !HoldsGlyphs(font)) continue;
            font.ClearFontAssetData(setAtlasSizeToZero: true);
            EditorUtility.SetDirty(font);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Fonts] 동적 글꼴 {count}개의 아틀라스를 비우고 설정을 저장했습니다.");
    }
}
#endif
