using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEditor;
using UnityEngine;
using CasAnimation = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset;
using PartsPlayer = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.SimpleAnimationPlayer;
using Object = UnityEngine.Object;

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
internal static class TacticalGunSetup
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

    // ---- The gun ----

    // The pack's suppressed shots for the gun (its settings' suppressedFireSounds), into our WeaponData, unless it has its own.
    private static void SuppressedSounds(GameObject source, WeaponData weapon)
    {
        var packWeapon = FindPack(source, "TacticalShooterWeapon");
        var packSettings = packWeapon != null ? new SerializedObject(packWeapon).FindProperty("tacWeaponSettings")?.objectReferenceValue : null;
        var sounds = packSettings != null ? new SerializedObject(packSettings).FindProperty("suppressedFireSounds") : null;
        var write = new SerializedObject(weapon);
        var mine = write.FindProperty("suppressedFireSounds");
        if (sounds == null || mine == null || mine.arraySize > 0) return;
        for (int i = 0; i < sounds.arraySize; i++)
        {
            if (!(sounds.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip clip)) continue;
            mine.InsertArrayElementAtIndex(mine.arraySize);
            mine.GetArrayElementAtIndex(mine.arraySize - 1).objectReferenceValue = clip;
        }
        write.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(weapon);
    }

    private static GameObject MakePrefab(GameObject source, string path, FPSWeaponSettings settings, AnimationClip idle, CasFPSWeapon rifle,
        CasAnimation fire, CasAnimation fireOut, CasAnimation reloadTactical, CasAnimation reloadEmpty, string builtInSuppressor)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = Path.GetFileNameWithoutExtension(path);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // The gun's own Animator, found before any attachment (which may bring one) goes on.
            var animator = instance.GetComponentInChildren<Animator>(true);

            // What the pack's own gun script knew: its sight, and the bones it keeps hidden.
            Transform aimPoint = null;
            var weapon = FindPack(instance, "TacticalShooterWeapon");
            if (weapon != null)
            {
                var read = new SerializedObject(weapon);
                aimPoint = read.FindProperty("aimPoint")?.objectReferenceValue as Transform;
                var hidden = read.FindProperty("bonesToHide");
                for (int i = 0; hidden != null && i < hidden.arraySize; i++)
                {
                    var bone = hidden.GetArrayElementAtIndex(i);
                    if (bone.FindPropertyRelative("isHidden").boolValue && bone.FindPropertyRelative("bone").objectReferenceValue is Transform shut)
                        shut.localScale = Vector3.zero;
                }
            }
            var sightAim = PutOnAttachments(instance, aimPoint, builtInSuppressor);
            if (sightAim != null) aimPoint = sightAim;

            // The pack's scripts expect its own player; its particle systems are its muzzle flash and brass.
            for (int pass = 0; pass < 4; pass++)
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null && IsPack(script)) Object.DestroyImmediate(script);
            foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particles == null) continue;
                var main = particles.main;
                main.playOnAwake = false;
                // Off altogether, unless it shares its object with a part of the gun itself.
                bool part = particles.gameObject == instance || particles.GetComponentsInChildren<MeshRenderer>(true).Length > 0
                    || particles.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
                if (!part) particles.gameObject.SetActive(false);
            }

            // The gun-part clips through CAS's player, the gun at rest on its idle clip.
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                var player = animator.gameObject.GetComponent<PartsPlayer>();
                if (player == null) player = animator.gameObject.AddComponent<PartsPlayer>();
                // A graph of its own, as KINEMATION's CAS guns have it: the Animator has no controller to build on.
                var write = new SerializedObject(player);
                var rest = write.FindProperty("defaultClipPose");
                if (rest != null) rest.objectReferenceValue = idle;
                var own = write.FindProperty("useAnimator");
                if (own != null) own.boolValue = false;
                write.ApplyModifiedPropertiesWithoutUndo();
            }

            var cas = instance.GetComponent<CasFPSWeapon>();
            if (cas == null) cas = instance.AddComponent<CasFPSWeapon>();
            cas.weaponSettings = settings;
            cas.aimPoint = aimPoint != null ? aimPoint : instance.transform;
            cas.fire = new FPSAnimationPair { weapon = fire };
            cas.fireOut = new FPSAnimationPair { weapon = fireOut };
            cas.reloadTactical = new FPSAnimationPair { weapon = reloadTactical };
            cas.reloadEmpty = new FPSAnimationPair { weapon = reloadEmpty };
            // The rifle's camera shake (a CAS asset, which FPSGun copies into FPSAF's); semi-automatic.
            var casWrite = new SerializedObject(cas);
            var shake = casWrite.FindProperty("cameraShake");
            var rifleShake = new SerializedObject(rifle).FindProperty("cameraShake");
            if (shake != null && rifleShake != null) shake.objectReferenceValue = rifleShake.objectReferenceValue;
            var mode = casWrite.FindProperty("fireMode");
            if (mode != null) mode.enumValueIndex = 0;
            casWrite.ApplyModifiedPropertiesWithoutUndo();

            var made = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool saved);
            return saved ? made : null;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // The pack's attachments, as its TacticalWeaponAttachments puts them on: every grip, muzzle and underbarrel choice on its
    // mount for GunAttachments to switch between (the customize menu, C), nothing on the muzzle or grip to start with, and every sight on the
    // sight mount for GunSights to switch between (the same menu), the pack's own pick shown. A magnified scope gets a GunScope with the pack's numbers, which renders its lens as the pack's
    // ScopeAttachment did. The gun's own iron sights are a choice too when it has them. Returns the shown sight's aim point.
    private static Transform PutOnAttachments(GameObject gun, Transform gunAim, string builtInSuppressor)
    {
        var list = new List<GunSights.Sight>();
        var irons = new List<GameObject>();
        int initial = 0;
        // The muzzle, grip and underbarrel choices (GunAttachments), each starting with nothing there.
        var none = new GunAttachments.Choice { kind = GunAttachments.Kind.None };
        var muzzles = new List<GunAttachments.Choice> { none };
        var grips = new List<GunAttachments.Choice> { none };
        var under = new List<GunAttachments.Choice> { none };
        var attachments = FindPack(gun, "TacticalWeaponAttachments");
        if (attachments != null)
        {
            var read = new SerializedObject(attachments);
            var ironList = read.FindProperty("ironSightObjects");
            for (int i = 0; ironList != null && i < ironList.arraySize; i++)
                if (ironList.GetArrayElementAtIndex(i).objectReferenceValue is GameObject iron) irons.Add(iron);
            foreach (string group in new[] { "scopes", "grips", "muzzle", "underbarrel" })
            {
                var mount = read.FindProperty($"{group}.attachmentPoint")?.objectReferenceValue as Transform;
                var choices = read.FindProperty($"{group}.attachments");
                if (mount == null || choices == null || choices.arraySize == 0) continue;
                int pick = Mathf.Clamp(read.FindProperty($"{group}.initialIndex")?.intValue ?? 0, 0, choices.arraySize - 1);
                if (group != "scopes")
                {
                    // Every choice on the mount for GunAttachments to switch between; the pack's empty entries are its "none".
                    var into = group == "grips" ? grips : group == "muzzle" ? muzzles : under;
                    for (int i = 0; i < choices.arraySize; i++)
                        if (choices.GetArrayElementAtIndex(i).objectReferenceValue is Component other)
                            into.Add(ChoiceOf(other, Put(other, mount), gun.transform));
                    continue;
                }
                // A sight's choice is a selection: its prefab (and secondary scopes, left off here).
                for (int i = 0; i < choices.arraySize; i++)
                {
                    if (!(choices.GetArrayElementAtIndex(i).FindPropertyRelative("prefab")?.objectReferenceValue is Component prefab)) continue;
                    var put = Put(prefab, mount);
                    var entry = SightChoice(prefab, put, gunAim);
                    if (i == pick) initial = list.Count;
                    list.Add(entry);
                }
            }
        }
        // A suppressor that is part of the gun's own model is a muzzle choice too, shown by its size.
        if (!string.IsNullOrEmpty(builtInSuppressor) && Hierarchy.Find(gun.transform, builtInSuppressor) is Transform built)
            muzzles.Add(new GunAttachments.Choice { kind = GunAttachments.Kind.Suppressor, bone = built });
        // Every gun gets one, empty or not, so a gun made before them is made again once (Run).
        var parts = gun.AddComponent<GunAttachments>();
        parts.muzzle = muzzles.Count > 1 ? muzzles.ToArray() : Array.Empty<GunAttachments.Choice>();
        parts.grips = grips.Count > 1 ? grips.ToArray() : Array.Empty<GunAttachments.Choice>();
        parts.underbarrel = under.Count > 1 ? under.ToArray() : Array.Empty<GunAttachments.Choice>();
        // Nothing on the muzzle or under the grip to start with, as a Siege gun comes; a flashlight stays on its rail, off.
        parts.initialUnderbarrel = Mathf.Max(0, under.FindIndex(choice => choice.kind == GunAttachments.Kind.Flashlight));
        parts.Begin();

        // The gun's own iron sights, a choice of their own when it has any, and the only one with no sights to pick from.
        if (gunAim != null && (irons.Count > 0 || list.Count == 0))
            list.Add(new GunSights.Sight { part = null, aimPoint = gunAim, ironSight = true });
        if (list.Count == 0) return null;
        var sights = gun.AddComponent<GunSights>();
        sights.sights = list.ToArray();
        sights.ironSights = irons.ToArray();
        sights.initial = Mathf.Clamp(initial, 0, list.Count - 1);
        sights.Select(sights.initial);
        return sights.AimPoint;
    }

    private static GameObject Put(Component prefab, Transform mount)
    {
        var put = Object.Instantiate(prefab.gameObject, mount, false);
        put.name = prefab.gameObject.name;
        put.transform.localPosition = Vector3.zero;
        put.transform.localRotation = Quaternion.identity;
        put.SetActive(true);
        return put;
    }

    // What the pack's attachment script on the part said it is, read before the pack's scripts come off.
    private static GunAttachments.Choice ChoiceOf(Component prefab, GameObject put, Transform gunRoot)
    {
        var choice = new GunAttachments.Choice { part = put };
        var script = put.GetComponent(prefab.GetType());
        var read = script != null ? new SerializedObject(script) : null;
        string type = prefab.GetType().Name;
        if (type.Contains("Muzzle"))
            choice.kind = read?.FindProperty("isSuppressed")?.boolValue == true ? GunAttachments.Kind.Suppressor : GunAttachments.Kind.None;
        else if (type.Contains("Grip"))
        {
            choice.kind = put.name.IndexOf("Angled", StringComparison.OrdinalIgnoreCase) >= 0
                ? GunAttachments.Kind.AngledGrip : GunAttachments.Kind.VerticalGrip;
            // The pack's LeftHand is where its Operator's left wrist goes, turned as that hand bone; kept as where the
            // palm's middle goes and the ways the fingers point and the palm faces, which any hand can take (ArmIK).
            if (read?.FindProperty("attachmentData.ikTarget")?.objectReferenceValue is Transform wrist
                && OperatorLeftHand(out Vector3 fingers, out Vector3 facing, out Vector3 palm))
            {
                choice.hasHand = true;
                choice.handPalm = gunRoot.InverseTransformPoint(wrist.position + wrist.rotation * palm);
                choice.handFingers = gunRoot.InverseTransformDirection(wrist.rotation * fingers);
                choice.handFacing = gunRoot.InverseTransformDirection(wrist.rotation * facing);
            }
        }
        else if (type.Contains("Flashlight"))
        {
            choice.kind = GunAttachments.Kind.Flashlight;
            choice.light = read?.FindProperty("flashlight")?.objectReferenceValue as Light;
            choice.lens = read?.FindProperty("emissiveRenderer")?.objectReferenceValue as Renderer;
            choice.lensGlow = read?.FindProperty("emissionStrength")?.floatValue ?? 1f;
        }
        return choice;
    }

    // A sight put on its mount, as GunSights keeps it: where the eye lines up through it, whether it is an iron sight, and a
    // magnified scope's lens (GunScope, with the pack's numbers), read before the pack's scripts come off.
    private static GunSights.Sight SightChoice(Component prefab, GameObject put, Transform gunAim)
    {
        var sight = put.GetComponent(prefab.GetType());
        var read = sight != null ? new SerializedObject(sight) : null;
        var aim = read?.FindProperty("sightData.aimPoint")?.objectReferenceValue as Transform;
        var entry = new GunSights.Sight
        {
            part = put,
            aimPoint = aim != null ? aim : gunAim,
            ironSight = read?.FindProperty("isIronSight")?.boolValue ?? false,
        };
        if (read != null && prefab.GetType().Name.Contains("Scope"))
        {
            var scope = put.AddComponent<GunScope>();
            scope.lens = read.FindProperty("lens")?.objectReferenceValue as Renderer;
            scope.capturePoint = read.FindProperty("capturePoint")?.objectReferenceValue as Transform;
            scope.magnification = read.FindProperty("magnification")?.floatValue ?? 1f;
            scope.textureResolution = read.FindProperty("textureResolution")?.intValue ?? 1024;
            scope.captureFov = read.FindProperty("captureFov")?.floatValue ?? 90f;
            entry.scope = scope;
        }
        return entry;
    }

    // ---- The FPS Animation Pack's guns (MX16A4, M1911) ----

    // KINEMATION's FPS Animation Pack has no parts of its own for its two guns, so they wear the Tactical Shooter Pack's
    // (user, 2026-11-09: "담 단계"), put on mounts measured off each gun's own mesh: the muzzle's front face, the top of the
    // rail over the receiver, the underside of the handguard where KINEMATION's support hand holds it, and ahead of that. A
    // copy of the gun's CAS prefab carries them (Data/Guns/FPSPackGuns), and Resources/Combat/FPSBody points at it.
    private const string PackGunsFolder = ProjectFolders.Guns + "FPSPackGuns";
    private const string CasGuns = "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons";

    private sealed class PackGun
    {
        public string weapon, source;
        public bool pistol;
        public string[] sights, muzzles, grips, underbarrel;
        // Iron sights modelled into the gun's body mesh: its pieces reaching higher than this (m) under the iron sight line
        // are them, cut out at run time so they fold away under an optic (GunSights.ironsInMesh). 0 leaves them on.
        public float ironsBelowSightLine;
        // The optics' mount goes as far ahead of the iron sight's aim point as the optics' own aim points sit behind
        // their mounts, so an optic's eye point meets the iron sight's and the optic sits on the receiver.
        public bool opticsMeetIrons;
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

    // One of FPS Animation Ultimate's guns in the Tactical Shooter Pack's parts: a long gun the MX16A4's sights, a pistol the
    // M1911's red dot (unless given others), the MCS suppressor, the angled foregrip on a long gun, the LS3 light.
    private static PackGun Ultimate(string weapon, string cas, bool pistol = false, string[] sights = null, bool muzzle = true,
        bool grip = true, bool light = true) => new PackGun
    {
        weapon = weapon, source = CasGuns + "/CAS_FPSPack_" + cas + ".prefab", pistol = pistol,
        sights = sights ?? (pistol ? new[] { "A_MicroRedDot_Picatinny" } : new[] { "A_Holograph", "A_DMCO" }),
        muzzles = muzzle ? new[] { "A_MCS_Suppressor" } : Array.Empty<string>(),
        grips = grip && !pistol ? new[] { "A_AngledForegrip" } : Array.Empty<string>(),
        underbarrel = light ? new[] { "A_LS3_Flashlight" } : Array.Empty<string>(),
    };

    /// <summary>
    /// The FPS Animation Pack's guns get their parts now (PackGunSetup, once it has put new ones in FPSBody, so they don't
    /// wait for the next time scripts load).
    /// </summary>
    internal static void FitPackGunsNow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        if (assets == null || assets.guns == null || !FitPackGuns(assets, false)) return;
        EditorUtility.SetDirty(assets);
        AssetDatabase.SaveAssets();
    }

    // How far either side of a point along the gun its surface is looked at (m), and how near the gun's middle line.
    private const float MountWindow = .03f, MountSide = .02f;

    private static bool FitPackGuns(FPSBodyAssets assets, bool all)
    {
        bool changed = false;
        var bakes = AssetDatabase.LoadAssetAtPath<GunBakes>(ProjectFolders.DataByName + GunBakes.ResourcePath + ".asset");
        foreach (var spec in PackGuns)
        {
            int index = Array.FindIndex(assets.guns, gun => gun.weapon != null && gun.weapon.name == spec.weapon);
            if (index < 0) continue;
            var current = assets.guns[index].prefab;
            if (!all && current != null && current.GetComponent<GunAttachments>() is GunAttachments made && !GripsWithoutHand(made)
                && !IronsNotCut(spec, current) && !PartsNotPlayed(current)) continue;
            try
            {
                var prefab = FitPackGun(spec, assets.guns[index].weapon, bakes);
                if (prefab == null) continue;
                assets.guns[index].prefab = prefab;
                changed = true;
                Debug.Log($"TacticalGunSetup: {spec.weapon} now wears the Tactical Shooter Pack's parts ({PackGunsFolder}).", prefab);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"TacticalGunSetup: {spec.weapon}'s parts could not be fitted: {error.Message}\n{error.StackTrace}");
            }
        }
        return changed;
    }

    private static GameObject FitPackGun(PackGun spec, WeaponData weapon, GunBakes bakes)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.source);
        if (source == null) return null;
        Directory.CreateDirectory(PackGunsFolder);
        AssetDatabase.Refresh();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = spec.weapon;
            var gun = instance.transform;
            gun.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var cas = instance.GetComponent<CasFPSWeapon>();
            var aim = cas != null && cas.aimPoint != null ? cas.aimPoint : gun;
            PlayPartsThroughCas(instance, source);

            // The gun's shape in its own frame: along the sight line (z), up (y) and across (x), its own parts only. Iron sights
            // modelled into the body are picked out first and left out of it, so the optics' mount finds the receiver.
            Quaternion frame = Quaternion.Inverse(gun.rotation) * Quaternion.LookRotation(aim.forward, aim.up);
            bool[] ironVertex = null;
            int[] ironTriangles = Array.Empty<int>();
            int ironTotal = 0;
            SkinnedMeshRenderer irons = null;
            if (spec.ironsBelowSightLine > 0f)
                irons = IronSightsIn(gun, frame, aim, spec.ironsBelowSightLine, out ironVertex, out ironTriangles, out ironTotal);
            var points = ShapeOf(gun, frame, aim, irons, ironVertex);
            GunMuzzle.Measure(gun, aim, .015f, out Vector3 muzzleAt, out Quaternion muzzleTurn);
            Vector3 muzzle = Quaternion.Inverse(frame) * (muzzleAt - gun.InverseTransformPoint(aim.position));
            // Where KINEMATION's support hand holds it: the bake's grip, kept in KINEMATION's weapon bone frame at the bake's
            // size (the gun model hangs in that frame turned by the gun's rotationOffset, at true size, FPSBody), else a
            // little under half way from the sight to the muzzle. Kept on the handguard.
            float hand = muzzle.z * .45f;
            if (bakes != null && bakes.entries != null)
                foreach (var entry in bakes.entries)
                    if (entry.weapon == weapon && entry.hasGrip && entry.skin != null)
                    {
                        Vector3 left = Quaternion.Inverse(FPSBody.GunFrameOf(cas)) * entry.leftGripPosition * GunClipRetarget.FitScaleOf(entry.skin);
                        hand = (Quaternion.Inverse(frame) * (left - gun.InverseTransformPoint(aim.position))).z;
                        break;
                    }
            hand = Mathf.Clamp(hand, .1f, Mathf.Max(.12f, muzzle.z - .08f));

            Transform Mount(string name, float z, bool top)
            {
                float y = top ? RailTop(points, z) : Underside(points, z);
                var mount = new GameObject(name).transform;
                mount.SetParent(gun, false);
                mount.localPosition = gun.InverseTransformPoint(aim.position) + frame * new Vector3(0f, y, z);
                mount.localRotation = frame;
                return mount;
            }

            var sightMount = Mount("SightMount", spec.pistol ? -.02f : .03f, true);
            var muzzleMount = new GameObject("MuzzleMount").transform;
            muzzleMount.SetParent(gun, false);
            muzzleMount.SetLocalPositionAndRotation(muzzleAt, muzzleTurn);
            var gripMount = Mount("GripMount", hand, false);
            var underMount = Mount("UnderMount", spec.pistol ? muzzle.z - .07f : hand + .1f, false);

            var sights = new List<GunSights.Sight>();
            foreach (string part in spec.sights)
                if (Pick(part) is Component prefab) sights.Add(SightChoice(prefab, Put(prefab, sightMount), aim));
            // The optics' eye points meet the iron sight's: the mount goes as far ahead of the iron aim point as their aim
            // points sit behind it (the MX16A4's holographic 15 cm, its DMCO 13.5), onto the receiver's top there.
            if (spec.opticsMeetIrons)
            {
                float behind = 0f;
                int counted = 0;
                foreach (var sight in sights)
                    if (sight.aimPoint != null && sight.aimPoint != aim)
                    {
                        behind += sightMount.InverseTransformPoint(sight.aimPoint.position).z;
                        counted++;
                    }
                if (counted > 0)
                {
                    float ahead = -behind / counted;
                    sightMount.localPosition = gun.InverseTransformPoint(aim.position) + frame * new Vector3(0f, RailTop(points, ahead), ahead);
                }
            }
            sights.Add(new GunSights.Sight { part = null, aimPoint = aim, ironSight = true });
            var none = new GunAttachments.Choice { kind = GunAttachments.Kind.None };
            var muzzles = new List<GunAttachments.Choice> { none };
            var grips = new List<GunAttachments.Choice> { none };
            var under = new List<GunAttachments.Choice> { none };
            foreach (string part in spec.muzzles) if (Pick(part) is Component prefab) muzzles.Add(ChoiceOf(prefab, Put(prefab, muzzleMount), gun));
            foreach (string part in spec.grips) if (Pick(part) is Component prefab) grips.Add(ChoiceOf(prefab, Put(prefab, gripMount), gun));
            foreach (string part in spec.underbarrel) if (Pick(part) is Component prefab) under.Add(ChoiceOf(prefab, Put(prefab, underMount), gun));

            var parts = instance.AddComponent<GunAttachments>();
            parts.muzzle = muzzles.Count > 1 ? muzzles.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.grips = grips.Count > 1 ? grips.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.underbarrel = under.Count > 1 ? under.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.initialUnderbarrel = Mathf.Max(0, under.FindIndex(choice => choice.kind == GunAttachments.Kind.Flashlight));
            parts.Begin();
            // The gun's own iron sights first, as it came.
            var shown = instance.AddComponent<GunSights>();
            shown.sights = sights.ToArray();
            shown.initial = sights.Count - 1;
            if (irons != null)
            {
                shown.ironsInMesh = irons;
                shown.ironTriangles = ironTriangles;
                shown.ironTriangleTotal = ironTotal;
            }
            shown.Select(shown.initial);

            // The parts' own pack scripts expect the pack's player; their particles are its muzzle flash.
            for (int pass = 0; pass < 4; pass++)
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null && IsPack(script)) Object.DestroyImmediate(script);

            var saved = PrefabUtility.SaveAsPrefabAsset(instance, $"{PackGunsFolder}/{spec.weapon}.prefab", out bool ok);
            return ok ? saved : null;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // One of the Tactical Shooter Pack's attachment prefabs by name, as its attachment script (what the gun setups read).
    private static Component Pick(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab", new[] { Pack }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.name != name) continue;
            foreach (var script in prefab.GetComponents<MonoBehaviour>())
                if (script != null && IsPack(script)) return script;
        }
        Debug.LogWarning($"TacticalGunSetup: the Tactical Shooter Pack's {name} wasn't found.");
        return null;
    }

    // The gun's mesh points in the sight line's frame from the aim point (x across, y up, z along), its parts as it stands.
    // The vertices of skipIn marked in skip are left out (iron sights modelled into the body, IronSightsIn).
    private static List<Vector3> ShapeOf(Transform gun, Quaternion frame, Transform aim, Renderer skipIn = null, bool[] skip = null)
    {
        var points = new List<Vector3>();
        var vertices = new List<Vector3>();
        Vector3 origin = gun.InverseTransformPoint(aim.position);
        Quaternion toFrame = Quaternion.Inverse(frame);
        foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            Matrix4x4 toWorld;
            bool baked = false;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = new Mesh();
                skinned.BakeMesh(mesh, true);
                baked = true;
                toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                toWorld = renderer.transform.localToWorldMatrix;
            }
            else continue;
            mesh.GetVertices(vertices);
            bool skipping = skip != null && skipIn != null && renderer == skipIn && skip.Length == vertices.Count;
            for (int i = 0; i < vertices.Count; i++)
                if (!skipping || !skip[i]) points.Add(toFrame * (gun.InverseTransformPoint(toWorld.MultiplyPoint3x4(vertices[i])) - origin));
            if (baked) Object.DestroyImmediate(mesh);
        }
        return points;
    }

    // Made before the MX16A4's iron sights folded away under optics (2026-11-10): made again once.
    private static bool IronsNotCut(PackGun spec, GameObject prefab)
    {
        if (spec.ironsBelowSightLine <= 0f) return false;
        var sights = prefab.GetComponent<GunSights>();
        return sights == null || sights.ironsInMesh == null;
    }

    // Made without the gun-part player its CAS prefab carries (the MX16A4 since 0.8.1): made again once.
    private static bool PartsNotPlayed(GameObject prefab)
        => prefab.GetComponentInChildren<Animator>(true) != null && prefab.GetComponentInChildren<PartsPlayer>(true) == null;

    /// <summary>
    /// The gun-part clips (a reload's magazine out and in, the bolt) play through CAS's player on the gun's own Animator,
    /// as on KINEMATION's CAS guns and our Tactical ones. KINEMATION's CAS_FPSPack_MX16A4 adds its player, and takes the
    /// Animator's controller away, as overrides on an object of the model prefab it holds; unpacked, the MX16A4 came out
    /// with neither, so its reload clip never moved the magazine and it stayed in the gun through every reload while the
    /// hand went to the pouch (user, 2026-11-12: "M4는 왜 재장전할 때 탄창을 안 빼냐"). The rest pose is the source's
    /// player's where it has one.
    /// </summary>
    private static void PlayPartsThroughCas(GameObject instance, GameObject source)
    {
        var animator = instance.GetComponentInChildren<Animator>(true);
        if (animator == null) return;
        animator.runtimeAnimatorController = null;
        var player = animator.GetComponent<PartsPlayer>();
        if (player == null) player = animator.gameObject.AddComponent<PartsPlayer>();
        var write = new SerializedObject(player);
        var own = write.FindProperty("useAnimator");
        if (own != null) own.boolValue = false;
        var rest = write.FindProperty("defaultClipPose");
        var theirs = source != null ? source.GetComponentInChildren<PartsPlayer>(true) : null;
        if (rest != null && rest.objectReferenceValue == null && theirs != null)
            rest.objectReferenceValue = new SerializedObject(theirs).FindProperty("defaultClipPose")?.objectReferenceValue;
        write.ApplyModifiedPropertiesWithoutUndo();
    }

    // The gun's body mesh (its biggest skinned one) and which of its vertices and triangles are iron sights modelled into
    // it: the mesh's pieces (vertices at one place welded, as a hard edge or a seam splits them) whose highest point is
    // less than `below` under the iron sight line (the aim point's height), with the gun posed as it stands. Triangles
    // are numbered through the submeshes in order, as GunSights cuts them. Null when there are none or it can't be read.
    private static SkinnedMeshRenderer IronSightsIn(Transform gun, Quaternion frame, Transform aim, float below,
        out bool[] ironVertex, out int[] ironTriangles, out int total)
    {
        ironVertex = null;
        ironTriangles = Array.Empty<int>();
        total = 0;
        SkinnedMeshRenderer body = null;
        foreach (var skinned in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skinned.sharedMesh != null && (body == null || skinned.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
                body = skinned;
        if (body == null) return null;
        var mesh = body.sharedMesh;
        if (!mesh.isReadable)
        {
            Debug.LogWarning($"TacticalGunSetup: {mesh.name} can't be read, so {gun.name}'s iron sights stay on under optics (switch Read/Write on).");
            return null;
        }

        var raw = new List<Vector3>();
        mesh.GetVertices(raw);
        int count = raw.Count;
        var posedMesh = new Mesh();
        body.BakeMesh(posedMesh, true);
        var posed = new List<Vector3>();
        posedMesh.GetVertices(posed);
        Object.DestroyImmediate(posedMesh);
        if (posed.Count != count) return null;

        var root = new int[count];
        for (int i = 0; i < count; i++) root[i] = i;
        int Find(int a)
        {
            while (root[a] != a) { root[a] = root[root[a]]; a = root[a]; }
            return a;
        }
        void Join(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) root[a] = b;
        }
        // One piece: vertices at one place (to a hundredth of a millimetre), and the corners of each triangle.
        var place = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < count; i++)
        {
            var key = new Vector3Int(Mathf.RoundToInt(raw[i].x * 1e5f), Mathf.RoundToInt(raw[i].y * 1e5f), Mathf.RoundToInt(raw[i].z * 1e5f));
            if (place.TryGetValue(key, out int first)) Join(i, first);
            else place[key] = i;
        }
        var triangles = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            mesh.GetTriangles(triangles, s);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Join(triangles[t], triangles[t + 1]);
                Join(triangles[t], triangles[t + 2]);
            }
        }

        // Each piece's highest point over the iron sight line.
        var toWorld = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
        Quaternion toFrame = Quaternion.Inverse(frame);
        Vector3 origin = gun.InverseTransformPoint(aim.position);
        var top = new Dictionary<int, float>();
        for (int i = 0; i < count; i++)
        {
            float height = (toFrame * (gun.InverseTransformPoint(toWorld.MultiplyPoint3x4(posed[i])) - origin)).y;
            int piece = Find(i);
            top[piece] = top.TryGetValue(piece, out float highest) ? Mathf.Max(highest, height) : height;
        }
        ironVertex = new bool[count];
        for (int i = 0; i < count; i++) ironVertex[i] = top[Find(i)] > -below;

        var irons = new List<int>();
        int number = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            mesh.GetTriangles(triangles, s);
            for (int t = 0; t + 2 < triangles.Count; t += 3, number++)
                if (ironVertex[triangles[t]]) irons.Add(number);
        }
        total = number;
        ironTriangles = irons.ToArray();
        if (irons.Count == 0) return null;
        Debug.Log($"TacticalGunSetup: {gun.name}'s iron sights are {irons.Count} of its {number} triangles; they fold away under optics.");
        return body;
    }

    // The top of the rail at z along the sight line: the median of the highest points in slices about it, so a sight post
    // or a charging handle sticking up in one slice doesn't lift the mount.
    private static float RailTop(List<Vector3> points, float z)
    {
        var tops = new List<float>();
        for (float slice = z - .06f; slice <= z + .06f; slice += .01f)
        {
            float top = float.NegativeInfinity;
            foreach (var point in points)
                if (Mathf.Abs(point.z - slice) <= .005f && Mathf.Abs(point.x) <= MountSide) top = Mathf.Max(top, point.y);
            if (!float.IsNegativeInfinity(top)) tops.Add(top);
        }
        if (tops.Count == 0) return 0f;
        tops.Sort();
        return tops[tops.Count / 2];
    }

    // The underside of the gun at z along the sight line, under its middle line.
    private static float Underside(List<Vector3> points, float z)
    {
        float bottom = float.PositiveInfinity;
        foreach (var point in points)
            if (Mathf.Abs(point.z - z) <= MountWindow && Mathf.Abs(point.x) <= MountSide) bottom = Mathf.Min(bottom, point.y);
        return float.IsPositiveInfinity(bottom) ? -.05f : bottom;
    }

    // KINEMATION's Operator's left hand (its hand_l, which a grip's LeftHand stands for), in that bone's own turn: the way
    // the fingers point (wrist to the middle knuckle), the way the palm faces (as ArmIK takes it for a left hand), and the
    // palm's middle from the wrist (a little over half way to the knuckles, as HandGrip finds ours), in metres.
    private static bool operatorMeasured, operatorFound;
    private static Vector3 operatorFingers, operatorFacing, operatorPalm;

    private static bool OperatorLeftHand(out Vector3 fingers, out Vector3 facing, out Vector3 palm)
    {
        if (!operatorMeasured)
        {
            operatorMeasured = true;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Operator);
            if (model != null)
            {
                var copy = Object.Instantiate(model);
                try
                {
                    copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    copy.transform.localScale = Vector3.one;
                    var hand = Hierarchy.Find(copy.transform, "hand_l");
                    var index = Hierarchy.Find(copy.transform, "index_01_l");
                    var middle = Hierarchy.Find(copy.transform, "middle_01_l");
                    var ring = Hierarchy.Find(copy.transform, "ring_01_l");
                    var pinky = Hierarchy.Find(copy.transform, "pinky_01_l");
                    if (hand != null && index != null && middle != null && ring != null && pinky != null)
                    {
                        Quaternion toHand = Quaternion.Inverse(hand.rotation);
                        Vector3 along = middle.position - hand.position, thumbSide = index.position - pinky.position;
                        Vector3 face = -Vector3.Cross(along, thumbSide);
                        Vector3 knuckles = (index.position + middle.position + ring.position + pinky.position) * .25f;
                        operatorFingers = toHand * along.normalized;
                        operatorFacing = toHand * face.normalized;
                        operatorPalm = toHand * ((knuckles - hand.position) * .55f);
                        operatorFound = along.sqrMagnitude > 1e-8f && face.sqrMagnitude > 1e-10f;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(copy);
                }
            }
            if (!operatorFound) Debug.LogWarning("TacticalGunSetup: the Operator's left hand bones weren't found; grips won't take the support hand.");
        }
        fingers = operatorFingers;
        facing = operatorFacing;
        palm = operatorPalm;
        return operatorFound;
    }

    private static bool IsPack(MonoBehaviour script)
    {
        string space = script.GetType().Namespace ?? string.Empty;
        return space.StartsWith("KINEMATION.TacticalShooterPack", StringComparison.Ordinal)
            || space.StartsWith("KINEMATION.KShooterCore", StringComparison.Ordinal);
    }

    private static MonoBehaviour FindPack(GameObject root, string typeName)
    {
        foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (script != null && IsPack(script))
                for (var type = script.GetType(); type != null; type = type.BaseType)
                    if (type.Name == typeName) return script;
        return null;
    }

    // ---- Assets ----

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
