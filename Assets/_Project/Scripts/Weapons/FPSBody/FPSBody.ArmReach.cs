using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// The plain hold fitted to each body's own arms (the user, 2026-11-12: "캐릭터가 총을 너무 앞쪽에 잡고 있어서 팔이 이미 최대로
/// 스트레치된 상황이라 팔이 조금만 움직여도 굉장히 들쑥날쑥하는 것 같아"; 2026-11-14: "캐릭터마다 총 드는 게 달라서 고장나는데 이거
/// 근본적인 원인 고쳐 주세요"). The bake puts each gun where KINEMATION's camera sees it from the reference body's eyes and
/// seats a stock in that body's shoulder pocket (GunClipRetarget), 6 to 12 cm further out than KINEMATION's own framing, in
/// true metres; every other body gets it the same way from its own eyes. Whether the arms then reach is each body's own
/// matter: its shoulders' place under its eyes, its arms' length, its hands' size.
/// One rule fits it, the same for every body and measured only against the body itself (HoldReach): a long gun first comes
/// down until the trigger hand rests under the shoulder joint (HipDrop); then, if an arm would be straighter than
/// Rig.reachMost of its own length, the gun comes back along its own barrel, as far as the trigger arm has room to fold
/// (Rig.reachLeast) and at most Rig.reachFitMost; a support arm still too straight then holds the gun further back, at most
/// Rig.reachSlideMost (FPSGun.SlideSupport). Along the barrel, so the gun keeps pointing where it pointed and never climbs
/// toward the face. The arms' IK eases in near full reach besides (ArmIK.Soften).
/// Until 0.9.91 the support arm was also held to how straight KINEMATION's own arm is in the gun's pose, and the gun was
/// pulled toward the shoulders in a straight line. KINEMATION's arms rig is no body: its shoulders are where first-person
/// arms look right, so its numbers mean nothing on a shoulder joint, and holding ours to them pulled a pistol 6 cm down and
/// back on one character, 2.5 cm on the next and not at all on a third, and slid the support hand off a pistol's grip; the
/// straight line toward the shoulders raised a gun held high toward the face.
/// The plan is made when a gun is built for a body, from the body as the gun's pose clip stands it; any body is fitted the
/// same way at load from its own bones, a mod's character too. In the editor each body and gun's numbers go to
/// Logs/gun-hold-report.txt once a session, and ENFILADE → 총 → 쥐기 점검 writes every character's and gun's to
/// Logs/gun-hold-probe.txt without playing (GunHoldProbe).
/// </summary>
public sealed partial class FPSBody
{
    [SerializeField, Range(.5f, 1f), Tooltip("No arm holds the gun straighter than this (shoulder joint to wrist over the arm's length): "
        + "the plain hold comes back along the barrel until they are.")]
    private float reachMost = .9f;
    [SerializeField, Range(0f, .8f), Tooltip("The gun comes back for that only while the trigger arm stays at least this straight (folded "
        + "tighter, the forearm lies on the upper arm).")]
    private float reachLeast = .4f;
    [SerializeField, Min(0f), Tooltip("The most the plain hold comes back along the barrel for that (m in game).")]
    private float reachFitMost = .06f;
    [SerializeField, Min(0f), Tooltip("A trigger arm folded tighter than reachLeast sends the gun forward along the barrel by this much at "
        + "most (m in game), as far as the support arm stays within reachMost.")]
    private float reachPushMost;
    [SerializeField, Min(0f), Tooltip("A support arm still too straight then holds the gun this much further back at most (m in game).")]
    private float reachSlideMost = .1f;
    [SerializeField, Min(.01f), Tooltip("Behind the plain hold's place on the handguard that slide fades out over this far, so a reload's hand on "
        + "the magazine keeps its clip's place (m in game; at least twice the slide).")]
    private float reachSlideFade = .1f;
    [SerializeField, Tooltip("A long gun's trigger hand rests at least this far under its shoulder joint in the plain hold (m in game, up the body): "
        + "a gun held higher comes down by the difference first (HipDrop).")]
    private float hipHandUnderShoulder = .02f;
    [SerializeField, Min(0f), Tooltip("The most a long gun comes down for that (m in game).")]
    private float hipDropMost = .15f;
    [SerializeField, Min(0f), Tooltip("A gun whose two palms sit further apart than this on it is a long gun (m in game); a pistol's are 6 to 7 cm apart.")]
    private float longGunHands = .15f;

