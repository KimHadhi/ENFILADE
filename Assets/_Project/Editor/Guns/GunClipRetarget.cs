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
///   from this body's eyes looking the way its hips face, since our camera rides the eyes;
/// - a gun with a stock then moves until the stock's butt rests in this body's right shoulder pocket, on the front of the
///   body or its clothes: the first-person view is the body's own hold, so the third-person view shows the same (True FPS);
///   the chest turns toward the gun side, as a rifleman stands, as far as the support hand needs to reach its place on the
///   gun (ShootingStanceFeature);
/// - the fingers are KINEMATION's own, read through Unity's humanoid muscles on KINEMATION's humanoid mannequin
///   (GunClipFingers), since Retarget Pro carried them across flat or spread past a hand's reach;
/// - the pose and both reloads are baked as humanoid clips (a shell reload's shell and end, the draw, holster and inspect, and
///   a bolt or pump worked after every shot too, where the gun has them), and where the gun sits in the pose is kept for FPS
///   Animation Framework's Pose Sampler (FPSBody).
/// It runs by itself after scripts load when a skin or a clip has no bake yet or changed, and from
/// ENFILADE → 총 → KINEMATION 총 클립 다시 굽기 to make them all again.
/// </summary>
[InitializeOnLoad]
internal static class GunClipRetarget
{
    private const string AssetsPath = ProjectFolders.DataByName + FPSBodyAssets.ResourcePath + ".asset";
    private const string BakesPath = ProjectFolders.DataByName + GunBakes.ResourcePath + ".asset";
    private const string Folder = ProjectFolders.Guns + "GunBakes";
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
    // 14: one reference body, with where the gun sits from its eyes (gunFromEyes) for fitting any other body at load.
    // 15: framed from the way the hips face, not the shoulders (HipsForward), and a gun's back end measured without the
    // parts it parks away from itself (GunMuzzle.NearSightLine): the R08 went over the head, the Herrington off to the side.
    // 16: the hands' reach measured as their real miss from KINEMATION's hold, and a gun the hands can't reach shouldered
    // brought back toward the body until they do (the Herrington sat at arm's length in front of the face).
    // 17: a stocked gun stays shouldered even where the hands can't reach it so: play fits the hold to each body's arms
    // (FPSBody.FitToReach). Brought back toward KINEMATION's camera framing (16), the Herrington rode at the cheek, its gun
    // 3 cm under the eyes where the other long guns sit 7 to 15, its trigger hand by the face (the user, 2026-11-12: "헤링턴은
    // 여전히 높게 잡고 있어"): its pump is 0.6 m from its butt, out of the reference body's reach (0.49 m arm) shouldered.
    private const int Version = 17;

    // The shooting stance: the chest turns in steps of this (degrees) up to the most, until the support hand comes within
    // the slack of KINEMATION's hold on the gun (m in game) with its arm no straighter than the share (shoulder to wrist
    // over the arm's length).
    private const float StanceStep = 2.5f, StanceMaxBlade = 40f, StanceReachSlack = .01f, StanceArmStraight = .97f;

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

    [MenuItem("ENFILADE/총/KINEMATION 총 클립 다시 굽기")]
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
        // One reference body only: the default skin's. Every other body, a mod's character included, plays the same humanoid
        // clips and has the gun fitted to its own eyes at load (FPSBody.FittedGunPose, from the bake's gun-from-eyes). Baking
        // every skin took far too long, and a character added later by a mod could never have been baked (user, 2026-11-09).
        var skins = new List<GameObject>();
        if (profile.ModelOf(null) is GameObject reference) skins.Add(reference);
        else
            foreach (var skin in profile.Skins)
                if (profile.ModelOf(skin) is GameObject model) { skins.Add(model); break; }

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
                    var made = Bake(skin, assets, fingers, entries, all);
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

