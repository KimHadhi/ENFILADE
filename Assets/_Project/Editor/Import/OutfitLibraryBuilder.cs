using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the hand-made outfits in Resources/Characters/Outfits (<see cref="OutfitLibrary"/>) in step with their folders,
/// so adding one takes files and a row of Outfits.csv, nothing clicked: Art/Models/Outfits/&lt;model&gt;/&lt;skin id&gt;.fbx
/// (or .prefab) is that model for that character, and each folder under &lt;model&gt;/Colors is a colour set whose
/// materials replace the model's materials of the same names. Entries whose model lives elsewhere (BOOTH packages,
/// BoothImport) are left as they are. Runs whenever something there is imported, moved or deleted, and from
/// Tools/Characters/옷 목록 다시 만들기.
/// </summary>
internal sealed class OutfitLibraryBuilder : AssetPostprocessor
{
    private const string OutfitsFolder = ModelImportRules.ModelsFolder + "Outfits";
    internal const string LibraryPath = "Assets/_Project/Resources/Characters/Outfits.asset";
    private const string ColoursFolder = "Colors";
    private static readonly string[] ModelExtensions = { ".fbx", ".prefab" };

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsOutfitPath)) Rebuild();
    }

    private static bool IsOutfitPath(string path) => path.StartsWith(OutfitsFolder + "/", StringComparison.OrdinalIgnoreCase);

    [MenuItem("Tools/Characters/옷 목록 다시 만들기")]
    private static void Rebuild()
    {
        var entries = new List<OutfitLibrary.Entry>();
        if (AssetDatabase.IsValidFolder(OutfitsFolder))
            foreach (string modelFolder in AssetDatabase.GetSubFolders(OutfitsFolder))
            {
                string id = Path.GetFileName(modelFolder);
                foreach (string path in Directory.GetFiles(modelFolder).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (!ModelExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null) continue;
                    entries.Add(new OutfitLibrary.Entry(id, Path.GetFileNameWithoutExtension(path), model, Colours(modelFolder, model)));
                }
            }
        var library = Library(entries.Count > 0);
        if (library == null) return;
        // An entry whose model is gone is dropped too; BoothImport puts its own back on its next run.
        library.EditorReplace(entry => entry.Model == null || IsOutfitPath(AssetDatabase.GetAssetPath(entry.Model)), entries);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    /// <summary>The library asset, made when <paramref name="create"/> and missing.</summary>
    internal static OutfitLibrary Library(bool create)
    {
        var library = AssetDatabase.LoadAssetAtPath<OutfitLibrary>(LibraryPath);
        if (library != null || !create) return library;
        library = ScriptableObject.CreateInstance<OutfitLibrary>();
        AssetDatabase.CreateAsset(library, LibraryPath);
        return library;
    }

    // The model as imported comes first ("기본"), then one set per folder under Colors.
    private static SkinVariant[] Colours(string modelFolder, GameObject model)
    {
        string folder = modelFolder + "/" + ColoursFolder;
        if (!AssetDatabase.IsValidFolder(folder)) return Array.Empty<SkinVariant>();
        var worn = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        var sets = new List<SkinVariant> { new SkinVariant("base", "기본", Array.Empty<SkinVariant.Swap>()) };
        foreach (string setFolder in AssetDatabase.GetSubFolders(folder))
        {
            var swaps = new List<SkinVariant.Swap>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { setFolder }))
            {
                var to = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var from = to != null ? worn.FirstOrDefault(m => string.Equals(m.name, to.name, StringComparison.OrdinalIgnoreCase)) : null;
                if (from != null) swaps.Add(new SkinVariant.Swap { from = from, to = to });
            }
            string id = Path.GetFileName(setFolder);
            if (swaps.Count > 0) sets.Add(new SkinVariant(id, id, swaps.ToArray()));
            else Debug.LogWarning($"[Outfits] {setFolder}: 모델의 재질과 이름이 같은 재질이 없어 색 세트로 쓰지 않습니다.");
        }
        return sets.Count > 1 ? sets.ToArray() : Array.Empty<SkinVariant>();
    }
}