    // HandGrip's and GunArms' palm middle: from the wrist toward the knuckles, this share of the way.
    private const float PalmMiddle = .55f;
    // Each body and gun's fit, noted once a session.
    private static readonly HashSet<(string skin, WeaponData gun)> notedReach = new();

    // One arm of this body as the gun's pose clip stands it, in the model's own space: its shoulder joint, its length (upper
    // arm and forearm) and its hand's run (wrist to the middle knuckle).
    internal struct ArmAt
    {
        public Vector3 Shoulder;
        public float Length, Run;
        public bool Valid;
    }

    // The fit: how far the gun comes (model space), the same from the eyes (m in game, in the eyes' frame), and how far the
    // support palm comes back along the gun (m in game) from where along the gun the hold has it (its z).
    private struct ReachFit
    {
        public Vector3 Move, MoveFromEyes;
        public float Slide, SlideAt;
    }

    /// <summary>When a long gun comes down (HipDrop), in metres in game.</summary>
    internal struct DropRule
    {
        public float UnderShoulder, Most, LongGunHands;
    }

    /// <summary>
    /// What the rule asks of one body for one gun, in the body's model units: how far the gun comes down, how far back along
    /// its barrel (under zero: forward), how far the support palm comes back on it, and how straight each arm then is.
    /// </summary>
    internal struct Plan
    {
        public float Drop, Pull, Slide, Right, Left;
    }

    private HoldReach.Limits ReachLimits => new HoldReach.Limits
    {
        Most = reachMost, Least = reachLeast, PullMost = reachFitMost, PushMost = reachPushMost, SlideMost = reachSlideMost
    };

    private DropRule HipDropRule => new DropRule { UnderShoulder = hipHandUnderShoulder, Most = hipDropMost, LongGunHands = longGunHands };

    /// <summary>The same limits as the table has them (Tables/FPSBody.csv, row Rig), for the editor's hold check.</summary>
    internal static HoldReach.Limits TableLimits() => new HoldReach.Limits
    {
        Most = GameTables.Number(TableName, TableRow, nameof(reachMost), .9f),
        Least = GameTables.Number(TableName, TableRow, nameof(reachLeast), .4f),
        PullMost = GameTables.Number(TableName, TableRow, nameof(reachFitMost), .06f),
        PushMost = GameTables.Number(TableName, TableRow, nameof(reachPushMost), 0f),
        SlideMost = GameTables.Number(TableName, TableRow, nameof(reachSlideMost), .1f)
    };

    internal static DropRule TableDrop() => new DropRule
    {
        UnderShoulder = GameTables.Number(TableName, TableRow, nameof(hipHandUnderShoulder), .02f),
        Most = GameTables.Number(TableName, TableRow, nameof(hipDropMost), .15f),
        LongGunHands = GameTables.Number(TableName, TableRow, nameof(longGunHands), .15f)
    };

