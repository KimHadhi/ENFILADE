using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// The body that holds a gun, as the gun's pose clip stands it: its eyes, its two arms, and the front of its right shoulder
/// and chest where a stock's butt rests, in its own model space. Everything a hold asks of a body it asks here (GunHold),
/// and everything here is measured from the body itself, its bones and its surface (BodyFront), so a hold made from it
/// fits that body: the default character, a longer-armed one, a mod's.
/// A humanoid clip stands every body facing its model's forward with the model's up, whatever its build, and at rest the
/// view looks that way too (the body's root turns with the legs under the view), so <see cref="Ahead"/> and
/// <see cref="Up"/> are the view's own axes in the body's space.
/// Numbers only: it keeps no bones, so a gun can keep the body it was fitted to for as long as the gun lives.
/// </summary>
public sealed class HoldBody
{
    // HandGrip's and GunArms' palm middle: from the wrist toward the knuckles, this share of the way.
    private const float PalmMiddle = .55f;

    /// <summary>
    /// Where a stock's butt rests, the one rule in play and when the gun clips are made (GunClipRetarget): before the place
    /// inside the shoulder that is from the joint toward the neck by PocketInward of the way across and under the joint by
    /// PocketDrop of the shoulder-to-neck distance (<see cref="UnderPocket"/>), where the body ends along the gun's line
    /// (BodyFront.Rest, the butt's pad reaching PocketRadius of that distance to each side); where no mesh of the body can
    /// be read, PocketDepth of it ahead. A shooter's shoulder pocket is just inside the ball of the joint and a little
    /// under it.
    /// </summary>
    public const float PocketInward = .25f, PocketDrop = .1f, PocketRadius = .2f, PocketDepth = .35f;

    // The body's surface is read within this many shoulder-to-neck distances of the shoulder joint: the body ends within
    // one of them before the shoulder and behind it, on the thickest body met so far.
    private const float SkinWithin = 1.6f;

    /// <summary>The way the body looks at rest and its up, in its own model space.</summary>
    internal static Vector3 Ahead => Vector3.forward;
    internal static Vector3 Up => Vector3.up;

    /// <summary>
    /// One arm: its shoulder joint, its length (upper arm and forearm) and its hand's run (wrist to the middle knuckle), and
    /// what it can say of a wrist it is asked to hold: how straight it would be, and how far the wrist must go along a line
    /// for the arm to be no straighter, or no more folded, than a share of its length.
    /// </summary>
    internal readonly struct Arm
    {
        internal readonly Vector3 Shoulder;
        internal readonly float Length, HandRun;
        /// <summary>Where the pose clip itself has this arm's wrist, and the way its fingers point there (unit; wrist to
        /// the middle knuckle).</summary>
        internal readonly Vector3 PosedWrist, PosedFingers;

        internal Arm(Vector3 shoulder, float length, float handRun, Vector3 posedWrist, Vector3 posedFingers)
        {
            Shoulder = shoulder;
            Length = length;
            HandRun = handRun;
            PosedWrist = posedWrist;
            PosedFingers = posedFingers;
        }

        internal bool Valid => Length > 1e-4f;

        /// <summary>The wrist for a palm whose middle is at <paramref name="palm"/>, the fingers pointing <paramref name="fingers"/>
        /// (unit): this hand's own run back from it.</summary>
        internal Vector3 Wrist(Vector3 palm, Vector3 fingers) => palm - fingers * (PalmMiddle * HandRun);

        /// <summary>How straight the arm holds a wrist: shoulder joint to wrist over the arm's length (1: locked straight).</summary>
        internal float Straight(Vector3 wrist) => Vector3.Distance(wrist, Shoulder) / Mathf.Max(1e-4f, Length);

        /// <summary>
        /// The least way a wrist goes along <paramref name="way"/> (unit) for the arm to be no straighter than
        /// <paramref name="share"/>; where that line never comes so near, as far as it comes nearest. Zero when the arm is
        /// bent enough already, or when going that way only straightens it.
        /// </summary>
        internal float ToBend(Vector3 wrist, Vector3 way, float share)
        {
            Vector3 from = wrist - Shoulder;
            float reach = share * Length;
            if (from.sqrMagnitude <= reach * reach) return 0f;
            float along = Vector3.Dot(from, way);
            float under = along * along - from.sqrMagnitude + reach * reach;
            return Mathf.Max(0f, under >= 0f ? -along - Mathf.Sqrt(under) : -along);
        }

        /// <summary>
        /// How far a wrist can go along <paramref name="way"/> before the arm is folded tighter than <paramref name="share"/>;
        /// no limit when the line passes the shoulder by more than that. Zero when it is folded tighter already.
        /// </summary>
        internal float RoomToFold(Vector3 wrist, Vector3 way, float share)
        {
            Vector3 from = wrist - Shoulder;
            float least = share * Length;
            if (from.sqrMagnitude <= least * least) return 0f;
            float along = Vector3.Dot(from, way);
            float under = along * along - from.sqrMagnitude + least * least;
            if (under < 0f) return float.PositiveInfinity;
            float first = -along - Mathf.Sqrt(under);
            return first > 0f ? first : float.PositiveInfinity;
        }

        /// <summary>
        /// The least way a wrist goes against <paramref name="way"/> for an arm folded tighter than <paramref name="share"/>
        /// to be that straight. Zero when it is that straight already.
        /// </summary>
        internal float ToUnfold(Vector3 wrist, Vector3 way, float share)
        {
            Vector3 from = wrist - Shoulder;
            float least = share * Length;
            if (from.sqrMagnitude >= least * least) return 0f;
            float along = Vector3.Dot(from, way);
            return Mathf.Max(0f, along + Mathf.Sqrt(along * along - from.sqrMagnitude + least * least));
        }

        /// <summary>How far a wrist can go against <paramref name="way"/> before the arm is straighter than
        /// <paramref name="share"/>. Zero when it already is.</summary>
        internal float RoomToStraighten(Vector3 wrist, Vector3 way, float share)
        {
            Vector3 from = wrist - Shoulder;
            float reach = share * Length;
            float along = Vector3.Dot(from, way);
            float under = along * along - from.sqrMagnitude + reach * reach;
            return under > 0f ? Mathf.Max(0f, along + Mathf.Sqrt(under)) : 0f;
        }
    }

