using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Resources/Combat/MeleeModels in step with Art/Models/Weapons/&lt;melee weapon&gt;/ (MeleeWeapons): the first model
/// file in a melee weapon's folder, by name, becomes that weapon's model, and the stand-in made in code steps aside
/// (KatanaView). Dropping the katana's FBX in Art/Models/Weapons/Katana is all it takes; removing it brings the stand-in
/// back. Folders of other weapons there (guns) are left alone. Model import settings stay ModelImportRules'.
/// </summary>
internal sealed class MeleeModelBuilder : AssetPostprocessor
{
    private const string WeaponsFolder = ModelImportRules.ModelsFolder + "Weapons";
    private const string OutputPath = "Assets/_Project/Resources/" + MeleeModels.ResourcePath + ".asset";
    private static bool queued;

    // A model already there when this builder first ran has no link yet: make it once the editor has loaded.
    [InitializeOnLoadMethod]
    private static void BuildMissing() => EditorApplication.delayCall += () =>
    {
        var found = Found();
        var asset = AssetDatabase.LoadAssetAtPath<MeleeModels>(OutputPath);
        if (found.Count > 0 && (asset == null || asset.Entries.Count != found.Count)) Refresh();
    };

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var paths = imported.Concat(deleted).Concat(moved).Concat(movedFrom);
        if (queued || !paths.Any(p => p.StartsWith(WeaponsFolder + "/", StringComparison.OrdinalIgnoreCase))) return;
        queued = true;
        EditorApplication.delayCall += Refresh;
    }

    // Each melee weapon whose folder holds a model: its first model file by name.
    private static List<MeleeModels.Entry> Found()
    {
        var found = new List<MeleeModels.Entry>();
        foreach (string weapon in MeleeWeapons.All)
        {
            string folder = WeaponsFolder + "/" + weapon;
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            string path = AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            var model = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (model != null) found.Add(new MeleeModels.Entry { weapon = weapon, model = model });
        }
        return found;
    }

    private static void Refresh()
    {
        queued = false;
        var found = Found();
        var asset = AssetDatabase.LoadAssetAtPath<MeleeModels>(OutputPath);
        if (asset == null)
        {
            // Nothing to link and nothing linked: no asset, so the build carries nothing for it.
            if (found.Count == 0) return;
            asset = ScriptableObject.CreateInstance<MeleeModels>();
            asset.EditorRefresh(found);
            AssetDatabase.CreateAsset(asset, OutputPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeleeModels] {string.Join(", ", found.Select(e => e.weapon))} 모델을 연결했습니다 ({OutputPath}).");
            return;
        }
        if (!asset.EditorRefresh(found)) return;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        Debug.Log($"[MeleeModels] 근접 무기 모델 연결을 갱신했습니다: {(found.Count > 0 ? string.Join(", ", found.Select(e => e.weapon)) : "없음(대역 모델)")}.");
    }
}
