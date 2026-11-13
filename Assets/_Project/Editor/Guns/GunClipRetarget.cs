using System;
using System.Collections.Generic;
using System.IO;
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
internal static partial class GunClipRetarget
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

    // ---- Measures on the posed preview bodies ----

    // Where both hands sit on the gun in the pose, in the gun bone's axes, as Retarget Pro's FPS feature put them there.
    // FPSBody's probe compares the hands in play with it.
    private static void MeasureGrip(Transform body, Transform weapon, ref GunBakes.Entry entry)
    {
        var right = PreviewHumanoid.Bone(body, HumanBodyBones.RightHand);
        var left = PreviewHumanoid.Bone(body, HumanBodyBones.LeftHand);
        float scale = Mathf.Max(1e-6f, Mathf.Abs(body.lossyScale.x));
        Quaternion toGun = Quaternion.Inverse(weapon.rotation);
        if (right != null) entry.rightGripPosition = toGun * (right.position - weapon.position) / scale;
        if (left != null) entry.leftGripPosition = toGun * (left.position - weapon.position) / scale;
        entry.hasGrip = right != null && left != null;
    }
}
