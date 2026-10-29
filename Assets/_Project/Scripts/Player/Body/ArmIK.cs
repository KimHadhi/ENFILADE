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

        TwoBoneIK.Solve(upper, lower, hand, target, hint);
        hand.rotation = Quaternion.Slerp(hand.rotation, rotation, weight * rotationWeight);
    }
}