    // A gun with a shell reload or a camera rig of its own adds those, so the guns made before them keep their bakes.
    private static string SourceOf(GameObject skin, FPSBodyAssets assets, FPSBodyAssets.Gun gun)
    {
        string source = $"{Version}|{Guid(skin)}|{Guid(gun.ArmsOr(assets.armsModel))}|{Guid(gun.sourcePose)}|{Guid(gun.sourceReloadTactical)}|{Guid(gun.sourceReloadEmpty)}";
        if (gun.sourceReloadShell != null || gun.sourceReloadEnd != null || gun.sourceCameraRig != null)
            source += $"|{Guid(gun.sourceReloadShell)}|{Guid(gun.sourceReloadEnd)}|{Guid(gun.sourceCameraRig)}";
        if (gun.sourceDraw != null || gun.sourceHolster != null || gun.sourceInspect != null)
            source += $"|{Guid(gun.sourceDraw)}|{Guid(gun.sourceHolster)}|{Guid(gun.sourceInspect)}";
        // A gun worked by hand after every shot (a bolt, a pump) adds that clip, so the others keep their bakes.
        if (gun.sourceFire != null) source += $"|fire {Guid(gun.sourceFire)}";
        return source;
    }

    private static string Guid(Object asset) => asset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)) : "-";

    private static bool UpToDate(List<GunBakes.Entry> entries, GameObject skin, FPSBodyAssets assets)
    {
        foreach (var gun in assets.guns)
        {
            if (gun.weapon == null || gun.sourcePose == null) continue;
            if (!GunUpToDate(entries, skin, assets, gun, out _)) return false;
        }
        return true;
    }

    // A gun's bake is current: made from its clips, arms and body as they are now, with every clip it has made. Or it was
    // tried from them and failed (failedSource), and isn't tried again until one of them changes: a clip Retarget Pro can't
    // make once it can't make at every script reload, and the whole skin was remade each time (the user's console filled with
    // the PDW90 idle's failure, 2026-11-13).
    private static bool GunUpToDate(List<GunBakes.Entry> entries, GameObject skin, FPSBodyAssets assets, FPSBodyAssets.Gun gun,
        out GunBakes.Entry entry)
    {
        entry = default;
        int match = entries.FindIndex(e => e.skin == skin && e.weapon == gun.weapon);
        if (match < 0) return false;
        entry = entries[match];
        string source = SourceOf(skin, assets, gun);
        if (!string.IsNullOrEmpty(entry.failedSource) && entry.failedSource == source) return true;
        return entry.source == source && entry.pose != null
            && !(gun.sourceReloadTactical != null && entry.reloadTactical == null)
            && !(gun.sourceReloadEmpty != null && entry.reloadEmpty == null)
            && !(gun.sourceReloadShell != null && entry.reloadShell == null)
            && !(gun.sourceReloadEnd != null && entry.reloadEnd == null)
            && !(gun.sourceDraw != null && entry.draw == null)
            && !(gun.sourceHolster != null && entry.holster == null)
            && !(gun.sourceInspect != null && entry.inspect == null)
            && !(gun.sourceFire != null && entry.fire == null);
    }

    // ---- One skin ----

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
            var sourceWeapon = Find(source, WeaponBone);
            var cameraRig = CameraRigOf(assets, arms);
            var sourceCamera = cameraRig != null ? AddCameraFrom(source, cameraRig) : AddCameraSocket(source);
            var bodyWeapon = Find(body, WeaponBone);
            if (sourceWeapon == null || bodyWeapon == null)
            {
                Debug.LogWarning($"GunClipRetarget: {arms.name} or {target.name} has no {WeaponBone}; the guns can't be placed.", profile);
                return null;
            }
            CheckFacing(source, body, skin);
            if (Mathf.Abs(Mathf.DeltaAngle(Yaw(source.InverseTransformDirection(SourceForward(source))), 0f)) > 10f)
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

    // Another pack's camera point: the Camera on its rig (the Tactical Shooter Pack's FPCamera on its character's head),
    // copied onto the source skeleton under the bone of the same name, with the same local pose, as AddCameraSocket does.
    private static Transform AddCameraFrom(Transform source, GameObject rig)
    {
        var camera = rig.GetComponentInChildren<Camera>(true);
        var parent = camera != null && camera.transform.parent != null ? FindLike(source, camera.transform.parent.name) : null;
        if (parent == null)
        {
            Debug.LogWarning($"GunClipRetarget: {rig.name} has no camera on a bone of {source.name}; its guns are placed where Retarget Pro puts them.", rig);
            return null;
        }
        var made = new GameObject(CameraSocketName) { hideFlags = HideFlags.DontSave }.transform;
        made.SetParent(parent, false);
        made.SetLocalPositionAndRotation(camera.transform.localPosition, camera.transform.localRotation);
        return made;
    }

    // ---- True size ----

    // The scale the game fits this skin to (PlayerBody.FitScaleOf, eyes to the standing camera height of Player.prefab).
    internal static float FitScaleOf(GameObject skin)
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

    // How the hands hold the gun as the body is placed: how far the farther wrist is from where KINEMATION's hold puts it on
    // the gun (m in game; Retarget Pro's FPS feature leaves a hand that can't reach short of its place), and how straight the
    // support arm is. Measured against the hold itself, not the gun bone: a gun whose bone is ahead of the support hand
    // (the Herrington's, at its pump) read as reaching when its hands fell short.
    private struct Placement
    {
        public float miss, extension;
        public bool Reaches => miss <= StanceReachSlack && extension <= StanceArmStraight;
    }

    private static Placement Measure(Transform body, Transform weapon, Transform source, Transform sourceWeapon, float fit)
    {
        var shoulder = Human(body, HumanBodyBones.LeftUpperArm);
        var elbow = Human(body, HumanBodyBones.LeftLowerArm);
        var wrist = Human(body, HumanBodyBones.LeftHand);
        if (shoulder == null || elbow == null || wrist == null) return new Placement { miss = 0f, extension = 0f };
        float arm = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
        return new Placement
        {
            miss = Mathf.Max(Miss(wrist, FindLike(source, "hand_l")), Miss(Human(body, HumanBodyBones.RightHand), FindLike(source, "hand_r"))),
            extension = arm > 1e-5f ? Vector3.Distance(shoulder.position, wrist.position) / arm : 0f
        };

        // Where the FPS feature asks this wrist to be: KINEMATION's hand on its gun, carried onto ours at the bake's size.
        float Miss(Transform hand, Transform sourceHand)
        {
            if (hand == null || sourceHand == null) return 0f;
            Vector3 wanted = weapon.position + weapon.rotation * (Quaternion.Inverse(sourceWeapon.rotation) * (sourceHand.position - sourceWeapon.position))
                / Mathf.Max(.01f, fit);
            return Vector3.Distance(hand.position, wanted) * fit;
        }
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

    // The eyes in the body's model space and the frame they look out of: ahead the way the hips face (the shooting stance
    // turns the shoulders, not the hips), up the model's up. FPSBody.EyeFrame measures any body the same way at load.
    private static bool EyeFrame(Transform body, out Vector3 eyes, out Quaternion frame)
    {
        eyes = default;
        frame = Quaternion.identity;
        var left = Human(body, HumanBodyBones.LeftUpperLeg);
        var right = Human(body, HumanBodyBones.RightUpperLeg);
        if (left == null || right == null) return false;
        Vector3 ahead = Vector3.Cross(body.InverseTransformPoint(right.position) - body.InverseTransformPoint(left.position), Vector3.up);
        if (ahead.sqrMagnitude < 1e-8f) return false;
        frame = Quaternion.LookRotation(ahead.normalized, Vector3.up);
        return Eyes(body, body.TransformDirection(ahead.normalized), out eyes);
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

    // The way the hips face (from the thighs, as EyeFrame takes it); without both thighs, the shoulders' way.
    private static Vector3 HipsForward(Transform body)
    {
        var left = Human(body, HumanBodyBones.LeftUpperLeg);
        var right = Human(body, HumanBodyBones.RightUpperLeg);
        if (left == null || right == null) return Forward(body);
        Vector3 forward = Vector3.Cross(right.position - left.position, body.up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : Forward(body);
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
