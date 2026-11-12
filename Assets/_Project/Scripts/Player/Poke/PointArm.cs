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
/// The hand's shape is the one drawn in Blender (Tools/Blender/point_hand.py writes PointHand.csv: each finger joint's way
/// in the hand's own frame, which fits any humanoid), the drawn index laid along the pointing way; without that table,
/// Poke.csv's Point.* angles make it. An F tap's ping points the same hand at what it pinged for a moment (PlayerPoint.Flash).
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
    // The hits of one line, reused (FirstSolid).
    private static readonly RaycastHit[] lineHits = new RaycastHit[24];
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
    // Where the fingertip is going, eased, in the eyes' space; the fingertip's reach past the wrist, from the last pose.
    private Vector3 reachedInView, touchLocal;
    private float tipLength;
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
        Vector3 shoulder = upper.position, origin = eyes.position, look = eyes.forward;
        // The view's own frame, never the hips': strafing turns the hips toward the step, and an arm laid out from them
        // swung its elbow forward and the hand across the chest (the user, 2026-11-12: "오른쪽으로 움직일 땐 팔이
        // 가슴쪽으로 오는데? 이 일정하지가 않아서").
        Vector3 viewRight = Vector3.Cross(Vector3.up, look);
        viewRight = viewRight.sqrMagnitude > 1e-6f ? viewRight.normalized : animator.transform.right;
        Vector3 viewUp = Vector3.Cross(look, viewRight);
        Vector3 viewAhead = Vector3.Cross(viewRight, Vector3.up);
        float armLength = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        float reach = armLength + (tipLength > 0f ? tipLength : armLength * .35f);

        // Where the crosshair's line really ends, as a gun's aim does (the user, 2026-11-12: "손찌검 하는 것도 총처럼
        // 조준선이 닿는 곳으로 검지 손가락 끝 방향이 가게끔 … 벽 뚫는 것도 막으면서"): the first wall, crate or zombie on it
        // (Point.aimRange at most). Players are met by their bodies' own surface (PokeBody), never by their movement
        // capsules, which stand a hand's width off the skin.
        float hover = PokeTables.Number("Point.hover", .015f);
        float aimRange = Mathf.Max(1f, PokeTables.Number("Point.aimRange", 60f));
        float wallAt = FirstSolid(origin, look, aimRange, out RaycastHit wall) ? wall.distance : aimRange;

        // What the finger is on: a friend's body along the look (held a moment through a flicker), in front of any wall
        // (none is poked or pointed at through one), within the arm's reach to touch.
        PokeHit hit = default;
        bool found = on && Touch(origin, look, PokeTables.Number("Point.range", 1.3f), dt, out hit) && hit.Distance <= wallAt + .01f;
        bool touches = found && Vector3.Distance(shoulder, hit.Point) <= reach * PokeTables.Number("Point.reach", .97f);
        // While it only points, the fingertip rests at a spot fixed in the view, low and left of the crosshair (Point.ahead,
        // Point.left, Point.down): it stays put on the screen however the body walks, strafes or turns. A wall closer than
        // that spot holds it just short of the wall (Point.hover), so a finger never goes into one.
        Vector3 spot = origin + look * PokeTables.Number("Point.ahead", .55f) - viewRight * PokeTables.Number("Point.left", .13f)
            - viewUp * PokeTables.Number("Point.down", .12f);
        Vector3 toSpot = spot - origin;
        float spotDistance = toSpot.magnitude;
        if (spotDistance > 1e-3f && FirstSolid(origin, toSpot / spotDistance, spotDistance + hover, out RaycastHit near))
            spot = origin + toSpot / spotDistance * Mathf.Max(.05f, near.distance - hover);
        // From there the finger points at what the crosshair's line ends on, as a gun converges on it: a friend out of
        // reach, what a tap's ping pinged, else the wall or the far end of the look.
        Vector3 aim = found ? hit.Point : point.FlashTarget(out Vector3 pinged) ? pinged : origin + look * wallAt;
        Vector3 way = touches ? hit.Point - shoulder : aim - spot;
        way = way.sqrMagnitude > .0025f ? way.normalized : look;

        // The hand along that way, the palm down and turned in, bent at the wrist as Point.wristBend/wristSide say: the
        // hand drawn in Blender (PointHand.csv) if there is one, else the index out and the rest in a fist by Point.*.
        Vector3 palm = WantedPalm(way, viewRight, viewAhead);
        Vector3 handWay = Wrist(way, palm);
        Vector3 handPalm = Vector3.ProjectOnPlane(palm, handWay);
        handPalm = handPalm.sqrMagnitude > 1e-8f ? handPalm.normalized : palm;
        Quaternion handGoal;
        if (PokeTables.HandPose(out var drawn))
        {
            var drawnTurn = DrawnFrame(drawn, handWay, handPalm);
            handGoal = HandAlong(drawnTurn * Vector3.right, -(drawnTurn * Vector3.forward));
            hand.rotation = handGoal;
            DrawnFingers(drawn, drawnTurn);
        }
        else
        {
            handGoal = HandAlong(handWay, handPalm);
            hand.rotation = handGoal;
            Fingers(handWay, handPalm);
        }
        Vector3 tipOffset = Fingertip() - hand.position;
        tipLength = tipOffset.magnitude;

        // Where the fingertip goes: into a zone that gives, eased in; just short of anything else it touches; or the
        // spot in the view.
        Vector3 goal;
        bool presses = false;
        if (touches)
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
            goal = spot;
        }
        // Eased in the view's own space (Point.follow): walking, strafing or turning never drag the hand behind, as easing
        // in the world did (running, a hand trailed 30 cm toward the chest); only a change of where it goes eases.
        float follow = PokeTables.Number("Point.follow", .05f);
        Vector3 goalInView = eyes.InverseTransformPoint(goal);
        reachedInView = hasReached && follow > 0f ? Vector3.Lerp(reachedInView, goalInView, 1f - Mathf.Pow(.5f, dt / follow)) : goalInView;
        hasReached = true;
        Vector3 reached = eyes.TransformPoint(reachedInView);

        // The arm puts the wrist where the fingertip lands, the elbow down, out to the left and back from the view by
        // Point.elbowDown/Out/Back.
        Vector3 hint = shoulder + (-Vector3.up * PokeTables.Number("Point.elbowDown", .8f) - viewRight * PokeTables.Number("Point.elbowOut", .55f)
            - viewAhead * PokeTables.Number("Point.elbowBack", .1f)) * reach;
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

    // The first solid thing a line meets that isn't a player (a wall, a crate, a zombie): players are met by their bodies'
    // surfaces instead (PokeBody), not by the movement capsules round them, nor this player's own colliders.
    private static bool FirstSolid(Vector3 from, Vector3 direction, float range, out RaycastHit first)
    {
        first = default;
        int count = Physics.RaycastNonAlloc(from, direction, lineHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var candidate = lineHits[i];
            lineHits[i] = default;
            if (candidate.collider == null || candidate.distance <= 0f || candidate.distance >= nearest) continue;
            if (candidate.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            nearest = candidate.distance;
            first = candidate;
        }
        return nearest < float.PositiveInfinity;
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

    // The way the pointing palm faces: down, turned in toward the middle of the view by Point.palmIn (1: the thumb on top).
    private static Vector3 WantedPalm(Vector3 way, Vector3 viewRight, Vector3 viewAhead)
    {
        Vector3 palm = Vector3.ProjectOnPlane(Vector3.Lerp(Vector3.down, viewRight, PokeTables.Number("Point.palmIn", .55f)), way);
        if (palm.sqrMagnitude < 1e-4f) palm = Vector3.ProjectOnPlane(viewAhead, way);
        return palm.sqrMagnitude > 1e-8f ? palm.normalized : Vector3.down;
    }

    // The hand's way out of the wrist: the pointing way bent toward the palm by Point.wristBend (degrees, - back toward
    // the back of the hand) and toward the thumb by Point.wristSide (- toward the little finger). The fingertip still
    // goes where the point goes; only the hand's angle to it changes.
    private static Vector3 Wrist(Vector3 way, Vector3 palm)
    {
        float bend = PokeTables.Number("Point.wristBend", 0f) * Mathf.Deg2Rad, side = PokeTables.Number("Point.wristSide", 0f) * Mathf.Deg2Rad;
        if (bend == 0f && side == 0f) return way;
        // For a left hand this runs from the little finger's side to the thumb's.
        Vector3 thumbSide = Vector3.Cross(way, palm).normalized;
        Vector3 bent = way * Mathf.Cos(bend) + palm * Mathf.Sin(bend);
        bent = bent * Mathf.Cos(side) + thumbSide * Mathf.Sin(side);
        return bent.sqrMagnitude > 1e-8f ? bent.normalized : way;
    }

    // The turn from the drawn hand's own frame (x: wrist to the index knuckle, y: across to the index side, z: the back of
    // the hand; PokeTables.HandPose) to the world: the drawn index (its joints' mean way) along the pointing way, the back
    // of the hand away from the palm wanted. So the finger itself points at the target, however the hand was drawn.
    private static Quaternion DrawnFrame(Vector3[] drawn, Vector3 way, Vector3 palm)
    {
        Vector3 index = drawn[0] + drawn[1] + drawn[2];
        index = index.sqrMagnitude > 1e-6f ? index.normalized : Vector3.right;
        Vector3 back = Vector3.ProjectOnPlane(Vector3.forward, index);
        if (back.sqrMagnitude < 1e-6f) back = Vector3.ProjectOnPlane(Vector3.up, index);
        Vector3 worldBack = Vector3.ProjectOnPlane(-palm, way);
        if (worldBack.sqrMagnitude < 1e-6f) worldBack = Vector3.ProjectOnPlane(Vector3.up, way);
        return Quaternion.LookRotation(way, worldBack) * Quaternion.Inverse(Quaternion.LookRotation(index, back));
    }

    // Each finger joint aimed the way the drawn hand has it, knuckle out; a joint the table leaves out keeps its pose.
    private void DrawnFingers(Vector3[] drawn, Quaternion drawnTurn)
    {
        for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = fingers[finger * 3 + joint];
                if (bone == null) break;
                Vector3 want = drawn[finger * 3 + joint];
                if (want.sqrMagnitude < 1e-6f) continue;
                Turn(bone, JointWay(finger, joint), drawnTurn * want, 1f);
            }
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

    // The index nearly straight along the way (Point.index1-3, degrees toward the palm at each joint), the middle, ring
    // and little fingers closed into a fist (Point.curl1-3 a joint, each finger toward the little one Point.curlSpread
    // more at its knuckle, all by Point.fist), the thumb tucked over them (its way from Point.thumbForward, thumbPalm and
    // thumbAcross, its next joints bent by Point.thumbBend2 and thumbBend3, all by Point.thumb). Every number is read each
    // frame, so a saved Poke.csv shows at once (with /point the hand holds still to look at).
    private void Fingers(Vector3 way, Vector3 palm)
    {
        // Turning about this brings a finger from the way toward the palm; for a left hand it also runs from the little
        // finger's side to the index's.
        Vector3 curl = Vector3.Cross(way, palm);
        if (curl.sqrMagnitude < 1e-10f) return;
        curl.Normalize();
        Finger(Index, way, curl, PokeTables.Number("Point.index1", 6f), PokeTables.Number("Point.index2", 6f),
            PokeTables.Number("Point.index3", 6f), 1f);
        float fist = Mathf.Clamp01(PokeTables.Number("Point.fist", 1f));
        float first = PokeTables.Number("Point.curl1", 80f), second = PokeTables.Number("Point.curl2", 95f), third = PokeTables.Number("Point.curl3", 55f);
        float spread = PokeTables.Number("Point.curlSpread", 2.5f);
        Finger(Middle, way, curl, first, second, third, fist);
        Finger(Ring, way, curl, first + spread, second, third, fist);
        Finger(Little, way, curl, first + 2f * spread, second, third, fist);
        float thumb = Mathf.Clamp01(PokeTables.Number("Point.thumb", .8f));
        if (thumb <= 0f || fingers[Thumb * 3] == null) return;
        // Across the palm toward the little finger and a little forward, over the closed fingers.
        Vector3 over = way * PokeTables.Number("Point.thumbForward", .35f) + palm * PokeTables.Number("Point.thumbPalm", .55f)
            - curl * PokeTables.Number("Point.thumbAcross", .75f);
        if (over.sqrMagnitude < 1e-8f) return;
        over.Normalize();
        float bend2 = PokeTables.Number("Point.thumbBend2", 20f), bend3 = PokeTables.Number("Point.thumbBend3", 25f);
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? bend2 : bend3, bendAxis.normalized) * over;
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
