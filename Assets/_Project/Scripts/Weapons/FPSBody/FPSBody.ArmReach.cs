using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// The plain hold fitted to each body's own arms (the user, 2026-11-12: "캐릭터가 총을 너무 앞쪽에 잡고 있어서 팔이 이미 최대로
/// 스트레치된 상황이라 팔이 조금만 움직여도 굉장히 들쑥날쑥하는 것 같아"). The bake puts each gun where KINEMATION's camera sees it
/// from the eyes and seats a stock in the shoulder pocket (GunClipRetarget), in true metres: KINEMATION's operator has long
/// arms, and on the reference body the support arm came out 0.94 to 0.97 of the way straight on the rifles (the bake's own
/// measure), where the elbow's angle runs away with the smallest move of the gun.
/// So when a gun is built for a body, its hold is measured against that body's arms as the gun's pose clip stands them:
/// each wrist where this hand's palm goes on the gun (KINEMATION's palm, FPSBody.Contacts), from this body's shoulder joint,
/// over this arm's length. The support arm may reach no straighter than KINEMATION's own does in the gun's pose clip (its
/// grip to shoulder over its arm's length, the same share of this arm) and neither arm past Rig.reachMost; the trigger hand
/// sits where the stock and the grip put it, so only Rig.reachMost holds it. The gun comes the least way toward the
/// shoulders that does it (along the arms' own lines), at most Rig.reachFitMost, and a support arm still too straight then
/// holds a bare handguard further back (FPSGun.SlideSupport), at most Rig.reachSlideMost. The arms' IK eases in near full
/// reach besides (ArmIK.Soften). Any body is fitted the same way at load from its own bones, a mod's character too. In the
/// editor each body and gun's numbers go to Logs/gun-hold-report.txt once a session.
/// </summary>
public sealed partial class FPSBody
{
    [SerializeField, Range(.5f, 1f), Tooltip("No arm holds the gun straighter than this (shoulder joint to wrist over the arm's length), and "
        + "the support arm no straighter than KINEMATION's own in the gun's pose: the plain hold comes toward the shoulders until they are.")]
    private float reachMost = .9f;
    [SerializeField, Min(0f), Tooltip("The most the plain hold comes toward the shoulders for that (m in game).")]
    private float reachFitMost = .06f;
    [SerializeField, Min(0f), Tooltip("A support arm still too straight then holds a bare handguard this much further back at most (m in game).")]
    private float reachSlideMost = .1f;
    [SerializeField, Min(.01f), Tooltip("Behind the plain hold's place on the handguard that slide fades out over this far, so a reload's hand on "
        + "the magazine keeps its clip's place (m in game; at least twice the slide).")]
    private float reachSlideFade = .1f;

    // HandGrip's and GunArms' palm middle: from the wrist toward the knuckles, this share of the way.
    private const float PalmMiddle = .55f;
    // Each body and gun's fit, noted once a session.
    private static readonly HashSet<(string skin, WeaponData gun)> notedReach = new();

    // One arm of this body as the gun's pose clip stands it, in the model's own space: its shoulder joint, its length (upper
    // arm and forearm) and its hand's run (wrist to the middle knuckle).
    private struct ArmAt
    {
        public Vector3 Shoulder;
        public float Length, Run;
        public bool Valid;
    }

    // The fit: how far the gun comes (model space), the same from the eyes (m in game, in the eyes' frame), and how far the
    // support palm comes back along a bare handguard (m in game) from where along the gun the hold has it (its z).
    private struct ReachFit
    {
        public Vector3 Move, MoveFromEyes;
        public float Slide, SlideAt;
    }

    // The plain hold at <paramref name="place"/> (KINEMATION's weapon bone in the model's space, where the Pose Sampler starts
    // the gun) brought within this body's reach.
    private ReachFit FitToReach(in FPSBodyAssets.Gun entry, in GunBakes.Entry bake, Quaternion gunFrame, KTransform place)
    {
        var fit = default(ReachFit);
        var prefab = assets != null ? entry.ArmsOr(assets.armsModel) : null;
        if (animator == null || bake.pose == null || prefab == null || entry.sourcePose == null) return fit;
        if (!gunArms.TryGetValue(prefab, out var arms)) gunArms[prefab] = arms = GunArms.Make(prefab);
        if (arms == null) return fit;
        // KINEMATION's arms in the gun's pose clip: each palm on the gun, its fingers' way and how straight the arm is; the
        // support hand's from the no-grip pose where the gun has one, as a bare gun is held (HoldSide).
        if (!SourceHand(arms, entry.sourcePose, gunFrame, false, out Vector3 rightPalm, out Vector3 rightFingers, out float sourceRight)) return fit;
        var supportPose = entry.sourcePoseNoGrip != null ? entry.sourcePoseNoGrip : entry.sourcePose;
        if (!SourceHand(arms, supportPose, gunFrame, true, out Vector3 leftPalm, out Vector3 leftFingers, out float sourceLeft)
            && !SourceHand(arms, entry.sourcePose, gunFrame, true, out leftPalm, out leftFingers, out sourceLeft)) return fit;
        if (!PoseArms(bake.pose, out ArmAt right, out ArmAt left)) return fit;

        float scale = Mathf.Max(1e-4f, ModelScale);
        Quaternion axes = place.rotation * gunFrame;
        // This body's wrist where its palm goes on the gun: the palm's middle there (KINEMATION's place, in metres in game from
        // the gun), the wrist this hand's run back from it along the fingers.
        Vector3 rightWrist = place.position + axes * (rightPalm / scale) - axes * rightFingers * (PalmMiddle * right.Run);
        Vector3 leftWrist = place.position + axes * (leftPalm / scale) - axes * leftFingers * (PalmMiddle * left.Run);
        float wantRight = reachMost, wantLeft = Mathf.Min(sourceLeft, reachMost);

        Vector3 move = Toward(rightWrist, right, wantRight, leftWrist, left, wantLeft);
        float most = reachFitMost / scale;
        if (move.magnitude > most) move = move.normalized * most;
        // A support arm still too straight holds the handguard further back, the least way that brings it in.
        Vector3 back = -(axes * Vector3.forward);
        float slide = Mathf.Min(SlideFor(leftWrist + move, left, wantLeft, back), reachSlideMost / scale);

        fit.Move = move;
        fit.Slide = slide * scale;
        fit.SlideAt = leftPalm.z;
        fit.MoveFromEyes = EyeFrame(bake.pose, out _, out Quaternion frame) ? Quaternion.Inverse(frame) * move * scale : move * scale;
        NoteReach(entry.weapon, Straight(rightWrist, right), Straight(leftWrist, left), sourceRight, sourceLeft,
            Straight(rightWrist + move, right), Straight(leftWrist + move + back * slide, left), fit);
        return fit;
    }

