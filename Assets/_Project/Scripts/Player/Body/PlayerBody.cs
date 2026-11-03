using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The player's own visible body for true first person. The PlayerBody profile's character stands under the
/// code-driven camera, scaled so its eyes meet the camera, and plays clips chosen from the movement state.
/// Its eyes also carry the camera (PlayerBody.Look); F5 toggles a view from behind to check the body.
/// Presentation only: it never moves the player. Clip names and feel come from Tables/Player.csv, row Body.
/// Runs after the camera effects and before the weapons, so shots and the gun start from where the eyes are.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(250)]
public sealed partial class PlayerBody : MonoBehaviour, ITableBound
{
    [Header("Clips (names in the PlayerBody profile)")]
    [SerializeField] private string idle = "mx_idle";
    [SerializeField] private string walk = "mx_walk", walkBack = "mx_walk_back";
    [SerializeField] private string run = "mx_run", runBack = "mx_run_back", sprint = "sprintfwd";
    [SerializeField] private string crouchIdle = "crouchstill", crouchWalk = "crouchfwd", crouchBack = "crouchbwd";
    [SerializeField] private string jump = "jumpfast", air = "jumpidle", fall = "mx_fall";
    [SerializeField] private string land = "mx_fall_land";
    [SerializeField] private string slideStart = "slidestart", slide = "slideloop", slideEnd = "slideend";
    [SerializeField] private string diveStart = "divestart", dive = "diveidle";
    [SerializeField, Tooltip("A dive on the back, as Beatrun's jump turn: lying back in the air, then its loop. Turned over "
        + "from a front dive, or off an edge while lying, the body is already down and goes straight to the loop.")]
    private string supineDiveStart = "jumpturnfly", supineDive = "jumpturnflyidle";
    [SerializeField] private string bellySlideStart = "diveslidestart", bellySlide = "diveslideidle", bellySlideEnd = "diveslideend";
    [SerializeField] private string supineSlideStart = "meslidestart", supineSlide = "meslideloop", supineSlideEnd = "meslideend";
    [SerializeField] private string prone = "diveslideidle", supine = "jumpturnlandidle", roll = "mx_sprint_roll";
    [SerializeField] private string dodgeLeft = "dodgejumpleft", dodgeRight = "dodgejumpright", dashPush = "bargein";
    [SerializeField] private string vault = "vaultover", mantle = "climb1", wallClimb = "wallrunvertical", wallKick = "jumpwrleft";
    [SerializeField] private string barSwing = "swing", barRelease = "swingjumpoff", ledgeHang = "mx_ledge_hang_wall", ledgeJump = "hangheaveup";
    [SerializeField] private string death = "diestand";

    [Header("Feel")]
    [SerializeField, Min(.01f), Tooltip("Crossfade between clips (s).")]
    private float blendTime = .15f;
    [SerializeField, Min(.1f), Tooltip("Ground speed where each gait plays at its natural cadence (m/s); tune to the player's movement speeds.")]
    private float walkClipSpeed = 1.7f, runClipSpeed = 5.2f, sprintClipSpeed = 8.4f, crouchClipSpeed = 3f;
    [SerializeField, Range(0f, 90f), Tooltip("How far the hips turn toward the heading while sprinting (degrees). "
        + "Walk, run and crouch have side and back clips and keep facing the camera.")]
    private float maxStrafeYaw = 60f;
    [SerializeField, Min(0f), Tooltip("The head sits this far behind the camera so looking down never shows the inside of the neck (m).")]
    private float backOffset = .12f;
    [SerializeField, Min(.01f), Tooltip("How quickly the body slides to keep its head under the camera (s). Slower than a stride, so running sway stays.")]
    private float headFollowTime = .25f;
    [SerializeField, Min(0f), Tooltip("Falling faster than this plays the fast-fall clip (m/s).")]
    private float fallClipSpeed = 7.2f;
    [SerializeField, Tooltip("F5 view from behind, in look-pivot space (m).")]
    private Vector3 thirdPersonOffset = new Vector3(.35f, .2f, -2.4f);

