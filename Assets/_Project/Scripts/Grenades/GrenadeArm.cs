using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The left arm of a quick throw on G (the user, 2026-11-12: "그냥 G 꾹 누르면 들고 있는 자세로 바뀌면서 쿠킹할 수 있고
/// 걍 바로 누르면 콜오브듀티 퀵그레네이드처럼 바로 던지는 모션만 나오게"): the hand leaves the gun, draws the throwable,
/// holds it out, throws it and goes back to the gun, playing the clips of its kind's hand motion (GrenadeArmPath: the
/// draw, the hold, the throw and the tap, each a table the Blender throw workspace sends: the user, 2026-11-12: "드는
/// 모션, 대기 모션, 던지는 모션 내가 블렌더로 수정할 수 있게 만들어서 적용해 봐"). PlayerGrenades says each frame which clip
/// and where in it (Drive); one clip blends into the next over its motion's fade, from wherever the arm was. This fits
/// the clip to the body that wears it: the wrist goes where the clip puts it from the eyes (or from this body's own
/// shoulder in this arm's lengths, for a motion measured so), the elbow toward where the clip has it (TwoBoneIK); the hand
/// turns to the clip's run and facing (measured from its knuckles, as PointArm does), each finger and the thumb close as
/// far as the clip closes them, and the throwable's model rides in the palm at its kind's place (Tables/Grenades held*,
/// its own grip point in the fist: grip*). Rig-agnostic: any humanoid a modder brings, with no clip or bake of its own.
/// The same on every PC: the owner's first-person body and the copies everyone else sees run the same clips from the
/// phases PlayerGrenades sends, so friends see the hold and the throw, in third person too.
/// The hand leaves the gun through the one shared way for every off-hand use (the manager's decision, 2026-11-12):
/// PlayerBodyHands.LendOffHand, so the left hand's gun contact, grip and fingers let go over the hands' own blend time
/// and take the gun again after; the throw's pose eases by how far the hand is lent (OffHandLent), never ahead of it,
/// in over GrenadeRules armIn and out over armOut at most. A throw has the off hand before a held F's point (PointArm
/// lets go while PlayerGrenades.HandBusy). It runs after FPS Animation Framework, the hands on the gun, the callout
/// gestures and the point (GunFramework + 7, PointArm is + 6); the cloth comes after, so a sleeve follows. The
/// first-person camera was placed earlier, so the view never moves with the arm.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 7)]
public sealed class GrenadeArm : MonoBehaviour
{
    // The fingers in order (index, middle, ring, little, thumb), three joints each from the knuckle out.
    private const int Index = 0, Middle = 1, Ring = 2, Little = 3, Thumb = 4;
    private static readonly HumanBodyBones[] FirstJoints =
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftThumbProximal,
    };
    // A finger's whole curl shared out over its joints, knuckle first: a real finger closes most at the knuckle and the
    // middle joint, and its last joint follows (the Blender workspace bends its drafts the same way).
    private static readonly float[] CurlShare = { .4f, .35f, .25f };

    private PlayerMovement movement;
    private PlayerBodyHands hands;
    private Animator animator;
    private Transform eyes, upper, lower, hand, holder;
    private readonly Transform[] fingers = new Transform[15];
    // The bones the throw turns, and how the frame had them, to ease from.
    private readonly Transform[] turned = new Transform[18];
    private readonly Quaternion[] framed = new Quaternion[18];
    private int turnedCount, drivenFrame = -1;
    // A finger joint's own length axis (the rig's, from the index's middle joint), for a last joint with no tip bone; the
    // hand's own way out from the wrist, for a hand with no fingers.
    private Vector3 jointAxis, handAxis, releaseLocal;
    // own: the throw's own ease (armIn, armOut); weight: what is posed, never ahead of the hand lent off the gun.
    private float armLength, handLength, own, weight, time, fade = 1f;
    private Throwable kind, heldKind;
    private bool bound, wanted, holds, hasRelease, posedBefore;
    private string clip = "", shownClip = "";
    // The arm as last posed (the clip's moment after any blend), and the moment a new clip blends from.
    private GrenadeArmPath.Key shown, from;
    private GameObject held;

    /// <summary>Where the throwable in the hand is now (world); the hand while there is none.</summary>
    public Vector3 HeldPoint { get; private set; }

    /// <summary>A player's arm for throws, made on their body's model the first time it is wanted; null without a humanoid body.</summary>
    internal static GrenadeArm Of(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out PlayerBody body)) return null;
        var model = body.BodyAnimator;
        if (model == null || !model.isHuman) return null;
        return model.TryGetComponent(out GrenadeArm arm) ? arm : model.gameObject.AddComponent<GrenadeArm>();
    }

    /// <summary>
    /// This frame's throw (PlayerGrenades, every frame while a throw is under way): which throwable, which clip (a table
    /// of GrenadeArmPath) and how far into it (s at the clip's own speed), whether the arm should be on it (false eases it
    /// back to the gun) and whether the throwable is still in the hand (false once it has left, though the clip's hand is
    /// still closed).
    /// </summary>
    internal void Drive(Throwable throwable, string clipTable, float clipTime, bool on, bool inHand)
    {
        kind = throwable;
        clip = clipTable ?? "";
        time = clipTime;
        wanted = on;
        holds = inHand;
        drivenFrame = Time.frameCount;
    }

    /// <summary>
    /// Where the throwable leaves the hand at its throw's release (the clip's own moment), from the eyes as they are now:
    /// the throw starts there and the aiming arc is drawn from there, so the arc is the flight. False before the arm has
    /// found its body and eyes.
    /// </summary>
    internal bool TryReleasePoint(out Vector3 point)
    {
        point = default;
        if (!hasRelease || eyes == null) return false;
        point = eyes.position + eyes.rotation * releaseLocal;
        return true;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || GamePause.IsPaused) return;
        if (!bound && !Bind()) return;
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        bool driven = drivenFrame >= Time.frameCount - 1;
        var motion = GrenadeArmPath.For(kind);
        if (driven) NoteRelease(motion);
        bool on = driven && wanted && Free();
        // The left hand off the gun through PlayerBodyHands, as every off-hand use takes it; the same share each frame
        // costs nothing.
        if (hands != null) hands.LendOffHand(this, on ? 1f : 0f);
        float blend = on ? Throwables.Rule("armIn", .08f) : Throwables.Rule("armOut", .14f);
        own = Mathf.MoveTowards(own, on ? 1f : 0f, dt / Mathf.Max(.01f, blend));
        weight = hands != null ? Mathf.Min(own, hands.OffHandLent) : own;
        if (weight <= 0f || !GrenadeArmPath.Sample(clip, time, out var key))
        {
            posedBefore = false;
            ShowHeld(false);
            HeldPoint = hand.position;
            return;
        }
        Pose(Faded(key, motion, dt), motion);
    }

    // A new clip takes over from wherever the arm was (the last clip's moment, or its blend), over the motion's fade.
    private GrenadeArmPath.Key Faded(in GrenadeArmPath.Key key, GrenadeArmPath.Motion motion, float dt)
    {
        if (!posedBefore)
        {
            // Coming on from the gun: the arm's own ease brings it in, nothing to blend from.
            posedBefore = true;
            shownClip = clip;
            fade = 1f;
        }
        else if (!string.Equals(clip, shownClip, System.StringComparison.Ordinal))
        {
            from = shown;
            shownClip = clip;
            fade = 0f;
        }
        fade = Mathf.Min(1f, fade + (motion.Fade > 1e-4f ? dt / motion.Fade : 1f));
        shown = fade < 1f ? GrenadeArmPath.Blend(from, key, Mathf.SmoothStep(0f, 1f, fade)) : key;
        return shown;
    }

    private void OnDisable()
    {
        ShowHeld(false);
        if (hands != null) hands.LendOffHand(this, 0f);
    }

    private bool Bind()
    {
        movement = GetComponentInParent<PlayerMovement>();
        hands = movement != null ? movement.GetComponent<PlayerBodyHands>() : null;
        animator = GetComponent<Animator>();
        if (movement == null || animator == null || !animator.isHuman) return false;
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
        jointAxis = middleJoint != null && lastJoint != null
            ? Quaternion.Inverse(middleJoint.rotation) * (lastJoint.position - middleJoint.position).normalized
            : Vector3.forward;
        handAxis = Quaternion.Inverse(hand.rotation) * (hand.position - lower.position).normalized;
        armLength = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        var knuckle = Knuckle();
        handLength = knuckle != null ? Vector3.Distance(hand.position, knuckle.position) : armLength * .17f;
        bound = armLength > 1e-3f && handLength > 1e-4f;
        return bound;
    }

    // The body can lift the arm: alive, up, standing or crouched (walking and in the air too) or sliding; not held,
    // climbing, hanging or vaulting.
    private bool Free()
    {
        if (movement.IsDead || movement.IsDowned || movement.IsHeld || movement.IsParkour) return false;
        var state = movement.CurrentState;
        return state == MovementState.Standing || state == MovementState.Crouching || state == MovementState.Sliding;
    }

    // Where the throwable will leave the hand: at the release of the clip playing if it has one (a tap's, a throw's),
    // else at the throw's; kept in the eyes' own space so the throw follows the look between frames.
    private void NoteRelease(GrenadeArmPath.Motion motion)
    {
        hasRelease = false;
        var playing = GrenadeArmPath.ClipOf(clip);
        var throwing = playing != null && playing.HasRelease ? playing : motion.ThrowClip;
        if (throwing == null || !throwing.HasRelease || !GrenadeArmPath.Sample(throwing.Table, throwing.ReleaseAt, out var key)) return;
        Quaternion view = eyes.rotation;
        Vector3 wrist = Place(motion, key.Wrist);
        Vector3 point = wrist + Quaternion.LookRotation(view * key.Along, view * key.Palm) * (HeldAt() * handLength);
        releaseLocal = Quaternion.Inverse(view) * (point - eyes.position);
        hasRelease = true;
    }

    // Where a key's wrist (or elbow) is now: from the eyes in metres, spread out as much as this player's view is wider
    // than the clips' (so the hand sits where it sat on the Blender camera's screen); or, for a motion measured from the
    // shoulder, from this shoulder in this arm's lengths.
    private Vector3 Place(GrenadeArmPath.Motion motion, Vector3 local)
    {
        Quaternion view = eyes.rotation;
        if (!motion.EyeSpace) return upper.position + view * (local * armLength);
        float spread = Spread(motion);
        return eyes.position + view * new Vector3(local.x * spread, local.y * spread, local.z);
    }

    // The player's own camera on their own PC; a friend's copy, seen from outside, keeps the clips' framing.
    private float Spread(GrenadeArmPath.Motion motion)
    {
        var camera = movement.IsLocal ? Camera.main : null;
        if (camera == null || motion.ViewFov <= 0f) return 1f;
        float ours = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad), theirs = Mathf.Tan(motion.ViewFov * .5f * Mathf.Deg2Rad);
        return theirs > 1e-3f ? Mathf.Clamp(ours / theirs, .5f, 2f) : 1f;
    }

    private void Pose(in GrenadeArmPath.Key key, GrenadeArmPath.Motion motion)
    {
        for (int i = 0; i < turnedCount; i++) framed[i] = turned[i].localRotation;
        Quaternion view = eyes.rotation;

        // The wrist where the clip has it, the elbow toward the clip's elbow.
        TwoBoneIK.Solve(upper, lower, hand, Place(motion, key.Wrist), Place(motion, key.Elbow));
        Vector3 along = view * key.Along, palm = view * key.Palm;
        TurnHand(along, palm);
        Fingers(along, palm, key);

        if (weight < 1f)
            for (int i = 0; i < turnedCount; i++) turned[i].localRotation = Quaternion.Slerp(framed[i], turned[i].localRotation, weight);
        Hold(key.Held && holds);
    }

    // The hand's rotation that lays its run (wrist to middle knuckle) along the clip's and its palm facing as the clip's.
    private void TurnHand(Vector3 along, Vector3 palm)
    {
        Vector3 alongNow = AlongNow();
        Vector3 palmNow = PalmNow(alongNow);
        if (alongNow.sqrMagnitude < 1e-10f || palmNow.sqrMagnitude < 1e-10f) return;
        hand.rotation = Quaternion.LookRotation(along, palm) * Quaternion.Inverse(Quaternion.LookRotation(alongNow, palmNow)) * hand.rotation;
    }

    private Transform Knuckle() => fingers[Middle * 3] != null ? fingers[Middle * 3] : fingers[Index * 3];

    // Out from the wrist along the hand: to the middle knuckle (the index's on a hand without one), else the hand's own way.
    private Vector3 AlongNow()
    {
        var knuckle = Knuckle();
        return knuckle != null ? knuckle.position - hand.position : hand.rotation * handAxis;
    }

    // The way the palm faces now, from the knuckles, as the clips were measured (grenade_tools.hand_frame): out of the
    // palm, square to the hand's run. A hand with no fingers is taken as palm down.
    private Vector3 PalmNow(Vector3 along)
    {
        var index = fingers[Index * 3];
        var side = fingers[Little * 3] != null ? fingers[Little * 3] : fingers[Ring * 3] != null ? fingers[Ring * 3] : fingers[Middle * 3];
        Vector3 palm = index != null && side != null && side != index ? -Vector3.Cross(along, index.position - side.position) : Vector3.zero;
        if (palm.sqrMagnitude < 1e-12f) palm = Vector3.down;
        palm = Vector3.ProjectOnPlane(palm, along);
        return palm.sqrMagnitude > 1e-12f ? palm.normalized : Vector3.zero;
    }

    // Each of the four fingers closed toward the palm by its own curl in the clip (GrenadeRules gripCurl of it), shared
    // over its joints; the thumb across the palm toward the little finger's side and on toward the fingertips, over the
    // throwable, as far as the clip closes it (GrenadeRules gripThumb of it).
    private void Fingers(Vector3 along, Vector3 palm, in GrenadeArmPath.Key key)
    {
        // Turning about this brings a finger from the hand's run toward the palm; it points to the thumb's side.
        Vector3 axis = Vector3.Cross(along, palm);
        if (axis.sqrMagnitude < 1e-10f) return;
        axis.Normalize();
        float fingersShare = Mathf.Max(0f, Throwables.Rule("gripCurl", 1f));
        for (int finger = Index; finger <= Little; finger++)
        {
            float curl = key.FingerCurl(finger), angle = 0f;
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = fingers[finger * 3 + joint];
                if (bone == null) break;
                angle += curl * fingersShare * CurlShare[joint];
                Turn(bone, JointWay(finger, joint), Quaternion.AngleAxis(angle, axis) * along, 1f);
            }
        }
        float close = Mathf.Clamp01(key.Thumb / 90f) * Mathf.Clamp01(Throwables.Rule("gripThumb", 1f));
        if (close <= 0f || fingers[Thumb * 3] == null) return;
        Vector3 over = (along * .5f + palm * .6f - axis * .45f).normalized;
        for (int joint = 0; joint < 3; joint++)
        {
            var bone = fingers[Thumb * 3 + joint];
            if (bone == null) break;
            Vector3 bendAxis = Vector3.Cross(over, palm);
            if (joint > 0 && bendAxis.sqrMagnitude > 1e-8f) over = Quaternion.AngleAxis(joint == 1 ? 15f : 20f, bendAxis.normalized) * over;
            Turn(bone, JointWay(Thumb, joint), over, close);
        }
    }

    // Which way a finger joint runs now: to its next joint, else along the rig's joint axis.
    private Vector3 JointWay(int finger, int joint)
    {
        var bone = fingers[finger * 3 + joint];
        var next = joint < 2 ? fingers[finger * 3 + joint + 1] : null;
        return next != null ? next.position - bone.position : bone.rotation * jointAxis;
    }

    private static void Turn(Transform bone, Vector3 now, Vector3 want, float share)
    {
        if (now.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) return;
        var turn = Quaternion.FromToRotation(now, want);
        bone.rotation = (share >= 1f ? turn : Quaternion.Slerp(Quaternion.identity, turn, share)) * bone.rotation;
    }

    // ---- The throwable in the hand ----

    private Vector3 HeldAt()
    {
        var info = Throwables.Of(kind);
        return info != null ? info.HeldAt : new Vector3(.12f, .6f, .9f);
    }

    // Its model at its kind's place in the palm, from the hand as it finally is this frame.
    private void Hold(bool show)
    {
        if (!show || kind == Throwable.None)
        {
            ShowHeld(false);
            HeldPoint = hand.position;
            return;
        }
        if (held == null || heldKind != kind) Dress();
        if (held == null) return;
        Vector3 along = AlongNow();
        Vector3 palm = PalmNow(along);
        if (along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return;
        var info = Throwables.Of(kind);
        Quaternion frame = Quaternion.LookRotation(along, palm);
        Vector3 turn = info != null ? info.HeldTurn : new Vector3(0f, 0f, 90f);
        holder.SetPositionAndRotation(hand.position + frame * (HeldAt() * handLength), frame * Quaternion.Euler(turn));
        // A body imported at another scale (an avatar's armature at 100) must not scale the throwable with it.
        float scale = transform.lossyScale.x;
        holder.localScale = Vector3.one / Mathf.Max(1e-4f, Mathf.Abs(scale));
        ShowHeld(true);
        HeldPoint = held.transform.position;
    }

    // The throwable's model under a holder of its own beside the body (so a scaled armature never scales it), drawn on
    // the body's own layer so the first-person camera and everyone else's see it as they see the body; moved back in the
    // holder by its grip, so the fist (the holder) closes on that point of it.
    private void Dress()
    {
        if (holder == null)
        {
            holder = new GameObject("Held Throwable").transform;
            holder.SetParent(transform, false);
        }
        if (held != null) Destroy(held);
        held = Throwables.Dress(kind, holder);
        heldKind = kind;
        var info = Throwables.Of(kind);
        if (held != null && info != null) held.transform.localPosition = -info.Grip;
        var skin = GetComponentInChildren<SkinnedMeshRenderer>();
        int layer = skin != null ? skin.gameObject.layer : gameObject.layer;
        foreach (var part in holder.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
    }

    private void ShowHeld(bool show)
    {
        if (holder != null && holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
    }

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }
}