    internal Arm Right { get; private set; }
    internal Arm Left { get; private set; }
    /// <summary>The eyes, where the camera rides.</summary>
    internal Vector3 Eyes { get; private set; }
    /// <summary>Model units to metres in game (the size the game draws the body).</summary>
    internal float Scale { get; private set; }
    /// <summary>The right shoulder pocket: where a stock's butt rests on this body.</summary>
    internal Vector3 Pocket { get; private set; }
    /// <summary>What of the body the pocket is on, for the reports: the mesh and the bone that carries it there, or that it
    /// was taken from the bones alone.</summary>
    internal string PocketOn { get; private set; } = "the bones alone (none of its meshes could be read there)";
    /// <summary>Every surface the line to the pocket passes and how far ahead (cm in game; + out through it, - in), when
    /// Measure was asked to describe it (the hold check).</summary>
    internal string PocketThrough { get; private set; } = "nothing";

    // The frame the hips face in as the clip stands the body: the bakes keep a gun's place from the eyes in it (GunBakes).
    private Quaternion hipsFrame = Quaternion.identity;

    private HoldBody() { }

    /// <summary>A point of the body's space from its eyes, in metres in game (x right, y up, z ahead as the body looks).</summary>
    internal Vector3 FromEyes(Vector3 point) => (point - Eyes) * Scale;

