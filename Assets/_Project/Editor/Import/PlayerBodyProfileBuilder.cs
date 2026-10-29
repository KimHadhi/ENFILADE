using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Resources/Characters/PlayerBody in step with the imported art: creates it with the first humanoid model
/// in Art/Models/Characters, and mirrors every clip found under Art/Animations into its clip library. A model the
/// user picked in the profile is never replaced.
/// </summary>
internal sealed class PlayerBodyProfileBuilder : AssetPostprocessor
{
    private const string ProfileFolder = "Assets/_Project/Resources/Characters";
    private const string ProfilePath = ProfileFolder + "/PlayerBody.asset";
    private const string CharactersFolder = ModelImportRules.ModelsFolder + "Characters";
    private static bool queued;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool relevant = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p =>
            p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
            && (p.StartsWith(CharactersFolder + "/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(ModelImportRules.AnimationsFolder, StringComparison.OrdinalIgnoreCase)));
        if (relevant) Queue();
    }

    // Animations imported together with a script change can lose their queued refresh to the domain reload that
    // follows, so check again whenever the editor loads (not on entering Play, where nothing was imported).
    [InitializeOnLoadMethod]
    private static void RefreshOnLoad()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) Queue();
    }

    private static void Queue()
    {
        if (queued) return;
        queued = true;
        EditorApplication.delayCall += Refresh;
    }

    private static void Refresh()
    {
        queued = false;
        var model = Models(CharactersFolder)
            .Where(p => AssetImporter.GetAtPath(p) is ModelImporter { animationType: ModelImporterAnimationType.Human })
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(g => g != null);
        var clips = Models(ModelImportRules.AnimationsFolder.TrimEnd('/')).OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();

        var profile = AssetDatabase.LoadAssetAtPath<PlayerBodyProfile>(ProfilePath);
        if (profile == null)
        {
            if (model == null) return;
            if (!AssetDatabase.IsValidFolder(ProfileFolder)) AssetDatabase.CreateFolder("Assets/_Project/Resources", "Characters");
            profile = ScriptableObject.CreateInstance<PlayerBodyProfile>();
            profile.EditorRefresh(model, clips);
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssets();
            return;
        }
        if (!profile.EditorRefresh(model, clips)) return;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
    }

    private static string[] Models(string folder) => AssetDatabase.IsValidFolder(folder)
        ? AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).ToArray()
        : Array.Empty<string>();
}