    // A hand of KINEMATION's arms in a pose clip: its palm and fingers' way in the gun's frame, and how straight its arm is
    // (1 when that can't be read, so Rig.reachMost holds it alone).
    private static bool SourceHand(GunArms arms, AnimationClip pose, Quaternion frame, bool isLeft, out Vector3 palm, out Vector3 fingers,
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
    private bool PoseArms(AnimationClip pose, out ArmAt right, out ArmAt left)
    {
        right = left = default;
        if (animator == null || pose == null) return false;
        var root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var saved = new (Vector3 position, Quaternion rotation)[bones.Length];
        for (int i = 0; i < bones.Length; i++) saved[i] = (bones[i].localPosition, bones[i].localRotation);
        try
        {
            pose.SampleAnimation(animator.gameObject, 0f);
            right = ArmOf(root, false);
            left = ArmOf(root, true);
            return right.Valid && left.Valid;
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(saved[i].position, saved[i].rotation);
        }
    }

    private ArmAt ArmOf(Transform root, bool isLeft)
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

    private static float Straight(Vector3 wrist, in ArmAt arm) => Vector3.Distance(wrist, arm.Shoulder) / Mathf.Max(1e-4f, arm.Length);

    // The least move of the gun (model space) that brings each wrist within its share of its arm's length from its shoulder:
    // along the line of an arm too straight, both lines at once (the shortest move meeting both) when both are.
    private static Vector3 Toward(Vector3 rightWrist, in ArmAt right, float wantRight, Vector3 leftWrist, in ArmAt left, float wantLeft)
    {
        Vector3 move = Vector3.zero;
        for (int pass = 0; pass < 12; pass++)
        {
            Vector3 r = rightWrist + move - right.Shoulder, l = leftWrist + move - left.Shoulder;
            float overRight = r.magnitude - wantRight * right.Length, overLeft = l.magnitude - wantLeft * left.Length;
            bool fixRight = overRight > 1e-5f && r.sqrMagnitude > 1e-10f, fixLeft = overLeft > 1e-5f && l.sqrMagnitude > 1e-10f;
            if (!fixRight && !fixLeft) break;
            if (fixRight && fixLeft)
            {
                Vector3 ur = r.normalized, ul = l.normalized;
                float c = Vector3.Dot(ur, ul), apart = 1f - c * c;
                if (apart > .05f) move += ur * ((-overRight + c * overLeft) / apart) + ul * ((-overLeft + c * overRight) / apart);
                else move -= (ur + ul).normalized * Mathf.Max(overRight, overLeft);
            }
            else if (fixRight) move -= r.normalized * overRight;
            else move -= l.normalized * overLeft;
        }
        return move;
    }

    // How far a wrist comes back along the gun (model units) to be within its share of its arm's length from the shoulder: the
    // nearer root of |v + back s| = reach, or where that line comes nearest when it never gets there.
    private static float SlideFor(Vector3 wrist, in ArmAt arm, float want, Vector3 back)
    {
        Vector3 v = wrist - arm.Shoulder;
        float reach = want * arm.Length;
        if (v.magnitude <= reach) return 0f;
        float along = Vector3.Dot(v, back);
        float under = along * along - v.sqrMagnitude + reach * reach;
        return Mathf.Max(0f, under >= 0f ? -along - Mathf.Sqrt(under) : -along);
    }

    private void NoteReach(WeaponData weapon, float rightBefore, float leftBefore, float sourceRight, float sourceLeft, float rightAfter,
        float leftAfter, in ReachFit fit)
    {
        if (!Application.isEditor || weapon == null) return;
        string skin = body != null && body.Skin != null ? body.Skin.Id : "?";
        if (!notedReach.Add((skin, weapon))) return;
        Vector3 m = fit.MoveFromEyes * 100f;
        PlayerBodyHands.NoteGunHold($"{System.DateTime.Now:HH:mm:ss} {skin} {weapon.name} fitted to the arms: straight right {rightBefore:0.00} "
            + $"left {leftBefore:0.00} (KINEMATION's right {sourceRight:0.00} left {sourceLeft:0.00}) -> right {rightAfter:0.00} left {leftAfter:0.00}; "
            + $"gun {m.magnitude:0.0} cm toward the shoulders ({-m.z:0.0} back, {-m.x:0.0} left, {-m.y:0.0} down from the eyes); "
            + $"support palm {fit.Slide * 100f:0.0} cm back along a bare handguard\n");
    }
}
