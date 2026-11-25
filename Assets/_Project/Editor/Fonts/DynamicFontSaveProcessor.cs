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
/// The font feature table (ligatures, kerning pairs, mark positions) is cleared with the glyphs: TMP fills it for each
/// glyph it draws, and ClearFontAssetData leaves it, so on 2026-11-20 four Pretendard weights went in at 9.6 MB each
/// (21,000 kerning pairs, 340,000 lines) and Unity was slow to import them. TMP reads the features again as it draws.
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
                if (HoldsGlyphs(font)) Clear(font);
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

    private static readonly string[] FeatureLists =
    {
        "m_FontFeatureTable.m_MultipleSubstitutionRecords", "m_FontFeatureTable.m_LigatureSubstitutionRecords",
        "m_FontFeatureTable.m_GlyphPairAdjustmentRecords", "m_FontFeatureTable.m_MarkToBaseAdjustmentRecords",
        "m_FontFeatureTable.m_MarkToMarkAdjustmentRecords",
    };

    /// <summary>Empties the glyphs, the atlas and the font feature table.</summary>
    internal static void Clear(TMP_FontAsset font)
    {
        font.ClearFontAssetData(setAtlasSizeToZero: true);
        var data = new SerializedObject(font);
        bool cleared = false;
        foreach (string name in FeatureLists)
        {
            var list = data.FindProperty(name);
            if (list == null || !list.isArray || list.arraySize == 0) continue;
            list.ClearArray();
            cleared = true;
        }
        if (!cleared) return;
        data.ApplyModifiedPropertiesWithoutUndo();
        font.ReadFontAssetDefinition();
    }

    /// <summary>Whether any of the font feature lists holds records.</summary>
    private static bool HoldsFeatures(TMP_FontAsset font)
    {
        var data = new SerializedObject(font);
        foreach (string name in FeatureLists)
        {
            var list = data.FindProperty(name);
            if (list != null && list.isArray && list.arraySize > 0) return true;
        }
        return false;
    }

    /// <summary>Whether a font asset holds drawn glyphs or what came with them: table entries, a grown atlas, extra atlas
    /// pages or font feature records.</summary>
    internal static bool HoldsGlyphs(TMP_FontAsset font)
    {
        if (font.glyphTable != null && font.glyphTable.Count > 0) return true;
        if (font.characterTable != null && font.characterTable.Count > 0) return true;
        var textures = font.atlasTextures;
        if (textures != null && (textures.Length > 1 || (textures.Length == 1 && textures[0] != null && textures[0].width > 1))) return true;
        return HoldsFeatures(font);
    }

    /// <summary>Empty every dynamic font's atlas now and save the fonts (a save does this by itself; this is for all at once).</summary>
    [MenuItem("ENFILADE/글꼴/동적 글꼴 비우고 저장")]
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
            Clear(font);
            EditorUtility.SetDirty(font);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Fonts] 동적 글꼴 {count}개의 아틀라스를 비우고 설정을 저장했습니다.");
    }
}
#endif