    // The plain hold at <paramref name="place"/> (KINEMATION's weapon bone in the model's space, where the Pose Sampler starts
    // the gun) brought within this body's reach.
    // <paramref name="reference"/>: this body is the one the gun's clips were made on, whose own plan every other body shares.
    private ReachFit FitToReach(in FPSBodyAssets.Gun entry, in GunBakes.Entry bake, Quaternion gunFrame, KTransform place, bool reference = false)
    {
        var fit = default(ReachFit);
        if (animator == null || bake.pose == null || !SourceHands(assets, entry, gunFrame, gunArms, out var hands)) return fit;
        if (!PoseArms(bake.pose, out ArmAt right, out ArmAt left)) return fit;

        float scale = Mathf.Max(1e-4f, ModelScale);
        Quaternion axes = place.rotation * gunFrame;
        Wrists(hands, right, left, place.position, axes, scale, out Vector3 rightWrist, out Vector3 leftWrist);
        // Every character holds the gun as the reference body does (SharedPlan), adding only what its own arms still need;
        // the reference body, or any body when its plan can't be had, by its own arms alone.
        Plan shared = default;
        bool hasShared = !reference
            && SharedPlan(entry.weapon, assets, GunBakes.Instance, movement != null ? movement.runtimeBodyDefaults.w : 0f, gunArms, out shared);
        var plan = HoldPlan(hands.RightPalm, hands.LeftPalm, rightWrist, right, leftWrist, left, axes, scale, ReachLimits, HipDropRule, shared, hasShared,
            IsPistol(entry.weapon));
        Vector3 back = -(axes * Vector3.forward);
        Vector3 move = Vector3.down * plan.Drop + back * plan.Pull;
        fit.Move = move;
        fit.Slide = plan.Slide * scale;
        fit.SlideAt = hands.LeftPalm.z;
        Quaternion fromEyes = EyeFrame(bake.pose, out _, out Quaternion frame) ? Quaternion.Inverse(frame) : Quaternion.identity;
        fit.MoveFromEyes = fromEyes * move * scale;
        NoteReach(entry.weapon, Straight(rightWrist, right), Straight(leftWrist, left), hands, plan, scale, hasShared ? shared : (Plan?)null);
        return fit;
    }

    /// <summary>
    /// The rule for one body and gun: the body's two arms as the gun's pose clip stands them, its wrists where its palms go
    /// on the gun, the gun's axes (<paramref name="axes"/>, its barrel along z) and KINEMATION's two palms on it (m in game,
    /// which tell a long gun from a pistol), all in the body's model space at <paramref name="scale"/> (model units to m).
    /// </summary>
    /// <remarks>
    /// With <paramref name="shared"/>, the plan the reference body made for this gun (<paramref name="reference"/>, m in
    /// game) comes first, whole, and only what this body's own arms still can't reach is added to it (SharedPlan).
    /// </remarks>
    internal static Plan HoldPlan(Vector3 rightPalm, Vector3 leftPalm, Vector3 rightWrist, in ArmAt right, Vector3 leftWrist, in ArmAt left,
        Quaternion axes, float scale, HoldReach.Limits limits, in DropRule drop, in Plan reference = default, bool shared = false,
        bool pistol = false)
    {
        Vector3 back = -(axes * Vector3.forward);
        limits.PullMost /= scale;
        limits.PushMost /= scale;
        limits.SlideMost /= scale;
        if (!shared)
        {
            float down = pistol ? 0f : HipDrop(rightPalm, leftPalm, rightWrist, right, scale, drop);
            Vector3 lower = Vector3.down * down;
            var fit = HoldReach.Solve(rightWrist + lower, right.Shoulder, right.Length, leftWrist + lower, left.Shoulder, left.Length, back, limits, true);
            return new Plan { Drop = down, Pull = fit.Pull, Slide = fit.Slide, Right = fit.Right, Left = fit.Left };
        }
        float down0 = reference.Drop / scale, pull0 = reference.Pull / scale, slide0 = reference.Slide / scale;
        Vector3 shift = Vector3.down * down0 + back * pull0;
        // What is left of the limits for this body's own arms; it only ever comes back further, never forward again.
        limits.PullMost = Mathf.Max(0f, limits.PullMost - Mathf.Max(0f, pull0));
        limits.PushMost = 0f;
        limits.SlideMost = Mathf.Max(0f, limits.SlideMost - slide0);
        var more = HoldReach.Solve(rightWrist + shift, right.Shoulder, right.Length, leftWrist + shift + back * slide0, left.Shoulder, left.Length,
            back, limits, true);
        return new Plan { Drop = down0, Pull = pull0 + more.Pull, Slide = slide0 + more.Slide, Right = more.Right, Left = more.Left };
    }

    // ---- The hold every character shares (the reference body's) ----

    // Each gun's plan on the reference body (the one the gun clips were made on, GunBakes), in metres in game: the same
    // numbers go onto every character from its own eyes, so every character holds every gun in the same place in the view
    // and by the same places on the gun, whatever its build; a character's own arms only add what they still can't reach.
    // Fitted to each body's own arms alone, the default character held a rifle 6 cm nearer her face than the next character
    // and her support hand as far back again, and a longer-armed one not at all: the hold changed with the character picked,
    // which only changes a look (the user, 2026-11-14: "캐릭터마다 총 드는 게 달라서 고장나는데"). Measured once a session on a
    // hidden copy of the reference model at the size the game draws it, and again after the tables are read again.
    private static readonly Dictionary<WeaponData, Plan> sharedPlans = new();
    private static bool sharedTried, sharedListening;

