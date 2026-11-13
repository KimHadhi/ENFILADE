using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes KINEMATION's Tactical Shooter Pack guns into guns the body holds the way it holds the FPS Animation Pack's
/// (FPSBody): each gets a prefab of its own in the shape of the CAS integration's guns, which FPSBody and the bake know.
/// - The pack's gun prefab is copied with its default attachments put on (an iron sight, a grip, a muzzle; a scope that
///   needs a camera of its own is left off for the iron sights), and the pack's own scripts and particle systems taken
///   off: its KShooterCore player isn't ours, and our muzzle, brass and sounds stand in for them.
/// - The gun-part Animator plays through CAS's SimpleAnimationPlayer at rest on the gun's idle clip, and a CasFPSWeapon
///   carries the sight, a copy of the rifle's FPS Animation Pack settings with the gun's own recoil (the pack's Recoil
///   asset is KINEMATION's Procedural Recoil data, as the FPS Animation Pack's is), and the gun-part clips.
/// - FPSAF's animation assets are the rifle's, without its clip-timed curves; the body plays the clips GunClipRetarget
///   makes for each skin from the pack's own (its Operator skeleton, framed from the pack's FPCamera).
/// - The gun is added to Resources/Combat/FPSBody, which sets GunClipRetarget baking.
/// Our numbers (damage, pellets, shells, reload times measured from the pack's clips) are Weapons.csv's.
/// It runs by itself after scripts load for a gun not in FPSBody yet, and from ENFILADE → 총 → Make Tactical
/// Shooter Pack Guns to make them all again.
/// </summary>
[InitializeOnLoad]
internal static partial class TacticalGunSetup
{
    private const string Pack = "Assets/ThirdParty/KINEMATION/TacticalShooterPack";
    private const string Folder = ProjectFolders.Guns + "TacticalGuns";
    private const string WeaponsFolder = ProjectFolders.Guns + "Weapons";
    private const string FPSBodyFolder = ProjectFolders.Guns + "FPSBody";
    private const string AssetsPath = ProjectFolders.DataByName + FPSBodyAssets.ResourcePath + ".asset";
    private const string Operator = Pack + "/Meshes/Character/Operator/SKM_Operator.fbx";
    private const string CameraRig = Pack + "/Prefabs/TacticalShooterCharacter.prefab";
    // The FPS Animation Pack guns whose settings and FPSAF assets the pack's guns start from: the rifle for the long guns,
    // the pistol for the one-handed ones (KINEMATION's isOneHanded).
    private const string RifleWeapon = "MX16A4", PistolWeapon = "M1911";

    /// <summary>
    /// One of the pack's guns. Clip names leave out the pack's prefixes (A_FP_ for the body, A_W_ for the gun). Recoil
    /// names the pack's Recoil asset; empty takes the one the pack's own gun settings name.
    /// </summary>
    private sealed class Spec
    {
        public string weapon, prefab, folder, recoil;
        // The name the pack's clips share (A_FP_<clip>_Draw, A_W_<clip>_Inspect, ...): its draw, holster and inspect are found by it.
        public string clip;
        public bool pistol;
        // A suppressor that is part of the gun's own model (a bone of it), shown or hidden as the muzzle choice.
        public string builtInSuppressor;
        public string pose, reloadTactical, reloadEmpty, reloadShell, reloadEnd;
        // The pose with no grip on the gun, where the pose holds one: the support hand's place on the handguard while the
        // gun wears no grip (FPSBodyAssets.Gun.sourcePoseNoGrip, FPSBody.Contacts).
        public string poseNoGrip;
        // A gun worked by hand after every shot (a bolt, a pump, a lever): the body's clip of that, played after each shot and
        // holding the next one until the hands are back (FPSBodyAssets.Gun.sourceFire, FPSGun.Cycle, WeaponCycle). Empty for
        // every gun whose shot only kicks it.
        public string fire;
        public string partsIdle, partsFire, partsFireOut, partsReloadTactical, partsReloadEmpty, partsReloadShell, partsReloadEnd;
    }

