using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Where the head and eyes go.
/// - Looking up or down bends the spine, neck and head with the view, and when the hips face away from the view (planted
///   legs, a sprint's hip turn, legs following a slide) the chest turns back toward it. KINEMATION Character Animation
///   System's Look does it in the animation pass (BodyCas): this works out how far each frame (upright or sliding, the
///   slide's limits) and leaves it to FPS Animation Framework while a gun is held (FPSBody).
/// - Q/E tips the spine sideways from the waist, CAS's Look roll, as far as takes the eyes, and the camera on them, the
///   lean's distance; the leaning hitboxes follow the eyes (PlayerBody.Lean).
/// - In every state the head looks where the view looks, as far as a committed action's clip doesn't carry it (FaceGaze).
/// - Aiming, the hips' gait bob is smoothed away so the eyes, the gun and the sight stay steady (PlayerBody.AimSteady).
/// - Near a wall a downward look gives back as much of the spine's bend as it takes for the face to stay clear (ClearFace).
/// - The mouse leads the camera; selected action clips add a bounded rotation (PlayerBody.Gaze).
/// - The camera rides the eyes, as KINEMATION's first-person camera rides its camera socket: aiming too, since FPS
///   Animation Framework lines the sight up with the eyes (FPSBody). On a ladder and through a punch it steadies toward
///   its place in the capsule.
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
    [SerializeField, Range(0f, 90f), Tooltip("Sliding look limit either side of the travel direction (degrees). The legs keep following travel.")]
    private float slideTurnLimit = 75f;
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

    // Turning to face a set way (onto the back from a front dive, along a swing bar), the body comes round this fast
    // (degrees per second).
    private const float FacingTurnRate = 720f;

    public float SlideLookYawLimit => Mathf.Clamp(slideTurnLimit, 0f, 90f);
    public float HeavyLandingLookYawLimit => Mathf.Clamp(heavyLandingTurnLimit, 0f, 180f);
    public float SupineDiveLookYawLimit => Mathf.Clamp(supineDiveTurnLimit, 0f, 90f);
    public float MantleLookYawLimit => Mathf.Clamp(mantleTurnLimit, 0f, 180f);
    public Vector2 SlideLookPitchLimits => new Vector2(Mathf.Clamp(slidePitchLimits.x, 0f, 85f), Mathf.Clamp(slidePitchLimits.y, 0f, 85f));
    /// <summary>Share of a downward / upward look the spine takes (CAS Look's spine angles); neck and head take the rest.</summary>
    internal float SpineBendDown => Mathf.Clamp01(spineBendDown);
    internal float SpineBendUp => Mathf.Clamp01(spineBendUp);

    /// <summary>What CAS's Look gets this frame (BodyCas): how much of it, the pitch (down-positive), the view's yaw in the
    /// world and the lean's roll (positive tips the top left), in degrees.</summary>
    internal float CasLookWeight { get; private set; }
    internal float CasLookPitch { get; private set; }
    internal float CasLookYaw { get; private set; }
    internal float CasLookRoll { get; private set; }

    private Transform lookPivot, leftUpperArm, rightUpperArm, neck;
    private float faceGazeWeight;
    private Camera viewCamera;
    private PlayerCameraEffects cameraEffects;
    private Vector3 restForward;
    private Vector3 eyeLocal;
    private float eyeViewWeight, bendWeight, baseNearClip;
    private float slideLookPitch, slideLookTwist;
    private Vector3 slideBodyOffset, slideWorldOffset;
    private bool slideBodyAnchored;
    private Transform eyes;

    private bool ShowsOwnHead => thirdPerson || !player.IsLocal || GameSettings.Current.showOwnHead;

    private void SetUpLook()
    {
        viewCamera = view != null ? view.GetComponent<Camera>() : null;
        eyes = PlayerView.EyesOf(player);
        if (viewCamera != null) baseNearClip = viewCamera.nearClipPlane;
        lookPivot = view == null || view.parent == null ? null
            : view.parent.name == "CameraEffectsPivot" && view.parent.parent != null ? view.parent.parent : view.parent;
        eyeViewWeight = 1f;
        bendWeight = CasLookWeight = CasLookPitch = CasLookRoll = 0f;
        slideLookPitch = slideLookTwist = 0f;
        slideBodyAnchored = false;
        faceGazeWeight = 0f;
        SetUpLean();
        SetUpAimSteady();
        neck = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
        if (head == null) return;
        // Imported avatars need not face their prefab's +Z (Hiro faces -Z in its rest pose).
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        restForward = leftUpperArm != null && rightUpperArm != null
            ? Vector3.Cross(rightUpperArm.position - leftUpperArm.position, body.up).normalized : body.forward;
        if (restForward.sqrMagnitude < .001f) restForward = body.forward;
        Vector3 eye = EyesAtRest() + body.up * eyeAdjust.x + restForward * (eyeAdjust.y + eyeViewAhead);
        eyeLocal = head.InverseTransformPoint(eye);
        SetUpGaze();
        SetUpClearance();
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
        return new Vector3(head.position.x, body.position.y + player.runtimeBodyDefaults.w, head.position.z) + restForward * backOffset;
    }

    // In Update, once the body faces its way this frame: how far CAS's Look bends it (BodyCas feeds it before CAS runs).
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

        CasLookPitch = pitch;
        // The view's yaw as the hips see it: the body's own facing plus the turn back toward the view.
        CasLookYaw = transform.eulerAngles.y + yaw + twist;
        // About the view's forward axis a positive angle tips the top to the left, as the camera's own roll does: as far as
        // takes the eyes the lean's distance (PlayerBody.Lean).
        CasLookRoll = CasLeanRoll();
        // FPS Animation Framework has the upper body while a gun is held; CAS hands it over and takes it back as smoothly.
        float target = UpperBodyFromFramework || dead ? 0f : bendWeight;
        CasLookWeight = Mathf.MoveTowards(CasLookWeight, target, dt / blendTime);
    }

    // In LateUpdate, after the animation pass (CAS's Look, FPSAF with a gun) and the hands.
    private void Look(float dt)
    {
        if (head == null || dt <= 0f) return;
        UpdateGaze(dt);
        float onEyes = 1f - punchEyeSteady * punchWeight;
        // Rung and slide clips put the head at different heights. A steady ladder view prevents repeated
        // up/down taps from turning that retargeted head offset into a camera lurch.
        if (state == MovementState.Ladder) onEyes = 0f;
        eyeViewWeight = Mathf.MoveTowards(eyeViewWeight, dead ? 0f : onEyes, dt / eyeViewTime);
        Vector3 eyeBefore = head.TransformPoint(eyeLocal);
        ClearFace();
        FaceGaze(dt);
        Vector3 eye = head.TransformPoint(eyeLocal);
        // Every PC's copy of the body moves its hitboxes, with or without a camera of its own.
        FollowLeanWithHitboxes(eye, dt);
        // In the F5 view too: the eyes take this pose, then the camera moves behind (PlaceViewBehind).
        if (view == null) return;
        Vector3 anchor = cameraEffects != null ? cameraEffects.CameraAnchorPosition
            : view.parent != null ? view.parent.TransformPoint(viewHome) : transform.position + Vector3.up * player.runtimeBodyDefaults.w;
        ApplyGazeToCamera();
        // Combat's head moves (a slip's dip) go on the eyes as they are, the body itself staying put (CombatHeadMotion).
        Vector3 headMove = cameraEffects != null ? cameraEffects.HeadDisplacement : Vector3.zero;
        PlaceCamera(Vector3.Lerp(anchor + eye - eyeBefore, eye, eyeViewWeight) + headMove);
        PlayerView.Follow(eyes, view);
    }

    // In every state the head turns to look where the view looks, as far as a committed action's clip doesn't carry it
    // (headAnimationWeight), as it did up to 0.7.34: with CAS's Look only upright or sliding, a vault, hang, roll or rung
    // left the head where the clip had it, away from the aim in the F5 view and on friends' screens (user picked it back,
    // 2026-11-08). Q/E tilts it only as far as the view rolls; the neck takes back the spine's tip. While FPS Animation
    // Framework holds a gun the head is its (its Look lines the sight up on the eyes, the gun hangs from the head), so
    // this steps back.
    private void FaceGaze(float dt)
    {
        faceGazeWeight = Mathf.MoveTowards(faceGazeWeight, dead || UpperBodyFromFramework ? 0f : 1f, dt / blendTime);
        if (faceGazeWeight <= 0f || head == null) return;
        Quaternion gaze = gazeRotation * Quaternion.Euler(0f, 0f, player.LeanRoll * bendWeight);
        Quaternion target = Quaternion.Slerp(gaze * Quaternion.Inverse(headToGaze), head.rotation, headAnimationWeight);
        target = Quaternion.Slerp(head.rotation, target, faceGazeWeight);
        if (neck != null)
            neck.rotation = Quaternion.Slerp(Quaternion.identity, target * Quaternion.Inverse(head.rotation), .3f) * neck.rotation;
        head.rotation = target;
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

    // Keep the lower body in the slide's world frame. Clamping or smoothing a camera-relative yaw would let
    // a quick look drag the legs around; the slide motor already smooths intentional steering.
    private void FaceSlide()
    {
        Vector3 direction = Vector3.ProjectOnPlane(player.SlideDirection, Vector3.up);
        if (direction.sqrMagnitude > .0001f) legYaw = SlideFacing(direction, transform.eulerAngles.y);
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

    /// <summary>
    /// The way a slide faces (degrees): along its travel, or against it when the slide carries the player backwards
    /// (a wall bounce, a backward entry), so neither the view nor the legs swing round; the body slides back instead.
    /// </summary>
    public static float SlideFacing(Vector3 slideDirection, float lookYaw)
    {
        float heading = Mathf.Atan2(slideDirection.x, slideDirection.z) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(heading, lookYaw)) > 90f ? heading + 180f : heading;
    }

    private void PlaceBody()
    {
        if (slideBodyAnchored)
        {
            if (!dead && state == MovementState.Sliding)
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

    // The pose clears the wall first; this stable capsule-to-eye sweep is the final guard for any remaining contact.
    private void PlaceCamera(Vector3 target)
    {
        // Heaving up over a swing bar the eyes stay under the hands while the capsule goes on above the bar, so the sweep
        // from the capsule down to the eyes met the bar and the camera stopped against it, seeing nothing (user,
        // 2026-11-08). The bar is thin; the camera goes past it to the eyes.
        var climbed = player.Abilities.Traversal.Obstacle.FromBar;
        clearance.Ignored = climbed != null && !dead && (state == MovementState.Mantle || heaving) ? climbed.transform : null;
        view.position = clearance.Resolve(SafeCameraAnchor(), target, CameraRadius(), CameraBlockers, transform);
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
        if (viewCamera != null)
            viewCamera.nearClipPlane = !thirdPerson && GameSettings.Current.showOwnHead ? Mathf.Max(baseNearClip, headNearClip) : baseNearClip;
    }
}
