using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Makes FPS Animation Ultimate's other guns into guns the body holds, the way its MX16A4 and M1911 are (user, 2026-11-13:
/// "KINEMATION에서 무기들 좀 더 임포트하면 안 됨? 다 가져오고 싶은데, 총이 너무 적어 지금").
/// - A WeaponData of its own (Data/Combat/Weapons), copied from the gun of ours most like it so it starts with that gun's
///   sound profile and feel; its numbers are its Weapons.csv row, its shots the pack's own recordings.
/// - Its entry in Resources/Combat/FPSBody: the CAS integration's prefab of it, the rifle's or the pistol's FPSAF assets
///   without their clip-timed curves, and the pack's own pose and reload clips, which GunClipRetarget bakes for each skin.
/// - Then TacticalGunSetup fits the Tactical Shooter Pack's parts on it as on the MX16A4 and M1911 (Data/Combat/FPSPackGuns).
/// Not made: the Kar98k and L96X (a bolt worked after every shot) and the KXG12 (a pump): FPSGun plays only the gun's own
/// clip on a shot, so their hand would never move; the RPG (a rocket, not a bullet); the pack's Mk14 EBR (ours is the
/// Tactical Shooter Pack's). Their files are in the project all the same.
/// It runs by itself after scripts load for a gun not in FPSBody yet, and from Tools → ENFILADE → Combat → Make FPS
/// Animation Pack Guns to make them all again.
/// </summary>
[InitializeOnLoad]
internal static class PackGunSetup
{
    private const string Pack = "Assets/ThirdParty/KINEMATION/FPSAnimationPack";
    private const string CasGuns = "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons";
    private const string WeaponsFolder = "Assets/_Project/Data/Combat/Weapons";
    private const string FPSBodyFolder = "Assets/_Project/Data/Combat/FPSBody";
    private const string AssetsPath = "Assets/_Project/Resources/" + FPSBodyAssets.ResourcePath + ".asset";
    // The pack's two guns already in FPSBody, whose profile and FPSAF assets the others start from.
    private const string RifleWeapon = "MX16A4", PistolWeapon = "M1911";

    /// <summary>
    /// One gun. weapon: our asset name (the Weapons.csv row); like: our gun its WeaponData is copied from; cas: the name in
    /// CAS_FPSPack_&lt;cas&gt;; folder: its Animations folder; sfx: its SFX folder; clips name A_FP_&lt;clip&gt;.anim there.
    /// </summary>
    private sealed class Spec
    {
        public string weapon, like, cas, folder, sfx, pose, reloadTactical, reloadEmpty;
        public bool pistol;
    }

    private static readonly Spec[] Guns =
    {
        new Spec { weapon = "AKX200", like = "AK105", cas = "AK", folder = "AK", sfx = "AK", pose = "AKX_Idle", reloadTactical = "AKX_Reload_Tac", reloadEmpty = "AKX_Reload_Empty" },
        new Spec { weapon = "ASVal", like = "AK105", cas = "ASVal", folder = "ASVal", sfx = "ASVal", pose = "Val_Pose", reloadTactical = "Val_Reload_Tac", reloadEmpty = "Val_Reload_Empty" },
        new Spec { weapon = "G3", like = "Mk14EBR", cas = "G3", folder = "G3", sfx = "G3", pose = "G3_Idle", reloadTactical = "G3_Reload_Tac", reloadEmpty = "G3_Reload_Empty" },
        new Spec { weapon = "SVD", like = "Mk14EBR", cas = "SVD", folder = "SVD", sfx = "SVD", pose = "SVD_Pose", reloadTactical = "SVD_Reload_Tac", reloadEmpty = "SVD_Reload_Empty" },
        new Spec { weapon = "MGX5", like = "MX68", cas = "MGX5", folder = "MGX5", sfx = "MGX5", pose = "MGX5_Idle", reloadTactical = "MGX5_Reload_Tac", reloadEmpty = "MGX5_Reload_Empty" },
        new Spec { weapon = "MPS5", like = "MKR9", cas = "MPS5", folder = "MPS5", sfx = "MPS5", pose = "MPS5_Pose", reloadTactical = "MPS5_Reload_Tac", reloadEmpty = "MPS5_Reload_Empty" },
        new Spec { weapon = "PDW90", like = "MKR9", cas = "PDW90", folder = "PDW90", sfx = "PDW90", pose = "PDW90_Idle", reloadTactical = "PDW90_Reload_Tac", reloadEmpty = "PDW90_Reload_Empty" },
        new Spec { weapon = "StrikerV", like = "MKR9", cas = "Striker-V", folder = "Striker-V", sfx = "Striker-V", pose = "Striker-V_Pose", reloadTactical = "Striker-V_Reload_Tac", reloadEmpty = "Striker-V_Reload_Empty" },
        new Spec { weapon = "Drake12", like = "SRM12", cas = "Drake-12", folder = "Drake-12", sfx = "Drake-12", pose = "Drake-12_Pose", reloadTactical = "Drake-12_Reload_Tac", reloadEmpty = "Drake-12_Reload_Empty" },
        new Spec { weapon = "X18", like = "WK11Viper", cas = "X18", folder = "X18", sfx = "X18", pose = "X18_Pose", reloadTactical = "X18_Reload_Tac", reloadEmpty = "X18_Reload_Empty", pistol = true },
        new Spec { weapon = "DGL50", like = "M1911", cas = "DGL50", folder = "DGL50", sfx = "DGL50", pose = "DGL50_Pose", reloadTactical = "DGL50_Reload_Tac", reloadEmpty = "DGL50_Reload_Empty", pistol = true },
        new Spec { weapon = "Kolibri", like = "M1911", cas = "Kolibri", folder = "Kolibri", sfx = "Colibri", pose = "Kolibri_Pose", reloadTactical = "Kolibri_Reload_Tac", reloadEmpty = "Kolibri_Reload_Empty", pistol = true },
        // The revolver has one reload, cylinder emptied and filled again, for both.
        new Spec { weapon = "Viper357", like = "R08", cas = "Viper-357", folder = "Viper-357", sfx = "Viper-357", pose = "Viper-357_Idle", reloadTactical = "Viper-357_Reload_Empty", reloadEmpty = "Viper-357_Reload_Empty", pistol = true },
    };

