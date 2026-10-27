using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Import rules for project models under Art/Models. On first import (or via Tools → Models → Reapply Import Rules):
/// an identity prefab root (Blender's -90°/×100 stays on the child), axis conversion baked, no cameras or lights, and
/// no rig or animation for Props/Environment. Materials stay embedded as URP Lit; a texture next to the model (or in
/// its Textures folder) named after the material or the model is assigned automatically — "tetopear.png",
/// "TetoPear_BaseColor.png", "TetoPear_Normal.png". Adding such a texture later reimports the model.
/// </summary>
internal sealed class ModelImportRules : AssetPostprocessor
{
    public const string ModelsFolder = "Assets/_Project/Art/Models/";
    private static readonly string[] StaticCategories = { "Props/", "Environment/" };
    private static readonly string[] ImageExtensions = { ".png", ".tga", ".jpg", ".jpeg", ".psd", ".tif", ".tiff", ".exr" };
    private static readonly string[] BaseSuffixes = { "", "_basecolor", "_albedo", "_diffuse", "_color", "_basemap", "_d" };
    private static readonly string[] NormalSuffixes = { "_normal", "_nrm", "_n" };
    private static readonly string[] LinearSuffixes = { "_mask", "_metallic", "_roughness", "_smoothness", "_ao", "_orm", "_height" };

    // After URP's FBXMaterialDescriptionPreprocessor (-980), which has already switched the material to URP Lit.
    public override int GetPostprocessOrder() => 10;

    private static bool IsProjectModel(string path) => path.StartsWith(ModelsFolder, StringComparison.OrdinalIgnoreCase);
    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private void OnPreprocessModel()
    {
        if (IsProjectModel(assetPath) && assetImporter.importSettingsMissing) Apply((ModelImporter)assetImporter);
    }

    private static void Apply(ModelImporter model)
    {
        model.preserveHierarchy = true;
        model.bakeAxisConversion = true;
        model.importCameras = false;
        model.importLights = false;
        model.isReadable = false;
        model.meshCompression = ModelImporterMeshCompression.Off;
        model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        model.materialLocation = ModelImporterMaterialLocation.InPrefab;
        string category = model.assetPath.Substring(ModelsFolder.Length);
        if (StaticCategories.Any(c => category.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
        {
            model.animationType = ModelImporterAnimationType.None;
            model.importAnimation = false;
        }
    }

    private void OnPreprocessTexture()
    {
        if (!IsProjectModel(assetPath) || !assetImporter.importSettingsMissing) return;
        var texture = (TextureImporter)assetImporter;
        string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        texture.maxTextureSize = 2048;
        texture.mipmapEnabled = true;
        if (NormalSuffixes.Any(name.EndsWith)) texture.textureType = TextureImporterType.NormalMap;
        else if (LinearSuffixes.Any(name.EndsWith)) texture.sRGBTexture = false;
    }

    private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (!IsProjectModel(assetPath)) return;
        string[] keys = { description.materialName, Path.GetFileNameWithoutExtension(assetPath) };
        var images = TexturesBeside(assetPath);
        string baseMap = Match(images, keys, BaseSuffixes)
            ?? (images.Length == 1 && !LooksLikeData(images[0]) ? images[0] : null);
        string normalMap = Match(images, keys, NormalSuffixes);

        if (baseMap != null && material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") == null)
        {
            var texture = Load(baseMap);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                // The FBX diffuse colour (Blender's 0.8 grey) would otherwise darken the texture.
                material.SetColor("_BaseColor", Color.white);
            }
        }
        if (normalMap != null && material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") == null)
        {
            var texture = Load(normalMap);
            if (texture != null) { material.SetTexture("_BumpMap", texture); material.EnableKeyword("_NORMALMAP"); }
        }
    }

    private Texture2D Load(string path)
    {
        context.DependsOnSourceAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // A texture added or renamed after its model: reimport models in that asset folder that still lack a base map.
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var folders = imported.Concat(moved).Where(p => IsProjectModel(p) && IsImage(p)).Select(AssetFolder).Distinct();
        foreach (string folder in folders)
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string model = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAllAssetsAtPath(model).OfType<Material>()
                    .Any(m => m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == null))
                    AssetDatabase.ImportAsset(model);
            }
    }

    [MenuItem("Tools/Models/Reapply Import Rules")]
    private static void ReapplySelected()
    {
        var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath)
            .SelectMany(p => AssetDatabase.IsValidFolder(p) ? AssetDatabase.FindAssets("t:Model", new[] { p }).Select(AssetDatabase.GUIDToAssetPath) : new[] { p })
            .Where(IsProjectModel).Distinct().ToArray();
        if (paths.Length == 0)
        {
            EditorUtility.DisplayDialog("Reapply Import Rules", "Art/Models 아래의 모델이나 폴더를 선택하세요.", "OK");
            return;
        }
        foreach (string path in paths)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter model) continue;
            Apply(model);
            model.SaveAndReimport();
        }
    }

    private static string AssetFolder(string path)
    {
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        return Path.GetFileName(folder).Equals("Textures", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(folder).Replace('\\', '/') : folder;
    }

    private static string[] TexturesBeside(string modelPath)
    {
        string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
        return new[] { folder, folder + "/Textures" }.Where(Directory.Exists)
            .SelectMany(f => Directory.GetFiles(f).Select(p => p.Replace('\\', '/'))).Where(IsImage).ToArray();
    }

    private static string Match(string[] images, string[] keys, string[] suffixes)
    {
        foreach (string key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            foreach (string suffix in suffixes)
            {
                string wanted = Normalize(key + suffix);
                string hit = images.FirstOrDefault(p => Normalize(Path.GetFileNameWithoutExtension(p)) == wanted);
                if (hit != null) return hit;
            }
        return null;
    }

    private static bool LooksLikeData(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return NormalSuffixes.Concat(LinearSuffixes).Any(name.EndsWith);
    }

    // "Teto Pear", "teto_pear" and "tetopear" all match.
    private static string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
