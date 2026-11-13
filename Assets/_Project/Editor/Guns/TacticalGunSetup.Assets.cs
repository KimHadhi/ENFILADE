using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEditor;
using UnityEngine;
using CasAnimation = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset;
using Object = UnityEngine.Object;

// TacticalGunSetup: Assets. One part of the class; the rest is in TacticalGunSetup.cs and the files beside it.
internal static partial class TacticalGunSetup
{
    // The rifle's FPS Animation Pack settings with this gun's recoil, magazine and turn on KINEMATION's weapon bone.
    private static FPSWeaponSettings Settings(Spec spec, WeaponData weapon, GameObject source, FPSWeaponSettings template, string path, bool hasFireOut)
    {
        var made = AssetDatabase.LoadAssetAtPath<FPSWeaponSettings>(path);
        bool fresh = made == null;
        if (fresh) made = Object.Instantiate(template);
        else EditorUtility.CopySerialized(template, made);
        // The gun's KINEMATION recoil: the one named, or the one the pack's own settings for the gun give it.
        var packWeaponForRecoil = FindPack(source, "TacticalShooterWeapon");
        var packSettingsForRecoil = packWeaponForRecoil != null
            ? new SerializedObject(packWeaponForRecoil).FindProperty("tacWeaponSettings")?.objectReferenceValue : null;
        var recoil = spec.recoil != null ? AssetDatabase.LoadAssetAtPath<RecoilAnimData>($"{Pack}/Recoil/{spec.recoil}.asset")
            : packSettingsForRecoil != null ? new SerializedObject(packSettingsForRecoil).FindProperty("recoilData")?.objectReferenceValue as RecoilAnimData
            : null;
        if (recoil != null) made.recoilAnimData = recoil;
        made.ammo = weapon.MagazineSize;
        made.fireRate = 60f / weapon.ShotInterval;
        made.fullAuto = weapon.FireMode == WeaponFireMode.Automatic;
        made.hasFireOut = hasFireOut;
        made.ikOffset = Vector3.zero;
        made.aimPointOffset = Vector3.zero;
        made.characterController = null;
        made.fireSounds = new List<AudioClip>();
        made.weaponEventSounds = new List<AudioClip>();
        // The pack's own settings say how its guns hang from KINEMATION's weapon bone.
        var packWeapon = FindPack(source, "TacticalShooterWeapon");
        var packSettings = packWeapon != null ? new SerializedObject(packWeapon).FindProperty("tacWeaponSettings")?.objectReferenceValue : null;
        var turn = packSettings != null ? new SerializedObject(packSettings).FindProperty("weaponRotationOffset") : null;
        if (turn != null) made.rotationOffset = turn.quaternionValue;
        made.name = Path.GetFileNameWithoutExtension(path);
        if (fresh) AssetDatabase.CreateAsset(made, path);
        else EditorUtility.SetDirty(made);
        return made;
    }

    private static CasAnimation PartsAsset(AnimationClip clip, string path)
    {
        if (clip == null) return null;
        var made = AssetDatabase.LoadAssetAtPath<CasAnimation>(path);
        bool fresh = made == null;
        if (fresh) made = ScriptableObject.CreateInstance<CasAnimation>();
        made.clip = clip;
        made.playRate = 1f;
        if (fresh) AssetDatabase.CreateAsset(made, path);
        else EditorUtility.SetDirty(made);
        return made;
    }

    internal static FPSAnimationAsset BodyAsset(FPSAnimationAsset template, string path, float blendIn, float blendOut)
    {
        if (template == null) return null;
        var made = AssetDatabase.LoadAssetAtPath<FPSAnimationAsset>(path);
        bool fresh = made == null;
        if (fresh) made = Object.Instantiate(template);
        else EditorUtility.CopySerialized(template, made);
        made.clip = null;
        made.blendTime.blendInTime = blendIn;
        made.blendTime.blendOutTime = blendOut;
        made.blendTime.rateScale = 1f;
        made.name = Path.GetFileNameWithoutExtension(path);
        if (fresh) AssetDatabase.CreateAsset(made, path);
        var write = new SerializedObject(made);
        write.FindProperty("curves")?.ClearArray();
        write.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(made);
        return made;
    }

    internal static AnimationClip Clip(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
        return null;
    }
}
