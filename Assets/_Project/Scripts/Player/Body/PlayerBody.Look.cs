using System.Collections.Generic;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Where the head and eyes go.
/// - The mouse leads the head's gaze; selected action clips add a bounded rotation shared by head and camera.
///   The camera rides the corrected eyes. Aiming down sights steadies it; Q/E still moves it sideways.
/// - Looking up or down bends the spine and neck with the view.
/// - Q/E peeks the way a person does round a corner: the hips go over onto the leaning side's foot while the feet stay
///   put, the spine bends sideways from the waist, and the head tilts only as far as the view rolls. The camera makes up
///   the rest of the peek.
/// - When the hips face away from the view, the chest twists back toward it: planted legs, a sprint's hip turn,
///   or legs following a slide.
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
    [SerializeField, Range(0f, 40f), Tooltip("Q/E at full lean: how far the spine bends sideways from the waist (degrees). "
        + "The head tilts only as far as the view rolls (Ability.leanAngle), so the neck takes back the difference.")]
    private float leanSpineBend = 20f;
    [SerializeField, Min(0f), Tooltip("Q/E at full lean: how far the hips go over toward the lean, the weight onto that foot; "
        + "the feet stay where they stood (m). The camera makes up what the body leaves of the peek.")]
    private float leanHipShift = .08f;

    // Turning to face a set way (onto the back from a front dive, along a swing bar), the body comes round this fast
    // (degrees per second).
    private const float FacingTurnRate = 720f;

    public float SlideLookYawLimit => Mathf.Clamp(slideTurnLimit, 0f, 90f);
    public float HeavyLandingLookYawLimit => Mathf.Clamp(heavyLandingTurnLimit, 0f, 180f);
    public float SupineDiveLookYawLimit => Mathf.Clamp(supineDiveTurnLimit, 0f, 90f);
    public Vector2 SlideLookPitchLimits => new Vector2(Mathf.Clamp(slidePitchLimits.x, 0f, 85f), Mathf.Clamp(slidePitchLimits.y, 0f, 85f));

    private readonly List<Transform> spine = new List<Transform>();
    private Transform lookPivot, neck, leftUpperArm, rightUpperArm;
    private Camera viewCamera;
    private PlayerCameraEffects cameraEffects;
    private Vector3 restForward;
    private Vector3 eyeLocal;
    private float eyeViewWeight, bendWeight, twistWeight, baseNearClip;
    private float slideLookPitch, slideLookTwist, leanRoll, leanHeadRoll;
    private Vector3 slideBodyOffset, slideWorldOffset, leanShift, hipLean;
    private Vector3[] leanJoints = new Vector3[0];
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
        spine.Clear();
        if (animator.isHuman)
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
                if (animator.GetBoneTransform(bone) is Transform found) spine.Add(found);
        neck = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
        leanJoints = new Vector3[spine.Count];
        hipLean = Vector3.zero;
        eyeViewWeight = 1f - AimBlend();
        bendWeight = twistWeight = 0f;
        slideLookPitch = slideLookTwist = 0f;
        slideBodyAnchored = false;
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

    private void Look(float dt)
    {
        if (head == null || dt <= 0f) return;
        UpdateGaze(dt);
        float onEyes = (KeepsEyesAiming() ? 1f : 1f - AimBlend()) * (1f - punchEyeSteady * punchWeight);
        // Rung and slide clips put the head at different heights. A steady ladder view prevents repeated
        // up/down taps from turning that retargeted head offset into a camera lurch.
        if (state == MovementState.Ladder) onEyes = 0f;
        eyeViewWeight = Mathf.MoveTowards(eyeViewWeight, dead ? 0f : onEyes, dt / eyeViewTime);
        bool upright = !dead && transition == null
            && (state == MovementState.Standing || state == MovementState.Crouching);
        bool sliding = !dead && state == MovementState.Sliding;
        // The spine bends the same with the gun raised or not. Straightening it for a raised gun moved the eyes, and the
        // gun with them, off what had raised it (crouched over a floor with a rifle), so the gun came down and the
        // spine bent again, back and forth.
        bendWeight = Mathf.MoveTowards(bendWeight, upright || sliding ? 1f : 0f, dt / blendTime);
        twistWeight = Mathf.MoveTowards(twistWeight, upright || sliding ? 1f : 0f, dt / blendTime);

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
        // The lean has already moved the hips over (LeanHips, before the feet were planted).
        Vector3 eyeNow = head.TransformPoint(eyeLocal), eyeBefore = eyeNow - hipLean;
        LeanBody(eyeNow);
        BendWithClearance(pitch * bendWeight, twist * twistWeight);
        HangLeanArm();
        // In the F5 view too: the eyes take this pose, then the camera moves behind (PlaceViewBehind).
        if (view == null) return;
        Vector3 eye = head.TransformPoint(eyeLocal);
        // The Q/E lean has already moved the camera off its pivot; the body has carried the eyes part of the way.
        Vector3 lean = (cameraEffects != null ? cameraEffects.LeanDisplacement : transform.right * player.LeanOffset) - leanShift;
        Vector3 anchor = cameraEffects != null ? cameraEffects.CameraAnchorPosition
            : view.parent != null ? view.parent.TransformPoint(viewHome) : transform.position + Vector3.up * player.runtimeBodyDefaults.w;
        ApplyGazeToCamera();
        PlaceCamera(Vector3.Lerp(anchor + lean + eye - eyeBefore, eye + lean, eyeViewWeight));
        PlayerView.Follow(eyes, view);
    }

    // Sliding, diving or lying, and going down onto the back or getting up from it, the camera stays on the eyes even aiming,
    // as Beatrun's Eye View keeps it: the steady aiming spot belongs to the capsule, which a body on the ground lies right
    // through, and there's no gait to steady.
    private bool KeepsEyesAiming()
    {
        switch (state)
        {
            case MovementState.Sliding:
            case MovementState.SupineSliding:
            case MovementState.HeadFirstSliding:
            case MovementState.Diving:
            case MovementState.SupineDiving:
            case MovementState.Proning:
            case MovementState.Supine:
                return true;
        }
        return transition != null && (transition == Find(supineSlideEndProne) || transition == Find(supineLand)
            || transition == Find(supineGetUp) || transition == Find(supineGetUpGun) || transition == Find(supineGetUpCrouch));
    }

    // How far the Q/E lean has gone, -1 (left) to 1 (right), while the body bends with the view. Every other PC knows it
    // from the player's pose, so the body leans there too.
    private float LeanShare => player.Abilities != null ? player.Abilities.Lean.Share * bendWeight : 0f;

    // Before the feet are planted: the weight goes onto the leaning side, and PlantFeet keeps the feet where they stood.
    private void LeanHips()
    {
        hipLean = Vector3.zero;
        float share = LeanShare;
        if (hips == null || dead || Mathf.Abs(share) < .001f) return;
        hipLean = transform.right * (share * leanHipShift);
        hips.position += hipLean;
    }

    // Where the lean takes the eyes: the hips' move, then each spine bone's turn carrying the ones above it, as
    // BendAndTwist will turn them. The camera makes up exactly the rest of the peek.
    private void LeanBody(Vector3 eye)
    {
        leanRoll = leanHeadRoll = 0f;
        leanShift = hipLean;
        float share = LeanShare;
        if (Mathf.Abs(share) < .001f || spine.Count == 0) return;
        // About the view's forward axis a positive angle tips the top to the left, as the camera's own roll does.
        leanRoll = -share * leanSpineBend;
        leanHeadRoll = player.LeanRoll * bendWeight;
        Quaternion step = Quaternion.AngleAxis(leanRoll / spine.Count, transform.forward);
        for (int i = 0; i < spine.Count; i++) leanJoints[i] = spine[i].position;
        Vector3 moved = eye;
        for (int i = 0; i < spine.Count; i++)
        {
            Vector3 pivot = leanJoints[i];
            for (int j = i + 1; j < spine.Count; j++) leanJoints[j] = pivot + step * (leanJoints[j] - pivot);
            moved = pivot + step * (moved - pivot);
        }
        // The eyes also come down a little; the camera keeps its height, as the lean always has.
        leanShift += moved - eye;
    }

    // The arm on the leaning side keeps hanging the way the clip had it. Turned with the spine it swung in through the hip
    // the weight had gone onto. The other arm stays with the spine: that side tips away, and the arm rests along it.
    // Hands on a gun or fists are put back on them afterwards (PlayerBodyHands).
    private void HangLeanArm()
    {
        if (Mathf.Abs(leanRoll) < .01f) return;
        var arm = leanRoll < 0f ? rightUpperArm : leftUpperArm;
        if (arm != null) arm.rotation = Quaternion.AngleAxis(-leanRoll, transform.forward) * arm.rotation;
    }

    // The spine takes its share of the pitch, then neck and head the rest, so the head ends up looking where the
    // camera looks. The twist turns the chest, neck and head back from the hips toward the view, and the lean tips it.
    private void BendAndTwist(float pitch, float twist, float spineScale = 1f)
    {
        Vector3 right = transform.right, up = transform.up, forward = transform.forward;
        float share = (pitch > 0f ? spineBendDown : spineBendUp) * spineScale;
        int count = Mathf.Max(1, spine.Count);
        Quaternion tip = Quaternion.AngleAxis(leanRoll / count, forward);
        foreach (var bone in spine)
        {
            Turn(bone, pitch * share / count, twist * .6f / count);
            if (bone != null) bone.rotation = tip * bone.rotation;
        }
        float rest = pitch * (1f - share);
        Turn(neck, rest * .3f, twist * .2f);
        Turn(head, rest * .7f, twist * .2f);
        // Resolve the absolute gaze even with no mouse pitch/twist. Otherwise a turn clip's authored nod
        // survives after the player stops looking. Every clearance candidate must use this final eye pose.
        if (!dead)
        {
            // Let committed actions carry the anatomical head with the body. Keeping the head world-upright
            // through a somersault would twist the neck; only the camera's share is comfort-limited.
            // Leaning, the head tilts with the view's roll and no further; the neck takes back the rest of the spine's bend.
            Quaternion gaze = gazeRotation * Quaternion.Euler(0f, 0f, leanHeadRoll);
            Quaternion target = Quaternion.Slerp(gaze * Quaternion.Inverse(headToGaze), head.rotation, headAnimationWeight);
            if (neck != null)
                neck.rotation = Quaternion.Slerp(Quaternion.identity, target * Quaternion.Inverse(head.rotation), .3f) * neck.rotation;
            head.rotation = target;
        }

        void Turn(Transform bone, float bend, float turn)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(bend, right) * Quaternion.AngleAxis(turn, up) * bone.rotation;
        }
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
