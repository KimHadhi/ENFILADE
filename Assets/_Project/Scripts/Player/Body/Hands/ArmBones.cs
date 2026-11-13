using UnityEngine;

/// <summary>
/// One arm as code poses it over the animation: its three bones and fifteen finger joints (any humanoid's, by the
/// Animator's own bone names), how the frame had them (to ease a pose in from), the rig's own axes for a hand with no
/// fingers or a last joint with no tip, the arm's and the hand's lengths, and the hand and finger maths such a pose
/// uses. A thrown grenade and a repair tool (PathArm), the guard pose (GuardPoseArm), the pointing hand (PointArm) and
/// the execution knife (ExecutionKnifeArm, a left and a right) each hold one. Before 0.9.15 each kept its own copy of
/// the bones, of the binding and of the maths, the same word for word.
/// The fingers come in the order index, middle, ring, little, thumb, three joints each from the knuckle out:
/// Fingers[finger * 3 + joint].
/// </summary>
public sealed class ArmBones
{
    public const int Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4;

    /// <summary>A finger's whole curl shared out over its joints, knuckle first: a real finger closes most at the knuckle
    /// and the middle joint, and its last joint follows (the Blender workspaces bend their drafts the same way).</summary>
    public static readonly float[] CurlShare = { .4f, .35f, .25f };