    /// <summary>The shared plans are measured again when next asked for (a new play session, the tables read again, the
    /// editor's hold check starting over).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ForgetSharedPlans()
    {
        sharedPlans.Clear();
        sharedTried = false;
    }

    /// <summary>
    /// The reference body's plan for <paramref name="weapon"/> (m in game), measured the first time any is asked for.
    /// <paramref name="camera"/> is the standing camera height bodies are sized to; <paramref name="gunArms"/> keeps the
    /// hidden copies of KINEMATION's arms the caller reads the guns' holds from. False where it couldn't be measured.
    /// </summary>
    internal static bool SharedPlan(WeaponData weapon, FPSBodyAssets assets, GunBakes bakes, float camera, Dictionary<GameObject, GunArms> gunArms,
        out Plan plan)
    {
        plan = default;
        if (weapon == null || assets == null || bakes == null || gunArms == null) return false;
        if (!sharedListening)
        {
            sharedListening = true;
            GameTables.Reloaded += ForgetSharedPlans;
        }
        if (!sharedTried)
        {
            sharedTried = true;
            MeasureSharedPlans(assets, bakes, camera, gunArms);
        }
        return sharedPlans.TryGetValue(weapon, out plan);
    }

    private static void MeasureSharedPlans(FPSBodyAssets assets, GunBakes bakes, float camera, Dictionary<GameObject, GunArms> gunArms)
    {
        GameObject model = null;
        foreach (var entry in bakes.entries)
            if (entry.skin != null && entry.pose != null) { model = entry.skin; break; }
        if (model == null || camera <= .1f) return;
        var limits = TableLimits();
        var drop = TableDrop();
        var copy = Object.Instantiate(model);
        copy.name = model.name + " (the shared gun hold)";
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            var animator = copy.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return;
            // As a player's body plays (PlayerBody.Build): with root motion on, a sampled clip turns the model's root by the
            // clip's own turn and lays the pose out the other way under it (the rifles' bladed stance by 27 degrees), and the
            // gun, placed in the model's space, was measured 13 and 28 cm off the hands (GunHoldProbe, 2026-11-15).
            animator.applyRootMotion = false;
            copy.transform.localScale = Vector3.one * PlayerBody.DrawnScaleOf(animator, copy.transform, camera);
            float scale = Mathf.Max(1e-4f, Mathf.Abs(animator.transform.lossyScale.x));
            foreach (var gun in assets.guns)
            {
                if (gun.weapon == null || gun.prefab == null || sharedPlans.ContainsKey(gun.weapon)) continue;
                if (!bakes.TryGet(model, gun.weapon, out var bake, out bool exact) || !exact) continue;
                Quaternion gunFrame = GunFrameOf(gun.prefab.GetComponent<KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts.CasFPSWeapon>());
                if (!SourceHands(assets, gun, gunFrame, gunArms, out var hands) || !PoseArms(animator, bake.pose, out ArmAt right, out ArmAt left)) continue;
                Quaternion axes = bake.gunRotation * gunFrame;
                Wrists(hands, right, left, bake.gunPosition, axes, scale, out Vector3 rightWrist, out Vector3 leftWrist);
                var own = HoldPlan(hands.RightPalm, hands.LeftPalm, rightWrist, right, leftWrist, left, axes, scale, limits, drop,
                    pistol: IsPistol(gun.weapon));
                sharedPlans[gun.weapon] = new Plan { Drop = own.Drop * scale, Pull = own.Pull * scale, Slide = own.Slide * scale, Right = own.Right, Left = own.Left };
            }
        }
        finally
        {
            // Gone before anything is drawn: in play at the frame's end, which comes before the cameras render.
            if (Application.isPlaying) Object.Destroy(copy);
            else Object.DestroyImmediate(copy);
        }
    }

    /// <summary>
    /// KINEMATION's two hands on a gun in its pose clip, in the gun's frame (m in game): each palm and its fingers' way; the
    /// support hand's from the no-grip pose where the gun has one, as a bare gun is held (HoldSide). How straight their own
    /// arms are on their own rig is only ever noted.
    /// </summary>
    internal struct SourceHold
    {
        public Vector3 RightPalm, RightFingers, LeftPalm, LeftFingers;
        public float SourceRight, SourceLeft;
    }

    internal static bool SourceHands(FPSBodyAssets assets, in FPSBodyAssets.Gun gun, Quaternion gunFrame, Dictionary<GameObject, GunArms> gunArms,
        out SourceHold hands)
    {
        hands = default;
        var prefab = assets != null ? gun.ArmsOr(assets.armsModel) : null;
        if (prefab == null || gun.sourcePose == null) return false;
        if (!gunArms.TryGetValue(prefab, out var arms)) gunArms[prefab] = arms = GunArms.Make(prefab);
        if (arms == null) return false;
        if (!SourceHand(arms, gun.sourcePose, gunFrame, false, out hands.RightPalm, out hands.RightFingers, out hands.SourceRight)) return false;
        var supportPose = gun.sourcePoseNoGrip != null ? gun.sourcePoseNoGrip : gun.sourcePose;
        return SourceHand(arms, supportPose, gunFrame, true, out hands.LeftPalm, out hands.LeftFingers, out hands.SourceLeft)
            || SourceHand(arms, gun.sourcePose, gunFrame, true, out hands.LeftPalm, out hands.LeftFingers, out hands.SourceLeft);
    }

    /// <summary>
    /// A body's wrists where its palms go on the gun, in its model space: each palm's middle at KINEMATION's place (m in game
    /// from the gun at <paramref name="at"/>, its axes <paramref name="axes"/>), the wrist this hand's own run back from it
    /// along the fingers.
    /// </summary>
    internal static void Wrists(in SourceHold hands, in ArmAt right, in ArmAt left, Vector3 at, Quaternion axes, float scale,
        out Vector3 rightWrist, out Vector3 leftWrist)
    {
        rightWrist = at + axes * (hands.RightPalm / scale) - axes * hands.RightFingers * (PalmMiddle * right.Run);
        leftWrist = at + axes * (hands.LeftPalm / scale) - axes * hands.LeftFingers * (PalmMiddle * left.Run);
    }

    // How far a long gun comes down (model units, down the model) so its trigger hand rests at least Rig.hipHandUnderShoulder
    // under this body's shoulder joint. Every long gun of the packs holds the trigger hand there or lower (18 to 27 cm under
    // the eyes on the reference body, its shoulder 17 under) but the Herrington: shouldered, its straight stock rides the
    // eye line, the trigger hand by the cheek 6 cm under the eyes and the gun across the middle of the view, and on a body a
    // little lower in the eyes it hid the view's top left (the user, 2026-11-12: "헤링턴은 여전히 높게 잡고 있어"; 2026-11-14,
    // KINEMATION's mannequin by the hub mirror: "나 샷건을 왜 이따구로 듦?"). A pistol is held up toward the eyes on purpose
    // and keeps its place: one whose palms are close together, and any gun that is a handgun by its sound profile (the
    // Kolibri's pose holds it in one hand, the other 45 cm away, and by its palms alone it came down as a long gun).
    private static float HipDrop(Vector3 rightPalm, Vector3 leftPalm, Vector3 rightWrist, in ArmAt right, float scale, in DropRule rule)
    {
        if (Vector3.Distance(rightPalm, leftPalm) <= rule.LongGunHands) return 0f;
        float over = rightWrist.y - (right.Shoulder.y - rule.UnderShoulder / scale);
        return Mathf.Clamp(over, 0f, rule.Most / scale);
    }

    // A hand of KINEMATION's arms in a pose clip: its palm and fingers' way in the gun's frame, and how straight its arm is
    // on its own rig (1 when that can't be read; only noted, never fitted to).
    internal static bool SourceHand(GunArms arms, AnimationClip pose, Quaternion frame, bool isLeft, out Vector3 palm, out Vector3 fingers,
        out float extension)
    {
        palm = fingers = Vector3.zero;
        extension = 1f;
        if (pose == null || !arms.Sample(pose, 0f)) return false;
        arms.Gun(frame, out Vector3 at, out Quaternion turn);
        if (!arms.Palm(isLeft, at, Quaternion.Inverse(turn), out palm, out fingers, out _)) return false;
        if (!arms.Extension(isLeft, out extension)) extension = 1f;
        return true;
    }

    // This body's arms as the gun's pose clip stands them (sampled and put back, as EyeFrame does), in the model's space.
    private bool PoseArms(AnimationClip pose, out ArmAt right, out ArmAt left) => PoseArms(animator, pose, out right, out left);

    /// <summary>The same for any humanoid's Animator (the editor's hold check, GunHoldProbe).</summary>
    internal static bool PoseArms(Animator animator, AnimationClip pose, out ArmAt right, out ArmAt left)
    {
        right = left = default;
        if (animator == null || pose == null) return false;
        var root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var saved = new (Vector3 position, Quaternion rotation)[bones.Length];
        for (int i = 0; i < bones.Length; i++) saved[i] = (bones[i].localPosition, bones[i].localRotation);
        try
        {
            ClipSampler.Sample(pose, animator.gameObject, 0f);
            right = ArmOf(animator, root, false);
            left = ArmOf(animator, root, true);
            return right.Valid && left.Valid;
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(saved[i].position, saved[i].rotation);
        }
    }

    private static ArmAt ArmOf(Animator animator, Transform root, bool isLeft)
    {
        var upper = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        if (upper == null || lower == null || hand == null) return default;
        Vector3 a = root.InverseTransformPoint(upper.position), b = root.InverseTransformPoint(lower.position);
        Vector3 c = root.InverseTransformPoint(hand.position);
        float length = Vector3.Distance(a, b) + Vector3.Distance(b, c);
        float run = middle != null ? Vector3.Distance(c, root.InverseTransformPoint(middle.position)) : length * .17f;
        return new ArmAt { Shoulder = a, Length = length, Run = run, Valid = length > 1e-4f };
    }

    /// <summary>A handgun by its sound profile: held up toward the eyes, never brought down as a long gun is (HipDrop).</summary>
    internal static bool IsPistol(WeaponData weapon) => weapon != null && weapon.SoundProfile == GunSoundProfile.Pistol;

    /// <summary>How straight an arm holds a wrist: shoulder joint to wrist over the arm's length.</summary>
    internal static float Straight(Vector3 wrist, in ArmAt arm) => Vector3.Distance(wrist, arm.Shoulder) / Mathf.Max(1e-4f, arm.Length);

    private void NoteReach(WeaponData weapon, float rightBefore, float leftBefore, in SourceHold hands, in Plan plan, float scale, Plan? shared)
    {
        if (!Application.isEditor || weapon == null) return;
        string skin = body != null && body.Skin != null ? body.Skin.Id : "?";
        if (!notedReach.Add((skin, weapon))) return;
        // What this body's own arms added to the shared plan, if anything.
        string own = shared is Plan from
            ? $" (as every character; this body's own arms added {(plan.Pull * scale - from.Pull) * 100f:0.0} cm back and "
                + $"{(plan.Slide * scale - from.Slide) * 100f:0.0} cm of the palm's)"
            : " (this body's own plan: the reference body's, which every character shares, or a body with none to share)";
        PlayerBodyHands.NoteGunHold($"{System.DateTime.Now:HH:mm:ss} {skin} {weapon.name} fitted to the arms: straight right {rightBefore:0.00} "
            + $"left {leftBefore:0.00} (KINEMATION's rig right {hands.SourceRight:0.00} left {hands.SourceLeft:0.00}, not fitted to) -> right "
            + $"{plan.Right:0.00} left {plan.Left:0.00}; gun down {plan.Drop * scale * 100f:0.0} cm to rest the trigger hand under the shoulder, then "
            + $"{plan.Pull * scale * 100f:0.0} cm back along its barrel; support palm {plan.Slide * scale * 100f:0.0} cm further back on it{own}\n");
    }
}
