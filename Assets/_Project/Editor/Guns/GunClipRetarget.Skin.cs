using System.Collections.Generic;
using System.IO;
using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using KINEMATION.RetargetPro.Editor.Scripts.Mapping;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features.FPSRetargeting;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// GunClipRetarget: One skin. One part of the class; the rest is in GunClipRetarget.cs and the files beside it.
internal static partial class GunClipRetarget
{
    // Each of KINEMATION's skeletons the guns' clips are made for (the FPS Animation Pack's arms, the Tactical Shooter Pack's
    // Operator) gets its own Retarget Pro profile onto the skin. A skeleton that can't be baked keeps its guns' older bakes.
    private static List<GunBakes.Entry> Bake(GameObject skin, FPSBodyAssets assets, GunClipFingers fingers, List<GunBakes.Entry> previous,
        bool all)
    {
        string dir = $"{Folder}/{FileName(skin.name)}";
        Directory.CreateDirectory(dir);
        AssetDatabase.Refresh();

        var sources = new List<GameObject>();
        foreach (var gun in assets.guns)
        {
            var arms = gun.ArmsOr(assets.armsModel);
            if (gun.weapon != null && gun.sourcePose != null && arms != null && !sources.Contains(arms)) sources.Add(arms);
        }
        var made = new List<GunBakes.Entry>();
        foreach (var arms in sources)
        {
            var group = BakeFrom(skin, arms, assets, fingers, dir, previous, all);
            if (group != null) { made.AddRange(group); continue; }
            // Its guns keep their older bakes, noted with the sources tried, so they wait for a change (GunUpToDate).
            foreach (var gun in assets.guns)
                if (gun.weapon != null && gun.sourcePose != null && gun.ArmsOr(assets.armsModel) == arms)
                {
                    string tried = SourceOf(skin, assets, gun);
                    int old = previous.FindIndex(entry => entry.skin == skin && entry.weapon == gun.weapon);
                    var kept = old >= 0 ? previous[old] : new GunBakes.Entry { skin = skin, weapon = gun.weapon, source = tried };
                    kept.failedSource = tried;
                    made.Add(kept);
                }
        }
        return made.Count > 0 ? made : null;
    }

    // The first camera rig given for a skeleton's guns (FPSBodyAssets.Gun.sourceCameraRig); null: the arms' CameraSocket.
    private static GameObject CameraRigOf(FPSBodyAssets assets, GameObject arms)
    {
        foreach (var gun in assets.guns)
            if (gun.sourceCameraRig != null && gun.ArmsOr(assets.armsModel) == arms) return gun.sourceCameraRig;
        return null;
    }

