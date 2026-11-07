using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features.FPSRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Remakes KINEMATION's gun clips (FPS Animation Pack, made for its arms skeleton SK_Arms_Mono) for each of our bodies
/// with KINEMATION Retarget Pro, the way its window does it, into GunBakes:
/// - each skin's model gets KINEMATION's weapon bone (ik_hand_gun) in a copy, the retarget target, turned about its root
///   to face the way KINEMATION's arms face in their own model space (our skins face -Z, the arms +Z): Retarget Pro's FPS
///   feature carries the gun's place across in model space, so facing apart it would put the gun behind the body. A
///   humanoid clip faces the Animator's +Z on any body, so the turn only matters while baking;
/// - a Retarget Pro profile from SK_Arms_Mono to it: Retarget Pro's own preset mapping (spine, arms, fingers, legs), then
///   its FPS feature, which keeps both hands on the weapon bone as KINEMATION's arms hold it;
/// - the gun goes where KINEMATION's camera sees it: its place from the camera bone, scaled to this body's arms, measured
///   from this body's eyes, since our camera rides the eyes;
/// - a gun with a stock then moves until the stock's butt rests in this body's right shoulder pocket, on the front of the
///   body or its clothes: the first-person view is the body's own hold, so the third-person view shows the same (True FPS);
///   the chest turns toward the gun side, as a rifleman stands, as far as the support hand needs to reach its place on the
///   gun (ShootingStanceFeature);
/// - the fingers are KINEMATION's own, read through Unity's humanoid muscles on KINEMATION's humanoid mannequin
///   (GunClipFingers), since Retarget Pro carried them across flat or spread past a hand's reach;
/// - the pose and both reloads are baked as humanoid clips, and where the gun sits in the pose is kept for FPS Animation
///   Framework's Pose Sampler (FPSBody).
/// It runs by itself after scripts load when a skin or a clip has no bake yet or changed, and from
/// Tools → ENFILADE → Combat → Retarget KINEMATION Gun Clips to make them all again.
/// </summary>
[InitializeOnLoad]
internal static class GunClipRetarget
{
    private const string AssetsPath = "Assets/_Project/Resources/" + FPSBodyAssets.ResourcePath + ".asset";
    private const string BakesPath = "Assets/_Project/Resources/" + GunBakes.ResourcePath + ".asset";
    private const string Folder = "Assets/_Project/Data/Combat/GunBakes";
    // KINEMATION's names on its arms skeleton; the weapon bone is added to our bodies under the same name.
    private const string WeaponBone = "ik_hand_gun";
    // Raised when the way clips are made changes, so every bake is made again. 2: the target turned to the arms' facing.
    // 3: the hands' grip on the gun is measured. 4: made at the size the body plays at, in KINEMATION's true metres
    // (TrueSize). 5: the gun placed from the eyes by measuring; both grips kept in the gun's axes. 6: measured from
    // KINEMATION's CameraSocket (AddCameraSocket); before, no camera was found and nothing framed the gun. 7: a stock's
    // butt in the shoulder pocket (ToShoulder); KINEMATION's camera framing put it at the cheek, and in the F5 view the
    // stock went through the head (user, 2026-11-07: "3인칭 = 1인칭"). 8: the shooting stance (ShootingStanceFeature),
    // since square to the gun the support hand fell 7 cm short of the handguard. 9: the fingers through KINEMATION's humanoid
    // mannequin (GunClipFingers); fingers closed on the gun's mesh (8) came out wrong (user, 2026-11-07). 10: the
    // mannequin sampled without its Animator, which had kept the clip off its fingers (every finger muscle 0 in 9).
    // 11: each hand placed by its knuckles, not its wrist (KnucklesOnto).
    // 13: no knuckle offset (V11/V12 put the support hand off the gun); the hands rest on the gun's surface in play.
    private const int Version = 13;

    // The shooting stance: the chest turns in steps of this (degrees) up to the most, until the support hand comes within
    // the slack of KINEMATION's hold on the gun (m in game) with its arm no straighter than the share (shoulder to wrist
    // over the arm's length).
    private const float StanceStep = 2.5f, StanceMaxBlade = 40f, StanceReachSlack = .005f, StanceArmStraight = .97f;

    // Where a stock's butt rests: from the shoulder joint (RightUpperArm) toward the neck by this share of the way across,
    // under the joint by this share of the shoulder-to-neck distance, then on the front of the body there (skin or
    // clothes, within this share of that distance from the line straight ahead; without a readable mesh, this share of
    // it ahead of the joint). A shooter's shoulder pocket is just inside the ball of the joint and a little under it.
    private const float PocketInward = .25f, PocketDrop = .1f, PocketRadius = .2f, PocketFallbackDepth = .35f;
    // A gun has a stock when its back end reaches this far behind the trigger hand's wrist (m in game); a pistol's doesn't.
    private const float StockReach = .1f;
    // How deep the gun's back face is taken when finding the butt's middle (m).
    private const float StockBackDepth = .015f;