    private sealed class Channel
    {
        public AnimationClipPlayable Playable;
        public int Input;
        public float Length, AuthoredSpeed, Weight, Target;
        public bool Synced;
    }

    private readonly List<Channel> channels = new List<Channel>();
    private readonly Dictionary<string, Channel> byName = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Renderer> headParts = new List<Renderer>();
    private readonly HashSet<string> reported = new HashSet<string>();
    private PlayerMovement player;
    private PlayerBodyProfile profile;
    private GameObject loadedModel;
    private PlayerBodyProfile.Skin loadedSkin;
    private string shownSkin;
    private Transform body, view, head, hips;
    private Animator animator;
    private Vector3 headAlignment;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private Channel transition;
    private MovementState state;
    private Vector3 viewHome;
    private string dodgeClip;
    private float transitionLeft, stateTime, airTime, phase, yaw, yawVelocity, targetYaw;
    private bool started, grounded, jumped, dead, thirdPerson, rebuild, playingOut, springing;
    private bool supineDiveAtOnce, slidesOnBack;
    private Vector3 lastHeadInBody;
    private int lastHeadFrame = -1;

    public bool IsThirdPerson => thirdPerson;
    /// <summary>The barge clip is playing: a dash push under way, or one that stopped on someone playing out.</summary>
    public bool PlaysDashPush => state == MovementState.DashPush || (transition != null && transition == Find(dashPush));
    internal Animator BodyAnimator => animator;
    internal Transform View => view;
    internal bool IsDead => dead;
    internal Vector2 LedgeGrip => ledgeGrip;
    /// <summary>Where the eyes are this frame, after the spine has bent with the view.</summary>
    internal Vector3 EyePosition => head != null ? head.TransformPoint(eyeLocal) : transform.position;
    internal PlayerBodyProfile.Skin Skin => loadedSkin;
    // A copy of someone else wears what they chose, whatever this PC's own setting says.
    private PlayerBodyProfile.Skin ChosenSkin => shownSkin != null ? profile.SkinOf(shownSkin) : profile.CurrentSkin;

    /// <summary>Wears the skin the player of this copy chose on their own PC.</summary>
    internal void ShowSkin(string id)
    {
        shownSkin = id;
        rebuild = started;
    }

    private void Start() { started = true; Build(); }
    private void OnEnable() { if (started) rebuild = true; }
    private void OnDisable() => Teardown();
    private void OnDestroy() => Teardown();

    // Clip names or feel may have changed in the table: rebuild the graph on the next frame.
    void ITableBound.OnTableApplied() => rebuild = started;

    private void Build()
    {
        bool wasThirdPerson = thirdPerson;
        Teardown();
        thirdPerson = wasThirdPerson;
        rebuild = false;
        player = GetComponent<PlayerMovement>();
        profile = PlayerBodyProfile.Load();
        if (player == null || profile == null) { enabled = false; return; }
        var camera = GetComponentInChildren<Camera>();
        view = camera != null ? camera.transform : null;
        cameraEffects = GetComponent<PlayerCameraEffects>();
        if (view != null) viewHome = cameraEffects != null ? cameraEffects.CameraLocalHome : view.localPosition;

        loadedSkin = ChosenSkin;
        loadedModel = profile.ModelOf(loadedSkin);
        body = Instantiate(loadedModel, transform, false).transform;
        body.name = "Body (" + loadedModel.name + ")";
        animator = body.GetComponentInChildren<Animator>();
        if (animator == null) animator = body.gameObject.AddComponent<Animator>();
        if (animator.avatar == null || !animator.avatar.isHuman)
            Report(loadedModel.name + " has no humanoid avatar; set Rig → Animation Type to Humanoid.");
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            foreach (string part in profile.FirstPersonShadowOnly)
                if (string.Equals(renderer.name, part, StringComparison.OrdinalIgnoreCase)) headParts.Add(renderer);
        }
        body.localScale = Vector3.one * FitScale();
        legYaw = transform.eulerAngles.y;
        head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        headAlignment = new Vector3(0f, 0f, -backOffset);
        body.localPosition = headAlignment;
        SetUpLook();
        SetUpFeet();
        hands = GetComponent<PlayerBodyHands>();
        hands?.BindAnimator(animator);
        GameSettings.Changed += OnSettingsChanged;