    private static readonly HumanBodyBones[] LeftFirstJoints =
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftThumbProximal,
    };
    private static readonly HumanBodyBones[] RightFirstJoints =
    {
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightThumbProximal,
    };

    /// <summary>A right arm: its palm lies on the other side of its knuckles, and its thumb closes the other way.</summary>
    public readonly bool Right;
    public Transform Upper, Lower, Hand;
    public readonly Transform[] Fingers = new Transform[15];

    // The bones a pose turns, and how the frame had them, to ease from (Keep, Ease).
    public readonly Transform[] Turned = new Transform[18];
    public readonly Quaternion[] Framed = new Quaternion[18];
    public int TurnedCount;

    // A finger joint's own length axis (the rig's, from the index's middle joint), for a last joint with no tip bone; the
    // hand's own way out from the wrist, for a hand with no fingers.
    public Vector3 JointAxis, HandAxis;

    /// <summary>Shoulder to elbow to wrist, and wrist to the middle knuckle (m), as the model stood when it was bound.</summary>
    public float ArmLength, HandLength;

    public ArmBones(bool right = false) { Right = right; }

    /// <summary>Whether the arm has a length to pose in (a model scaled to nothing has not).</summary>
    public bool HasLength => ArmLength > 1e-3f && HandLength > 1e-4f;

    /// <summary>Finds the arm on a humanoid. False when it has no upper arm, forearm or hand; fingers may be missing.</summary>
    public bool Bind(Animator animator)
    {
        Upper = animator.GetBoneTransform(Right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        Lower = animator.GetBoneTransform(Right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        Hand = animator.GetBoneTransform(Right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (Upper == null || Lower == null || Hand == null) return false;
        TurnedCount = 0;
        Turned[TurnedCount++] = Upper;
        Turned[TurnedCount++] = Lower;
        Turned[TurnedCount++] = Hand;
        var first = Right ? RightFirstJoints : LeftFirstJoints;
        for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = animator.GetBoneTransform(first[finger] + joint);
                Fingers[finger * 3 + joint] = bone;
                if (bone != null) Turned[TurnedCount++] = bone;
            }
        var middleJoint = Fingers[Index * 3 + 1];
        var lastJoint = Fingers[Index * 3 + 2];
        JointAxis = middleJoint != null && lastJoint != null
            ? Quaternion.Inverse(middleJoint.rotation) * (lastJoint.position - middleJoint.position).normalized
            : Vector3.forward;
        HandAxis = Quaternion.Inverse(Hand.rotation) * (Hand.position - Lower.position).normalized;
        ArmLength = Vector3.Distance(Upper.position, Lower.position) + Vector3.Distance(Lower.position, Hand.position);
        var knuckle = Knuckle();
        HandLength = knuckle != null ? Vector3.Distance(Hand.position, knuckle.position) : ArmLength * .17f;
        return true;
    }

    /// <summary>Remembers how the frame has the arm, before a pose turns it.</summary>
    public void Keep()
    {
        for (int i = 0; i < TurnedCount; i++) Framed[i] = Turned[i].localRotation;
    }

    /// <summary>Lets the pose in by this much: at less than 1 each bone stands between what the frame had and the pose.</summary>
    public void Ease(float weight)
    {
        if (weight < 1f)
            for (int i = 0; i < TurnedCount; i++) Turned[i].localRotation = Quaternion.Slerp(Framed[i], Turned[i].localRotation, weight);
    }

    /// <summary>
    /// Turns the hand so its run (wrist to middle knuckle) lies along this and its palm faces that. Left as it is when
    /// either way, or the hand's own, has no length (a key with nothing in it).
    /// </summary>
    public void TurnHand(Vector3 along, Vector3 palm)
    {
        Vector3 alongNow = AlongNow();
        Vector3 palmNow = PalmNow(alongNow);
        if (alongNow.sqrMagnitude < 1e-10f || palmNow.sqrMagnitude < 1e-10f || along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return;
        Hand.rotation = Quaternion.LookRotation(along, palm) * Quaternion.Inverse(Quaternion.LookRotation(alongNow, palmNow)) * Hand.rotation;
    }

    public Transform Knuckle() => Fingers[Middle * 3] != null ? Fingers[Middle * 3] : Fingers[Index * 3];

    /// <summary>Out from the wrist along the hand: to the middle knuckle (the index's on a hand without one), else the hand's own way.</summary>
    public Vector3 AlongNow()
    {
        var knuckle = Knuckle();
        return knuckle != null ? knuckle.position - Hand.position : Hand.rotation * HandAxis;
    }

    /// <summary>
    /// The way the palm faces now, from the knuckles, as the clips were measured (grenade_tools.hand_frame): out of the
    /// palm, square to the hand's run. The index's side and the little finger's side cross the other way round on a right
    /// hand, so its cross is taken the other way. A hand with no fingers is taken as palm down.
    /// </summary>
    public Vector3 PalmNow(Vector3 along)
    {
        var index = Fingers[Index * 3];
        var outer = Fingers[Little * 3] != null ? Fingers[Little * 3] : Fingers[Ring * 3] != null ? Fingers[Ring * 3] : Fingers[Middle * 3];
        Vector3 palm = index != null && outer != null && outer != index
            ? (Right ? 1f : -1f) * Vector3.Cross(along, index.position - outer.position) : Vector3.zero;
        if (palm.sqrMagnitude < 1e-12f) palm = Vector3.down;
        palm = Vector3.ProjectOnPlane(palm, along);
        return palm.sqrMagnitude > 1e-12f ? palm.normalized : Vector3.zero;
    }

    /// <summary>Which way a finger joint runs now: to its next joint, else along the rig's joint axis.</summary>
    public Vector3 JointWay(int finger, int joint)
    {
        var bone = Fingers[finger * 3 + joint];
        var next = joint < 2 ? Fingers[finger * 3 + joint + 1] : null;
        return next != null ? next.position - bone.position : bone.rotation * JointAxis;
    }

    /// <summary>
    /// The four fingers closed by a curl (degrees), shared over their joints; the thumb across the palm and on round as
    /// far as its own close goes (degrees, all the way at 90), mirrored for a right hand.
    /// </summary>
    public void Close(Vector3 along, Vector3 palm, float curl, float thumb)
    {
        Vector3 axis = Vector3.Cross(along, palm);
        if (axis.sqrMagnitude < 1e-10f) return;
        axis.Normalize();
        for (int finger = Index; finger <= Little; finger++)
        {
            float angle = 0f;
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = Fingers[finger * 3 + joint];
                if (bone == null) break;
                angle += curl * CurlShare[joint];
                Turn(bone, JointWay(finger, joint), Quaternion.AngleAxis(angle, axis) * along, 1f);
            }
        }
        float close = Mathf.Clamp01(thumb / 90f);
        if (close <= 0f || Fingers[Thumb * 3] == null) return;
        Vector3 over = (along * .5f + palm * .6f + (Right ? 1f : -1f) * axis * .45f).normalized;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = Fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? 15f : 20f, bendAxis.normalized) * over;
            Turn(bone, JointWay(Thumb, joint), over, close);
        }
    }

    /// <summary>Turns a bone so the way it runs now becomes the way wanted, all the way or by a share of it.</summary>
    public static void Turn(Transform bone, Vector3 now, Vector3 want, float share = 1f)
    {
        if (now.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) return;
        var turn = Quaternion.FromToRotation(now, want);
        bone.rotation = (share >= 1f ? turn : Quaternion.Slerp(Quaternion.identity, turn, share)) * bone.rotation;
    }
}