    private static readonly Spec[] Guns =
    {
        // A semi-automatic shotgun loaded a shell at a time (KINEMATION's TacticalShotgun).
        new Spec
        {
            weapon = "Herrington1187", clip = "Herrington_11-87", prefab = "W_Herrington_11-87_Police", folder = "Herrington_11-87", recoil = "Recoil_Herrington_11-87",
            builtInSuppressor = "Suppressor",
            pose = "Herrington_11-87_Idle", reloadTactical = "Herrington_11-87_Reload_Tac_Start",
            reloadEmpty = "Herrington_11-87_Reload_Empty_Start", reloadShell = "Herrington_11-87_Reload_Loop", reloadEnd = "Herrington_11-87_Reload_End",
            partsIdle = "Herrington_11-87_Idle", partsFire = "Herrington_11-87_Fire", partsFireOut = "Herrington_11-87_Fire_Out",
            partsReloadTactical = "Herrington_11-87_Reload_Tac_Start", partsReloadEmpty = "Herrington_11-87_Reload_Empty_Start",
            partsReloadShell = "Herrington_11-87_Reload_Loop", partsReloadEnd = "Herrington_11-87_Reload_End",
        },
        // A semi-automatic shotgun with a box magazine.
        new Spec
        {
            weapon = "SRM12", clip = "SRM-12", prefab = "W_Oryx_SRM-12", folder = "SRM-12", recoil = "Recoil_Oryx_SRM-12",
            pose = "SRM-12_Idle_Pose", reloadTactical = "SRM-12_Reload_Tactical", reloadEmpty = "SRM-12_Reload_Empty",
            partsIdle = "SRM-12_Idle", partsFire = "SRM-12_Fire", partsReloadTactical = "SRM-12_Reload_Tac", partsReloadEmpty = "SRM-12_Reload_Empty",
        },
        // Assault rifles.
        new Spec
        {
            weapon = "AK105", clip = "AK105", prefab = "W_AK105", folder = "AK105",
            pose = "AK105_Idle", reloadTactical = "AK105_Reload_Tac", reloadEmpty = "AK105_Reload_Empty",
            partsIdle = "AK105_Idle", partsFire = "AK105_Fire", partsReloadTactical = "AK105_Reload_Tac", partsReloadEmpty = "AK105_Reload_Empty",
        },
        new Spec
        {
            weapon = "TR15", clip = "TR15", prefab = "W_TR15", folder = "TR15",
            pose = "TR15_Idle_Pose_Grip", poseNoGrip = "TR15_Idle_Pose_Non_Grip", reloadTactical = "TR15_Reload_Tactical", reloadEmpty = "TR15_Reload_Empty",
            partsIdle = "TR15_Idle", partsFire = "TR15_Fire", partsFireOut = "TR15_Fire_Out",
            partsReloadTactical = "TR15_Reload_Tac", partsReloadEmpty = "TR15_Reload_Empty",
        },
        // A marksman rifle; its gun clip "Empty" holds the bolt back after the last round.
        new Spec
        {
            weapon = "Mk14EBR", clip = "Mk14EBR", prefab = "W_Mk14EBR", folder = "Mk14EBR",
            pose = "Mk14EBR_Idle", reloadTactical = "Mk14EBR_Reload_Tactical", reloadEmpty = "Mk14EBR_Reload_Empty",
            partsFire = "Mk14EBR_Fire", partsFireOut = "Mk14EBR_Empty",
            partsReloadTactical = "Mk14EBR_Reload_Tactical", partsReloadEmpty = "Mk14EBR_Reload_Empty",
        },
        // A belt-fed machine gun: KINEMATION's three reloads (TacticalMX68), the first for a part-used belt, the third from empty.
        new Spec
        {
            weapon = "MX68", clip = "MX68", prefab = "W_MX68", folder = "MX68",
            pose = "MX68_Idle", reloadTactical = "MX68_Reload_01", reloadEmpty = "MX68_Reload_03",
            partsIdle = "MX68_Idle", partsFire = "MX68_Fire_Belt", partsReloadTactical = "MX68_Reload_01", partsReloadEmpty = "MX68_Reload_03",
        },
        // A submachine gun.
        new Spec
        {
            weapon = "MKR9", clip = "MKR9", prefab = "W_MKR9", folder = "MKR9",
            pose = "MKR9_Idle", reloadTactical = "MKR9_Reload_Tac", reloadEmpty = "MKR9_Reload_Empty",
            partsIdle = "MKR9_Idle", partsFire = "MKR9_Fire", partsReloadTactical = "MKR9_Reload_Tac", partsReloadEmpty = "MKR9_Reload_Empty",
        },
        // A revolver: KINEMATION's TacticalRevolver loads it round by round (Reload_01 to 07) or all at once with a speed
        // loader (Reload_08); here always the speed loader.
        new Spec
        {
            weapon = "R08", clip = "R08", prefab = "W_R08", folder = "R08", pistol = true,
            pose = "R08_Idle", reloadTactical = "R08_Reload_08", reloadEmpty = "R08_Reload_08",
            partsIdle = "R08_Idle", partsFire = "R08_Fire", partsReloadTactical = "R08_Reload_08", partsReloadEmpty = "R08_Reload_08",
        },
        // A machine pistol.
        new Spec
        {
            weapon = "WK11Viper", clip = "WK-11_Viper", prefab = "W_WK-11_Viper", folder = "WK-11_Viper", pistol = true,
            pose = "WK-11_Viper_Idle_Pose", reloadTactical = "WK-11_Viper_Reload_Tactical", reloadEmpty = "WK-11_Viper_Reload_Empty",
            partsIdle = "WK-11_Viper_Idle", partsFire = "WK-11_Viper_Fire", partsFireOut = "WK-11_Viper_Fire_Empty",
            partsReloadTactical = "WK-11_Viper_Reload_Tac", partsReloadEmpty = "WK-11_Viper_Reload_Empty",
        },
    };