    private static List<GunBakes.Entry> BakeFrom(GameObject skin, GameObject arms, FPSBodyAssets assets, GunClipFingers fingers, string dir,
        List<GunBakes.Entry> previous, bool all)
    {
        // The FPS Animation Pack's arms keep the file names the bakes always had.
        string targetName = arms == assets.armsModel ? FileName(skin.name) : $"{FileName(skin.name)}_{FileName(arms.name)}";
        var target = MakeTarget(skin, $"{dir}/{targetName}_RetargetTarget.prefab", ArmsFacing(arms));
        var profile = MakeProfile(arms, target, dir, out var stance, out var fps);
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
            var sourceWeapon = Hierarchy.Find(source, WeaponBone);
            var cameraRig = CameraRigOf(assets, arms);
            var sourceCamera = cameraRig != null ? AddCameraFrom(source, cameraRig) : AddCameraSocket(source);
            var bodyWeapon = Hierarchy.Find(body, WeaponBone);
            if (sourceWeapon == null || bodyWeapon == null)
            {
                Debug.LogWarning($"GunClipRetarget: {arms.name} or {target.name} has no {WeaponBone}; the guns can't be placed.", profile);
                return null;
            }
            CheckFacing(source, body, skin);
            if (Mathf.Abs(Mathf.DeltaAngle(MovementFixtures.YawOf(source.InverseTransformDirection(SourceForward(source))), 0f)) > 10f)
                Debug.LogWarning($"GunClipRetarget: {arms.name} does not face +Z in its own model space; the guns may sit turned on the bodies.", arms);

            var made = new List<GunBakes.Entry>();
            foreach (var gun in assets.guns)
            {
                if (gun.weapon == null || gun.sourcePose == null || gun.ArmsOr(assets.armsModel) != arms) continue;
                // Only a gun whose bake isn't current is made again (all: every one), so a new gun or a changed clip
                // doesn't remake the rest.
                if (!all && GunUpToDate(previous, skin, assets, gun, out var current)) { made.Add(current); continue; }
                // Where the body faces: the way its hips face, as the camera looks. KINEMATION's Tactical Shooter Pack
                // stands with the shoulders turned 14° to 47° off the hips (the Herrington most), and framed from the
                // shoulders the guns sat turned that far to the left (user, 2026-11-09: the Herrington's arms off to
                // the side). Our stance turns the shoulders, not the hips.
                stance.blade = 0f;
                fps.weaponOffset = Vector3.zero;
                baker.RetargetAtTime(gun.sourcePose, null, 0f);
                Vector3 facing = HipsForward(body);
                bool hasStock = MeasureStock(gun.prefab, out var stock);

                // The gun where KINEMATION's camera sees it (its CameraSocket, in the camera's own axes), seen the same way
                // from this body's eyes looking ahead, in true metres in game (the bake is at 1/fit of the size the body
                // plays at). Placed by measuring where Retarget Pro put it and moving it the rest of the way, twice. A gun
                // with a stock then goes back along itself until its butt rests in the shoulder pocket, twice again, as the
                // arms reach for it anew.
                Vector3 fromCamera = Vector3.zero;
                // Whether the butt went to the shoulder: the gun reaches back past the wrist (ToShoulder), not only that
                // its shape could be measured (hasStock), which a pistol's can too.
                bool shouldered = false;
                Placement Place(float blade, bool pullBack)
                {
                    shouldered = false;
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
                        shouldered = true;
                        fps.weaponOffset += move;
                        baker.RetargetAtTime(gun.sourcePose, null, 0f);
                    }
                    // The hands stay where Retarget Pro puts them (V10); in play they rest on the gun's real surface
                    // (PlayerBodyHands.GunFingers). Moving them by the knuckles here put the support hand off the gun.
                    // A gun the hands can't reach shouldered, even with the chest turned, stays shouldered (17): brought
                    // back toward KINEMATION's camera framing it rode at the cheek. Play brings it within each body's own
                    // reach, the gun toward the shoulders and the support hand further back on a bare handguard
                    // (FPSBody.FitToReach), so the pullBack asked for here no longer moves it.
                    return Measure(body, bodyWeapon, source, sourceWeapon, fit);
                }

                // Shouldered, the chest turns toward the gun side only as far as the hands need to reach their places on
                // the gun with the support arm still bent (ShootingStanceFeature); if they still can't, the gun comes back
                // toward the body until they do. A gun without a stock is held square.
                float bladed = 0f;
                var placed = Place(0f, false);
                if (shouldered && !placed.Reaches)
                {
                    var best = placed;
                    for (float blade = StanceStep; blade <= StanceMaxBlade + .001f; blade += StanceStep)
                    {
                        var trial = Place(blade, false);
                        if (trial.Reaches || trial.miss < best.miss - .001f) { best = trial; bladed = blade; }
                        if (trial.Reaches) break;
                    }
                    placed = Place(bladed, true);
                }
                EditorUtility.SetDirty(fps);
                EditorUtility.SetDirty(stance);

                string shoulder = !hasStock ? "stock not measured"
                    : ToShoulder(body, bodyWeapon, stock, fit, facing, out Vector3 rest, out float reach, out _)
                        ? $"butt in the shoulder pocket, {rest.magnitude * fit:0.000} m off (reaches {reach:0.000} m behind the wrist)"
                        : $"no stock (back end {reach:0.000} m behind the wrist)";
                shoulder += shouldered
                    ? $"; chest turned {bladed:0.#}°, hands {placed.miss:0.000} m off KINEMATION's hold, support arm {placed.extension:0.00} straight"
                        + " (play fits the hold to each body's arms: FPSBody.FitToReach)"
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
                // Where the gun sits from the eyes (metres in game, in the eyes' frame facing the way the hips do), so any
                // other humanoid has it put the same way from its own eyes at load (FPSBody.FittedGunPose).
                if (EyeFrame(body, out Vector3 eyesAt, out Quaternion eyeFrame))
                {
                    entry.gunFromEyes = Quaternion.Inverse(eyeFrame) * (entry.gunPosition - eyesAt) * fit;
                    entry.gunTurnFromEyes = Quaternion.Inverse(eyeFrame) * entry.gunRotation;
                    entry.hasEyeFrame = true;
                }
                Report(skin, gun.weapon, source, sourceWeapon, body, bodyWeapon, entry, fit, trueSize, fromCamera, facing, shoulder);

                // Each clip's fingers are KINEMATION's own, through Unity's humanoid muscles (GunClipFingers).
                string name = FileName(gun.weapon.name);
                bool failed = false;
                AnimationClip Baked(AnimationClip sourceClip, string path)
                {
                    var clip = fingers.Apply(sourceClip, BakeClip(baker, sourceClip, path));
                    if (sourceClip != null && clip == null) failed = true;
                    return clip;
                }
                entry.pose = Baked(gun.sourcePose, $"{dir}/{name}_Pose.anim");
                entry.reloadTactical = Baked(gun.sourceReloadTactical, $"{dir}/{name}_ReloadTactical.anim");
                entry.reloadEmpty = Baked(gun.sourceReloadEmpty, $"{dir}/{name}_ReloadEmpty.anim");
                entry.reloadShell = Baked(gun.sourceReloadShell, $"{dir}/{name}_ReloadShell.anim");
                entry.reloadEnd = Baked(gun.sourceReloadEnd, $"{dir}/{name}_ReloadEnd.anim");
                entry.draw = Baked(gun.sourceDraw, $"{dir}/{name}_Draw.anim");
                entry.holster = Baked(gun.sourceHolster, $"{dir}/{name}_Holster.anim");
                entry.inspect = Baked(gun.sourceInspect, $"{dir}/{name}_Inspect.anim");
                // The bolt or pump worked after every shot, as the reloads are (FPSGun.Cycle).
                entry.fire = Baked(gun.sourceFire, $"{dir}/{name}_Fire.anim");
                // A clip that couldn't be made (BakeClip said which) is noted with the sources it was tried from, so it isn't
                // tried again at every script reload (GunUpToDate); without its pose the gun keeps its last good bake, if any.
                if (failed)
                {
                    entry.failedSource = entry.source;
                    int old = previous.FindIndex(e => e.skin == skin && e.weapon == gun.weapon && e.pose != null);
                    if (entry.pose == null && old >= 0)
                    {
                        var lastGood = previous[old];
                        lastGood.failedSource = entry.source;
                        entry = lastGood;
                    }
                    Debug.LogWarning($"GunClipRetarget: {gun.weapon.name}'s clips couldn't all be remade for {skin.name}; it is tried again when "
                        + "its clips or the body change, or from ENFILADE → 총 → KINEMATION 총 클립 다시 굽기.", gun.weapon);
                }
                if (entry.pose != null || failed) made.Add(entry);
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
            float turn = Mathf.DeltaAngle(MovementFixtures.YawOf(instance.transform.InverseTransformDirection(Forward(instance.transform))), armsYaw);
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
        var lasting = Lasting(source);
        AnimationClip baked;
        try
        {
            baked = baker.BakeAnimation(lasting);
        }
        finally
        {
            if (lasting != source) Object.DestroyImmediate(lasting);
        }
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

    // Retarget Pro bakes a clip over its length through a temporary FBX, so a clip of one pose (no length: the FPS Animation
    // Pack's A_FP_PDW90_Idle keys every bone at 0 only, start and stop 0) made an FBX of no frames, which Unity can't import as
    // humanoid: the PDW90 never baked (the user's console, 2026-11-13). Such a clip goes in as a copy holding the same pose
    // from 0 to its first frame, any pack's: the same name, frame rate and curves, each at its value at 0.
    private static AnimationClip Lasting(AnimationClip source)
    {
        float frame = 1f / Mathf.Max(1f, source.frameRate);
        if (source.length >= frame * .5f) return source;
        var copy = new AnimationClip { name = source.name, frameRate = source.frameRate, wrapMode = source.wrapMode };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            if (curve == null) continue;
            float value = curve.Evaluate(0f);
            AnimationUtility.SetEditorCurve(copy, binding, new AnimationCurve(new Keyframe(0f, value), new Keyframe(frame, value)));
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            var keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            if (keys == null || keys.Length == 0) continue;
            AnimationUtility.SetObjectReferenceCurve(copy, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = keys[0].value },
                new ObjectReferenceKeyframe { time = frame, value = keys[0].value },
            });
        }
        return copy;
    }
}