    static PackGunSetup() => EditorApplication.delayCall += () => Run(false);

    [MenuItem("Tools/ENFILADE/Combat/Make FPS Animation Pack Guns")]
    private static void RunAll() => Run(true);

    private static void Run(bool all)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        if (assets == null || assets.guns == null) return;
        if (!Template(assets, RifleWeapon, out var rifle) || !Template(assets, PistolWeapon, out var pistol)) return;

        bool changed = false;
        foreach (var spec in Guns)
        {
            try
            {
                if (Make(spec, assets, spec.pistol ? pistol : rifle, all)) changed = true;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"PackGunSetup: {spec.weapon} could not be made: {error.Message}\n{error.StackTrace}");
            }
        }
        if (!changed) return;
        // Saved, FPSBody is imported again and GunClipRetarget bakes the new guns' clips for every skin.
        EditorUtility.SetDirty(assets);
        AssetDatabase.SaveAssets();
        // Their sights, muzzle, grip and light go on as the MX16A4's and M1911's do.
        TacticalGunSetup.FitPackGunsNow();
    }

    // One of the pack's two guns in FPSBody, which the others start from.
    private static bool Template(FPSBodyAssets assets, string name, out FPSBodyAssets.Gun gun)
    {
        gun = Array.Find(assets.guns, entry => entry.weapon != null && entry.weapon.name == name);
        if (gun.weapon != null && gun.profile != null && gun.pose != null && gun.reloadTactical != null) return true;
        Debug.LogWarning($"PackGunSetup: {name}'s gun in Resources/Combat/FPSBody is missing; the FPS Animation Pack's other guns start from it.");
        return false;
    }

    private static bool Make(Spec spec, FPSBodyAssets assets, FPSBodyAssets.Gun template, bool all)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{CasGuns}/CAS_FPSPack_{spec.cas}.prefab");
        string clips = $"{Pack}/Animations/{spec.folder}/Character";
        var pose = Clip($"{clips}/A_FP_{spec.pose}.anim");
        var reloadTactical = Clip($"{clips}/A_FP_{spec.reloadTactical}.anim");
        var reloadEmpty = Clip($"{clips}/A_FP_{spec.reloadEmpty}.anim");
        if (source == null || source.GetComponent<CasFPSWeapon>() == null || pose == null || reloadTactical == null || reloadEmpty == null)
        {
            Debug.LogWarning($"PackGunSetup: CAS_FPSPack_{spec.cas} or its clips in {clips} are not in the project.");
            return false;
        }
        var weapon = Weapon(spec);
        if (weapon == null) return false;
        int index = Array.FindIndex(assets.guns, gun => gun.weapon == weapon);
        // Made already: its prefab is ours (TacticalGunSetup fits the parts and points the entry at its copy).
        if (!all && index >= 0 && assets.guns[index].prefab != null) return false;

        Directory.CreateDirectory(FPSBodyFolder);
        string stem = $"{FPSBodyFolder}/Anim_{spec.weapon}";
        var emptyTemplate = template.reloadEmpty != null ? template.reloadEmpty : template.reloadTactical;
        var entry = new FPSBodyAssets.Gun
        {
            weapon = weapon,
            // The CAS integration's prefab until TacticalGunSetup puts our copy with the parts in its place.
            prefab = index >= 0 && assets.guns[index].prefab != null ? assets.guns[index].prefab : source,
            profile = template.profile,
            sourcePose = pose,
            sourceReloadTactical = reloadTactical,
            sourceReloadEmpty = reloadEmpty,
            pose = BodyAsset(template.pose, $"{stem}_Pose.asset", .15f, .15f),
            reloadTactical = BodyAsset(template.reloadTactical, $"{stem}_ReloadTactical.asset", .15f, .15f),
            reloadEmpty = BodyAsset(emptyTemplate, $"{stem}_ReloadEmpty.asset", .15f, .15f),
        };
        var list = new List<FPSBodyAssets.Gun>(assets.guns);
        if (index >= 0) list[index] = entry;
        else list.Add(entry);
        assets.guns = list.ToArray();
        Debug.Log($"PackGunSetup: {weapon.name} is made from FPS Animation Ultimate's {spec.cas}; GunClipRetarget bakes its clips next.", weapon);
        return true;
    }

    // ---- Assets ----

    // Our WeaponData for the gun: made once, as a copy of the gun of ours most like it, with the pack's own shots.
    private static WeaponData Weapon(Spec spec)
    {
        string path = $"{WeaponsFolder}/{spec.weapon}.asset";
        var made = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (made != null) return made;
        string like = $"{WeaponsFolder}/{spec.like}.asset";
        if (AssetDatabase.LoadAssetAtPath<WeaponData>(like) == null || !AssetDatabase.CopyAsset(like, path))
        {
            Debug.LogWarning($"PackGunSetup: {spec.weapon}'s WeaponData could not be copied from {like}.");
            return null;
        }
        made = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (made == null) return null;
        var write = new SerializedObject(made);
        var shown = write.FindProperty("displayName");
        if (shown != null) shown.stringValue = spec.weapon;
        // The pack's recordings of its shots: every sound of the gun but its reloads and its draw.
        var shots = new List<AudioClip>();
        string sfx = $"{Pack}/SFX/{spec.sfx}";
        if (AssetDatabase.IsValidFolder(sfx))
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { sfx }))
            {
                string file = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Equip", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
                if (clip != null) shots.Add(clip);
            }
        if (shots.Count > 0) Fill(write.FindProperty("fireSounds"), shots);
        // The X18 has its magazine and slide as sounds of their own (the others only whole reloads).
        Fill(write.FindProperty("magOutSounds"), SoundsNamed(sfx, "Mag_Out"));
        Fill(write.FindProperty("magInSounds"), SoundsNamed(sfx, "Mag_In"));
        Fill(write.FindProperty("boltForwardSounds"), SoundsNamed(sfx, "Slide"));
        write.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(made);
        return made;
    }

    // The gun's sounds whose names hold the word; empty when it has none (then the copied gun's stay).
    private static List<AudioClip> SoundsNamed(string folder, string word)
    {
        var found = new List<AudioClip>();
        if (!AssetDatabase.IsValidFolder(folder)) return found;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
        {
            string file = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(file).IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
            if (clip != null) found.Add(clip);
        }
        return found;
    }

    private static void Fill(SerializedProperty array, List<AudioClip> clips)
    {
        if (array == null || !array.isArray || clips.Count == 0) return;
        array.ClearArray();
        for (int i = 0; i < clips.Count; i++)
        {
            array.InsertArrayElementAtIndex(i);
            array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }

    // FPSAF's asset for one of the gun's body clips: the template's blends without its clip and its clip-timed curves (the
    // body plays the clip GunClipRetarget makes for its skin, FPSBody.Copy), as TacticalGunSetup makes the Tactical guns'.
    private static FPSAnimationAsset BodyAsset(FPSAnimationAsset template, string path, float blendIn, float blendOut)
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

    private static AnimationClip Clip(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
        return null;
    }
}
