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
/// - A look down bends the spine no further than keeps the face, the eyes and the shoulders off the player's own legs and the
///   face out of walls, with a gun as with nothing in hand (PlayerBody.Clearance, SpineRoom).
/// - The mouse leads the camera; selected action clips add a bounded rotation (BodyGaze).
/// - The camera rides the eyes (BodyCamera): this hands it the eyes, the lean's carry and the gaze each frame, and how far
///   to ride them (off them toward the capsule on a ladder and through a punch).
/// - Wherever the camera goes, the head goes with it (HeadReach): where it steadies off the eyes, the spine, chest and neck
///   bend the head to it, so the camera never sits outside the head (the user, 2026-11-12).
/// - The Esc menu's own-head option shows the hair (and ears, a hat), laid over the view so what comes right up to the camera
///   thins out instead of being cut (OwnHeadView, ownHeadFade); the face itself is never drawn in first person
///   (HeadMeshes.IsFace), so the near clip plane stays a centimetre and a hand at the face is never cut (firstPersonNear).
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
    [SerializeField, Min(.001f), Tooltip("Near clip plane in first person: anything closer to the camera is cut (m). Small, so another "
        + "player's finger at the face is never cut; the face itself isn't drawn in first person instead of being cut.")]
    private float firstPersonNear = .01f;
    [SerializeField, Tooltip("In first person the own hair, ears and hat show nothing of themselves nearer the camera than x and all "
        + "of themselves from y (m), thinning out between (OwnHeadView).")]
    private Vector2 ownHeadFade = new Vector2(.02f, .05f);
    [SerializeField, Tooltip("In first person the own hair, ears and hat show all of themselves within x degrees of where the head "
        + "looks, seen from its eyes, and nothing past y, thinning out between (OwnHeadView): what the head's own eyes couldn't "
        + "see, the camera turned further than the head doesn't either.")]
    private Vector2 ownHeadSight = new Vector2(80f, 95f);
    [SerializeField, Tooltip("Sliding, how far the view turns from the way the slide faces: left; right (degrees). The legs keep following "
        + "travel; the torso turns at most SlideTwistLimit of it and the head the rest. To the left it stops short: the slide's clips "
        + "(BIPER SlideToRun) sit turned to the right, the chest 40-70 degrees round, and a look further left turned the head into "
        + "the shoulder.")]
    private Vector2 slideLookLimits = new Vector2(35f, 120f);
    // The torso's own share of a slide's look: past this the spine would wring, so the head and neck turn the rest.
    private const float SlideTwistLimit = 90f;
    // The most the look turns the spine, neck and head back toward the view from the chest (degrees), and its turn and its
    // reading on the last frame (AimLook). A turn held over a shoulder keeps that side while the view is within LookSideSlack
    // of the back on the other, and goes over to it at LookSwapRate (degrees a second) past what the view itself turned.
    private const float LookTwistMost = 150f, LookSideSlack = 20f, LookSwapRate = 900f;
    private float lookTwist, lookReading;
    private bool lookTwistKnown;
    [SerializeField, Tooltip("Sliding look pitch limits in degrees: up, down. Shared with the camera to keep the torso and view together.")]
    private Vector2 slidePitchLimits = new Vector2(40f, 55f);
    [SerializeField, Range(0f, 180f), Tooltip("Down after a heavy landing, look limit either side of the way the body faces "
        + "(degrees). The camera rides the eyes, so looking farther round would look through the own head.")]
    private float heavyLandingTurnLimit = 90f;
    [SerializeField, Range(0f, 90f), Tooltip("A dive turned onto the back in the air: look limit either side of the way the "
        + "back faces (degrees), as Beatrun's jump turn holds it (75), so the view can't swing round to a front dive again.")]
    private float supineDiveTurnLimit = 75f;
    [SerializeField, Tooltip("A dive onto the back in the air: how far the view may look up; down (degrees). Flying on the back, a "
        + "look down bent the head into the chest and the camera went into the body.")]
    private Vector2 supineDivePitchLimits = new Vector2(85f, 20f);
    [SerializeField, Range(0f, 180f), Tooltip("Climbing up (a mantle): look limit either side of the way the climb goes "
        + "(degrees), as Beatrun holds its high climb (45). The camera rides the head, so looking back over the shoulder "
        + "would look through the own neck.")]
    private float mantleTurnLimit = 45f;
    [SerializeField, Tooltip("Climbing a wall: how far the view looks up; down from level (degrees). It slows into the limit over "
        + "climbPitchBand and never moves by itself.")]
    private Vector2 wallClimbPitchLimits = new Vector2(70f, 45f);
    [SerializeField, Tooltip("Climbing up onto a top (a mantle, a climb up from a hang, a clamber) and swinging on a bar: how far "
        + "the view looks up; down from level (degrees), as wallClimbPitchLimits.")]
    private Vector2 climbPitchLimits = new Vector2(60f, 60f);
    [SerializeField, Min(0f), Tooltip("Within this far of a climb's pitch limit the mouse turns the view less and less toward it "
        + "(degrees): the share left of this, squared.")]
    private float climbPitchBand = 15f;
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
    private Vector2 runCalmSpeeds = new Vector2(1.5f, 2.6f);

    // How much of the run's calm is on (eased, so a landing or a stop never jolts the camera back onto the eyes).
    private float runCalmWeight;
    private const float RunCalmEase = .25f;

    // Turning to face a set way (onto the back from a front dive, along a swing bar), the body comes round this fast
    // (degrees per second).
    private const float FacingTurnRate = 720f;

    /// <summary>How far round the view turns from the way a slide faces: left (x), right (y), degrees (PlayerLook).</summary>
    public Vector2 SlideLookLimits => new Vector2(Mathf.Clamp(slideLookLimits.x, 0f, 150f), Mathf.Clamp(slideLookLimits.y, 0f, 150f));
    public float HeavyLandingLookYawLimit => Mathf.Clamp(heavyLandingTurnLimit, 0f, 180f);
    public float SupineDiveLookYawLimit => Mathf.Clamp(supineDiveTurnLimit, 0f, 90f);
    public Vector2 SupineDiveLookPitchLimits => new Vector2(Mathf.Clamp(supineDivePitchLimits.x, 0f, 89f), Mathf.Clamp(supineDivePitchLimits.y, 0f, 89f));
    public float MantleLookYawLimit => Mathf.Clamp(mantleTurnLimit, 0f, 180f);

    /// <summary>
    /// In a climb, how far the view looks up (x) and down (y) from level, degrees, and over how many degrees before them it
    /// slows (PlayerLook). False where the view looks up and down as far as it likes.
    /// </summary>
    internal bool ClimbLookPitch(MovementState climb, out Vector2 limits, out float band)
    {
        band = Mathf.Max(0f, climbPitchBand);
        switch (climb)
        {
            case MovementState.WallClimb:
                limits = wallClimbPitchLimits;
                break;
            case MovementState.Mantle:
            case MovementState.BarSwing:
                limits = climbPitchLimits;
                break;
            default:
                limits = default;
                return false;
        }
        limits = new Vector2(Mathf.Clamp(limits.x, 0f, 89f), Mathf.Clamp(limits.y, 0f, 89f));
        return true;
    }
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
    // How high the eyes stand over the head bone with the head level (m).
    private float eyeOverHead;
    private float bendWeight;
    private float slideLookPitch, slideLookTwist;
    private Vector3 slideBodyOffset, slideWorldOffset;
    private bool slideBodyAnchored;

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
        SetUpRoom();
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
        // Imported avatars need not face their prefab's +Z (Kazusa and Sil face -Z in their rest pose).
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        restForward = leftUpperArm != null && rightUpperArm != null
            ? Vector3.Cross(rightUpperArm.position - leftUpperArm.position, body.up).normalized : body.forward;
        if (restForward.sqrMagnitude < .001f) restForward = body.forward;
        SetUpNeck();
        Vector3 eye = EyesAtRest() + body.up * eyeAdjust.x + restForward * (eyeAdjust.y + eyeViewAhead);
        eyeLocal = head.InverseTransformPoint(eye);
        eyeOverHead = eye.y - head.position.y;
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
        // Back to the view from where the chest is: a run's turn clip has it round in its own pose (RunTurnFacing). Within what a
        // spine and neck turn (LookTwistMost) it is the reading's, the short way, as the hips come round. With the view behind,
        // past that, it keeps the side it is turned to, so a reading going from +170 to -170 as the chest swings past the back
        // doesn't swing the spine round (0.9.402: the spine and the arms on it swung 160 degrees round and back three times in a
        // run's turn); once the view is well round on the other side it goes over to it through the front, at LookSwapRate past
        // what the view turned. From 0.9.402 to 0.9.420 it was carried on the long way and held at the limit while the hips came
        // back the short way: once the real turn was 330 round the held one wrapped, and the spine and the arms on it swung 108
        // degrees in a frame (the pops record of 2026-11-25 05:10; the user: "고개를 … 휙휙 돌렸을 때 가끔 … 사람이 돌릴 수
        // 없을 정도로 꺾이거나"; the animation expert's review).
        float twistNow = Mathf.DeltaAngle(yaw + RunTurnFacing, 0f), twist = twistNow;
        if (lookTwistKnown)
        {
            float side = Mathf.Abs(lookTwist) > LookTwistMost - LookSideSlack ? Mathf.Sign(lookTwist) : 0f;
            bool behind = Mathf.Abs(twistNow) > LookTwistMost
                || side * twistNow < 0f && Mathf.Abs(twistNow) > LookTwistMost - LookSideSlack;
            float wanted = behind ? (side != 0f ? side : Mathf.Sign(twistNow)) * LookTwistMost : twistNow;
            twist = Mathf.MoveTowards(lookTwist, wanted, Mathf.Abs(Mathf.DeltaAngle(lookReading, twistNow)) + LookSwapRate * dt);
        }
        lookReading = twistNow;
        lookTwist = twist;
        lookTwistKnown = true;
        if (sliding)
        {
            Vector2 limits = SlideLookPitchLimits;
            pitch = Mathf.Clamp(pitch, -limits.x, limits.y);
            twist = Mathf.Clamp(twist, -SlideTwistLimit, SlideTwistLimit);
            slideLookPitch = Mathf.Clamp(slideLookPitch, -limits.x, limits.y);
            slideLookTwist = Mathf.Clamp(slideLookTwist, -SlideTwistLimit, SlideTwistLimit);
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
        // spine's bend for the Q/E lean (BodyLean).
        TuneLean();
        FollowAim(dt);
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
        bodyGaze.Update(LedClipWeight(), CarriedClipWeight(), AimBlend(), dead, spineLook.ClipHeadRotation, dt);
        float onEyes = 1f - punchEyeSteady * punchWeight;
        bodyCamera.EaseOntoEyes(dead ? 0f : onEyes, eyeViewTime, dt);
        Vector3 eyeBefore = head.TransformPoint(eyeLocal);
        bodyLean.HangArm(spineLookRoll * spineLookWeight, dead, HoldsGun);
        // The head to the gaze, Q/E tilting it as far as the view rolls; FPSAF's while it holds a gun (BodyGaze).
        // No further from the chest than a neck turns (PlayerBody.Neck).
        Vector3 chestAhead = GuardedChestAhead(out float upright);
        bool neckHeld = bodyGaze.FaceHead(dead || UpperBodyFromFramework, player.LeanRoll * bendWeight, blendTime, dt,
            chestAhead, neckTurnLimit + NeckGuardSlack, upright);
        Vector3 eye = head.TransformPoint(eyeLocal), eyeOnHead = eye;
        // Aiming on the gun's aim clips (AimsOnClip) the camera goes to the right eye, and the gun, sighted on the camera
        // (FPSBody.KeepGunInView), lines up with the right eye and the stock runs by the right cheek; the head stays as the
        // clip has it. Between the eyes the gun's line went through the middle of the face, between the brows (the user,
        // 2026-11-22: "조준하는 것도 이 애니메이션 써. 지금 제 3자가 보기에 내 미간 사이를 뚫어서 총을 위치하게 해서
        // 조준하잖아, 카메라 때문이지만"). 0.9.319 put the camera on the sight line of the gun in the clip's hand instead,
        // and the camera went in among the arms ("줌 하면 이 지랄이 나는데").
        float onClip = AimsOnClip;
        if (onClip > 0f) eye += RightEyeShift() * onClip;
        // Every PC's copy of the body moves its leaning hitboxes, with or without a camera of its own.
        bodyLean.Carry(eye, dead, dt);
        // In the F5 view too: the eyes take this pose, then the camera moves behind (PlaceViewBehind).
        // Running on the ground, part of the eyes' bob is steadied (runCalm); off the cycles it eases back onto the eyes.
        float calmTarget = dead || strideFrame != Time.frameCount ? 0f
            : runCalmShare * Mathf.InverseLerp(runCalmSpeeds.x, Mathf.Max(runCalmSpeeds.x + .01f, runCalmSpeeds.y), gaitSpeed);
        runCalmWeight = Mathf.MoveTowards(runCalmWeight, calmTarget, dt / RunCalmEase);
        // Through its own eyes the camera keeps where the capsule has it: the body's slack off the capsule (PlayerBody.Slack)
        // is taken out, so a body left behind never leaves the view behind, and the head bends after the camera (HeadReach).
        // Seen from outside (a friend's copy, F5) the eyes, the gun held on them (PlayerView) and the camera go with the body.
        Vector3 ownSlack = SeenFromOutside ? Vector3.zero : slackShown;
        bodyCamera.Place(eye - ownSlack, eyeBefore - ownSlack, bodyGaze.Rotation, bodyGaze.Input, CameraIgnores(),
            dt, bodySwayTime, runCalm * runCalmWeight, Mathf.Max(FlinchHold, EmoteHold));
        // Then the head goes where the camera went: off the eyes (a punch, a slip's dip, a wall, less body
        // sway) the spine, chest and neck bend it there, so the view is never outside the head, seen from outside too (the
        // user, 2026-11-12: "캐릭터의 머리와 카메라가 일치해서 True FPS의 몰입감은 있으면서 너무 멀미나거나 게임 플레이에
        // 방해가 되지 않게"). With FPSAF holding a gun only the neck bends, so the hands stay on it. Most frames the camera is
        // on the eyes and nothing moves.
        if (!dead && bodyCamera.HasPlace)
            headReach.Meet(eyeLocal, Vector3.Lerp(bodyCamera.Placed, eyeOnHead, onClip), headReachMove, headReachLimits, UpperBodyFromFramework);
        RecordCameraOnHead(dt, neckHeld);
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
        Vector3 error = wanted - (spineLook.ClipHeadPosition - bodyLean.ClipHeadOffset(dead, dt) - slackShown);
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

    // From between the eyes to the right eye: half of left eye to right eye where the avatar's eye bones sit like eyes
    // (as EyesAtRest takes them), else half a person's eye span to the view's right.
    private const float HalfEyeSpan = .032f;
    private Vector3 RightEyeShift()
    {
        var left = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
        var right = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;
        if (left != null && right != null)
        {
            float apart = Vector3.Distance(left.position, right.position);
            if (apart > .015f && apart < player.runtimeBodyDefaults.w * .2f) return (right.position - left.position) * .5f;
        }
        return bodyGaze.Rotation * Vector3.right * HalfEyeSpan;
    }

    private float AimBlend()
    {
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        return weapons != null && weapons.EquippedData != null && !weapons.IsHighReady ? weapons.AimBlend : 0f;
    }

    /// <summary>How near the camera the own hair, ears and hat thin out in first person (m: none of them nearer than x, all
    /// from y; OwnHeadView).</summary>
    internal Vector2 OwnHeadFade => ownHeadFade;

    /// <summary>How far off where the head looks, seen from its eyes, the own hair, ears and hat thin out in first person
    /// (degrees: all of them within x, none past y; OwnHeadView).</summary>
    internal Vector2 OwnHeadSight => ownHeadSight;

    /// <summary>The head's eyes and the way the head looks (world), as the body is posed now (OwnHeadView). False with no
    /// head.</summary>
    internal bool OwnEyes(out Vector3 eye, out Vector3 gaze)
    {
        eye = gaze = default;
        if (head == null) return false;
        eye = head.TransformPoint(eyeLocal);
        gaze = bodyGaze.HeadGaze * Vector3.forward;
        return true;
    }

    // In first person the camera's own passes draw none of the head: the hair, ears and hat are laid over the view by
    // OwnHeadView when the Esc menu says so, thinning out as they come up to the camera, and the face never shows, only its
    // shadow (HeadMeshes.IsFace). Up to 0.9.365 the whole head was drawn and the near clip plane cut what sat within 5 cm of
    // the camera (the inside of the face, the nearest hair), and with it the finger of anyone who poked a cheek (the user,
    // 2026-11-23: "누가 내 입에 손가락 집어 넣을 때 손가락이 짤리면 안 되잖아"); in 0.9.366 the plane was a centimetre and a
    // strand swinging into the camera was cut where it crossed ("머리카락이 자꾸 가까운 부분은 사라져").
    private void ApplyHeadVisibility()
    {
        var mode = SeenFromOutside ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        foreach (var part in headParts)
            if (part != null) part.shadowCastingMode = mode;
        // The face, and a mask or glasses before the eyes, which would cover the view (OutfitSlot.View.Hide).
        foreach (var part in faceParts)
            if (part != null) part.shadowCastingMode = mode;
        LayOwnHead(!SeenFromOutside && GameSettings.Current.showOwnHead);
        bodyCamera.NearInFirstPerson(!thirdPerson && !watchedFromOutside, firstPersonNear);
    }

    private OwnHeadView ownHead;

    private void LayOwnHead(bool show)
    {
        var eyes = show && view != null ? view.GetComponent<Camera>() : null;
        if (eyes == null)
        {
            if (ownHead != null) ownHead.Hide();
            return;
        }
        if (ownHead == null && !TryGetComponent(out ownHead)) ownHead = gameObject.AddComponent<OwnHeadView>();
        ownHead.Show(eyes, headParts, this);
    }
}