    static TacticalGunSetup() => EditorApplication.delayCall += () => Run(false);

    [MenuItem("ENFILADE/총/Tactical Shooter Pack 총 만들기")]
    private static void RunAll() => Run(true);

    private static void Run(bool all)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        if (assets == null || assets.guns == null || AssetDatabase.LoadAssetAtPath<GameObject>(Operator) == null) return;
        if (!Template(assets, RifleWeapon, out var rifle, out var rifleWeapon)) return;
        bool hasPistol = Template(assets, PistolWeapon, out var pistol, out var pistolWeapon);

        bool changed = false;
        foreach (var spec in Guns)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>($"{WeaponsFolder}/{spec.weapon}.asset");
            if (weapon == null) continue;
            int index = Array.FindIndex(assets.guns, gun => gun.weapon == weapon);
            // Made already, unless it was made before the sights could be switched (GunSights), before draw, holster and
            // inspect, or before its muzzle, grip and underbarrel parts (GunAttachments).
            if (!all && index >= 0 && assets.guns[index].prefab != null && assets.guns[index].prefab.GetComponent<GunSights>() != null
                && assets.guns[index].prefab.GetComponent<GunAttachments>() is GunAttachments parts && !GripsWithoutHand(parts)
                && assets.guns[index].sourceDraw != null) continue;
            bool onePistol = spec.pistol && hasPistol;
            try
            {
                if (Make(spec, weapon, assets, onePistol ? pistol : rifle, onePistol ? pistolWeapon : rifleWeapon)) changed = true;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"TacticalGunSetup: {spec.weapon} could not be made: {error.Message}\n{error.StackTrace}");
            }
        }
        // The FPS Animation Pack's two guns wear the Tactical Shooter Pack's parts too.
        if (FitPackGuns(assets, all)) changed = true;
        if (!changed) return;
        // Saved, FPSBody is imported again and GunClipRetarget bakes the new guns' clips for every skin.
        EditorUtility.SetDirty(assets);
        AssetDatabase.SaveAssets();
    }

    // Grips made before the support hand went on them (7b, 2026-11-09): made again once.
    private static bool GripsWithoutHand(GunAttachments parts)
    {
        foreach (var grip in parts.grips)
            if (grip.kind != GunAttachments.Kind.None && !grip.hasHand) return true;
        return false;
    }

    // An FPS Animation Pack gun in Resources/Combat/FPSBody that the pack's guns start from.
    private static bool Template(FPSBodyAssets assets, string name, out FPSBodyAssets.Gun gun, out CasFPSWeapon weapon)
    {
        gun = Array.Find(assets.guns, entry => entry.weapon != null && entry.weapon.name == name);
        weapon = gun.prefab != null ? gun.prefab.GetComponent<CasFPSWeapon>() : null;
        if (weapon != null && weapon.weaponSettings != null && gun.profile != null) return true;
        Debug.LogWarning($"TacticalGunSetup: {name}'s gun in Resources/Combat/FPSBody is missing; the Tactical Shooter Pack's guns start from it.");
        return false;
    }

    // rifle: the FPS Animation Pack gun this one starts from (MX16A4, or M1911 for a one-handed gun).
    private static bool Make(Spec spec, WeaponData weapon, FPSBodyAssets assets, FPSBodyAssets.Gun rifle, CasFPSWeapon rifleWeapon)
    {
        string template = rifle.weapon.name;
        string clips = $"{Pack}/Animations/{spec.folder}";
        AnimationClip Body(string name) => name != null ? Clip($"{clips}/Character/A_FP_{name}.FBX") : null;
        AnimationClip Parts(string name) => name != null ? Clip($"{clips}/Weapon/A_W_{name}.FBX") : null;

        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/Prefabs/Weapons/{spec.prefab}.prefab");
        var pose = Body(spec.pose);
        if (source == null || pose == null)
        {
            Debug.LogWarning($"TacticalGunSetup: {spec.prefab} or its clips are not in the Tactical Shooter Pack.");
            return false;
        }
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        string stem = $"{Folder}/{spec.weapon}";

        var settings = Settings(spec, weapon, source, rifleWeapon.weaponSettings, $"{stem}_Settings.asset", Parts(spec.partsFireOut) != null);
        var partsFire = PartsAsset(Parts(spec.partsFire), $"{stem}_Parts_Fire.asset");
        var partsFireOut = PartsAsset(Parts(spec.partsFireOut), $"{stem}_Parts_FireOut.asset");
        var partsTactical = PartsAsset(Parts(spec.partsReloadTactical), $"{stem}_Parts_ReloadTactical.asset");
        var partsEmpty = PartsAsset(Parts(spec.partsReloadEmpty), $"{stem}_Parts_ReloadEmpty.asset");
        var partsShell = PartsAsset(Parts(spec.partsReloadShell), $"{stem}_Parts_ReloadShell.asset");
        var partsEnd = PartsAsset(Parts(spec.partsReloadEnd), $"{stem}_Parts_ReloadEnd.asset");

        var prefab = MakePrefab(source, $"{stem}.prefab", settings, Parts(spec.partsIdle), rifleWeapon,
            partsFire, partsFireOut, partsTactical, partsEmpty, spec.builtInSuppressor);
        if (prefab == null) return false;
        SuppressedSounds(source, weapon);

        // The rifle's FPSAF assets carry curves timed for its own clips; these keep its blends only.
        var poseTemplate = rifle.pose != null ? rifle.pose : AssetDatabase.LoadAssetAtPath<FPSAnimationAsset>($"{FPSBodyFolder}/Anim_{template}_Pose.asset");
        var reloadTemplate = rifle.reloadTactical != null ? rifle.reloadTactical
            : AssetDatabase.LoadAssetAtPath<FPSAnimationAsset>($"{FPSBodyFolder}/Anim_{template}_ReloadTactical.asset");
        bool shells = spec.reloadShell != null;
        var entry = new FPSBodyAssets.Gun
        {
            weapon = weapon,
            prefab = prefab,
            profile = rifle.profile,
            sourcePose = pose,
            sourcePoseNoGrip = Body(spec.poseNoGrip),
            sourceReloadTactical = Body(spec.reloadTactical),
            sourceReloadEmpty = Body(spec.reloadEmpty),
            pose = BodyAsset(poseTemplate, $"{stem}_Anim_Pose.asset", .15f, .15f),
            // A shell reload's start and shells hand straight over to the next step; FPSBody eases its end into the hold.
            reloadTactical = BodyAsset(reloadTemplate, $"{stem}_Anim_ReloadTactical.asset", .15f, shells ? .1f : .15f),
            reloadEmpty = BodyAsset(reloadTemplate, $"{stem}_Anim_ReloadEmpty.asset", .15f, shells ? .1f : .15f),
            sourceArms = AssetDatabase.LoadAssetAtPath<GameObject>(Operator),
            sourceCameraRig = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRig),
            sourceReloadShell = Body(spec.reloadShell),
            sourceReloadEnd = Body(spec.reloadEnd),
            reloadShell = shells ? BodyAsset(reloadTemplate, $"{stem}_Anim_ReloadShell.asset", .1f, .1f) : null,
            reloadEnd = spec.reloadEnd != null ? BodyAsset(reloadTemplate, $"{stem}_Anim_ReloadEnd.asset", .1f, .15f) : null,
            partsReloadShell = partsShell,
            partsReloadEnd = partsEnd,
            // Drawn and put away quickly (fitted to the gun's Draw and Holster Duration by FPSBody), looked over at leisure.
            sourceDraw = Body($"{spec.clip}_Draw"),
            sourceHolster = Body($"{spec.clip}_Holster"),
            sourceInspect = Body($"{spec.clip}_Inspect"),
            draw = BodyAsset(reloadTemplate, $"{stem}_Anim_Draw.asset", .05f, .2f),
            holster = BodyAsset(reloadTemplate, $"{stem}_Anim_Holster.asset", .1f, .05f),
            inspect = BodyAsset(reloadTemplate, $"{stem}_Anim_Inspect.asset", .2f, .25f),
            partsDraw = PartsAsset(Parts($"{spec.clip}_Draw"), $"{stem}_Parts_Draw.asset"),
            partsHolster = PartsAsset(Parts($"{spec.clip}_Holster"), $"{stem}_Parts_Holster.asset"),
            partsInspect = PartsAsset(Parts($"{spec.clip}_Inspect"), $"{stem}_Parts_Inspect.asset"),
            // In at once with the shot, eased into the hold over its last 0.2 s (as PackGunSetup's bolt and pump guns).
            sourceFire = Body(spec.fire),
            fire = spec.fire != null ? BodyAsset(reloadTemplate, $"{stem}_Anim_Fire.asset", .1f, .2f) : null,
        };
        var list = new List<FPSBodyAssets.Gun>(assets.guns);
        int index = list.FindIndex(gun => gun.weapon == weapon);
        if (index >= 0) list[index] = entry;
        else list.Add(entry);
        assets.guns = list.ToArray();
        Debug.Log($"TacticalGunSetup: {weapon.name} is made from the Tactical Shooter Pack's {spec.prefab}; GunClipRetarget bakes its clips next.", prefab);
        return true;
    }

    private static readonly PackGun[] PackGuns =
    {
        // KINEMATION's MX16A4 has its carry handle (with the rear sight) and front sight tower in its body mesh, and its
        // iron aim point is the eye's place, 21 cm behind the gun's middle. Optics were mounted 3 cm ahead of that, over
        // the stock behind the carry handle, looking through the rear sight and the front post (user, 2026-11-10:
        // "조준경을 달아도 가늠쇠가 여전히 있어서 조준경이 뒤에 있는 이상한 상황"). Its receiver top is 3.4 cm under the iron sight
        // line and the lowest of the carry handle's mounts 3.1 cm, hence 3.25 cm.
        new PackGun
        {
            weapon = "MX16A4", source = CasGuns + "/CAS_FPSPack_MX16A4.prefab",
            sights = new[] { "A_Holograph", "A_DMCO" }, muzzles = new[] { "A_MCS_Suppressor" },
            grips = new[] { "A_Zetco_MountedGrip", "A_AngledForegrip" }, underbarrel = new[] { "A_LS3_Flashlight" },
            ironsBelowSightLine = .0325f, opticsMeetIrons = true,
        },
        new PackGun
        {
            weapon = "M1911", source = CasGuns + "/CAS_FPSPack_M1911.prefab", pistol = true,
            sights = new[] { "A_MicroRedDot_Picatinny" }, muzzles = new[] { "A_MCS_Suppressor" },
            grips = Array.Empty<string>(), underbarrel = new[] { "A_LS3_Flashlight" },
        },
        // FPS Animation Ultimate's other guns (2026-11-13, user: "KINEMATION에서 무기들 좀 더 임포트하면 안 됨? 다 가져오고
        // 싶은데"), put in FPSBody by PackGunSetup. Each starts as KINEMATION made it; what fits it is a choice in the
        // customize menu. No suppressor where the gun has its own (AS Val, PDW-90) or a muzzle device that would sit under
        // one, no grip or light where nothing hangs under the barrel.
        Ultimate("AKX200", "AK"), Ultimate("ASVal", "ASVal", muzzle: false), Ultimate("G3", "G3"),
        Ultimate("SVD", "SVD", sights: new[] { "A_DMCO" }, grip: false, light: false),
        Ultimate("MGX5", "MGX5", sights: new[] { "A_Holograph" }, muzzle: false, grip: false, light: false),
        Ultimate("MPS5", "MPS5"), Ultimate("PDW90", "PDW90", muzzle: false, grip: false, light: false),
        Ultimate("StrikerV", "Striker-V"), Ultimate("Drake12", "Drake-12", sights: new[] { "A_Holograph" }, muzzle: false, grip: false),
        Ultimate("X18", "X18", pistol: true), Ultimate("DGL50", "DGL50", pistol: true, muzzle: false),
        Ultimate("Kolibri", "Kolibri", pistol: true, sights: Array.Empty<string>(), muzzle: false, light: false),
        Ultimate("Viper357", "Viper-357", pistol: true, sights: Array.Empty<string>(), muzzle: false, light: false),
        // Worked by hand after every shot (PackGunSetup, FPSGun.Cycle). The bolt rifles may take the DMCO and a suppressor for
        // the ghost's quiet shot, nothing under the barrel. The KXG12's pump is the support hand's, so nothing hangs under it.
        Ultimate("Kar98k", "Kar98k", sights: new[] { "A_DMCO" }, grip: false, light: false),
        Ultimate("L96X", "L96X", sights: new[] { "A_DMCO" }, grip: false, light: false),
        Ultimate("KXG12", "KXG12", sights: new[] { "A_Holograph" }, muzzle: false, grip: false, light: false),
    };
}
