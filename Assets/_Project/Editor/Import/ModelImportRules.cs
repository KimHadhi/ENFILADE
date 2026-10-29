using System;
using System.Globalization;
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
/// Characters get a humanoid avatar. Files under Art/Animations are humanoid clips; a "&lt;file&gt;.clips.csv" beside one,
/// or "&lt;folder&gt;.clips.csv" for a folder of one-clip files (columns clip, loop, inPlace, inPlaceY, inPlaceYaw,
/// yawOffset, cycleOffset, from, to), names its clips, marks loops and keeps the authored root unless an inPlace
/// column is 1.
/// yawOffset corrects the source clip's forward axis in degrees without discarding its authored turns.
/// A folder may also hold "&lt;folder&gt;.tpose.fbx", the clips' character in its T-pose with its mesh (Mixamo: the character
/// downloaded "T-Pose", with skin). Every other file there then copies that avatar.
/// </summary>
internal sealed class ModelImportRules : AssetPostprocessor
{
    public const string ModelsFolder = "Assets/_Project/Art/Models/";
    public const string AnimationsFolder = "Assets/_Project/Art/Animations/";
    private const string CharactersCategory = "Characters/";
    private static readonly string[] StaticCategories = { "Props/", "Environment/" };
    private static readonly string[] ImageExtensions = { ".png", ".tga", ".jpg", ".jpeg", ".psd", ".tif", ".tiff", ".exr" };
    private static readonly string[] BaseSuffixes = { "", "_basecolor", "_albedo", "_diffuse", "_color", "_basemap", "_d" };
    private static readonly string[] NormalSuffixes = { "_normal", "_nrm", "_n" };
    private static readonly string[] LinearSuffixes = { "_mask", "_metallic", "_roughness", "_smoothness", "_ao", "_orm", "_height" };

    // After URP's FBXMaterialDescriptionPreprocessor (-980), which has already switched the material to URP Lit.
    public override int GetPostprocessOrder() => 10;

    private static bool IsProjectModel(string path) => path.StartsWith(ModelsFolder, StringComparison.OrdinalIgnoreCase);
    private static bool IsAnimationFile(string path) => path.StartsWith(AnimationsFolder, StringComparison.OrdinalIgnoreCase);
    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private void OnPreprocessModel()
    {
        if ((IsProjectModel(assetPath) || IsAnimationFile(assetPath)) && assetImporter.importSettingsMissing)
            Apply((ModelImporter)assetImporter);
        if (IsAnimationFile(assetPath)) ShareTPose((ModelImporter)assetImporter);
    }

    private const string TPoseSuffix = ".tpose.fbx";

    private static string FolderTPose(string path)
    {
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        string tpose = folder + "/" + Path.GetFileName(folder) + TPoseSuffix;
        return File.Exists(tpose) ? tpose : null;
    }

    // A file downloaded without skin has no T-pose, so an avatar made from it takes its first frame as the rest pose,
    // and every clip retargets off by however that frame's knees and feet were bent and turned (Mixamo feet came out
    // rolled half a turn). With the folder's T-pose beside them, all its files share that one true avatar.
    private void ShareTPose(ModelImporter model)
    {
        string tpose = FolderTPose(assetPath);
        if (tpose == null) return;
        if (string.Equals(tpose, assetPath, StringComparison.OrdinalIgnoreCase))
        {
            model.importAnimation = false;
            return;
        }
        context.DependsOnArtifact(tpose);
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(tpose);
        if (avatar == null || !avatar.isHuman) return;
        if (model.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && model.sourceAvatar == avatar) return;
        model.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        model.sourceAvatar = avatar;
    }

