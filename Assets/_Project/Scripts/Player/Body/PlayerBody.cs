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
    [SerializeField] private string bellySlideStart = "diveslidestart", bellySlide = "diveslideidle", bellySlideEnd = "diveslideend";
    [SerializeField] private string supineSlideStart = "meslidestart", supineSlide = "meslideloop", supineSlideEnd = "meslideend";
    [SerializeField] private string prone = "diveslideidle", supine = "meslideloop", roll = "mx_fall_roll";
    [SerializeField] private string dodgeLeft = "dodgejumpleft", dodgeRight = "dodgejumpright", dashPush = "bargein";
    [SerializeField] private string vault = "mx_vault_one_hand", mantle = "climb1", wallClimb = "wallrunvertical", wallKick = "jumpwrleft";
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
    private Transform body, view, head;
    private Animator animator;
    private Vector3 headAlignment;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private Channel transition;
    private MovementState state;
    private Vector3 viewHome;
    private string dodgeClip;
    private float transitionLeft, stateTime, airTime, phase, yaw, yawVelocity, targetYaw;
    private bool started, grounded, jumped, dead, thirdPerson, rebuild, playingOut;

    public bool IsThirdPerson => thirdPerson;
    /// <summary>The barge clip is playing: a dash push under way, or one that stopped on someone playing out.</summary>
    public bool PlaysDashPush => state == MovementState.DashPush || (transition != null && transition == Find(dashPush));
    internal Animator BodyAnimator => animator;
    internal Transform View => view;
    internal bool IsDead => dead;
    internal Vector2 LedgeGrip => ledgeGrip;
    /// <summary>Where the eyes are this frame, after the spine has bent with the view.</summary>
    internal Vector3 EyePosition => head != null ? head.TransformPoint(eyeLocal) : transform.position;

    private void Start() { started = true; Build(); }
    private void OnEnable() { if (started) rebuild = true; }
    private void OnDisable() => Teardown();
    private void OnDestroy() => Teardown();

    // Clip names or feel may have changed in the table: rebuild the graph on the next frame.
    void ITableBound.OnTableApplied() => rebuild = started;

    private void Build()
    {
        Teardown();
        rebuild = false;
        player = GetComponent<PlayerMovement>();
        profile = PlayerBodyProfile.Load();
        if (player == null || profile == null) { enabled = false; return; }
        var camera = GetComponentInChildren<Camera>();
        view = camera != null ? camera.transform : null;
        cameraEffects = GetComponent<PlayerCameraEffects>();
        if (view != null) viewHome = cameraEffects != null ? cameraEffects.CameraLocalHome : view.localPosition;

        body = Instantiate(profile.Model, transform, false).transform;
        body.name = "Body (" + profile.Model.name + ")";
        animator = body.GetComponentInChildren<Animator>();
        if (animator == null) animator = body.gameObject.AddComponent<Animator>();
        if (animator.avatar == null || !animator.avatar.isHuman)
            Report(profile.Model.name + " has no humanoid avatar; set Rig → Animation Type to Humanoid.");
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
        GameSettings.Changed += ApplyView;

        graph = PlayableGraph.Create(name + " body");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 0);
        AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
        foreach (string clip in new[] { idle, crouchIdle, jump, air, fall, land, slideStart, slide, slideEnd,
                     diveStart, dive, bellySlideStart, bellySlide, bellySlideEnd, supineSlideStart, supineSlide, supineSlideEnd,
                     prone, supine, roll, dodgeLeft, dodgeRight, dashPush,
                     vault, mantle, wallClimb, wallKick, barSwing, barRelease, ledgeHang, ledgeJump, death })
            AddChannel(clip, 0f);
        foreach (string clip in VariantClips) AddChannel(clip, 0f);
        foreach (string clip in FixtureClips) AddChannel(clip, 0f);
        AddArmedChannels();
        AddLocomotionChannels();
        leftHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        rightHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

        state = player.CurrentState;
        grounded = player.IsGrounded;
        stateTime = airTime = gripWeight = 0f;
        jumpClip = jump; obstacleClip = vault; wallKickClip = wallKick; rollClip = roll; downClip = heavyLand; tucked = flailing = false;
        var rest = Find(idle);
        if (rest != null) rest.Weight = rest.Target = 1f;
        graph.Play();
        ApplyView();
    }

    private void Teardown()
    {
        GameSettings.Changed -= ApplyView;
        if (viewCamera != null) viewCamera.nearClipPlane = baseNearClip;
        viewCamera = null;
        if (graph.IsValid()) graph.Destroy();
        channels.Clear();
        byName.Clear();
        headParts.Clear();
        transition = null;
        playingOut = false;
        if (body != null) Destroy(body.gameObject);
        body = head = leftHand = rightHand = null;
        animator = null;
        if (view != null) view.localPosition = viewHome;
        thirdPerson = false;
    }

    // Eyes sit about 45% of the way from the head bone to the crown; the standing camera height is the target.
    private float FitScale()
    {
        var head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return 1f;
        float headY = body.InverseTransformPoint(head.position).y, top = headY;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, renderer.bounds.max.y - body.position.y);
        float eye = top > headY + .02f ? headY + (top - headY) * .45f : headY * 1.08f;
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
        if (GamePause.IsPaused || dt <= 0f) return;
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
        {
            thirdPerson = !thirdPerson;
            ApplyView();
        }
        foreach (var channel in channels) channel.Target = 0f;
        targetYaw = 0f;
        legsPlanted = false;
        ChoosePose(dt);
        Blend(dt);
        if (legsPlanted) { yaw = targetYaw; yawVelocity = 0f; }
        else
        {
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, .12f);
            legYaw = transform.eulerAngles.y + yaw;
        }
        body.localRotation = Quaternion.Euler(0f, yaw, 0f) * BalanceLean(dt);
        PlaceBody();
    }

    private void LateUpdate()
    {
        if (GamePause.IsPaused || Time.deltaTime <= 0f) return;
        if (body != null)
        {
            // Hanging, the hands hold the body in place instead of the head following the camera. FollowHead aims at
            // the camera's pivot, not the camera, so it keeps running while the camera rides the eyes: the camera
            // stays over the capsule and takes the head's dips and quick sway.
            if (state == MovementState.Balance) CentreOverFeet(Time.deltaTime);
            else if (gripWeight <= 0f && !slideBodyAnchored) FollowHead(Time.deltaTime);
            Grip(Time.deltaTime);
            PlantFeet(Time.deltaTime);
            Look(Time.deltaTime);
            PublishBalanceCycle();
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

        // Landing and slide exits play out on the feet unless the player is already moving off; a vault plays on to
        // its touchdown whatever the feet are doing.
        if (transition != null)
        {
            transitionLeft -= dt;
            bool onFeet = onGround && (state == MovementState.Standing || state == MovementState.Crouching
                || state == MovementState.Proning);
            bool movingOff = transitionLeft < transition.Length - .25f && HorizontalSpeed() > walkClipSpeed;
            bool hold = playingOut ? state == MovementState.Standing || state == MovementState.Crouching : onFeet && !movingOff;
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
                else AirPose();
                break;
            case MovementState.Sliding: Sequence(slideStart, slide); FaceSlide(); break;
            case MovementState.SupineSliding: Sequence(supineSlideStart, supineSlide); break;
            case MovementState.HeadFirstSliding: Sequence(bellySlideStart, bellySlide); break;
            case MovementState.Diving:
            case MovementState.SupineDiving: Sequence(diveStart, dive); break;
            case MovementState.Proning: Want(prone); break;
            case MovementState.Supine: Want(supine); break;
            case MovementState.StepDodge: Want(dodgeClip); break;
            case MovementState.LandingRoll: Drive(rollClip, stateTime / Mathf.Max(.05f, player.AbilitySettings.rollDuration)); break;
            case MovementState.HeavyLanding: DownPose(); break;
            case MovementState.DashPush: Want(dashPush); break;
            case MovementState.Vault: Want(obstacleClip); break;
            case MovementState.Mantle: Drive(obstacleClip, player.Abilities.Traversal.ObstacleProgress); break;
            case MovementState.WallClimb: Sequence(wallClimbStart, wallClimb); break;
            case MovementState.WallKick: Want(wallKickClip); break;
            case MovementState.BarGrab:
            case MovementState.BarSwing: Want(barSwing); break;
            case MovementState.LacheRelease: Want(barRelease); break;
            case MovementState.LedgeHang: LedgePose(); break;
            case MovementState.LedgeJump: Want(ledgeJump); break;
            case MovementState.Ladder: LadderPose(); break;
            case MovementState.Zipline: ZiplinePose(); break;
            case MovementState.Balance: BalancePose(); break;
            case MovementState.BalanceFall: Want(balanceFallClip); break;
            default: Want(idle); break;
        }
    }

    // One-shot clips start from their first frame when their state begins.
    private void Enter(MovementState from, MovementState to, bool onGround)
    {
        if (onGround && (to == MovementState.Standing || to == MovementState.Crouching || to == MovementState.Proning))
        {
            string exit = SlideEndFor(from, to);
            if (exit != null) PlayTransition(exit);
        }
        if (from == MovementState.Vault && (to == MovementState.Standing || to == MovementState.Crouching)) PlayOutVault();
        if (from == MovementState.DashPush && onGround && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOn(dashPush);
        switch (to)
        {
            case MovementState.Sliding: Restart(Find(slideStart)); break;
            case MovementState.SupineSliding: Restart(Find(supineSlideStart)); break;
            case MovementState.HeadFirstSliding: Restart(Find(bellySlideStart)); break;
            case MovementState.Diving:
            case MovementState.SupineDiving: Restart(Find(diveStart)); break;
            case MovementState.StepDodge:
                dodgeClip = Vector3.Dot(player.Abilities.ActionDirection, transform.right) < 0f ? dodgeLeft : dodgeRight;
                Restart(Find(dodgeClip));
                break;
            case MovementState.LandingRoll: rollClip = RollFor(); break;
            case MovementState.HeavyLanding: downClip = DownClip(player.Abilities.Landing.HeavyPose); break;
            case MovementState.DashPush: Restart(Find(dashPush)); break;
            case MovementState.Vault:
            case MovementState.Mantle:
                obstacleClip = from == MovementState.Ladder ? LadderExitClip() : ObstacleClip(to == MovementState.Vault, from == MovementState.LedgeHang);
                if (to == MovementState.Vault) StartVault();
                break;
            case MovementState.WallClimb: Restart(Find(wallClimbStart)); break;
            case MovementState.WallKick:
                wallKickClip = WallKickFor();
                Restart(Find(wallKickClip));
                break;
            case MovementState.LedgeHang: Restart(Find(ledgeCatch)); break;
            case MovementState.LacheRelease: Restart(Find(barRelease)); break;
            case MovementState.LedgeJump: Restart(Find(ledgeJump)); break;
            case MovementState.Ladder: StartLadder(); break;
            case MovementState.Zipline: Restart(Find(ziplineStart)); break;
            case MovementState.BalanceFall: StartBalanceFall(); break;
        }
    }

    private void Touch(bool onGround)
    {
        bool wasTucked = tucked;
        tucked = flailing = false;
        if (!onGround)
        {
            jumped = player.VerticalVelocity > 1f;
            if (jumped)
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
        }
    }

    private void Blend(float dt)
    {
        float step = dt / blendTime, total = 0f;
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

    private void PlaceViewBehind()
    {
        if (view == null || view.parent == null) return;
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