        graph = PlayableGraph.Create(name + " body");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 0);
        AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(BuildLayers());
        foreach (string clip in new[] { idle, crouchIdle, jump, air, fall, land, slideStart, slide, slideEnd,
                     diveStart, dive, supineDiveStart, supineDive, bellySlideStart, bellySlide, bellySlideEnd,
                     supineSlideStart, supineSlide, supineSlideEnd,
                     prone, supine, roll, dodgeLeft, dodgeRight, dashPush,
                     vault, mantle, wallClimb, wallKick, barSwing, barRelease, ledgeHang, ledgeJump, death })
            AddChannel(clip, 0f);
        foreach (string clip in VariantClips) AddChannel(clip, 0f);
        AddHangChannels();
        foreach (string clip in FixtureClips) AddChannel(clip, 0f);
        BindHops();
        AddArmedChannels();
        AddLocomotionChannels();
        SetUpAnimationViewChannels();
        leftHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        rightHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

        state = player.CurrentState;
        grounded = player.IsGrounded;
        stateTime = airTime = gripWeight = standBlendLeft = 0f;
        heaving = false;
        heaveClip = null;
        hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        jumpClip = jump; obstacleClip = vault; wallKickClip = wallKick; rollClip = roll; downClip = heavyLand; tucked = flailing = false;
        var rest = Find(idle);
        if (rest != null) rest.Weight = rest.Target = 1f;
        graph.Play();
        ApplyView();
    }

    private void Teardown()
    {
        GameSettings.Changed -= OnSettingsChanged;
        UnbindHops();
        if (viewCamera != null) viewCamera.nearClipPlane = baseNearClip;
        viewCamera = null;
        if (graph.IsValid()) graph.Destroy();
        TeardownLayers();
        channels.Clear();
        byName.Clear();
        headParts.Clear();
        transition = null;
        playingOut = false;
        if (body != null) Destroy(body.gameObject);
        body = head = hips = leftHand = rightHand = null;
        loadedModel = null;
        loadedSkin = null;
        animator = null;
        if (view != null) { view.localPosition = viewHome; view.localRotation = viewRotationHome; }
        thirdPerson = false;
    }

    // Prefer mapped eyes; otherwise estimate them from the head and crown. Fit to the standing camera height.
    private float FitScale()
    {
        var head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return 1f;
        float headY = body.InverseTransformPoint(head.position).y, top = headY;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, renderer.bounds.max.y - body.position.y);
        float eye = top > headY + .02f ? headY + (top - headY) * .45f : headY * 1.08f;
        var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
        if (leftEye != null && rightEye != null)
        {
            float apart = Vector3.Distance(leftEye.position, rightEye.position);
            float eyeY = body.InverseTransformPoint((leftEye.position + rightEye.position) * .5f).y;
            // Hair, animal ears and halos must not raise the estimated eye point on stylised characters.
            if (apart > .015f && apart < .2f && eyeY > headY && eyeY < headY + .25f) eye = eyeY;
        }
        float camera = player.runtimeBodyDefaults.w;
        return eye > .1f && camera > .1f ? camera / eye : 1f;
    }

    private void AddChannel(string clipName, float authoredSpeed)
    {
        if (string.IsNullOrEmpty(clipName) || byName.ContainsKey(clipName)) return;
        var clip = profile.Find(clipName);
        if (clip == null)
        {
            Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return;
        }
        var playable = AnimationClipPlayable.Create(graph, clip);
        // Foot IK chases the source clip's foot goals, which the yawOffset turn leaves twisted at the ankles.
        playable.SetApplyFootIK(false);
        int input = mixer.GetInputCount();
        mixer.SetInputCount(input + 1);
        graph.Connect(playable, 0, mixer, input);
        var channel = new Channel
        {
            Playable = playable, Input = input, Length = Mathf.Max(.01f, clip.length),
            AuthoredSpeed = authoredSpeed, Synced = authoredSpeed > 0f
        };
        // Locomotion cycles share one phase so blending between them never puts the feet out of step.
        if (channel.Synced) playable.SetSpeed(0);
        channels.Add(channel);
        byName[clipName] = channel;
    }

    // With a gun out, the gun-holding version of the clip when there is one.
    private Channel Find(string clipName) => !string.IsNullOrEmpty(clipName) && byName.TryGetValue(Armed(clipName), out var c) ? c : null;

    private void Update()
    {
        if (rebuild) Build();
        if (!graph.IsValid() || player == null) return;
        float dt = Time.deltaTime;
        if (HoldsForPause() || dt <= 0f) return;
        var keyboard = Keyboard.current;
        if (keyboard != null && player.IsLocal && keyboard.f5Key.wasPressedThisFrame)
        {
            thirdPerson = !thirdPerson;
            ApplyView();
        }
        if (StaysLimp()) return;
        foreach (var channel in channels) channel.Target = 0f;
        targetYaw = 0f;
        legsPlanted = false;
        ChoosePose(dt);
        Blend(dt);
        BlendLayers(dt);
        if (legsPlanted) { yaw = targetYaw; yawVelocity = 0f; }
        else
        {
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, .12f);
            legYaw = transform.eulerAngles.y + yaw;
        }
        body.localRotation = Quaternion.Euler(0f, yaw, 0f);
        PlaceBody();
    }

    private void LateUpdate()
    {
        if (GamePause.IsPaused || Time.deltaTime <= 0f) return;
        if (body != null && !limp)
        {
            // Hanging, the hands hold the body in place instead of the head following the camera. FollowHead aims at
            // the camera's pivot, not the camera, so it keeps running while the camera rides the eyes: the camera
            // stays over the capsule and takes the head's dips and quick sway.
            HoldHeadPlace();
            if (gripWeight <= 0f && !slideBodyAnchored && !ClipCarriesBody) FollowHead(Time.deltaTime);
            Grip(Time.deltaTime);
            LiftTuckedFeet(Time.deltaTime);
            LeanHips();
            PlantFeet(Time.deltaTime);
            KeepLegsOffWall();
            Look(Time.deltaTime);
        }
        if (thirdPerson) PlaceViewBehind();
    }

    // Clips put the head in different places (lying slides keep it far behind the feet). Ease the body so its head
    // stays just behind the camera; the follow is slower than a stride, so running sway is not cancelled.
    private void FollowHead(float dt)
    {
        if (head == null || view == null || view.parent == null || dt <= 0f) return;
        Vector3 camera = transform.InverseTransformPoint(view.parent.position) - Vector3.forward * backOffset;
        Vector3 error = camera - transform.InverseTransformPoint(head.position);
        error.y = 0f;
        headAlignment += error * (1f - Mathf.Exp(-dt / headFollowTime));
        headAlignment = Vector3.ClampMagnitude(headAlignment, 1.5f);
    }

    // Lying back from a slide or a dive, Beatrun's clips carry the whole body on ahead: meslideendprone moves it about
    // 0.9 m, starting from a slide pose that sits that much further back than our slide's. Beatrun's camera isn't on the
    // head; ours is, so through those clips the head keeps its place over the ground instead. The view only goes down,
    // and the legs stretch out ahead of it.
    private void HoldHeadPlace()
    {
        if (head == null) return;
        Vector3 inBody = body.InverseTransformPoint(head.position);
        bool hold = !dead && transition != null && (transition == Find(supineSlideEndProne) || transition == Find(supineLand));
        if (hold && lastHeadFrame == Time.frameCount - 1)
        {
            Vector3 drift = transform.InverseTransformVector(body.TransformVector(inBody - lastHeadInBody));
            drift.y = 0f;
            headAlignment -= drift;
            body.localPosition -= drift;
        }
        lastHeadInBody = inBody;
        lastHeadFrame = Time.frameCount;
    }

    private void ChoosePose(float dt)
    {
        var receiver = player.DamageReceiver;
        if (receiver != null && !receiver.IsAlive)
        {
            if (!dead) Restart(Find(death));
            dead = true;
            Want(death);
            return;
        }
        dead = false;

        var current = player.CurrentState;
        bool onGround = player.IsGrounded;
        if (current != state) { Enter(state, current, onGround); state = current; stateTime = 0f; }
        if (onGround != grounded) { Touch(onGround); grounded = onGround; }
        stateTime += dt;
        airTime = onGround ? 0f : airTime + dt;
        TickHops(dt);

        // Landing and slide exits play out on the feet unless the player is already moving off; a vault plays on to
        // its touchdown whatever the feet are doing.
        if (transition != null)
        {
            transitionLeft -= dt;
            bool onFeet = onGround && (state == MovementState.Standing || state == MovementState.Crouching
                || state == MovementState.Proning || state == MovementState.Supine);
            bool movingOff = transitionLeft < transition.Length - .25f && HorizontalSpeed() > walkClipSpeed;
            bool upright = state == MovementState.Standing || state == MovementState.Crouching;
            bool hold = playingOut ? upright && (!onGround || !movingOff) : onFeet && !movingOff;
            if (transitionLeft > blendTime && hold)
            {
                transition.Target = 1f;
                return;
            }
            transition = null;
            playingOut = false;
        }

        switch (state)
        {
            case MovementState.Standing:
            case MovementState.Crouching:
                if (onGround) Locomotion(state == MovementState.Crouching, dt);
                else if (ShowsTouchdown) Want(touchdown);
                else AirPose();
                break;
            case MovementState.Sliding: Sequence(slideStart, slide); FaceSlide(); break;
            case MovementState.SupineSliding:
                if (slidesOnBack) Sequence(supineLand, supine);
                else Sequence(supineSlideStart, supineSlide);
                break;
            case MovementState.HeadFirstSliding: Sequence(bellySlideStart, bellySlide); break;
            case MovementState.Diving: Sequence(diveStart, dive); break;
            case MovementState.SupineDiving:
                Sequence(supineDiveAtOnce ? null : supineDiveStart, supineDive);
                FaceBack(dt);
                break;
            case MovementState.Proning: Want(prone); break;
            case MovementState.Supine: Want(supine); break;
            case MovementState.StepDodge: Want(dodgeClip); break;
            case MovementState.LandingRoll:
                // A roll lasts as long as the move; a landing on the spot plays at its own pace and outlasts it.
                if (player.Abilities.Landing.LandsInPlace) Want(rollClip);
                else Drive(rollClip, stateTime / Mathf.Max(.05f, player.AbilitySettings.rollDuration));
                break;
            case MovementState.HeavyLanding: DownPose(); break;
            case MovementState.DashPush: Want(dashPush); break;
            case MovementState.Vault: ObstaclePose(); break;
            case MovementState.Mantle:
                var exitLadder = player.Abilities.Traversal.Obstacle.ExitLadder;
                if (exitLadder != null) targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, exitLadder.ClimberYaw);
                if (player.Abilities.Traversal.Obstacle.Kind == VaultKind.None) Drive(obstacleClip, player.Abilities.Traversal.ObstacleProgress);
                else ObstaclePose();
                break;
            case MovementState.WallClimb: Sequence(wallClimbStart, wallClimb); break;
            case MovementState.WallKick: Want(wallKickClip); break;
            case MovementState.BarGrab:
            case MovementState.BarSwing: BarPose(dt); break;
            case MovementState.LacheRelease: Want(barRelease); break;
            case MovementState.LedgeHang: LedgePose(); break;
            case MovementState.LedgeJump: Want(ledgeJump); break;
            case MovementState.Ladder: LadderPose(); break;
            case MovementState.Zipline: ZiplinePose(); break;
            default: Want(idle); break;
        }
    }

    // One-shot clips start from their first frame when their state begins.
    private void Enter(MovementState from, MovementState to, bool onGround)
    {
        if (onGround && (to == MovementState.Standing || to == MovementState.Crouching || to == MovementState.Proning
            || to == MovementState.Supine))
        {
            string exit = SlideEndFor(from, to);
            if (exit != null) PlayTransition(exit);
        }
        if ((from == MovementState.Vault || from == MovementState.Mantle) && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOutObstacle();
        if (heaving && from == MovementState.Mantle) standBlendLeft = heaveStandTime;
        if (from == MovementState.DashPush && onGround && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOn(dashPush);
        if (from == MovementState.LandingRoll && onGround && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOn(rollClip);
        switch (to)
        {
            case MovementState.Sliding: Restart(Find(slideStart)); break;
            case MovementState.SupineSliding:
                // Down from a dive on the back still sliding: Beatrun lands its jump turn with jumpturnland at any speed.
                slidesOnBack = from == MovementState.SupineDiving;
                Restart(Find(slidesOnBack ? supineLand : supineSlideStart));
                break;
            case MovementState.HeadFirstSliding: Restart(Find(bellySlideStart)); break;
            case MovementState.Diving: Restart(Find(diveStart)); break;
            case MovementState.SupineDiving:
                // Turned over from a front dive, or off an edge while lying, the body is already down.
                supineDiveAtOnce = from == MovementState.Diving || from == MovementState.Supine;
                if (!supineDiveAtOnce) Restart(Find(supineDiveStart));
                break;
            case MovementState.StepDodge:
                dodgeClip = Vector3.Dot(player.Abilities.ActionDirection, transform.right) < 0f ? dodgeLeft : dodgeRight;
                Restart(Find(dodgeClip));
                break;
            case MovementState.LandingRoll:
                rollClip = RollFor();
                Restart(Find(rollClip));
                break;
            case MovementState.HeavyLanding: downClip = DownClip(player.Abilities.Landing.HeavyPose); break;
            case MovementState.DashPush: Restart(Find(dashPush)); break;
            case MovementState.Vault:
            case MovementState.Mantle:
                obstacleClip = from == MovementState.Ladder ? LadderExitClip() : ObstacleClip();
                StartObstacleClip();
                break;
            case MovementState.WallClimb: Restart(Find(wallClimbStart)); break;
            case MovementState.WallKick:
                wallKickClip = WallKickFor();
                Restart(Find(wallKickClip));
                break;
            case MovementState.LedgeHang:
                Restart(Find(ledgeCatch));
                hangYaw = 0f;
                break;
            case MovementState.LacheRelease: Restart(Find(barRelease)); break;
            case MovementState.LedgeJump: Restart(Find(ledgeJump)); break;
            case MovementState.Ladder: StartLadder(); break;
            case MovementState.Zipline: Restart(Find(ziplineStart)); break;
        }
    }

    private void Touch(bool onGround)
    {
        bool wasTucked = tucked;
        tucked = flailing = false;
        if (onGround && springing) { transition = null; playingOut = springing = false; }
        if (!onGround)
        {
            // A jump starts its clip in TickHops; anything else that throws the body up starts it here.
            jumped = jumpQueued || jumpPending || player.VerticalVelocity > 1f;
            if (jumped && !jumpQueued && !jumpPending)
            {
                jumpClip = JumpFor(HorizontalSpeed());
                Restart(Find(jumpClip));
            }
            return;
        }
        if (state == MovementState.Standing || state == MovementState.Crouching)
        {
            string landing = LandingFor(state == MovementState.Crouching, wasTucked);
            if (landing != null) PlayTransition(landing);
            else if (wasTucked && state == MovementState.Standing) SettleFromTuck();
        }
    }

    private void Blend(float dt)
    {
        float step = dt / (standBlendLeft > 0f ? heaveStandTime : blendTime), total = 0f;
        standBlendLeft -= dt;
        foreach (var channel in channels)
        {
            channel.Weight = Mathf.MoveTowards(channel.Weight, channel.Target, step);
            total += channel.Weight;
        }
        if (total <= .0001f) return;
        foreach (var channel in channels)
        {
            mixer.SetInputWeight(channel.Input, channel.Weight / total);
            if (channel.Synced) channel.Playable.SetTime(phase * channel.Length);
        }
    }

    private void Want(string clipName)
    {
        var channel = Find(clipName) ?? Find(idle);
        if (channel != null) channel.Target = 1f;
    }

    private void Mix(string clipName, float weight)
    {
        var channel = Find(clipName);
        if (channel != null && weight > 0f) channel.Target += weight;
    }

    // An entry clip plays once, then the state's loop takes over.
    private void Sequence(string entry, string loop)
    {
        var first = Find(entry);
        Want(first != null && stateTime < first.Length - blendTime ? entry : loop);
    }

    private void PlayTransition(string clipName)
    {
        var channel = Find(clipName);
        if (channel == null) return;
        Restart(channel);
        transition = channel;
        transitionLeft = channel.Length;
        playingOut = false;
    }

    // A one-shot clip carries on from where it is as a landing-like transition: the barge recovers on the spot when a
    // dash push stops on someone, and gives way at once to a run that carries on.
    private void PlayOn(string clipName)
    {
        var channel = Find(clipName);
        if (channel == null || channel.Synced) return;
        float left = channel.Length - (float)channel.Playable.GetTime();
        if (left <= blendTime) return;
        transition = channel;
        transitionLeft = left;
        playingOut = false;
    }

    private static void Restart(Channel channel)
    {
        if (channel == null || channel.Synced) return;
        channel.Playable.SetSpeed(1);
        channel.Playable.SetTime(0);
        channel.Playable.SetDone(false);
    }

    // A one-shot clip held at the share of its length the movement has reached (a vault's progress), so the body
    // takes exactly as long as the move.
    private void Drive(string clipName, float share)
    {
        var channel = Find(clipName);
        if (channel == null || channel.Synced) { Want(clipName); return; }
        channel.Playable.SetSpeed(0);
        channel.Playable.SetTime(Mathf.Clamp01(share) * channel.Length);
        channel.Target = 1f;
    }

    private float HorizontalSpeed() => new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;

    private void ApplyView()
    {
        ApplyHeadVisibility();
    }

    private void OnSettingsChanged()
    {
        if (profile != null && ChosenSkin != loadedSkin) rebuild = true;
        ApplyView();
    }

    private void PlaceViewBehind()
    {
        if (view == null || view.parent == null) return;
        view.localRotation = viewRotationHome;
        Transform pivot = view.parent;
        Vector3 origin = pivot.position, wanted = pivot.TransformPoint(thirdPersonOffset), offset = wanted - origin;
        float distance = offset.magnitude;
        if (distance > .01f && Physics.SphereCast(origin, .2f, offset / distance, out RaycastHit hit, distance,
                ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
            wanted = origin + offset / distance * Mathf.Max(.1f, hit.distance - .05f);
        view.position = wanted;
    }

    private void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("[PlayerBody] " + message, this);
    }
}