    private static void Apply(ModelImporter model)
    {
        model.bakeAxisConversion = true;
        model.importCameras = false;
        model.importLights = false;
        model.isReadable = false;
        model.meshCompression = ModelImporterMeshCompression.Off;
        if (IsAnimationFile(model.assetPath))
        {
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.animationType = ModelImporterAnimationType.Human;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            model.importAnimation = true;
            return;
        }
        model.preserveHierarchy = true;
        model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        model.materialLocation = ModelImporterMaterialLocation.InPrefab;
        string category = model.assetPath.Substring(ModelsFolder.Length);
        if (StaticCategories.Any(c => category.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
        {
            model.animationType = ModelImporterAnimationType.None;
            model.importAnimation = false;
        }
        else if (category.StartsWith(CharactersCategory, StringComparison.OrdinalIgnoreCase))
        {
            // Humanoid, so clips from Art/Animations retarget onto any character.
            model.animationType = ModelImporterAnimationType.Human;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            model.importAnimation = false;
        }
    }

    // Clip names, loops and root handling come from the "<file>.clips.csv" beside an animation file, or from the
    // folder's "<folder>.clips.csv" for a folder of one-clip files (Mixamo), on every import.
    private void OnPreprocessAnimation()
    {
        if (!IsAnimationFile(assetPath)) return;
        string listPath = Path.ChangeExtension(assetPath, null) + ".clips.csv";
        if (!File.Exists(listPath))
        {
            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            listPath = folder + "/" + Path.GetFileName(folder) + ".clips.csv";
            if (!File.Exists(listPath)) return;
        }
        context.DependsOnSourceAsset(listPath);
        var list = CsvTable.Parse(File.ReadAllText(listPath), Path.GetFileName(listPath));
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            int bar = clip.takeName.LastIndexOf('|');
            clip.name = bar >= 0 ? clip.takeName.Substring(bar + 1) : clip.takeName;
            // Mixamo names every take "mixamo.com"; a file of one such take is named after the file instead.
            if (clips.Length == 1 && clip.name == GenericMixamoTake) clip.name = Path.GetFileNameWithoutExtension(assetPath);
            list.TryGetRow(clip.name, out var row);
            clip.loopTime = Flag(row, "loop");
            // Height and facing stay as authored. Travel stays too, so feet keep their place, unless inPlace = 1;
            // inPlaceY = 1 drops the rise and fall as well, for clips the player's own path carries over an obstacle,
            // and inPlaceYaw = 1 drops the turn, for turn-on-the-spot clips whose turn the body makes itself.
            clip.lockRootRotation = !Flag(row, "inPlaceYaw");
            clip.keepOriginalOrientation = true;
            // Source clips can use a different forward axis from their humanoid rest pose.
            // Correct the clip basis while keeping its authored turns and body motion.
            clip.rotationOffset = Number(row, "yawOffset");
            clip.lockRootHeightY = !Flag(row, "inPlaceY");
            clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = !Flag(row, "inPlace");
            clip.keepOriginalPositionXZ = true;
            // Which share of a loop it starts at, so cycles that blend together put down the same foot first.
            clip.cycleOffset = Number(row, "cycleOffset");
            // from/to keep only those frames, counted from the clip's first one (a landing starts at touchdown).
            float first = clip.firstFrame, from = Number(row, "from"), to = Number(row, "to");
            if (from > 0f) clip.firstFrame = Mathf.Min(first + from, clip.lastFrame - 1f);
            if (to > from) clip.lastFrame = Mathf.Min(first + to, clip.lastFrame);
        }
        importer.clipAnimations = clips;
    }

    private const string GenericMixamoTake = "mixamo.com";

    private static bool Flag(System.Collections.Generic.IReadOnlyDictionary<string, string> row, string column)
        => row != null && row.TryGetValue(column, out string value) && value.Trim() == "1";

    private static float Number(System.Collections.Generic.IReadOnlyDictionary<string, string> row, string column)
        => row != null && row.TryGetValue(column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 0f;

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
        // A T-pose just added: the clips beside it were imported with their own avatars, so give them its avatar now.
        foreach (string tpose in imported.Concat(moved).Where(p => IsAnimationFile(p) && p.EndsWith(TPoseSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(tpose);
            if (avatar == null) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Path.GetDirectoryName(tpose).Replace('\\', '/') }))
            {
                string clip = AssetDatabase.GUIDToAssetPath(guid);
                if (clip != tpose && AssetImporter.GetAtPath(clip) is ModelImporter model && model.sourceAvatar != avatar)
                    AssetDatabase.ImportAsset(clip);
            }
        }

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
            .Where(p => IsProjectModel(p) || IsAnimationFile(p)).Distinct().ToArray();
        if (paths.Length == 0)
        {
            EditorUtility.DisplayDialog("Reapply Import Rules", "Art/Models 또는 Art/Animations 아래의 모델이나 폴더를 선택하세요.", "OK");
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
