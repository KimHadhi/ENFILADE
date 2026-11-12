using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The pointing arm of a held F (PlayerPoint; the user, 2026-11-12: "핑 찍을 때 남는 손으로 손가락질 하는 모션"): the
/// left hand, the one free while a gun is held (the gun stays in the right), leaves what it holds and points where the
/// player looks, the index out and the other fingers in a fist, the thumb tucked over them. When the look is on a friend
/// within the arm's reach, the fingertip goes to them: into a zone that gives (a cheek, the belly, an arm, a shoulder,
/// the top of the head) by that zone's press, eased in (Poke.csv per zone: press, pressTime), and the skin there dents
/// under it (SkinPoke); anywhere else, or on a friend who switched pokes off, it stops just short (Point.hover). Out of
/// reach it simply points.
/// The same on every PC: the owner's first-person body and the copies everyone else sees run this from the same point
/// mark and look, so a poke shows alike everywhere (not to the millimetre: each PC has the look a moment late).
/// Procedural and rig-agnostic, for any humanoid a modder brings: the hand is turned so the line from the wrist to the
/// index knuckle runs along the pointing way and the palm faces down and in (Point.palmIn), each finger joint is aimed in
/// world space from where its next joint is (FromToRotation), and the arm bends by TwoBoneIK, the elbow down and out, to
/// put the wrist where the fingertip lands. It runs after FPS Animation Framework, the hands, the gun and the callout
/// gestures (GunFramework + 6) and before the cloth, so a sleeve and hair follow; the first-person camera was placed
/// earlier, so the view never moves with the arm.
/// The hand leaves the gun through the one shared way for every off-hand use (the manager's decision, 2026-11-12):
/// PlayerBodyHands.LendOffHand, so the left hand's gun contacts, grip and fingers let go over the hands' own blend time
/// as the point comes in, and take the gun again as it goes; the pose eases by how far the hand is lent
/// (OffHandLent), never ahead of it, and out over Point.out at most. A G throw has the off hand before a point
/// (PlayerGrenades.HandBusy): while the throw holds it, nothing of the point is posed.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 6)]
public sealed class PointArm : MonoBehaviour
{
    // The fingers in order (index, middle, ring, little, thumb), three joints each from the knuckle out.
    private const int Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4;
    private static readonly HumanBodyBones[] FirstJoints =
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftThumbProximal,
    };

    private PlayerMovement movement;
    private PlayerPoint point;
    private PlayerBodyHands hands;
    private Animator animator;
    private Transform eyes, upper, lower, hand, indexTip;
    private readonly Transform[] fingers = new Transform[15];
    // The bones the point turns, and how the frame had them, to ease from.
    private readonly Transform[] turned = new Transform[18];
    private readonly Quaternion[] framed = new Quaternion[18];
    private int turnedCount;
    // A joint's own length axis (the rig's convention, from the index's middle joint), for a last joint with no tip
    // bone; and the hand's own way out from the wrist, for a hand with no fingers.
    private Vector3 jointAxis, handAxis;
    // own: the point's own ease (Point.in, Point.out); weight: what is posed, never ahead of the hand lent off the gun.
    private float own, weight, press, lostFor;
    private Vector3 reached, touchLocal;
    private bool bound, hasReached, hasTouch, touching;
    private PokeHit touch;

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || GamePause.IsPaused) return;
        if (!bound && !Bind()) return;
        // A G throw has the off hand first: while it does, the point gives the hand up at once.
        bool thrown = PlayerGrenades.HandBusy(movement);
        bool on = point != null && point.Pointing && Free() && !thrown;
        // The left hand off the gun through PlayerBodyHands, as every off-hand use takes it; the same share each frame
        // costs nothing.
        if (hands != null) hands.LendOffHand(this, on ? 1f : 0f);
        float blend = on ? PokeTables.Number("Point.in", .14f) : PokeTables.Number("Point.out", .2f);
        own = Mathf.MoveTowards(own, on ? 1f : 0f, dt / Mathf.Max(.01f, blend));
        weight = thrown ? 0f : hands != null ? Mathf.Min(own, hands.OffHandLent) : own;
        if (weight <= 0f)
        {
            Let();
            hasReached = hasTouch = false;
            press = 0f;
            return;
        }
        Pose(dt, on);
    }

    private void OnDisable()
    {
        Let();
        if (hands != null) hands.LendOffHand(this, 0f);
    }

    // The fingertip leaves whoever it pressed.
    private void Let()
    {
        if (!touching) return;
        touching = false;
        SkinPoke.Lift(movement);
    }

    private bool Bind()
    {
        movement = GetComponentInParent<PlayerMovement>();
        point = movement != null ? movement.GetComponent<PlayerPoint>() : null;
        hands = movement != null ? movement.GetComponent<PlayerBodyHands>() : null;
        animator = GetComponent<Animator>();
        if (movement == null || point == null || animator == null || !animator.isHuman) return false;
        upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (upper == null || lower == null || hand == null) return false;
        turnedCount = 0;
        turned[turnedCount++] = upper;
        turned[turnedCount++] = lower;
        turned[turnedCount++] = hand;
        for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = animator.GetBoneTransform(FirstJoints[finger] + joint);
                fingers[finger * 3 + joint] = bone;
                if (bone != null) turned[turnedCount++] = bone;
            }
        var middleJoint = fingers[Index * 3 + 1];
        var lastJoint = fingers[Index * 3 + 2];
        indexTip = lastJoint != null && lastJoint.childCount > 0 ? lastJoint.GetChild(0) : null;
        jointAxis = middleJoint != null && lastJoint != null
            ? Quaternion.Inverse(middleJoint.rotation) * (lastJoint.position - middleJoint.position).normalized
            : Vector3.forward;
        handAxis = Quaternion.Inverse(hand.rotation) * (hand.position - lower.position).normalized;
        bound = true;
        return true;
    }

    // The body can lift an arm: alive, up, standing or crouched (walking too); not held, climbing, sliding or diving.
    private bool Free()
    {
        if (movement.IsDead || movement.IsDowned || movement.IsHeld) return false;
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching;
    }

    private void Pose(float dt, bool on)
    {
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        for (int i = 0; i < turnedCount; i++) framed[i] = turned[i].localRotation;
        var frame = animator.transform;
        Vector3 shoulder = upper.position, origin = eyes.position, look = eyes.forward;

        // What the finger is on: a friend's body along the look (held a moment through a flicker), else nothing.
        PokeHit hit = default;
        bool found = on && Touch(origin, look, PokeTables.Number("Point.range", 1.3f), dt, out hit);
        // The way the finger goes from the shoulder: at what it is on, else where the look goes far off.
        Vector3 way = (found ? hit.Point : origin + look * 10f) - shoulder;
        way = way.sqrMagnitude > 1e-6f ? way.normalized : look;

        // The hand along that way, the palm down and turned in, the index out and the rest in a fist.
        Vector3 palm = WantedPalm(way, frame);
        Quaternion handGoal = HandAlong(way, palm);
        hand.rotation = handGoal;
        Fingers(way, palm);
        Vector3 tipOffset = Fingertip() - hand.position;
        float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) + tipOffset.magnitude;

        // Where the fingertip goes: into a zone that gives, eased in; just short of anything else it is on; or out along
        // the way.
        Vector3 goal;
        bool presses = false;
        if (found && Vector3.Distance(shoulder, hit.Point) <= reach * PokeTables.Number("Point.reach", .97f))
        {
            if (hit.Pokes)
            {
                var feel = PokeTables.Of(hit.Zone);
                press = Mathf.MoveTowards(press, feel.Press, dt * feel.Press / Mathf.Max(.01f, feel.PressTime));
                goal = hit.Point + way * press;
                presses = true;
            }
            else
            {
                press = 0f;
                goal = hit.Point - way * PokeTables.Number("Point.hover", .015f);
            }
        }
        else
        {
            press = 0f;
            goal = shoulder + way * (reach * PokeTables.Number("Point.extend", .9f));
        }
        float follow = PokeTables.Number("Point.follow", .05f);
        reached = hasReached && follow > 0f ? Vector3.Lerp(reached, goal, 1f - Mathf.Pow(.5f, dt / follow)) : goal;
        hasReached = true;

        // The arm puts the wrist where the fingertip lands, the elbow down and out (the left arm's out is the body's left).
        Vector3 hint = shoulder + (-frame.up * .8f - frame.right * .55f - frame.forward * .1f) * reach;
        TwoBoneIK.Solve(upper, lower, hand, reached - tipOffset, hint);
        hand.rotation = handGoal;

        if (weight < 1f)
            for (int i = 0; i < turnedCount; i++) turned[i].localRotation = Quaternion.Slerp(framed[i], turned[i].localRotation, weight);

        if (presses && on && weight >= .99f)
        {
            touching = true;
            SkinPoke.Press(movement, hit, Fingertip(), way);
        }
        else Let();
    }

    // The friend the finger is on: found along the look this frame, else the last one for a moment (Touch.lose), so a
    // look flickering off the edge of a cheek doesn't drop the poke.
    private bool Touch(Vector3 origin, Vector3 look, float range, float dt, out PokeHit hit)
    {
        if (SkinPoke.Find(movement, origin, look, range, out hit))
        {
            touch = hit;
            touchLocal = hit.Bone != null ? hit.Bone.InverseTransformPoint(hit.Point) : hit.Point;
            hasTouch = true;
            lostFor = 0f;
            return true;
        }
        lostFor += dt;
        if (!hasTouch || touch.Bone == null || lostFor > PokeTables.Number("Touch.lose", .08f))
        {
            hasTouch = false;
            return false;
        }
        hit = new PokeHit(touch.Target, touch.Body, touch.Bone.TransformPoint(touchLocal), touch.Bone, touch.Zone, touch.Allowed, touch.Distance);
        return true;
    }

    // The way the pointing palm faces: down, turned in toward the body's middle by Point.palmIn (1: the thumb on top).
    private static Vector3 WantedPalm(Vector3 way, Transform frame)
    {
        Vector3 palm = Vector3.ProjectOnPlane(Vector3.Lerp(Vector3.down, frame.right, PokeTables.Number("Point.palmIn", .55f)), way);
        if (palm.sqrMagnitude < 1e-4f) palm = Vector3.ProjectOnPlane(frame.forward, way);
        return palm.sqrMagnitude > 1e-8f ? palm.normalized : Vector3.down;
    }

    // The hand's rotation that lays the wrist-to-index-knuckle line along the way and the palm facing as wanted.
    private Quaternion HandAlong(Vector3 way, Vector3 palm)
    {
        Vector3 along = Along();
        Vector3 palmNow = PalmNow(along);
        if (along.sqrMagnitude < 1e-10f || palmNow.sqrMagnitude < 1e-10f) return hand.rotation;
        return Quaternion.LookRotation(way, -palm) * Quaternion.Inverse(Quaternion.LookRotation(along, -palmNow)) * hand.rotation;
    }

    // Out from the wrist along the hand: to the index knuckle, else the hand's own way out.
    private Vector3 Along()
    {
        var knuckle = fingers[Index * 3];
        return knuckle != null ? knuckle.position - hand.position : hand.rotation * handAxis;
    }

    // The way the palm faces now, from the knuckles: across them from the little finger to the index, the palm on the
    // side a left hand has it. A hand with no fingers is taken as palm down.
    private Vector3 PalmNow(Vector3 along)
    {
        var index = fingers[Index * 3];
        var side = fingers[Little * 3] != null ? fingers[Little * 3] : fingers[Ring * 3] != null ? fingers[Ring * 3] : fingers[Middle * 3];
        Vector3 palm = index != null && side != null ? -Vector3.Cross(along, index.position - side.position) : Vector3.zero;
        if (palm.sqrMagnitude < 1e-12f) palm = Vector3.ProjectOnPlane(Vector3.down, along);
        return palm.sqrMagnitude > 1e-12f ? palm.normalized : Vector3.zero;
    }

    // The index nearly straight along the way (Point.indexBend a joint), the middle, ring and little fingers closed into
    // a fist (Point.fist), the thumb tucked over them (Point.thumb).
    private void Fingers(Vector3 way, Vector3 palm)
    {
        // Turning about this brings a finger from the way toward the palm; for a left hand it also runs from the little
        // finger's side to the index's.
        Vector3 curl = Vector3.Cross(way, palm);
        if (curl.sqrMagnitude < 1e-10f) return;
        curl.Normalize();
        float bend = PokeTables.Number("Point.indexBend", 6f);
        float fist = Mathf.Clamp01(PokeTables.Number("Point.fist", 1f));
        Finger(Index, way, curl, bend, bend, bend, 1f);
        Finger(Middle, way, curl, 80f, 95f, 55f, fist);
        Finger(Ring, way, curl, 82f, 95f, 55f, fist);
        Finger(Little, way, curl, 85f, 95f, 55f, fist);
        float thumb = Mathf.Clamp01(PokeTables.Number("Point.thumb", .8f));
        if (thumb <= 0f || fingers[Thumb * 3] == null) return;
        // Across the palm toward the little finger and a little forward, over the closed fingers.
        Vector3 over = (way * .35f + palm * .55f - curl * .75f).normalized;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? 20f : 25f, bendAxis.normalized) * over;
            Turn(bone, JointWay(Thumb, joint), over, thumb);
        }
    }

    // One finger's joints, each aimed at the way turned toward the palm by its angle more than the last, by share.
    private void Finger(int finger, Vector3 way, Vector3 curl, float first, float second, float third, float share)
    {
        if (share <= 0f) return;
        float angle = 0f;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = fingers[finger * 3 + joint];
            if (bone == null) return;
            angle += joint == 0 ? first : joint == 1 ? second : third;
            Turn(bone, JointWay(finger, joint), Quaternion.AngleAxis(angle * share, curl) * way, 1f);
        }
    }

    // Which way a finger joint runs now: to its next joint, to the index's tip bone, else along the rig's joint axis.
    private Vector3 JointWay(int finger, int joint)
    {
        var bone = fingers[finger * 3 + joint];
        var next = joint < 2 ? fingers[finger * 3 + joint + 1] : finger == Index ? indexTip : null;
        return next != null ? next.position - bone.position : bone.rotation * jointAxis;
    }

    private static void Turn(Transform bone, Vector3 now, Vector3 want, float share)
    {
        if (now.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) return;
        var turn = Quaternion.FromToRotation(now, want);
        bone.rotation = (share >= 1f ? turn : Quaternion.Slerp(Quaternion.identity, turn, share)) * bone.rotation;
    }

    // The index's tip: its tip bone, else past its last joint by most of the middle joint's length; a hand with no
    // fingers, its own way out by most of the forearm's length.
    private Vector3 Fingertip()
    {
        if (indexTip != null) return indexTip.position;
        var middleJoint = fingers[Index * 3 + 1];
        var lastJoint = fingers[Index * 3 + 2];
        if (lastJoint != null)
        {
            float length = middleJoint != null ? Vector3.Distance(middleJoint.position, lastJoint.position) : .02f;
            return lastJoint.position + lastJoint.rotation * jointAxis * (length * .85f);
        }
        return hand.position + hand.rotation * handAxis * (Vector3.Distance(lower.position, hand.position) * .7f);
    }
}
