using UnityEngine;

/// <summary>
/// The left arm led along a path drawn in Blender while its hand holds something: the bones it turns, the hand's
/// measures and the hand and finger maths. A thrown grenade (GrenadeArm) and a repair tool (RepairArm) share all of it;
/// each adds what it holds and when it plays. The fingers come in the order index, middle, ring, little, thumb, three
/// joints each from the knuckle out.
/// </summary>
public abstract class PathArm : MonoBehaviour
{
    // The fingers in order (index, middle, ring, little, thumb), three joints each from the knuckle out.
    protected const int Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4;
    protected static readonly HumanBodyBones[] FirstJoints =
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftThumbProximal,
    };

    // A finger's whole curl shared out over its joints, knuckle first: a real finger closes most at the knuckle and the
    // middle joint, and its last joint follows (the Blender workspace bends its drafts the same way).
    protected static readonly float[] CurlShare = { .4f, .35f, .25f };
    protected PlayerMovement movement;
    protected PlayerBodyHands hands;
    protected Animator animator;
    protected Transform eyes, upper, lower, hand, holder;
    protected readonly Transform[] fingers = new Transform[15];

    // The bones the throw turns, and how the frame had them, to ease from.
    protected readonly Transform[] turned = new Transform[18];
    protected readonly Quaternion[] framed = new Quaternion[18];
    protected int turnedCount, drivenFrame = -1;

    // A finger joint's own length axis (the rig's, from the index's middle joint), for a last joint with no tip bone; the
    // hand's own way out from the wrist, for a hand with no fingers.
    protected Vector3 jointAxis, handAxis;

    // own: the throw's own ease (armIn, armOut); weight: what is posed, never ahead of the hand lent off the gun.
    protected float armLength, handLength, own, weight;
    protected bool bound;

    // The hand's rotation that lays its run (wrist to middle knuckle) along the clip's and its palm facing as the clip's.
    protected void TurnHand(Vector3 along, Vector3 palm)
    {
        Vector3 alongNow = AlongNow();
        Vector3 palmNow = PalmNow(alongNow);
        if (alongNow.sqrMagnitude < 1e-10f || palmNow.sqrMagnitude < 1e-10f) return;
        hand.rotation = Quaternion.LookRotation(along, palm) * Quaternion.Inverse(Quaternion.LookRotation(alongNow, palmNow)) * hand.rotation;
    }

    protected Transform Knuckle() => fingers[Middle * 3] != null ? fingers[Middle * 3] : fingers[Index * 3];

    // Out from the wrist along the hand: to the middle knuckle (the index's on a hand without one), else the hand's own way.
    protected Vector3 AlongNow()
    {
        var knuckle = Knuckle();
        return knuckle != null ? knuckle.position - hand.position : hand.rotation * handAxis;
    }

    // The way the palm faces now, from the knuckles, as the clips were measured (grenade_tools.hand_frame): out of the
    // palm, square to the hand's run. A hand with no fingers is taken as palm down.
    protected Vector3 PalmNow(Vector3 along)
    {
        var index = fingers[Index * 3];
        var side = fingers[Little * 3] != null ? fingers[Little * 3] : fingers[Ring * 3] != null ? fingers[Ring * 3] : fingers[Middle * 3];
        Vector3 palm = index != null && side != null && side != index ? -Vector3.Cross(along, index.position - side.position) : Vector3.zero;
        if (palm.sqrMagnitude < 1e-12f) palm = Vector3.down;
        palm = Vector3.ProjectOnPlane(palm, along);
        return palm.sqrMagnitude > 1e-12f ? palm.normalized : Vector3.zero;
    }

    // Which way a finger joint runs now: to its next joint, else along the rig's joint axis.
    protected Vector3 JointWay(int finger, int joint)
    {
        var bone = fingers[finger * 3 + joint];
        var next = joint < 2 ? fingers[finger * 3 + joint + 1] : null;
        return next != null ? next.position - bone.position : bone.rotation * jointAxis;
    }

    internal static void Turn(Transform bone, Vector3 now, Vector3 want, float share)
    {
        if (now.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) return;
        var turn = Quaternion.FromToRotation(now, want);
        bone.rotation = (share >= 1f ? turn : Quaternion.Slerp(Quaternion.identity, turn, share)) * bone.rotation;
    }
}
