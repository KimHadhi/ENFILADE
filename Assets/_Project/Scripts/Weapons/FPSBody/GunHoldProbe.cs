#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's check of how every character holds every gun, without playing: each character's model is stood in each gun's
/// pose clip at the size the game draws it, and its arms are measured against the gun's hold exactly as the game does when it
/// builds the gun for a body (FPSBody.HoldPlan, the same code), at the hip and with the sight on the eyes. One line per
/// character and gun goes to Logs/gun-hold-probe.txt: how straight each arm would be with no fit, what the fit asks for and
/// how straight the arms are after.
/// So a hold that breaks on one character shows up as a number before anyone plays it, a mod's character too, and a change
/// to the rule can be read on every body at once (the user, 2026-11-14: "캐릭터마다 총 드는 게 달라서 고장나는데 이거 근본적인
/// 원인 고쳐 주세요": the causes were found by measuring every body here first).
/// ENFILADE → 총 → 쥐기 점검 runs it; so does making the file Temp/enfilade-holdprobe.request (a tool outside Unity asking,
/// as RefreshOnRequest is asked). It lives beside FPSBody because it measures with FPSBody's own rules.
/// </summary>
[InitializeOnLoad]
internal static class GunHoldProbe
{
    private const string Request = "Temp/enfilade-holdprobe.request", ReportPath = "Logs/gun-hold-probe.txt";
    // The first guns of the list (a rifle, a pistol, a shotgun, ...) also get the layout line (Frames).
    private const int FramedGuns = 6;
    private static double next;

