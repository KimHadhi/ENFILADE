using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Resources/Combat/AkimboModels (<see cref="AkimboModels"/>) in step with Art/Models/Weapons: each folder there
/// with a prefab or a model file in it is an entry named after the folder, its first prefab (else its first model file)
/// by name. So another pistol for the dual pistols is its files in Art/Models/Weapons/&lt;name&gt;/ and that name in
/// Akimbo.csv's model column, nothing clicked. Runs whenever something there is imported, moved or deleted, once the editor
/// has loaded if the folder exists without the asset, and from Tools/Guns/쌍권총 모델 목록 다시 만들기.
/// </summary>
internal sealed class AkimboModelsBuilder : AssetPostprocessor
{
    private const string WeaponsFolder = ModelImportRules.ModelsFolder + "Weapons";
    internal const string LibraryPath = "Assets/_Project/Resources/" + AkimboModels.ResourcePath + ".asset";
    private static readonly string[] Prefabs = { ".prefab" };
    private static readonly string[] Models = { ".fbx", ".obj", ".dae", ".blend" };
    private static bool queued;

    [InitializeOnLoadMethod]
    private static void BuildMissing() => EditorApplication.delayCall += () =>
    {
        if (AssetDatabase.IsValidFolder(WeaponsFolder) && AssetDatabase.LoadAssetAtPath<AkimboModels>(LibraryPath) == null) Rebuild();
    };

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (queued || !imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsWeaponPath)) return;
        queued = true;
        EditorApplication.delayCall += Rebuild;
    }

    private static bool IsWeaponPath(string path) => path.StartsWith(WeaponsFolder + "/", StringComparison.OrdinalIgnoreCase);

    [MenuItem("Tools/Guns/쌍권총 모델 목록 다시 만들기")]
    private static void Rebuild()
    {
        queued = false;
        var entries = new List<AkimboModels.Entry>();
        if (AssetDatabase.IsValidFolder(WeaponsFolder))
            foreach (string folder in AssetDatabase.GetSubFolders(WeaponsFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var model = First(folder, Prefabs) ?? First(folder, Models);
                if (model != null) entries.Add(new AkimboModels.Entry { id = Path.GetFileName(folder), model = model });
            }
        var library = AssetDatabase.LoadAssetAtPath<AkimboModels>(LibraryPath);
        if (library == null)
        {
            if (entries.Count == 0) return;
            library = ScriptableObject.CreateInstance<AkimboModels>();
            library.EditorSet(entries.ToArray());
            AssetDatabase.CreateAsset(library, LibraryPath);
            AssetDatabase.SaveAssets();
            return;
        }
        if (!library.EditorSet(entries.ToArray())) return;
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    // The first file of these kinds straight in the folder, by name, as a GameObject.
    private static GameObject First(string folder, string[] extensions)
    {
        foreach (string path in Directory.GetFiles(folder).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null) return model;
        }
        return null;
    }
}
