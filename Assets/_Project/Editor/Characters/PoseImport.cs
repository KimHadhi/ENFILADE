using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Import rules for the results show's victory poses (Resources/Poses, read by ResultsMannequin): FBX files of Mayo's
/// armature alone, made in Blender by Tools/Blender/Poses/make_victory_poses.py and touched up in ArtSource/Poses (the
/// user, 2026-11-12: "포즈 네가 블렌더나 그런 거 써서 직접 만들어 ... 세부 조정은 방법 알려 주면 내가 알아서 해 줄
/// 테니까"). Each is a humanoid clip on Mayo's own avatar, so it fits every character by the humanoid rig, and is named
/// after its file (Blender names its one take "Scene"). Mayo's FBX was written by Blender's exporter (Y up, -Z forward,
/// bone axes Y and X, scale "FBX All": metres in the file, the armature at 1), and the poses go out the same way, so her
/// bones' local frames match hers exactly; her import keeps the axis conversion on the root (bakeAxisConversion off),
/// and so do these. No mesh, materials or
/// cameras come in. A pose plays once and holds, standing where it is (the stage places it), so the root stays put.
/// Set on every import, so a file dropped in or sent again from Blender needs nothing done by hand.
/// ModelImportRules calls these: not an AssetPostprocessor of its own, because adding one makes Unity import every model
/// in the project again (postprocessors are part of each model's import dependencies).
/// </summary>
internal static class PoseImport
{
    public const string Folder = "Assets/_Project/Resources/Poses/";
    public const string AvatarModel = "Assets/ThirdParty/BOOTH/Chocolate rice/0.MAYO/FBX/FBX_MAYO.fbx";

    public static bool IsPose(string path)
        => path != null && path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    /// <summary>From ModelImportRules.OnPreprocessModel, for a file under <see cref="Folder"/>.</summary>
    public static void PreprocessModel(ModelImporter model, AssetImportContext context)
    {
        model.importCameras = false;
        model.importLights = false;
        model.importBlendShapes = false;
        model.importVisibility = false;
        model.materialImportMode = ModelImporterMaterialImportMode.None;
        model.bakeAxisConversion = false;
        model.isReadable = false;
        model.animationType = ModelImporterAnimationType.Human;
        model.importAnimation = true;
        // A held pose: no keys dropped by compression, so the fingers sit exactly where Blender put them.
        model.animationCompression = ModelImporterAnimationCompression.Off;
        context.DependsOnArtifact(AvatarModel);
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarModel);
        if (avatar == null || !avatar.isHuman)
        {
            Debug.LogWarning($"[PoseImport] {model.assetPath}: Mayo's humanoid avatar was not found at {AvatarModel}; the pose makes its own.");
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            return;
        }
        model.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        model.sourceAvatar = avatar;
    }

    /// <summary>From ModelImportRules.OnPreprocessAnimation, for a file under <see cref="Folder"/>.</summary>
    public static void PreprocessAnimation(ModelImporter importer)
    {
        var clips = importer.defaultClipAnimations;
        string name = Path.GetFileNameWithoutExtension(importer.assetPath);
        for (int i = 0; i < clips.Length; i++)
        {
            var clip = clips[i];
            clip.name = clips.Length == 1 ? name : name + "_" + i;
            clip.loopTime = false;
            // Height, facing and place all stay in the pose (baked), where the stage stands it.
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }
}