    static GunHoldProbe() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            File.Delete(Request);
        }
        catch (System.Exception error) when (error is IOException || error is System.UnauthorizedAccessException)
        {
            return;
        }
        Run();
    }

    // What KINEMATION's own arms and the gun itself say about a gun's hold, the same for every body.
    private struct Source
    {
        public bool Valid, HasAim, Pistol;
        public Quaternion Frame;
        public FPSBody.SourceHold Hands;
        // The gun from the eyes with its sight on them (FPSGun.AimedFromSight), in the gun's own frame.
        public Vector3 AimedAt;
        public Quaternion AimedTurn;
    }

    [MenuItem("ENFILADE/총/쥐기 점검 (모든 캐릭터)")]
    private static void Run()
    {
        var profile = PlayerBodyProfile.Load();
        var assets = FPSBodyAssets.Instance;
        var bakes = GunBakes.Instance;
        if (profile == null || assets == null || bakes == null)
        {
            Debug.LogWarning("GunHoldProbe: the body profile, the gun list or the gun bakes are missing; nothing to check.");
            return;
        }
        // Measured afresh: outside play nothing else forgets the reference body's plans, and the table may have changed.
        FPSBody.ForgetSharedPlans();
        var limits = FPSBody.TableLimits();
        var text = new StringBuilder();
        text.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} gun hold check. Lengths in cm in game; places from the eyes (x right, y up, z ahead). "
            + "straight = shoulder joint to wrist over the arm's length (1 = locked straight; a shouldered rifle's trigger arm is about 0.5). "
            + $"Limits: most {limits.Most:0.00}, least {limits.Least:0.00}, pull {limits.PullMost * 100f:0.#}, push {limits.PushMost * 100f:0.#}, "
            + $"slide {limits.SlideMost * 100f:0.#}. 'shared' = the reference body's plan, which every character takes whole; "
            + "'own' = what a body's own arms would ask for alone.");
        var armsByModel = new Dictionary<GameObject, GunArms>();
        var sources = new Source[assets.guns.Length];
        try
        {
            for (int i = 0; i < assets.guns.Length; i++) sources[i] = SourceOf(assets, assets.guns[i], armsByModel);
            float camera = CameraHeight();
            foreach (var skin in profile.Skins)
            {
                var model = skin != null ? profile.ModelOf(skin) : null;
                if (model == null) continue;
                Check(skin.Id, model, camera, assets, bakes, sources, armsByModel, limits, text);
            }
        }
        finally
        {
            foreach (var arms in armsByModel.Values) arms?.ReleaseNow();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, text.ToString());
        Debug.Log($"GunHoldProbe: every character's hold of every gun is in {ReportPath}.");
    }

    // The standing camera height the game sizes every body to (Player.prefab's CrouchPivot, as GunClipRetarget takes it).
    private static float CameraHeight()
    {
        var player = Resources.Load<GameObject>("Characters/Player");
        var pivot = player != null ? player.transform.Find("CrouchPivot") : null;
        return pivot != null ? pivot.localPosition.y : 1.6f;
    }

    private static Source SourceOf(FPSBodyAssets assets, in FPSBodyAssets.Gun gun, Dictionary<GameObject, GunArms> armsByModel)
    {
        var source = default(Source);
        if (gun.weapon == null || gun.prefab == null) return source;
        var kinemation = gun.prefab.GetComponent<CasFPSWeapon>();
        source.Frame = FPSBody.GunFrameOf(kinemation);
        source.Pistol = FPSBody.IsPistol(gun.weapon);
        if (!FPSBody.SourceHands(assets, gun, source.Frame, armsByModel, out source.Hands)) return source;
        // The sight the gun starts with, else KINEMATION's own aim point.
        var sights = gun.prefab.GetComponent<GunSights>();
        Transform aim = sights != null && sights.Count > 0 ? sights.sights[Mathf.Clamp(sights.initial, 0, sights.Count - 1)].aimPoint : null;
        if (aim == null && kinemation != null) aim = kinemation.aimPoint;
        if (aim != null)
        {
            var root = gun.prefab.transform;
            Quaternion toGun = Quaternion.Inverse(root.rotation);
            source.AimedTurn = Quaternion.Inverse(toGun * aim.rotation);
            source.AimedAt = -(source.AimedTurn * (toGun * (aim.position - root.position)));
            source.HasAim = true;
        }
        source.Valid = true;
        return source;
    }

    private static void Check(string id, GameObject model, float camera, FPSBodyAssets assets, GunBakes bakes, Source[] sources,
        Dictionary<GameObject, GunArms> armsByModel, in HoldReach.Limits limits, StringBuilder text)
    {
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                text.AppendLine($"{id}: no humanoid avatar, not checked.");
                return;
            }
            // As a player's body plays (PlayerBody.Build): with root motion on, a sampled clip turns the model's root and lays
            // the pose out the other way under it, which a gun placed in the model's space doesn't follow.
            animator.applyRootMotion = false;
            instance.transform.localScale = Vector3.one * PlayerBody.DrawnScaleOf(animator, instance.transform, camera);
            float scale = Mathf.Max(1e-4f, Mathf.Abs(animator.transform.lossyScale.x));
            bool first = true;
            for (int i = 0; i < assets.guns.Length; i++)
            {
                var gun = assets.guns[i];
                var source = sources[i];
                if (!source.Valid || !bakes.TryGet(model, gun.weapon, out var bake, out bool exact) || bake.pose == null) continue;
                if (!FPSBody.PoseArms(animator, bake.pose, out var right, out var left)
                    || !FPSBody.EyeFrame(animator, bake.pose, out Vector3 eyes, out Quaternion frame)) continue;
                if (first)
                {
                    first = false;
                    Vector3 r = Quaternion.Inverse(frame) * (right.Shoulder - eyes) * scale * 100f, l = Quaternion.Inverse(frame) * (left.Shoulder - eyes) * scale * 100f;
                    text.AppendLine();
                    text.AppendLine($"== {id} ({model.name}{(exact ? ", the reference body" : "")}): drawn x{scale:0.000}; arms right {right.Length * scale * 100f:0.0} "
                        + $"left {left.Length * scale * 100f:0.0}; hand (wrist to middle knuckle) {right.Run * scale * 100f:0.0}; in {gun.weapon.name}'s pose "
                        + $"the shoulder joints sit right ({r.x:0.0}, {r.y:0.0}, {r.z:0.0}) left ({l.x:0.0}, {l.y:0.0}, {l.z:0.0})");
                }
                var start = exact || !bake.hasEyeFrame ? new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one)
                    : new KTransform(eyes + frame * (bake.gunFromEyes / scale), frame * bake.gunTurnFromEyes, Vector3.one);
                if (i < FramedGuns) Frames(gun.weapon.name, animator, bake, source, eyes, frame, scale, start, text);
                FPSBody.Plan shared = default;
                bool hasShared = !exact && FPSBody.SharedPlan(gun.weapon, assets, bakes, camera, armsByModel, out shared);
                Line(gun.weapon.name, source, right, left, eyes, frame, scale, start, limits, shared, hasShared, text);
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // How the gun's pose clip lays this body out in its own model space, and where the gun's two possible places fall in it:
    // the yaw of the hips, the chest and the head off the model's forward (degrees, right positive), the eyes, the hands as
    // the clip alone leaves them, and each place for the gun (the bake's own in the model's space; the one from the eyes in
    // the hips' frame) with how far the clip's own wrists are from the wrists that place asks for. The place the clip was
    // made with has the hands on the gun.
    private static void Frames(string gun, Animator animator, in GunBakes.Entry bake, in Source source, Vector3 eyes, Quaternion frame,
        float scale, KTransform start, StringBuilder text)
    {
        var root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var saved = new (Vector3 position, Quaternion rotation)[bones.Length];
        for (int i = 0; i < bones.Length; i++) saved[i] = (bones[i].localPosition, bones[i].localRotation);
        Vector3 rootWas = root.position;
        Quaternion rootTurnWas = root.rotation;
        try
        {
            ClipSampler.Sample(bake.pose, animator.gameObject, 0f);
            Vector3 In(HumanBodyBones bone) => animator.GetBoneTransform(bone) is Transform found ? root.InverseTransformPoint(found.position) : Vector3.zero;
            float Yaw(Vector3 across) => Mathf.Atan2(Vector3.Cross(across, Vector3.up).x, Vector3.Cross(across, Vector3.up).z) * Mathf.Rad2Deg;
            float hips = Yaw(In(HumanBodyBones.RightUpperLeg) - In(HumanBodyBones.LeftUpperLeg));
            float chest = Yaw(In(HumanBodyBones.RightUpperArm) - In(HumanBodyBones.LeftUpperArm));
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 headWay = head != null ? root.InverseTransformDirection(head.forward) : Vector3.forward;
            Vector3 rightHand = In(HumanBodyBones.RightHand), leftHand = In(HumanBodyBones.LeftHand);
            string Cm(Vector3 point) => $"({point.x * scale * 100f:0.0}, {point.y * scale * 100f:0.0}, {point.z * scale * 100f:0.0})";
            text.Append($"   {gun} laid out in the model's space: root moved {Vector3.Distance(root.position, rootWas) * 100f:0.0} cm turned "
                + $"{Quaternion.Angle(root.rotation, rootTurnWas):0.0}°; hips face {hips:0.0}°, chest {chest:0.0}°, head {Mathf.Atan2(headWay.x, headWay.z) * Mathf.Rad2Deg:0.0}°; "
                + $"eyes {Cm(eyes)}, clip's hands R {Cm(rightHand - eyes)} L {Cm(leftHand - eyes)} from the eyes");
            foreach (bool own in new[] { true, false })
            {
                var place = own ? new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one)
                    : new KTransform(eyes + frame * (bake.gunFromEyes / scale), frame * bake.gunTurnFromEyes, Vector3.one);
                Quaternion axes = place.rotation * source.Frame;
                // KINEMATION's wrists on the gun there: the palm's place less its own hand's run (9.5 cm palms).
                Vector3 wantRight = place.position + axes * ((source.Hands.RightPalm - source.Hands.RightFingers * .052f) / scale);
                Vector3 wantLeft = place.position + axes * ((source.Hands.LeftPalm - source.Hands.LeftFingers * .052f) / scale);
                Vector3 barrel = axes * Vector3.forward;
                text.Append($"; {(own ? "bake's place in the model's space" : "place from the eyes in the hips' frame")} gun {Cm(place.position - eyes)} "
                    + $"pointing {Mathf.Atan2(barrel.x, barrel.z) * Mathf.Rad2Deg:0.0}°, clip's wrists {Vector3.Distance(rightHand, wantRight) * scale * 100f:0.0} / "
                    + $"{Vector3.Distance(leftHand, wantLeft) * scale * 100f:0.0} cm off it{(start.position == place.position ? " (used)" : "")}");
            }
            text.AppendLine();
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(saved[i].position, saved[i].rotation);
        }
    }

    private static void Line(string gun, in Source source, in FPSBody.ArmAt right, in FPSBody.ArmAt left, Vector3 eyes, Quaternion frame,
        float scale, KTransform start, in HoldReach.Limits limits, in FPSBody.Plan shared, bool hasShared, StringBuilder text)
    {
        Quaternion toEyes = Quaternion.Inverse(frame);
        string At(Vector3 point)
        {
            Vector3 p = toEyes * (point - eyes) * scale * 100f;
            return $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";
        }
        string Moves(in FPSBody.Plan plan) => $"down {plan.Drop * scale * 100f:0.0} back {plan.Pull * scale * 100f:0.0} slide {plan.Slide * scale * 100f:0.0} "
            + $"-> R {plan.Right:0.00} L {plan.Left:0.00}";

        // At the hip: the gun where the Pose Sampler starts it, and the game's own plan for it on this body.
        Quaternion axes = start.rotation * source.Frame;
        FPSBody.Wrists(source.Hands, right, left, start.position, axes, scale, out Vector3 rightWrist, out Vector3 leftWrist);
        var drop = FPSBody.TableDrop();
        var own = FPSBody.HoldPlan(source.Hands.RightPalm, source.Hands.LeftPalm, rightWrist, right, leftWrist, left, axes, scale, limits, drop,
            pistol: source.Pistol);
        var plan = hasShared
            ? FPSBody.HoldPlan(source.Hands.RightPalm, source.Hands.LeftPalm, rightWrist, right, leftWrist, left, axes, scale, limits, drop, shared, true,
                source.Pistol)
            : own;
        text.Append($"{gun}: hip gun {At(start.position)}, palms {Vector3.Distance(source.Hands.RightPalm, source.Hands.LeftPalm) * 100f:0.0} apart, "
            + $"unfitted R {FPSBody.Straight(rightWrist, right):0.00} L {FPSBody.Straight(leftWrist, left):0.00} "
            + $"(KINEMATION's rig R {source.Hands.SourceRight:0.00} L {source.Hands.SourceLeft:0.00})");
        text.Append(hasShared ? $" | shared {Moves(plan)} | own alone {Moves(own)}" : $" | own {Moves(own)}");

        // Down the sights: the sight on the eyes, where FPSAF's Ads brings it; the gun stays, the support palm keeps the
        // plan's place on it.
        if (source.HasAim)
        {
            Vector3 at = eyes + frame * (source.AimedAt / scale);
            Quaternion aimed = frame * source.AimedTurn;
            FPSBody.Wrists(source.Hands, right, left, at, aimed, scale, out Vector3 aimedRight, out Vector3 aimedLeft);
            Vector3 slid = aimedLeft - aimed * Vector3.forward * plan.Slide;
            text.Append($" | aimed gun {At(at)} R {FPSBody.Straight(aimedRight, right):0.00} L {FPSBody.Straight(aimedLeft, left):0.00}"
                + $" (with the slide L {FPSBody.Straight(slid, left):0.00})");
        }
        text.AppendLine();
    }
}
#endif