    static GunClipRetarget() => EditorApplication.delayCall += () => Run(false);

    [MenuItem("Tools/ENFILADE/Combat/Retarget KINEMATION Gun Clips")]
    private static void RunAll() => Run(true);

    // A new skin, a gun clip or the gun list arriving (a pull, an import) is baked once the import is over.
    private sealed class Watcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
                if (path == AssetsPath || path.EndsWith("/PlayerBody.asset", StringComparison.Ordinal))
                {
                    EditorApplication.delayCall += () => Run(false);
                    return;
                }
        }
    }

    private static void Run(bool all)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        var profile = PlayerBodyProfile.Load();
        if (assets == null || assets.armsModel == null || assets.guns == null || profile == null) return;

        var bakes = AssetDatabase.LoadAssetAtPath<GunBakes>(BakesPath);
        var entries = new List<GunBakes.Entry>(bakes != null ? bakes.entries : Array.Empty<GunBakes.Entry>());
        var skins = new List<GameObject>();
        foreach (var skin in profile.Skins)
            if (profile.ModelOf(skin) is GameObject model && !skins.Contains(model)) skins.Add(model);
        if (profile.ModelOf(null) is GameObject fallback && !skins.Contains(fallback)) skins.Add(fallback);

        var due = new List<GameObject>();
        foreach (var skin in skins)
            if (all || !UpToDate(entries, skin, assets)) due.Add(skin);
        if (due.Count == 0) return;

        if (bakes == null)
        {
            bakes = ScriptableObject.CreateInstance<GunBakes>();
            Directory.CreateDirectory(Path.GetDirectoryName(BakesPath));
            AssetDatabase.CreateAsset(bakes, BakesPath);
        }
        // KINEMATION's humanoid mannequin the gun clips' fingers are read through, open for the whole run.
        using var fingers = GunClipFingers.Open(assets);
        try
        {
            for (int i = 0; i < due.Count; i++)
            {
                var skin = due[i];
                EditorUtility.DisplayProgressBar("ENFILADE: KINEMATION guns", $"Retargeting gun clips for {skin.name}", i / (float)due.Count);
                try
                {
                    var made = Bake(skin, assets, fingers);
                    if (made == null) continue;
                    entries.RemoveAll(entry => entry.skin == skin);
                    entries.AddRange(made);
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"GunClipRetarget: KINEMATION's gun clips could not be remade for {skin.name}: {error.Message}\n{error.StackTrace}", skin);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        entries.RemoveAll(entry => entry.skin == null || !skins.Contains(entry.skin));
        bakes.entries = entries.ToArray();
        EditorUtility.SetDirty(bakes);
        AssetDatabase.SaveAssets();
    }

    private static string SourceOf(GameObject skin, FPSBodyAssets assets, FPSBodyAssets.Gun gun)
        => $"{Version}|{Guid(skin)}|{Guid(assets.armsModel)}|{Guid(gun.sourcePose)}|{Guid(gun.sourceReloadTactical)}|{Guid(gun.sourceReloadEmpty)}";

    private static string Guid(Object asset) => asset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)) : "-";

    private static bool UpToDate(List<GunBakes.Entry> entries, GameObject skin, FPSBodyAssets assets)
    {
        foreach (var gun in assets.guns)
        {
            if (gun.weapon == null || gun.sourcePose == null) continue;
            int match = entries.FindIndex(entry => entry.skin == skin && entry.weapon == gun.weapon);
            if (match < 0) return false;
            var entry = entries[match];
            if (entry.source != SourceOf(skin, assets, gun) || entry.pose == null
                || (gun.sourceReloadTactical != null && entry.reloadTactical == null)
                || (gun.sourceReloadEmpty != null && entry.reloadEmpty == null)) return false;
        }
        return true;
    }

    // ---- One skin ----

    private static List<GunBakes.Entry> Bake(GameObject skin, FPSBodyAssets assets, GunClipFingers fingers)
    {
        string dir = $"{Folder}/{FileName(skin.name)}";
        Directory.CreateDirectory(dir);
        AssetDatabase.Refresh();

        var target = MakeTarget(skin, $"{dir}/{FileName(skin.name)}_RetargetTarget.prefab", ArmsFacing(assets.armsModel));
        var profile = MakeProfile(assets.armsModel, target, dir, out var stance, out var fps);
        if (profile == null) return null;

        var baker = new RetargetAnimBaker();
        try
        {
            baker.SetProfile(profile, false, out string profileMessage, out MessageType profileMessageType);
            if (profileMessageType == MessageType.Warning || profileMessageType == MessageType.Error)
                Debug.LogWarning($"GunClipRetarget: Retarget Pro on {skin.name}: {profileMessage}", profile);
            baker.SetRigType(true);
            baker.OutputType = RetargetAnimBaker.BakeOutputType.AnimationClip;
            baker.UseRootMotion = false;
            if (!baker.TryInitializeBaker(out string error))
            {
                Debug.LogWarning($"GunClipRetarget: Retarget Pro could not start on {skin.name}: {error}", profile);
                return null;
            }
            float fit = FitScaleOf(skin);
            bool trueSize = TrueSize(baker, fit);

            var source = baker.SourcePreviewInstance.transform;
            var body = baker.TargetPreviewInstance.transform;
            var sourceWeapon = Find(source, WeaponBone);
            var sourceCamera = AddCameraSocket(source);
            var bodyWeapon = Find(body, WeaponBone);
            if (sourceWeapon == null || bodyWeapon == null)
            {
                Debug.LogWarning($"GunClipRetarget: {assets.armsModel.name} or {target.name} has no {WeaponBone}; the guns can't be placed.", profile);
                return null;
            }
            CheckFacing(source, body, skin);
            if (Mathf.Abs(Mathf.DeltaAngle(Yaw(source.InverseTransformDirection(SourceForward(source))), 0f)) > 10f)
                Debug.LogWarning($"GunClipRetarget: {assets.armsModel.name} does not face +Z in its own model space; the guns may sit turned on the bodies.", assets.armsModel);

            var sourceLeft = FindLike(source, "hand_l");
            var made = new List<GunBakes.Entry>();
            foreach (var gun in assets.guns)
            {
                if (gun.weapon == null || gun.sourcePose == null) continue;
                // Where the body faces, taken standing square: the stance turns the shoulders, not where the gun points.
                stance.blade = 0f;
                fps.weaponOffset = Vector3.zero;
                baker.RetargetAtTime(gun.sourcePose, null, 0f);
                Vector3 facing = Forward(body);
                bool hasStock = MeasureStock(gun.prefab, out var stock);
                float supportReach = sourceLeft != null ? Vector3.Distance(sourceLeft.position, sourceWeapon.position) : 0f;

                // The gun where KINEMATION's camera sees it (its CameraSocket, in the camera's own axes), seen the same way
                // from this body's eyes looking ahead, in true metres in game (the bake is at 1/fit of the size the body
                // plays at). Placed by measuring where Retarget Pro put it and moving it the rest of the way, twice. A gun
                // with a stock then goes back along itself until its butt rests in the shoulder pocket, twice again, as the
                // arms reach for it anew.
                Vector3 fromCamera = Vector3.zero;
                Placement Place(float blade)
                {
                    stance.blade = blade;
                    fps.weaponOffset = fps.rightHandOffset = fps.leftHandOffset = Vector3.zero;
                    baker.RetargetAtTime(gun.sourcePose, null, 0f);
                    Vector3 eyes = Vector3.zero;
                    bool framed = sourceCamera != null && Eyes(body, facing, out eyes);
                    fromCamera = framed ? Quaternion.Inverse(sourceCamera.rotation) * (sourceWeapon.position - sourceCamera.position) : Vector3.zero;
                    Vector3 desired = eyes + body.InverseTransformDirection(Quaternion.LookRotation(facing, body.up) * fromCamera) / Mathf.Max(.01f, fit);
                    for (int pass = 0; framed && pass < 2; pass++)
                    {
                        fps.weaponOffset += desired - body.InverseTransformPoint(bodyWeapon.position);
                        baker.RetargetAtTime(gun.sourcePose, null, 0f);
                    }
                    for (int pass = 0; framed && hasStock && pass < 2; pass++)
                    {
                        if (!ToShoulder(body, bodyWeapon, stock, fit, facing, out Vector3 move, out _, out _)) break;
                        fps.weaponOffset += move;
                        baker.RetargetAtTime(gun.sourcePose, null, 0f);
                    }
                    // The hands stay where Retarget Pro puts them (V10); in play they rest on the gun's real surface
                    // (PlayerBodyHands.GunFingers). Moving them by the knuckles here put the support hand off the gun.
                    return Measure(body, bodyWeapon, fit, supportReach);
                }

                // Shouldered, the chest turns toward the gun side only as far as the support hand needs to reach its place
                // on the gun with the arm still bent (ShootingStanceFeature). A gun without a stock is held square.
                float bladed = 0f;
                var placed = Place(0f);
                if (hasStock && !placed.Reaches)
                {
                    var best = placed;
                    for (float blade = StanceStep; blade <= StanceMaxBlade + .001f; blade += StanceStep)
                    {
                        var trial = Place(blade);
                        if (trial.Reaches || trial.shortfall < best.shortfall - .001f) { best = trial; bladed = blade; }
                        if (trial.Reaches) break;
                    }
                    placed = Place(bladed);
                }
                EditorUtility.SetDirty(fps);
                EditorUtility.SetDirty(stance);

                string shoulder = !hasStock ? "stock not measured"
                    : ToShoulder(body, bodyWeapon, stock, fit, facing, out Vector3 rest, out float reach, out _)
                        ? $"butt in the shoulder pocket, {rest.magnitude * fit:0.000} m off (reaches {reach:0.000} m behind the wrist)"
                        : $"no stock (back end {reach:0.000} m behind the wrist)";
                shoulder += hasStock && placed.shortfall >= 0f
                    ? $"; chest turned {bladed:0.#}°, support hand {placed.shortfall:0.000} m short of KINEMATION's hold, its arm {placed.extension:0.00} straight"
                    : "; standing square";
                var entry = new GunBakes.Entry
                {
                    skin = skin,
                    weapon = gun.weapon,
                    gunPosition = body.InverseTransformPoint(bodyWeapon.position),
                    gunRotation = Quaternion.Inverse(body.rotation) * bodyWeapon.rotation,
                    source = SourceOf(skin, assets, gun)
                };
                MeasureGrip(body, bodyWeapon, ref entry);
                Report(skin, gun.weapon, source, sourceWeapon, body, bodyWeapon, entry, fit, trueSize, fromCamera, facing, shoulder);

                // Each clip's fingers are KINEMATION's own, through Unity's humanoid muscles (GunClipFingers).
                string name = FileName(gun.weapon.name);
                entry.pose = fingers.Apply(gun.sourcePose, BakeClip(baker, gun.sourcePose, $"{dir}/{name}_Pose.anim"));
                entry.reloadTactical = fingers.Apply(gun.sourceReloadTactical, BakeClip(baker, gun.sourceReloadTactical, $"{dir}/{name}_ReloadTactical.anim"));
                entry.reloadEmpty = fingers.Apply(gun.sourceReloadEmpty, BakeClip(baker, gun.sourceReloadEmpty, $"{dir}/{name}_ReloadEmpty.anim"));
                if (entry.pose != null) made.Add(entry);
            }
            return made;
        }
        finally
        {
            baker.UnInitializeBaker();
            baker.CleanupPreviewResources();
            AssetDatabase.SaveAssets();
        }
    }

    // The skin's model with KINEMATION's weapon bone under its root, for Retarget Pro's FPS feature to put the gun on, the
    // body turned about the root (its paths unchanged) to face the way the arms do.
    private static GameObject MakeTarget(GameObject skin, string path, float armsYaw)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(skin);
        try
        {
            ResetRootRotation(instance.transform);
            float turn = Mathf.DeltaAngle(Yaw(instance.transform.InverseTransformDirection(Forward(instance.transform))), armsYaw);
            if (Mathf.Abs(turn) > .5f)
                foreach (Transform child in instance.transform)
                    child.RotateAround(instance.transform.position, instance.transform.up, turn);
            var bone = new GameObject(WeaponBone).transform;
            bone.SetParent(instance.transform, false);
            return PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // Retarget Pro's profile as its "Create Retarget Profile" menu makes it (rigs and preset mapping), plus, in this order,
    // the shooting stance (ours) and its FPS feature.
    private static RetargetProfile MakeProfile(GameObject arms, GameObject target, string dir, out ShootingStanceFeature stance,
        out FPSRetargetFeature fps)
    {
        stance = null;
        fps = null;
        string path = $"{dir}/{FileName(target.name)}_Profile.asset";
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<RetargetProfile>();
        profile.sourceCharacter = arms;
        profile.targetCharacter = target;
        profile.saveFolderPath = dir;
        profile.retargetFeatures = new List<KINEMATION.RetargetPro.Runtime.Features.RetargetFeature>();
        AssetDatabase.CreateAsset(profile, path);
        if (!RetargetProfileModelRigUtility.TryComposeProfileRigs(profile, true, out string message)
            || profile.retargetFeatures.Count == 0)
        {
            Debug.LogWarning($"GunClipRetarget: Retarget Pro could not map {arms.name} onto {target.name}: {message}", profile);
            return null;
        }

        stance = AddFeature<ShootingStanceFeature>(profile);
        fps = AddFeature<FPSRetargetFeature>(profile);
        fps.MapChains();
        // The weapon bones are not skinned, so Retarget Pro's chains leave them out; they are in each rig's hierarchy.
        fps.sourceWeapon = Chain(profile.sourceRig, WeaponBone);
        fps.targetWeapon = Chain(profile.targetRig, WeaponBone);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    // A feature at the end of the profile's list, run in that order every frame.
    private static T AddFeature<T>(RetargetProfile profile) where T : KINEMATION.RetargetPro.Runtime.Features.RetargetFeature
    {
        var feature = ScriptableObject.CreateInstance<T>();
        feature.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        feature.name = typeof(T).Name;
        feature.sourceRig = profile.sourceRig;
        feature.targetRig = profile.targetRig;
        AssetDatabase.AddObjectToAsset(feature, profile);
        profile.retargetFeatures.Add(feature);
        return feature;
    }

    private static KRigElementChain Chain(KRig rig, string bone)
    {
        var chain = new KRigElementChain { chainName = "Weapon", elementChain = new List<KRigElement>() };
        if (rig != null && rig.rigHierarchy != null)
            foreach (var element in rig.rigHierarchy)
                if (element.name == bone) { chain.elementChain.Add(element); break; }
        return chain;
    }

    // Retarget Pro saves a humanoid bake beside the profile under its own name; it goes where the bakes list keeps it, the
    // older clip there taking the new curves so whatever points at it keeps pointing at it.
    private static AnimationClip BakeClip(RetargetAnimBaker baker, AnimationClip source, string path)
    {
        if (source == null) return null;
        var baked = baker.BakeAnimation(source);
        if (baked == null)
        {
            Debug.LogWarning($"GunClipRetarget: Retarget Pro could not bake {source.name}.", source);
            return null;
        }
        string bakedPath = AssetDatabase.GetAssetPath(baked);
        if (bakedPath == path) return baked;
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            string moved = AssetDatabase.MoveAsset(bakedPath, path);
            if (!string.IsNullOrEmpty(moved)) Debug.LogWarning($"GunClipRetarget: {moved}");
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(string.IsNullOrEmpty(moved) ? path : bakedPath);
        }
        EditorUtility.CopySerialized(baked, existing);
        existing.name = Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(existing);
        AssetDatabase.DeleteAsset(bakedPath);
        return existing;
    }

    // ---- KINEMATION's camera ----

    // KINEMATION's camera point on its arms: the CameraSocket its CAS integration puts on the arms skeleton's head
    // (SK_Arms_Mono_Skeleton), copied onto the source arms here with the same parent bone and local pose. The arms model
    // itself has no camera bone, and the clips' camera_bone (under root) only carries the camera's motion, not where it
    // sits: the guns were placed with no camera at all until 2026-11-07, and sat at the eyes' height.
    private const string CameraSocketName = "CameraSocket", ArmsSkeletonName = "SK_Arms_Mono_Skeleton";

    private static Transform AddCameraSocket(Transform source)
    {
        var existing = FindLike(source, CameraSocketName);
        if (existing != null) return existing;
        Transform socket = null;
        foreach (string guid in AssetDatabase.FindAssets($"{ArmsSkeletonName} t:Prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            socket = prefab != null ? FindLike(prefab.transform, CameraSocketName) : null;
            if (socket != null) break;
        }
        var parent = socket != null && socket.parent != null ? FindLike(source, socket.parent.name) : null;
        if (parent == null)
        {
            Debug.LogWarning($"GunClipRetarget: KINEMATION's {CameraSocketName} ({ArmsSkeletonName}) was not found; the guns are placed where Retarget Pro puts them.");
            return null;
        }
        var made = new GameObject(CameraSocketName) { hideFlags = HideFlags.DontSave }.transform;
        made.SetParent(parent, false);
        made.SetLocalPositionAndRotation(socket.localPosition, socket.localRotation);
        return made;
    }

    // ---- True size ----

    // The scale the game fits this skin to (PlayerBody.FitScaleOf, eyes to the standing camera height of Player.prefab).
    private static float FitScaleOf(GameObject skin)
    {
        var player = Resources.Load<GameObject>("Characters/Player");
        var pivot = player != null ? player.transform.Find("CrouchPivot") : null;
        if (pivot == null) return 1f;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(skin);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            return PlayerBody.FitScaleOf(instance.GetComponentInChildren<Animator>(), instance.transform, pivot.localPosition.y);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // Retarget Pro's FPS feature scales where the gun goes and where the hands sit on it by the arms' length ratio
    // (FPSRetargetFeatureState._fpsScale), as if the gun shrank or grew with the body. Our guns are their true size, so
    // the hands missed their grips and the gun sat too near the eyes on a body with shorter arms (user, 2026-11-07). Made
    // for the body at the size it plays at, every length on it comes out in KINEMATION's true metres: the scale is one
    // over the body's fit scale. The field is private to Retarget Pro and read through reflection; without it, the bake
    // keeps Retarget Pro's own scale and the report says so.
    private static bool TrueSize(RetargetAnimBaker baker, float fit)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var states = baker.RetargetComponent != null
            ? typeof(RetargetProComponent).GetField("_retargetFeatureStates", Private)?.GetValue(baker.RetargetComponent) as System.Collections.IList
            : null;
        var scale = typeof(FPSRetargetFeatureState).GetField("_fpsScale", Private);
        if (states == null || scale == null || fit <= 0f)
        {
            Debug.LogWarning("GunClipRetarget: Retarget Pro's FPS scale could not be set; the guns are baked at Retarget Pro's own arm-length scale.");
            return false;
        }
        bool set = false;
        foreach (var state in states)
            if (state is FPSRetargetFeatureState)
            {
                scale.SetValue(state, 1f / fit);
                set = true;
            }
        return set;
    }

    // ---- Measures on the posed preview bodies ----

    // Where both hands sit on the gun in the pose, in the gun bone's axes, as Retarget Pro's FPS feature put them there.
    // FPSBody's probe compares the hands in play with it.
    private static void MeasureGrip(Transform body, Transform weapon, ref GunBakes.Entry entry)
    {
        var right = Human(body, HumanBodyBones.RightHand);
        var left = Human(body, HumanBodyBones.LeftHand);
        float scale = Mathf.Max(1e-6f, Mathf.Abs(body.lossyScale.x));
        Quaternion toGun = Quaternion.Inverse(weapon.rotation);
        if (right != null) entry.rightGripPosition = toGun * (right.position - weapon.position) / scale;
        if (left != null) entry.leftGripPosition = toGun * (left.position - weapon.position) / scale;
        entry.hasGrip = right != null && left != null;
    }

    // ---- The support hand ----

    // How the support hand holds the gun as the body is placed: how much nearer the gun bone it sits than KINEMATION's
    // hold (m in game; Retarget Pro's FPS feature leaves a hand that can't reach short of it) and how straight its arm is.
    private struct Placement
    {
        public float shortfall, extension;
        public bool Reaches => shortfall <= StanceReachSlack && extension <= StanceArmStraight;
    }

    private static Placement Measure(Transform body, Transform weapon, float fit, float supportReach)
    {
        var shoulder = Human(body, HumanBodyBones.LeftUpperArm);
        var elbow = Human(body, HumanBodyBones.LeftLowerArm);
        var wrist = Human(body, HumanBodyBones.LeftHand);
        if (shoulder == null || elbow == null || wrist == null || supportReach <= 0f) return new Placement { shortfall = -1f, extension = 0f };
        float arm = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
        return new Placement
        {
            shortfall = supportReach - Vector3.Distance(wrist.position, weapon.position) * fit,
            extension = arm > 1e-5f ? Vector3.Distance(shoulder.position, wrist.position) / arm : 0f
        };
    }

    // ---- The stock in the shoulder ----

    // A gun's back end (the butt of a stock), the way it points, both in its own space in true metres, and the frame it
    // hangs in from KINEMATION's weapon bone, as FPSBody hangs it (FPSBody.GunFrameOf).
    private struct Stock
    {
        public Vector3 butt, forward;
        public Quaternion frame;
    }

    private static bool MeasureStock(GameObject prefab, out Stock stock)
    {
        stock = default;
        if (prefab == null) return false;
        var instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var kinemation = instance.GetComponent<CasFPSWeapon>();
            stock.frame = FPSBody.GunFrameOf(kinemation);
            GunMuzzle.Back(instance.transform, kinemation != null ? kinemation.aimPoint : null, StockBackDepth, out stock.butt, out stock.forward);
            return stock.forward.sqrMagnitude > .5f;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // How far the gun has to move (in the body's model space) for its butt to rest in the shoulder pocket. False when the
    // gun has no stock: its back end reaches less than StockReach behind the trigger hand's wrist (reach, m in game).
    private static bool ToShoulder(Transform body, Transform weapon, in Stock stock, float fit, Vector3 facing, out Vector3 move,
        out float reach, out Vector3 pocket)
    {
        move = pocket = Vector3.zero;
        reach = 0f;
        var hand = Human(body, HumanBodyBones.RightHand);
        if (hand == null) return false;
        // The bake is at 1/fit of the size the body plays at; the gun is its true size in game.
        Quaternion gun = weapon.rotation * stock.frame;
        Vector3 butt = weapon.position + gun * stock.butt / Mathf.Max(.01f, fit);
        Vector3 pointing = gun * stock.forward;
        reach = Vector3.Dot(hand.position - butt, pointing) * fit;
        if (reach < StockReach || !Pocket(body, facing, pointing, out pocket)) return false;
        move = body.InverseTransformPoint(pocket) - body.InverseTransformPoint(butt);
        return true;
    }

    // The right shoulder pocket as the body stands in the pose: measured from its shoulder joint and neck (across and
    // down as the body faces), then on the front of the body or its clothes where the gun, sliding back along itself,
    // would meet it.
    private static bool Pocket(Transform body, Vector3 facing, Vector3 pointing, out Vector3 pocket)
    {
        pocket = default;
        var shoulder = Human(body, HumanBodyBones.RightUpperArm);
        var neck = Human(body, HumanBodyBones.Neck);
        if (neck == null) neck = Human(body, HumanBodyBones.UpperChest);
        if (neck == null) neck = Human(body, HumanBodyBones.Chest);
        if (shoulder == null || neck == null) return false;
        Vector3 up = body.up, side = Vector3.Cross(up, facing).normalized, along = pointing.normalized;
        Vector3 joint = shoulder.position;
        float span = Vector3.Distance(joint, neck.position);
        Vector3 point = joint - side * (Vector3.Dot(joint - neck.position, side) * PocketInward) - up * (span * PocketDrop);
        pocket = point + along * (FrontOf(body, point, along, span * PocketRadius, out float front) ? front : span * PocketFallbackDepth);
        return true;
    }

    // How far ahead of point, along forward, the body's outermost surface (skin or clothes) is within radius of that line,
    // from its meshes as posed. The forearms, hands and head are left out: they pass in front of the chest.
    private static bool FrontOf(Transform body, Vector3 point, Vector3 forward, float radius, out float front)
    {
        front = float.NegativeInfinity;
        var skip = new List<Transform>();
        foreach (var bone in new[] { HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.Head })
            if (Human(body, bone) is Transform found) skip.Add(found);
        var vertices = new List<Vector3>();
        bool any = false;
        foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = renderer.sharedMesh;
            if (!renderer.enabled || mesh == null || !mesh.isReadable) continue;
            var bones = renderer.bones;
            var weights = mesh.boneWeights;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                // Baked with the renderer's scale, in its position and rotation.
                var toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 offset = toWorld.MultiplyPoint3x4(vertices[i]) - point;
                    float ahead = Vector3.Dot(offset, forward);
                    if (ahead <= front || (offset - forward * ahead).sqrMagnitude > radius * radius) continue;
                    if (i < weights.Length && Under(bones, weights[i].boneIndex0, skip)) continue;
                    front = ahead;
                    any = true;
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }
        return any;
    }

    // The vertex's main bone (Unity keeps the heaviest first) is one of skip or under one.
    private static bool Under(Transform[] bones, int index, List<Transform> skip)
    {
        if (bones == null || index < 0 || index >= bones.Length || bones[index] == null) return false;
        foreach (var root in skip)
            if (bones[index].IsChildOf(root)) return true;
        return false;
    }

    // Each bake's hold, written to Logs/gun-bake-report.txt (no console): how far each hand is from the gun bone here and on
    // KINEMATION's own arms, and where the gun and hands sit from the eyes (x right, y up, z ahead, m at scale 1).
    private static void Report(GameObject skin, WeaponData weapon, Transform source, Transform sourceWeapon, Transform body,
        Transform bodyWeapon, in GunBakes.Entry entry, float fit, bool trueSize, Vector3 fromCamera, Vector3 facing, string shoulder)
    {
        try
        {
            var right = Human(body, HumanBodyBones.RightHand);
            var left = Human(body, HumanBodyBones.LeftHand);
            var sourceRight = FindLike(source, "hand_r");
            var sourceLeft = FindLike(source, "hand_l");
            string Gap(Transform hand, Transform gun) => hand != null && gun != null ? Vector3.Distance(hand.position, gun.position).ToString("0.000") : "-";
            Vector3 forward = facing, up = body.up, side = Vector3.Cross(up, forward);
            Eyes(body, facing, out Vector3 eyesLocal);
            Vector3 eyes = body.TransformPoint(eyesLocal);
            string FromEyes(Transform point)
            {
                if (point == null) return "-";
                Vector3 d = point.position - eyes;
                return $"({Vector3.Dot(d, side):0.000}, {Vector3.Dot(d, up):0.000}, {Vector3.Dot(d, forward):0.000})";
            }
            string line = $"{System.DateTime.Now:HH:mm:ss} {skin.name} {weapon.name}: right hand to gun {Gap(right, bodyWeapon)} (KINEMATION {Gap(sourceRight, sourceWeapon)}), "
                + $"left {Gap(left, bodyWeapon)} (KINEMATION {Gap(sourceLeft, sourceWeapon)}); from the eyes: gun {FromEyes(bodyWeapon)} right {FromEyes(right)} left {FromEyes(left)}; grip {entry.hasGrip}; "
                + $"fit scale {fit:0.000}, true size {trueSize} (in game every length here x fit scale); "
                + $"KINEMATION's gun from its camera {fromCamera.ToString("F3")} (wanted in game from the eyes, before the stock); {shoulder}\n";
            string path = Path.Combine(Application.dataPath, "..", "Logs", "gun-bake-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, line);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"GunClipRetarget: the bake report could not be written: {error.Message}");
        }
    }

    // The eyes in the body's model space, as PlayerBody finds them: the eye bones when they sit like eyes, else a little
    // above and ahead of the head bone (ahead the way the body faces standing square).
    private static bool Eyes(Transform body, Vector3 facing, out Vector3 eyes)
    {
        eyes = default;
        var head = Human(body, HumanBodyBones.Head);
        if (head == null) return false;
        var left = Human(body, HumanBodyBones.LeftEye);
        var right = Human(body, HumanBodyBones.RightEye);
        Vector3 forward = facing, up = body.up;
        if (left != null && right != null)
        {
            Vector3 middle = (left.position + right.position) * .5f, fromHead = middle - head.position;
            float apart = Vector3.Distance(left.position, right.position);
            float above = Vector3.Dot(fromHead, up), ahead = Vector3.Dot(fromHead, forward);
            if (apart > .015f && apart < .2f && above > -.05f && above < .25f && ahead > .01f && ahead < .25f)
            {
                eyes = body.InverseTransformPoint(middle);
                return true;
            }
        }
        eyes = body.InverseTransformPoint(head.position + up * .08f + forward * .1f);
        return true;
    }

    // Both skeletons must face the same way in their own model space for Retarget Pro's FPS feature to carry the gun
    // across as it is (MakeTarget turns the body for it); a mismatch left is reported, since the guns would sit turned.
    private static void CheckFacing(Transform source, Transform body, GameObject skin)
    {
        Vector3 bodyForward = body.InverseTransformDirection(Forward(body));
        Vector3 sourceForward = source.InverseTransformDirection(SourceForward(source));
        float angle = Vector3.Angle(Vector3.ProjectOnPlane(sourceForward, Vector3.up), Vector3.ProjectOnPlane(bodyForward, Vector3.up));
        if (angle > 10f)
            Debug.LogWarning($"GunClipRetarget: KINEMATION's arms and {skin.name} still face {angle:0}° apart in their own model space; the guns may sit turned.", skin);
    }

    // KINEMATION's arms skeleton faces (UE names), from its shoulders.
    private static Vector3 SourceForward(Transform source)
    {
        var left = FindLike(source, "upperarm_l");
        var right = FindLike(source, "upperarm_r");
        if (left == null || right == null) return source.forward;
        Vector3 forward = Vector3.Cross(right.position - left.position, source.up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : source.forward;
    }

    // The way KINEMATION's arms face in their own model space, as Retarget Pro lays them out (root turned straight).
    private static float ArmsFacing(GameObject arms)
    {
        var copy = Object.Instantiate(arms);
        try
        {
            ResetRootRotation(copy.transform);
            return Yaw(copy.transform.InverseTransformDirection(SourceForward(copy.transform)));
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    // Retarget Pro's preview turns a model's root straight and keeps its children where they were.
    private static void ResetRootRotation(Transform root)
    {
        if (Quaternion.Angle(root.rotation, Quaternion.identity) <= .0001f) return;
        var children = new List<(Transform child, Vector3 position, Quaternion rotation)>();
        foreach (Transform child in root) children.Add((child, child.position, child.rotation));
        root.rotation = Quaternion.identity;
        foreach (var (child, position, rotation) in children) child.SetPositionAndRotation(position, rotation);
    }

    private static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

    private static Vector3 Forward(Transform body)
    {
        var left = Human(body, HumanBodyBones.LeftUpperArm);
        var right = Human(body, HumanBodyBones.RightUpperArm);
        if (left == null || right == null) return body.forward;
        Vector3 forward = Vector3.Cross(right.position - left.position, body.up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : body.forward;
    }

    private static Transform Human(Transform body, HumanBodyBones bone) => PreviewHumanoid.Bone(body, bone);

    private static Transform Find(Transform root, string name) => PreviewHumanoid.Find(root, name);

    private static Transform FindLike(Transform root, string name)
    {
        if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase)) return root;
        foreach (Transform child in root)
            if (FindLike(child, name) is Transform found) return found;
        return null;
    }

    private static string FileName(string name)
    {
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name.Replace(' ', '_');
    }
}
