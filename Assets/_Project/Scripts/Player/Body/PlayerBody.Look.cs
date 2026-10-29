using System.Collections.Generic;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Where the head and eyes go.
/// - The camera rides the eyes in every state, the way Beatrun's Eye View does; aim stays with the mouse. Aiming
///   down sights steadies it back at the camera pivot. The Q/E lean still moves it sideways.
/// - Looking up or down bends the spine and neck with the view.
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

    public float SlideLookYawLimit => Mathf.Clamp(slideTurnLimit, 0f, 90f);
    public Vector2 SlideLookPitchLimits => new Vector2(Mathf.Clamp(slidePitchLimits.x, 0f, 85f), Mathf.Clamp(slidePitchLimits.y, 0f, 85f));

    private readonly List<Transform> spine = new List<Transform>();
    private Transform lookPivot, neck;
    private Camera viewCamera;
    private Vector3 eyeLocal;
    private float eyeViewWeight, bendWeight, twistWeight, baseNearClip;
    private float slideLookPitch, slideLookTwist;
    private Vector3 slideBodyOffset, slideWorldOffset;
    private bool slideBodyAnchored;

    private bool ShowsOwnHead => thirdPerson || GameSettings.Current.showOwnHead;

    private void SetUpLook()
    {
        viewCamera = view != null ? view.GetComponent<Camera>() : null;
        if (viewCamera != null) baseNearClip = viewCamera.nearClipPlane;
        lookPivot = view == null || view.parent == null ? null
            : view.parent.name == "CameraEffectsPivot" && view.parent.parent != null ? view.parent.parent : view.parent;
        spine.Clear();
        if (animator.isHuman)
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
                if (animator.GetBoneTransform(bone) is Transform found) spine.Add(found);
        neck = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
        eyeViewWeight = bendWeight = twistWeight = 0f;
        slideLookPitch = slideLookTwist = 0f;
        slideBodyAnchored = false;
        if (head == null) return;
        Vector3 eye = EyesAtRest() + body.up * eyeAdjust.x + body.forward * (eyeAdjust.y + eyeViewAhead);
        eyeLocal = head.InverseTransformPoint(eye);
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
            float up = Vector3.Dot(fromHead, body.up), ahead = Vector3.Dot(fromHead, body.forward);
            if (apart > .03f && apart < .12f && up > -.02f && up < .2f && ahead > .02f && ahead < .2f) return middle;
        }
        return new Vector3(head.position.x, body.position.y + player.runtimeBodyDefaults.w, head.position.z) + body.forward * backOffset;
    }

    private void Look(float dt)
    {
        if (head == null || dt <= 0f) return;
        eyeViewWeight = Mathf.MoveTowards(eyeViewWeight, dead ? 0f : 1f - AimBlend(), dt / eyeViewTime);
        bool upright = !dead && transition == null && (state == MovementState.Standing || state == MovementState.Crouching);
        bool sliding = !dead && state == MovementState.Sliding;
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
        Vector3 eyeBefore = head.TransformPoint(eyeLocal);
        BendAndTwist(pitch * bendWeight, twist * twistWeight);
        if (thirdPerson || view == null) return;
        Vector3 eye = head.TransformPoint(eyeLocal);
        // The Q/E lean has already moved the camera off its pivot; the eyes keep that offset.
        Vector3 lean = view.parent != null ? view.position - view.parent.TransformPoint(viewHome) : Vector3.zero;
        PlaceCamera(Vector3.Lerp(view.position + eye - eyeBefore, eye + lean, eyeViewWeight));
    }

    // The spine takes its share of the pitch, then neck and head the rest, so the head ends up looking where the
    // camera looks. The twist turns the chest, neck and head back from the hips toward the view.
    private void BendAndTwist(float pitch, float twist)
    {
        if (Mathf.Abs(pitch) < .01f && Mathf.Abs(twist) < .01f) return;
        Vector3 right = transform.right, up = transform.up;
        float share = pitch > 0f ? spineBendDown : spineBendUp;
        int count = Mathf.Max(1, spine.Count);
        foreach (var bone in spine) Turn(bone, pitch * share / count, twist * .6f / count);
        float rest = pitch * (1f - share);
        Turn(neck, rest * .3f, twist * .2f);
        Turn(head, rest * .7f, twist * .2f);

        void Turn(Transform bone, float bend, float turn)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(bend, right) * Quaternion.AngleAxis(turn, up) * bone.rotation;
        }
    }

    // Keep the lower body in the slide's world frame. Clamping or smoothing a camera-relative yaw would let
    // a quick look drag the legs around; the slide motor already smooths intentional steering.
    private void FaceSlide()
    {
        Vector3 direction = Vector3.ProjectOnPlane(player.SlideDirection, Vector3.up);
        if (direction.sqrMagnitude > .0001f) legYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        Quaternion frame = Quaternion.Euler(0f, legYaw, 0f);
        if (!slideBodyAnchored)
        {
            slideBodyOffset = Quaternion.Inverse(frame) * (body.position - transform.position);
            slideBodyAnchored = true;
        }
        slideWorldOffset = frame * slideBodyOffset;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, legYaw);
        legsPlanted = true;
        turning = 0;
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

    // Moves the camera only as far as the world lets it, so the eyes never put it inside a wall.
    private void PlaceCamera(Vector3 target)
    {
        Vector3 from = view.position, move = target - from;
        float distance = move.magnitude;
        if (distance < .0005f) return;
        if (Physics.SphereCast(from, .1f, move / distance, out RaycastHit hit, distance + .02f, player.groundLayers,
                QueryTriggerInteraction.Ignore))
            target = from + move / distance * Mathf.Max(0f, hit.distance - .02f);
        view.position = target;
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