    /// <summary>
    /// Where a gun's pose clip alone has the gun on this body (KINEMATION's weapon bone, in the body's space): where the bake
    /// measured it on the body the clip was made on (<paramref name="own"/>), and on any other body as far from its own eyes
    /// and as turned from where its hips face, in metres in game.
    /// </summary>
    internal KTransform PlaceOf(in GunBakes.Entry bake, bool own)
        => own || !bake.hasEyeFrame
            ? new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one)
            : new KTransform(Eyes + hipsFrame * (bake.gunFromEyes / Scale), hipsFrame * bake.gunTurnFromEyes, Vector3.one);

    /// <summary>
    /// The place inside the shoulder a stock's butt goes toward: from the joint toward the neck by PocketInward of the way
    /// across (along <paramref name="side"/>, the body's right), under it by PocketDrop of the joint-to-neck distance. The butt
    /// rests on the body's surface before it.
    /// </summary>
    public static Vector3 UnderPocket(Vector3 joint, Vector3 neck, Vector3 side, Vector3 up)
        => joint - side * (Vector3.Dot(joint - neck, side) * PocketInward) - up * (Vector3.Distance(joint, neck) * PocketDrop);

    /// <summary>
    /// The body itself round its right shoulder, where a stock rests (BodyFront tells it from the body's meshes). Read once
    /// for a body and given to every <see cref="Measure"/> of it, and read again when the body is dressed differently
    /// (BodyFront.Reads). Null for a body without the bones.
    /// </summary>
    internal static BodyFront ShoulderOf(Animator animator)
    {
        if (animator == null || !animator.isHuman) return null;
        var joint = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var neck = NeckOf(animator);
        if (joint == null || neck == null) return null;
        return BodyFront.Read(animator.transform, joint.position, Vector3.Distance(joint.position, neck.position) * SkinWithin);
    }

    /// <summary>
    /// Measures <paramref name="animator"/>'s body as <paramref name="pose"/> stands it: the clip is sampled on the body once
    /// and every bone put back. <paramref name="shoulder"/> is the body's surface round its right shoulder (ShoulderOf);
    /// without it the pocket is taken from the bones alone. Null for a body without the bones a hold needs (no humanoid
    /// arms, head or thighs). <paramref name="describe"/> also notes every surface the pocket's line passes (PocketThrough).
    /// The Animator must play as a player's body does, without root motion (PlayerBody.Build): with it on, a sampled clip
    /// turns the model's root by the clip's own turn and lays the pose out the other way under it, and a gun placed in the
    /// model's space was measured 13 and 28 cm off the hands (2026-11-15).
    /// </summary>
    internal static HoldBody Measure(Animator animator, AnimationClip pose, BodyFront shoulder, bool describe = false)
    {
        if (animator == null || pose == null || !animator.isHuman) return null;
        var root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var saved = new (Vector3 position, Quaternion rotation)[bones.Length];
        for (int i = 0; i < bones.Length; i++) saved[i] = (bones[i].localPosition, bones[i].localRotation);
        try
        {
            ClipSampler.Sample(pose, animator.gameObject, 0f);
            Vector3 In(Transform bone) => root.InverseTransformPoint(bone.position);
            var body = new HoldBody
            {
                Scale = Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.x)),
                Right = ArmOf(animator, root, false),
                Left = ArmOf(animator, root, true)
            };
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (!body.Right.Valid || !body.Left.Valid || head == null || leftLeg == null || rightLeg == null) return null;

            // The eyes as GunClipRetarget takes them on the reference body: the eye bones when they sit like eyes, else a
            // little above and ahead of the head bone, ahead the way the hips face.
            Vector3 hipsAhead = Vector3.Cross(In(rightLeg) - In(leftLeg), Up);
            if (hipsAhead.sqrMagnitude < 1e-8f) return null;
            hipsAhead.Normalize();
            body.hipsFrame = Quaternion.LookRotation(hipsAhead, Up);
            Vector3 headAt = In(head);
            body.Eyes = headAt + Up * .08f + hipsAhead * .1f;
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye != null && rightEye != null)
            {
                Vector3 l = In(leftEye), r = In(rightEye), middle = (l + r) * .5f, fromHead = middle - headAt;
                float apart = Vector3.Distance(l, r), above = fromHead.y, forward = Vector3.Dot(fromHead, hipsAhead);
                if (apart > .015f && apart < .2f && above > -.05f && above < .25f && forward > .01f && forward < .25f) body.Eyes = middle;
            }

            // Where a stock rests: where the body ends before the place inside the shoulder, as it stands now.
            var neck = NeckOf(animator);
            Vector3 joint = body.Right.Shoulder, neckAt = neck != null ? In(neck) : new Vector3(0f, joint.y, joint.z);
            float span = Vector3.Distance(joint, neckAt);
            Vector3 inside = UnderPocket(joint, neckAt, Vector3.Cross(Up, Ahead), Up);
            float ahead = span * PocketDepth;
            if (shoulder != null && shoulder.Measured)
            {
                shoulder.Pose();
                if (shoulder.Rest(root.TransformPoint(inside), root.TransformDirection(Ahead).normalized, root.TransformDirection(Up).normalized,
                        span * PocketRadius * body.Scale, out float met, out int vertex))
                {
                    ahead = met / body.Scale;
                    body.PocketOn = shoulder.Named(vertex);
                    if (describe) body.PocketThrough = shoulder.Through(root.TransformPoint(inside), root.TransformDirection(Ahead).normalized);
                }
            }
            body.Pocket = inside + Ahead * ahead;
            return body;
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(saved[i].position, saved[i].rotation);
        }
    }

    // The neck, or the nearest spine bone the body has.
    private static Transform NeckOf(Animator animator)
    {
        var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.Chest);
        return neck;
    }

    private static Arm ArmOf(Animator animator, Transform root, bool isLeft)
    {
        var upper = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var middle = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        if (upper == null || lower == null || hand == null) return default;
        Vector3 a = root.InverseTransformPoint(upper.position), b = root.InverseTransformPoint(lower.position);
        Vector3 c = root.InverseTransformPoint(hand.position);
        float length = Vector3.Distance(a, b) + Vector3.Distance(b, c);
        Vector3 knuckle = middle != null ? root.InverseTransformPoint(middle.position) : c + (c - b).normalized * (length * .17f);
        return new Arm(a, length, Vector3.Distance(c, knuckle), c, (knuckle - c).normalized);
    }
}
