using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Where the head and eyes go.
/// - Looking up or down bends the spine, neck and head with the view, and when the hips face away from the view (planted
///   legs, a sprint's hip turn, legs following a slide) the chest turns back toward it. SpineLook does the bend first
///   thing after the animation pass; this works out how far each frame (upright or sliding, the slide's limits) and
///   leaves it to FPS Animation Framework while a gun is held (FPSBody).
/// - Q/E tips the spine sideways from the waist, the look bend's roll; the leaning hitboxes follow (BodyLean).
/// - In every state the head looks where the view looks, as far as a committed action's clip doesn't carry it (BodyGaze).
/// - Aiming, the hips' gait bob is smoothed away so the eyes, the gun and the sight stay steady (PlayerBody.AimSteady).
/// - Near a wall a downward look gives back as much of the spine's bend as it takes for the face to stay clear (ClearFace).
/// - The mouse leads the camera; selected action clips add a bounded rotation (BodyGaze).
/// - The camera rides the eyes (BodyCamera): this hands it the eyes, the lean's carry and the gaze each frame, and how far
///   to ride them (off them toward the capsule on a ladder and through a punch).
/// - Wherever the camera goes, the head goes with it (HeadReach): where it steadies off the eyes, the spine, chest and neck
///   bend the head to it, so the camera never sits outside the head (the user, 2026-11-12).
/// - The Esc menu's own-head option shows the head and hair; the near clip plane cuts what sits around the camera.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Look and eye view")]
    [SerializeField, Range(0f, 1f), Tooltip("Share of the look pitch the spine takes looking down / up; neck and head take the rest.")]
    private float spineBendDown = .45f, spineBendUp = .2f;
    [SerializeField, Min(.01f), Tooltip("How quickly the camera moves between the eyes and the steady aiming position (s).")]
    private float eyeViewTime = .15f;
    [SerializeField, Min(0f), Tooltip("The camera sits this far ahead of the eyes (m).")]
    private float eyeViewAhead = .02f;
    [SerializeField, Tooltip("Moves the eye point if it sits wrong on a character: up, forward (m).")]
    private Vector2 eyeAdjust;
    [SerializeField, Min(.01f), Tooltip("Near clip plane while the own head is shown: anything closer to the camera is cut (m).")]
    private float headNearClip = .05f;
    [SerializeField, Range(0f, 150f), Tooltip("Sliding look limit either side of the travel direction (degrees). The legs keep following travel; "
        + "the torso turns at most SlideTwistLimit of it and the head the rest (friend feedback C11: 75 was too narrow).")]
    private float slideTurnLimit = 120f;
    // The torso's own share of a slide's look: past this the spine would wring, so the head and neck turn the rest.
    private const float SlideTwistLimit = 90f;
    [SerializeField, Tooltip("Sliding look pitch limits in degrees: up, down. Shared with the camera to keep the torso and view together.")]
    private Vector2 slidePitchLimits = new Vector2(40f, 55f);
    [SerializeField, Range(0f, 180f), Tooltip("Down after a heavy landing, look limit either side of the way the body faces "
        + "(degrees). The camera rides the eyes, so looking farther round would look through the own head.")]
    private float heavyLandingTurnLimit = 90f;
    [SerializeField, Range(0f, 90f), Tooltip("A dive turned onto the back in the air: look limit either side of the way the "
        + "back faces (degrees), as Beatrun's jump turn holds it (75), so the view can't swing round to a front dive again.")]
    private float supineDiveTurnLimit = 75f;
    [SerializeField, Range(0f, 180f), Tooltip("Climbing up (a mantle): look limit either side of the way the climb goes "
        + "(degrees), as Beatrun holds its high climb (45). The camera rides the head, so looking back over the shoulder "
        + "would look through the own neck.")]
    private float mantleTurnLimit = 45f;
    [SerializeField, Min(0f), Tooltip("Where the camera steadies off the eyes, the most the body bends the head to it (m, HeadReach). "
        + "Past this the camera keeps the rest off the head, as before.")]
    private float headReachMove = .08f;
    [SerializeField, Tooltip("The most each spine bone (x) and the neck (y) turns to bring the head to the camera (degrees).")]
    private Vector2 headReachLimits = new Vector2(6f, 12f);
    [SerializeField, Min(0f), Tooltip("With less body sway (Settings → 화면 → 몸 흔들림), how slowly the camera's path through the "
        + "eyes follows them (s).")]
    private float bodySwayTime = .12f;
    [SerializeField, Range(0f, 1f), Tooltip("Running on the ground, the share of the eyes' bob the first-person camera steadies: it keeps "
        + "that much to the eyes' eased path (bodySwayTime), as a runner's eyes hold the view while the head bobs, and the head "
        + "bends to it (HeadReach). 0 rides the eyes as before.")]
    private float runCalm = .45f;
    [SerializeField, Tooltip("The run's calm starts at x and is whole from y (m/s).")]
    private Vector2 runCalmSpeeds = new Vector2(2f, 4f);

    // How much of the run's calm is on (eased, so a landing or a stop never jolts the camera back onto the eyes).
    private float runCalmWeight;
    private const float RunCalmEase = .25f;

    // Turning to face a set way (onto the back from a front dive, along a swing bar), the body comes round this fast
    // (degrees per second).
    private const float FacingTurnRate = 720f;

    /// <summary>How far round the view turns from a slide's travel (PlayerLook).</summary>
    public float SlideViewYawLimit => Mathf.Clamp(slideTurnLimit, 0f, 150f);
    /// <summary>How far round the torso twists from a slide's travel: the view's limit, no more than SlideTwistLimit.</summary>
    public float SlideLookYawLimit => Mathf.Min(SlideViewYawLimit, SlideTwistLimit);
    public float HeavyLandingLookYawLimit => Mathf.Clamp(heavyLandingTurnLimit, 0f, 180f);
    public float SupineDiveLookYawLimit => Mathf.Clamp(supineDiveTurnLimit, 0f, 90f);
    public float MantleLookYawLimit => Mathf.Clamp(mantleTurnLimit, 0f, 180f);
    public Vector2 SlideLookPitchLimits => new Vector2(Mathf.Clamp(slidePitchLimits.x, 0f, 85f), Mathf.Clamp(slidePitchLimits.y, 0f, 85f));
    /// <summary>Share of a downward / upward look the spine takes (SpineLook); neck and head take the rest.</summary>
    internal float SpineBendDown => Mathf.Clamp01(spineBendDown);
    internal float SpineBendUp => Mathf.Clamp01(spineBendUp);

    // The look bend (SpineLook, first in LateUpdate) and what it gets this frame, worked out in Update: how much of it, the
    // pitch (down-positive), the view's yaw in the world and the lean's roll (positive tips the top left), in degrees.
    private readonly SpineLook spineLook = new SpineLook();
    private float spineLookWeight, spineLookPitch, spineLookYaw, spineLookRoll;

    // The camera on the eyes (BodyCamera): it holds its own state, and Look hands it the pose each frame.
    private readonly BodyCamera bodyCamera = new BodyCamera();
    // The head brought to wherever the camera went (HeadReach), last in the pose work.
    private readonly HeadReach headReach = new HeadReach();

    private Transform lookPivot, leftUpperArm, rightUpperArm;
    private Vector3 restForward;
    private Vector3 eyeLocal;
    private float bendWeight;
    private float slideLookPitch, slideLookTwist;
    private Vector3 slideBodyOffset, slideWorldOffset;
    private bool slideBodyAnchored;

    private bool ShowsOwnHead => SeenFromOutside || GameSettings.Current.showOwnHead;
    // Nobody looks out through this head: F5, watched as a corpse, or another player's copy.
    private bool SeenFromOutside => thirdPerson || watchedFromOutside || !player.IsLocal;

    // Backlog 722: dead and watching someone else (PlayerSpectate), the own fallen body is seen from outside, head and all,
    // and the near clip plane is the camera's own again.
    private bool watchedFromOutside;
    internal void ShowFromOutside(bool outside)
    {
        if (watchedFromOutside == outside) return;
        watchedFromOutside = outside;
        ApplyView();
    }

    private void SetUpLook()
    {
        bodyCamera.SetUp();
        lookPivot = view == null || view.parent == null ? null
            : view.parent.name == "CameraEffectsPivot" && view.parent.parent != null ? view.parent.parent : view.parent;
        spineLook.Bind(animator, SpineBendDown, SpineBendUp);
        headReach.Bind(animator);
        bendWeight = spineLookWeight = spineLookPitch = spineLookRoll = 0f;
        slideLookPitch = slideLookTwist = 0f;
        slideBodyAnchored = false;
        bodyGaze.Reset(transform, lookPivot);
        TuneGaze();
        bodyLean.Reset(player, transform, body, head, spineLook, HoldsGun);
        TuneLean();
        SetUpAimSteady();
        if (head == null) return;
        // Imported avatars need not face their prefab's +Z (Hiro faces -Z in its rest pose).
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        restForward = leftUpperArm != null && rightUpperArm != null
            ? Vector3.Cross(rightUpperArm.position - leftUpperArm.position, body.up).normalized : body.forward;
        if (restForward.sqrMagnitude < .001f) restForward = body.forward;
        Vector3 eye = EyesAtRest() + body.up * eyeAdjust.x + restForward * (eyeAdjust.y + eyeViewAhead);
        eyeLocal = head.InverseTransformPoint(eye);
        bodyLean.Bind(eyeLocal, leftUpperArm, rightUpperArm);
        bodyGaze.SetUp(body, head, animator.GetBoneTransform(HumanBodyBones.Neck), restForward);
    }

    // The avatar's eye bones when they sit like eyes (ahead of and above the head bone, an eye-width apart);
    // otherwise where the scale put them: camera height, backOffset ahead of the head bone.
    private Vector3 EyesAtRest()
    {
        var left = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
        var right = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;
        if (left != null && right != null)
        {
            Vector3 middle = (left.position + right.position) * .5f, fromHead = middle - head.position;
            float apart = Vector3.Distance(left.position, right.position);
            float up = Vector3.Dot(fromHead, body.up), ahead = Vector3.Dot(fromHead, restForward);
            // Allow stylised heads instead of rejecting a correctly mapped pair for exceeding human eye width.
            if (apart > .015f && apart < player.runtimeBodyDefaults.w * .2f
                && up > -.05f && up < .25f && ahead > .01f && ahead < .25f) return middle;
        }
        // The capsule's floor, the camera's height and what the shoes add to it (StandOnSoles).
        return new Vector3(head.position.x, body.position.y + soleLift + player.runtimeBodyDefaults.w + eyeRise, head.position.z) + restForward * backOffset;
    }

    // In Update, once the body faces its way this frame: how far the look bends it (SpineLook, first in LateUpdate).
    private void AimLook(float dt)
    {
        bool upright = !dead && transition == null
            && (state == MovementState.Standing || state == MovementState.Crouching);
        bool sliding = !dead && state == MovementState.Sliding;
        // The spine bends the same with the gun raised or not. Straightening it for a raised gun moved the eyes, and the
        // gun with them, off what had raised it (crouched over a floor with a rifle), so the gun came down and the
        // spine bent again, back and forth.
        bendWeight = Mathf.MoveTowards(bendWeight, upright || sliding ? 1f : 0f, dt / blendTime);

        float pitch = lookPivot != null ? Mathf.DeltaAngle(0f, lookPivot.localEulerAngles.x) : 0f;
        float twist = Mathf.DeltaAngle(yaw, 0f);
        if (sliding)
        {
            Vector2 limits = SlideLookPitchLimits;
            pitch = Mathf.Clamp(pitch, -limits.x, limits.y);
            twist = Mathf.Clamp(twist, -SlideLookYawLimit, SlideLookYawLimit);
            slideLookPitch = Mathf.Clamp(slideLookPitch, -limits.x, limits.y);
            slideLookTwist = Mathf.Clamp(slideLookTwist, -SlideLookYawLimit, SlideLookYawLimit);
            float follow = 1f - Mathf.Exp(-dt / Mathf.Max(.01f, blendTime * .5f));
            slideLookPitch = Mathf.Lerp(slideLookPitch, pitch, follow);
            slideLookTwist = Mathf.Lerp(slideLookTwist, twist, follow);
            pitch = slideLookPitch;
            twist = slideLookTwist;
        }
        else { slideLookPitch = pitch; slideLookTwist = twist; }

        spineLookPitch = pitch;
        // The view's yaw as the hips see it: the body's own facing plus the turn back toward the view.
        spineLookYaw = transform.eulerAngles.y + yaw + twist;
        // About the view's forward axis a positive angle tips the top to the left, as the camera's own roll does: the
        // spine's bend for the Q/E lean, the old one with nothing in hand, as far as takes the eyes the whole peek with
        // the fists (BodyLean).
        TuneLean();
        bodyLean.BlendHands(HoldsGun, dt);
        spineLookRoll = bodyLean.SpineRoll(bendWeight);
        // FPS Animation Framework has the upper body while a gun is held; the look bend hands it over and takes it back
        // as smoothly.
        // A paired execution's clip has the spine and head as it was made (PlayerBody.Execution).
        float target = UpperBodyFromFramework || dead || PlaysPaired ? 0f : bendWeight;
        spineLookWeight = Mathf.MoveTowards(spineLookWeight, target, dt / blendTime);
    }

    // Last in LateUpdate's pose work, after the look bend (or FPSAF's, with a gun), the body's placing and the legs.
    private void Look(float dt)
    {
        if (head == null || dt <= 0f) return;
        TuneGaze();
        bodyGaze.Update(GazeClipWeight(), AimBlend(), dead, spineLook.ClipHeadRotation, dt);
        float onEyes = 1f - punchEyeSteady * punchWeight;
        // Rung and slide clips put the head at different heights. A steady ladder view prevents repeated
        // up/down taps from turning that retargeted head offset into a camera lurch.
        if (state == MovementState.Ladder) onEyes = 0f;
        bodyCamera.EaseOntoEyes(dead ? 0f : onEyes, eyeViewTime, dt);
        Vector3 eyeBefore = head.TransformPoint(eyeLocal);
        bodyLean.HangArm(spineLookRoll * spineLookWeight, dead, HoldsGun);
        ClearFace();
        // The head to the gaze, Q/E tilting it as far as the view rolls; FPSAF's while it holds a gun (BodyGaze).
        bodyGaze.FaceHead(dead || UpperBodyFromFramework, player.LeanRoll * bendWeight, blendTime, dt);
        Vector3 eye = head.TransformPoint(eyeLocal);
        // Every PC's copy of the body moves its leaning hitboxes, with or without a camera of its own.
        Vector3 carried = bodyLean.Carry(eye, bendWeight, dead, dt);
        // In the F5 view too: the eyes take this pose, then the camera moves behind (PlaceViewBehind). With nothing in hand
        // the body carries part of the Q/E peek and the camera makes up the rest, as up to 0.7.34; holding a gun or the
        // fists the body carries all of it and the camera stays on the eyes, where the guard and the sight are placed from.
        // Running on the ground, part of the eyes' bob is steadied (runCalm); off the cycles it eases back onto the eyes.
        float calmTarget = dead || strideFrame != Time.frameCount ? 0f
            : runCalmShare * Mathf.InverseLerp(runCalmSpeeds.x, Mathf.Max(runCalmSpeeds.x + .01f, runCalmSpeeds.y), gaitSpeed);
        runCalmWeight = Mathf.MoveTowards(runCalmWeight, calmTarget, dt / RunCalmEase);
        bodyCamera.Place(eye, eyeBefore, carried, bodyLean.CameraMakesUp, bodyGaze.Rotation, bodyGaze.Input, CameraIgnores(),
            dt, bodySwayTime, runCalm * runCalmWeight);
        // Then the head goes where the camera went: off the eyes (a punch, a slip's dip, a peek's share, a wall, less body
        // sway) the spine, chest and neck bend it there, so the view is never outside the head, seen from outside too (the
        // user, 2026-11-12: "캐릭터의 머리와 카메라가 일치해서 True FPS의 몰입감은 있으면서 너무 멀미나거나 게임 플레이에
        // 방해가 되지 않게"). With FPSAF holding a gun only the neck bends, so the hands stay on it. Most frames the camera is
        // on the eyes and nothing moves.
        if (!dead && bodyCamera.HasPlace)
            headReach.Meet(eyeLocal, bodyCamera.Placed, headReachMove, headReachLimits, UpperBodyFromFramework);
    }

    // On the back in the air the body faces back the way it flies, as Beatrun's jump turn sets it, and the view turns within
    // supineDiveTurnLimit of that (PlayerLook). Turned over from a front dive, it comes round quickly rather than at once.
    private void FaceBack(float dt)
    {
        Vector3 back = Vector3.ProjectOnPlane(-player.DiveBodyForward, Vector3.up);
        if (back.sqrMagnitude > .0001f)
            legYaw = Mathf.MoveTowardsAngle(legYaw, Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg, FacingTurnRate * dt);
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, legYaw);
        legsPlanted = true;
        turning = 0;
    }

    // Keep the lower body in the slide's world frame, the feet-first slide and the slide on the back alike. Clamping or
    // smoothing a camera-relative yaw would let a quick look drag the legs around; the slide motor already smooths
    // intentional steering. The way it faces is the slide's own (PlayerMovement.SlideFacingYaw): worked out here from the
    // view, a look past 90° swung the legs, the body's place and the camera on its eyes round 180°. The slide on the back
    // had nothing holding it, so the lying body turned with every look (user, 2026-11-12).
    private void FaceSlide()
    {
        Vector3 direction = Vector3.ProjectOnPlane(player.SlideDirection, Vector3.up);
        if (direction.sqrMagnitude > .0001f) legYaw = player.SlideFacingYaw;
        Quaternion frame = Quaternion.Euler(0f, legYaw, 0f);
        if (!slideBodyAnchored)
        {
            // From where PlaceBody puts the body, not where last frame left it: the feet may have lowered the hips.
            slideBodyOffset = Quaternion.Inverse(frame) * transform.TransformVector(headAlignment);
            slideBodyAnchored = true;
        }
        slideWorldOffset = frame * slideBodyOffset;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, legYaw);
        legsPlanted = true;
        turning = 0;
    }

    // Sliding on the back, the clips keep the head far from where the slide began (lying back from the feet-first slide,
    // landing from a dive on the back), so the body is eased until the head is just behind the camera's pivot, as
    // FollowHead does in the other states; but in the slide's own frame, never the view's: FollowHead's offset turns with
    // the view, so on a lying body every look round swung it round the capsule first. The feet-first slide keeps the
    // place it began with.
    private void FollowHeadAlongSlide(float dt)
    {
        if (dead || state != MovementState.SupineSliding || head == null || view == null || view.parent == null || dt <= 0f) return;
        Quaternion frame = Quaternion.Euler(0f, legYaw, 0f);
        Vector3 wanted = view.parent.position - frame * Vector3.forward * backOffset;
        Vector3 error = wanted - (spineLook.ClipHeadPosition - bodyLean.ClipHeadOffset(dead, dt));
        error.y = 0f;
        slideBodyOffset += Quaternion.Inverse(frame) * error * (1f - Mathf.Exp(-dt / headFollowTime));
        slideBodyOffset = Vector3.ClampMagnitude(slideBodyOffset, 1.5f);
    }

    private void PlaceBody()
    {
        if (slideBodyAnchored)
        {
            if (!dead && (state == MovementState.Sliding || state == MovementState.SupineSliding))
            {
                body.position = transform.position + slideWorldOffset;
                return;
            }
            // Resume head following from the same world offset rather than jumping back into camera space.
            headAlignment = transform.InverseTransformVector(slideWorldOffset);
            slideBodyAnchored = false;
        }
        body.localPosition = headAlignment;
    }

    // What the camera's sweep goes past. Heaving up over a swing bar the eyes stay under the hands while the capsule goes on
    // above the bar, so the sweep from the capsule down to the eyes met the bar and the camera stopped against it, seeing
    // nothing (user, 2026-11-08). The bar is thin; the camera goes past it to the eyes.
    private Transform CameraIgnores()
    {
        var climbed = player.Abilities.Traversal.Obstacle.FromBar;
        return climbed != null && !dead && (state == MovementState.Mantle || heaving) ? climbed.transform : null;
    }

    private float AimBlend()
    {
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        return weapons != null && weapons.EquippedData != null && !weapons.IsHighReady ? weapons.AimBlend : 0f;
    }

    // Head and hair render in first person when the Esc menu says so; the near clip plane then cuts what sits
    // around the camera (the inside of the face) instead of hiding the whole head.
    private void ApplyHeadVisibility()
    {
        var mode = ShowsOwnHead ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        foreach (var part in headParts)
            if (part != null) part.shadowCastingMode = mode;
        // A mask or glasses sit in front of the eyes and would cover the view, so in first person only their shadows stay
        // (OutfitSlot.View.Hide).
        var face = SeenFromOutside ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        foreach (var part in faceParts)
            if (part != null) part.shadowCastingMode = face;
        bodyCamera.CutOwnHead(!thirdPerson && !watchedFromOutside && GameSettings.Current.showOwnHead, headNearClip);
    }
}
