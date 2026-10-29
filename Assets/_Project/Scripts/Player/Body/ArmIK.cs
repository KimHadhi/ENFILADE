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

    public Transform Hand => hand;
    public Transform Upper => upper;
    public float Reach => Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);

    private ArmIK(Transform shoulder, Transform upper, Transform lower, Transform hand, Quaternion handFromFrame)
    {
        this.shoulder = shoulder; this.upper = upper; this.lower = lower; this.hand = hand;
        this.handFromFrame = handFromFrame;
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
            Quaternion.Inverse(frame) * hand.rotation);
    }

    /// <summary>Hand rotation whose fingers point along <paramref name="fingers"/> and whose palm faces <paramref name="palm"/>.</summary>
    public Quaternion HandRotation(Vector3 fingers, Vector3 palm) => Quaternion.LookRotation(fingers, palm) * handFromFrame;

    /// <summary>
    /// Bends the arm toward the wrist target. Out of reach, the shoulder turns toward it by up to
    /// <paramref name="shoulderAngle"/> degrees, then the arm straightens.
    /// </summary>
    public void Solve(Vector3 target, Quaternion rotation, Vector3 hint, float weight, float rotationWeight, float shoulderAngle)
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

        Vector3 a = upper.position, b = lower.position, c = hand.position;
        Vector3 ab = b - a, bc = c - b, ac = c - a, at = target - a;
        float abLength = ab.magnitude, bcLength = bc.magnitude;
        float atLength = Mathf.Clamp(at.magnitude, .001f, (abLength + bcLength) * .9999f);
        float oldElbow = TriangleAngle(ac.magnitude, abLength, bcLength);
        float newElbow = TriangleAngle(atLength, abLength, bcLength);
        Vector3 axisElbow = Vector3.Cross(ab, bc);
        if (axisElbow.sqrMagnitude < 1e-8f) axisElbow = Vector3.Cross(hint - a, bc);
        if (axisElbow.sqrMagnitude < 1e-8f) axisElbow = Vector3.Cross(at, Vector3.up);
        if (axisElbow.sqrMagnitude > 1e-8f)
            lower.rotation = Quaternion.AngleAxis((oldElbow - newElbow) * Mathf.Rad2Deg, axisElbow.normalized) * lower.rotation;
        upper.rotation = Quaternion.FromToRotation(hand.position - a, at) * upper.rotation;

        // Swing the elbow round the shoulder-wrist line toward the hint.
        Vector3 line = (hand.position - a).normalized;
        Vector3 elbow = Vector3.ProjectOnPlane(lower.position - a, line), toward = Vector3.ProjectOnPlane(hint - a, line);
        if (elbow.sqrMagnitude > 1e-6f && toward.sqrMagnitude > 1e-6f)
            upper.rotation = Quaternion.FromToRotation(elbow, toward) * upper.rotation;

        hand.rotation = Quaternion.Slerp(hand.rotation, rotation, weight * rotationWeight);
    }

    // The angle (radians) between sides b and c of a triangle whose third side is a.
    private static float TriangleAngle(float a, float b, float c)
        => Mathf.Acos(Mathf.Clamp((b * b + c * c - a * a) / Mathf.Max(1e-6f, 2f * b * c), -1f, 1f));
}
