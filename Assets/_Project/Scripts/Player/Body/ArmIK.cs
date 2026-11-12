using UnityEngine;

/// <summary>
/// One humanoid arm (shoulder, upper arm, forearm, hand) bent so the wrist lands on a target, with the elbow toward a
/// hint. Hand orientation is given as the direction the fingers point and the direction the palm faces, measured
/// from the character's own finger bones, so the same target fits any character's hand.
/// </summary>
internal sealed class ArmIK
{
    private readonly Transform shoulder, upper, lower, hand;
    private readonly Quaternion handFromFrame;
    private readonly HandGrip grip;

    public Transform Hand => hand;
    public Transform Upper => upper;
    public Vector3 Elbow => lower.position;
    public float PalmLength { get; }
    public float PalmDepth => grip.PalmDepth;
    public float HalfPalmWidth => grip.HalfWidth;
    public Vector3 PalmPosition => hand.TransformPoint(grip.PalmLocal);
    public Vector3 PalmFacing => grip.PalmFacing;
    public Vector3 FingersForward => grip.FingersForward;

    /// <summary>The hand takes one of our gun-holding shapes (HandGrip.Wear), going to another as hold falls.</summary>
    public void WearShape(HandGrip.Shape on, HandGrip.Shape off, float hold, float weight) => grip.Wear(on, off, hold, weight);
    /// <summary>As WearShape, with a gun's own shape (GunGrips) held instead of a shared one.</summary>
    public void WearShape(GunGrips.Grip on, HandGrip.Shape off, float hold, float weight) => grip.WearCustom(on, off, hold, weight);
    /// <summary>The fingers as KINEMATION's clip has them on the gun (HandGrip.WearDigits).</summary>
    public void WearDigits(Vector3[] digits, float weight, GunSurface surface, float clearance, float mostOpen)
        => grip.WearDigits(digits, weight, surface, clearance, mostOpen);
    /// <summary>The fingers' ways now, and as a shape has them, in the hand's own frame (HandGrip.Ways, ShapeWays).</summary>
    public bool FingerWays(Vector3[] into) => grip.Ways(into);
    public bool ShapeWays(HandGrip.Shape shape, GunGrips.Grip custom, Vector3[] into) => grip.ShapeWays(shape, custom, into);
    public float Reach => Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);

    /// <summary>
    /// From this share of an arm's length a target eases in (Soften): Player.csv Hands.softReachFrom, set by PlayerBodyHands.
    /// </summary>
    internal static float SoftReachFrom = .93f;

    /// <summary>
    /// A target nearly at an arm's full length, eased in so the arm never snaps straight (soft IK): near full reach the
    /// elbow's angle runs away with the distance (1 mm short of straight bends it degrees), so a hand at the end of its reach
    /// jumped with every small move of the gun (the user, 2026-11-12: "팔이 이미 최대로 스트레치된 상황이라 팔이 조금만 움직여도 굉장히
    /// 들쑥날쑥"). Up to SoftReachFrom of <paramref name="length"/> from <paramref name="root"/> the target is as it is; past it
    /// the distance closes on the full length without ever reaching it, as smoothly as it went before.
    /// </summary>
    internal static Vector3 Soften(Vector3 root, Vector3 target, float length)
    {
        float from = Mathf.Clamp(SoftReachFrom, .5f, .999f) * length;
        Vector3 way = target - root;
        float distance = way.magnitude;
        if (distance <= from || distance < 1e-6f) return target;
        float room = length - from;
        float eased = from + room * (1f - Mathf.Exp(-(distance - from) / room));
        return root + way * (eased / distance);
    }

    private ArmIK(Transform shoulder, Transform upper, Transform lower, Transform hand, Quaternion handFromFrame, float palmLength, HandGrip grip)
    {
        this.shoulder = shoulder; this.upper = upper; this.lower = lower; this.hand = hand;
        this.handFromFrame = handFromFrame;
        PalmLength = palmLength;
        this.grip = grip;
    }

    public static ArmIK Create(Animator animator, bool left)
    {
        if (animator == null || !animator.isHuman) return null;
        Transform Bone(HumanBodyBones l, HumanBodyBones r) => animator.GetBoneTransform(left ? l : r);
        var upper = Bone(HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
        var lower = Bone(HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm);
        var hand = Bone(HumanBodyBones.LeftHand, HumanBodyBones.RightHand);
        if (upper == null || lower == null || hand == null) return null;
        var middle = Bone(HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal);
        var index = Bone(HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal);
        var little = Bone(HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal);
        // Knuckle positions do not move with finger curl, so this frame holds in any pose.
        Vector3 fingers = (middle != null ? middle.position : hand.position + (hand.position - lower.position)) - hand.position;
        Vector3 thumbSide = index != null && little != null ? index.position - little.position : animator.transform.forward;
        Vector3 palm = Vector3.Cross(fingers, thumbSide) * (left ? -1f : 1f);
        var frame = Quaternion.LookRotation(fingers, palm);
        return new ArmIK(Bone(HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder), upper, lower, hand,
            Quaternion.Inverse(frame) * hand.rotation, Mathf.Clamp(fingers.magnitude, .03f, .15f), HandGrip.Create(animator, left, fingers.normalized, palm.normalized));
    }

    /// <summary>Hand rotation whose fingers point along <paramref name="fingers"/> and whose palm faces <paramref name="palm"/>.</summary>
    public Quaternion HandRotation(Vector3 fingers, Vector3 palm) => Quaternion.LookRotation(fingers, palm) * handFromFrame;

    public Vector3 WristAtPalm(Vector3 palm, Quaternion rotation)
        => palm - rotation * Vector3.Scale(grip.PalmLocal, hand.lossyScale);

    public void PrepareBarGrip(float halfThickness, float palmClearance, float clearance)
        => grip.PrepareBar(halfThickness, palmClearance, clearance);

    public void GripBar(float halfThickness, float palmClearance, float clearance, float weight, float curl, float sectionAngle = 0f)
        => grip.Bar(halfThickness, palmClearance, clearance, weight, curl, sectionAngle);

    public void PrepareThumb(float underPalm, float bend) => grip.PrepareThumb(underPalm, bend);

    public void RelaxThumb(float weight) => grip.RelaxThumb(weight);

    /// <summary>Every finger joint back within a hand's reach (HandGrip.KeepHuman).</summary>
    public void KeepFingersHuman() => grip.KeepHuman();

    public void ClearLedgeSkin() => grip.ClearLedgeSkin();

    public void GripLedge(Collider surface, Vector3 fingers, Vector3 palm, Vector3 palmPosition, float clearance, float weight)
    {
        Quaternion rotation = HandRotation(fingers, palm);
        grip.Ledge(surface, fingers, palm, WristAtPalm(palmPosition, rotation), rotation, clearance, weight);
    }

    /// <summary>
    /// Bends the arm toward the wrist target. Out of reach, the shoulder turns toward it by up to
    /// <paramref name="shoulderAngle"/> degrees, then the arm straightens; with <paramref name="soft"/> (the gun holds) it eases
    /// toward straight instead (Soften). The elbow comes round to the hint with the weight, like everything else, so a hold
    /// fading in or out never flips it.
    /// </summary>
    public void Solve(Vector3 target, Quaternion rotation, Vector3 hint, float weight, float rotationWeight, float shoulderAngle, bool soft = false)
    {
        if (weight <= 0f) return;
        target = Vector3.Lerp(hand.position, target, weight);
        float reach = Reach;
        if (shoulder != null && shoulderAngle > 0f)
        {
            float over = Vector3.Distance(upper.position, target) - reach * .98f;
            if (over > 0f)
            {
                Quaternion.FromToRotation(upper.position - shoulder.position, target - shoulder.position).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                float turn = Mathf.Clamp(angle, -shoulderAngle, shoulderAngle) * Mathf.Clamp01(over / .1f);
                if (axis.sqrMagnitude > .0001f) shoulder.rotation = Quaternion.AngleAxis(turn, axis) * shoulder.rotation;
            }
        }

        // Within reach the arm bends to the target; holding a gun, at the end of its reach it eases in rather than snapping
        // straight (a ledge or a bar hold keeps its full reach).
        TwoBoneIK.Solve(upper, lower, hand, soft ? Soften(upper.position, target, reach) : target, hint, weight);
        hand.rotation = Quaternion.Slerp(hand.rotation, rotation, weight * rotationWeight);
    }
}
