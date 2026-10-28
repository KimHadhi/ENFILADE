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
/// Head and hair only cast shadows in first person; F5 toggles a view from behind to check the body.
/// Presentation only: it never moves the player. Clip names and feel come from Tables/Player.csv, row Body.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(450)]
public sealed class PlayerBody : MonoBehaviour, ITableBound
{
    [Header("Clips (names in the PlayerBody profile)")]
    [SerializeField] private string idle = "stand";
    [SerializeField] private string walk = "walkfwd", walkBack = "walkback";
    [SerializeField] private string run = "runfwd", runBack = "runbwd", sprint = "sprintfwd";
    [SerializeField] private string crouchIdle = "crouchstill", crouchWalk = "crouchfwd", crouchBack = "crouchbwd";
    [SerializeField] private string jump = "jumpfast", air = "jumpidle", fall = "fallinguncontrolled";
    [SerializeField] private string land = "land", landMoving = "runland";
    [SerializeField] private string slideStart = "slidestart", slide = "slideloop", slideEnd = "slideend";
    [SerializeField] private string diveStart = "divestart", dive = "diveidle";
    [SerializeField] private string bellySlideStart = "diveslidestart", bellySlide = "diveslideidle", bellySlideEnd = "diveslideend";
    [SerializeField] private string supineSlideStart = "meslidestart", supineSlide = "meslideloop", supineSlideEnd = "meslideend";
    [SerializeField] private string prone = "diveslideidle", supine = "meslideloop", roll = "meroll";
    [SerializeField] private string dodgeLeft = "dodgejumpleft", dodgeRight = "dodgejumpright", tackle = "bargein";
    [SerializeField] private string vault = "vaultover", mantle = "climb1", wallClimb = "wallrunvertical", wallKick = "jumpwrleft";
    [SerializeField] private string barSwing = "swing", barRelease = "swingjumpoff", ledgeHang = "hang", ledgeJump = "hangheaveup";
    [SerializeField] private string death = "diestand";

    [Header("Feel")]
    [SerializeField, Min(.01f), Tooltip("Crossfade between clips (s).")]
    private float blendTime = .15f;
    [SerializeField, Min(.1f), Tooltip("Ground speed each locomotion clip was authored for (m/s); playback scales around it.")]
    private float walkClipSpeed = 1.5f, runClipSpeed = 4f, sprintClipSpeed = 8f, crouchClipSpeed = 2f;
    [SerializeField, Range(0f, 90f), Tooltip("How far the body turns toward a strafe or backpedal direction (degrees).")]
    private float maxStrafeYaw = 60f;
    [SerializeField, Min(0f), Tooltip("The head sits this far behind the camera so looking down never shows the inside of the neck (m).")]
    private float backOffset = .12f;
    [SerializeField, Min(.01f), Tooltip("How quickly the body slides to keep its head under the camera (s). Slower than a stride, so running sway stays.")]
    private float headFollowTime = .25f;
    [SerializeField, Min(0f), Tooltip("Falls faster than this end in a landing clip (m/s).")]
    private float hardLandingSpeed = 9f;
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
    private float transitionLeft, stateTime, airTime, fallSpeed, phase, yaw, yawVelocity, targetYaw;
    private bool started, grounded, jumped, dead, thirdPerson, rebuild;

    public bool IsThirdPerson => thirdPerson;

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
        if (view != null) viewHome = view.localPosition;

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
        head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        headAlignment = new Vector3(0f, 0f, -backOffset);

        graph = PlayableGraph.Create(name + " body");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 0);
        AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
        foreach (string clip in new[] { idle, crouchIdle, jump, air, fall, land, landMoving, slideStart, slide, slideEnd,
                     diveStart, dive, bellySlideStart, bellySlide, bellySlideEnd, supineSlideStart, supineSlide, supineSlideEnd,
                     prone, supine, roll, dodgeLeft, dodgeRight, tackle,
                     vault, mantle, wallClimb, wallKick, barSwing, barRelease, ledgeHang, ledgeJump, death })
            AddChannel(clip, 0f);
        AddChannel(walk, walkClipSpeed);
        AddChannel(walkBack, walkClipSpeed);
        AddChannel(run, runClipSpeed);
        AddChannel(runBack, runClipSpeed);
        AddChannel(sprint, sprintClipSpeed);
        AddChannel(crouchWalk, crouchClipSpeed);
        AddChannel(crouchBack, crouchClipSpeed);

        state = player.CurrentState;
        grounded = player.IsGrounded;
        stateTime = airTime = fallSpeed = 0f;
        var rest = Find(idle);
        if (rest != null) rest.Weight = rest.Target = 1f;
        graph.Play();
        ApplyView();
    }

    private void Teardown()
    {
        if (graph.IsValid()) graph.Destroy();
        channels.Clear();
        byName.Clear();
        headParts.Clear();
        transition = null;
        if (body != null) Destroy(body.gameObject);
        body = head = null;
        animator = null;
        if (thirdPerson && view != null) view.localPosition = viewHome;
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

    private Channel Find(string clipName) => !string.IsNullOrEmpty(clipName) && byName.TryGetValue(clipName, out var c) ? c : null;

    private void Update()
    {
        if (rebuild) Build();
        if (!graph.IsValid() || player == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
        {
            thirdPerson = !thirdPerson;
            ApplyView();
        }
        foreach (var channel in channels) channel.Target = 0f;
        targetYaw = 0f;
        ChoosePose(dt);
        Blend(dt);
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, .12f);
        body.localPosition = headAlignment;
        body.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void LateUpdate()
    {
        if (body != null) FollowHead(Time.deltaTime);
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
        if (!onGround) fallSpeed = Mathf.Min(fallSpeed, player.VerticalVelocity);

        // Landing and slide exits play out on the feet unless the player is already moving off.
        if (transition != null)
        {
            transitionLeft -= dt;
            bool onFeet = onGround && (state == MovementState.Standing || state == MovementState.Crouching);
            bool movingOff = transitionLeft < transition.Length - .25f && HorizontalSpeed() > walkClipSpeed;
            if (transitionLeft > blendTime && onFeet && !movingOff)
            {
                transition.Target = 1f;
                return;
            }
            transition = null;
        }

        switch (state)
        {
            case MovementState.Standing:
            case MovementState.Crouching:
                if (onGround) Locomotion(state == MovementState.Crouching, dt);
                else if (jumped && airTime < (Find(jump)?.Length ?? 0f)) Want(jump);
                else Want(player.VerticalVelocity < -hardLandingSpeed * .6f ? fall : air);
                break;
            case MovementState.Sliding: Sequence(slideStart, slide); break;
            case MovementState.SupineSliding: Sequence(supineSlideStart, supineSlide); break;
            case MovementState.HeadFirstSliding: Sequence(bellySlideStart, bellySlide); break;
            case MovementState.Diving:
            case MovementState.SupineDiving: Sequence(diveStart, dive); break;
            case MovementState.Proning: Want(prone); break;
            case MovementState.Supine: Want(supine); break;
            case MovementState.StepDodge: Want(dodgeClip); break;
            case MovementState.LandingRoll: Want(roll); break;
            case MovementState.Tackle: Want(tackle); break;
            case MovementState.Vault: Want(vault); break;
            case MovementState.Mantle: Want(mantle); break;
            case MovementState.WallClimb: Want(wallClimb); break;
            case MovementState.WallKick: Want(wallKick); break;
            case MovementState.BarGrab:
            case MovementState.BarSwing: Want(barSwing); break;
            case MovementState.LacheRelease: Want(barRelease); break;
            case MovementState.LedgeHang: Want(ledgeHang); break;
            case MovementState.LedgeJump: Want(ledgeJump); break;
            default: Want(idle); break;
        }
    }

    // One-shot clips start from their first frame when their state begins.
    private void Enter(MovementState from, MovementState to, bool onGround)
    {
        if (onGround && (to == MovementState.Standing || to == MovementState.Crouching))
        {
            if (from == MovementState.Sliding) PlayTransition(slideEnd);
            else if (from == MovementState.SupineSliding) PlayTransition(supineSlideEnd);
            else if (from == MovementState.HeadFirstSliding) PlayTransition(bellySlideEnd);
        }
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
            case MovementState.LandingRoll: Restart(Find(roll)); break;
            case MovementState.Tackle: Restart(Find(tackle)); break;
            case MovementState.Vault: Restart(Find(vault)); break;
            case MovementState.Mantle: Restart(Find(mantle)); break;
            case MovementState.WallKick: Restart(Find(wallKick)); break;
            case MovementState.LacheRelease: Restart(Find(barRelease)); break;
            case MovementState.LedgeJump: Restart(Find(ledgeJump)); break;
        }
    }

    private void Touch(bool onGround)
    {
        if (!onGround)
        {
            jumped = player.VerticalVelocity > 1f;
            fallSpeed = 0f;
            if (jumped) Restart(Find(jump));
            return;
        }
        if (fallSpeed < -hardLandingSpeed && (state == MovementState.Standing || state == MovementState.Crouching))
            PlayTransition(HorizontalSpeed() > walkClipSpeed ? landMoving : land);
        fallSpeed = 0f;
    }

    private void Locomotion(bool crouched, float dt)
    {
        Vector3 local = transform.InverseTransformDirection(player.ActualVelocity);
        local.y = 0f;
        float speed = local.magnitude;
        bool backward = speed > .3f && local.z < -.3f * speed;
        if (speed > .3f)
        {
            // Legs face the direction of travel within limits, like a strafing person turning their hips.
            float heading = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            targetYaw = Mathf.Clamp(backward ? Mathf.DeltaAngle(180f, heading) : heading, -maxStrafeYaw, maxStrafeYaw);
        }
        if (crouched)
        {
            float moving = Mathf.Clamp01(speed / (crouchClipSpeed * .5f));
            Mix(crouchIdle, 1f - moving);
            Mix(backward ? crouchBack : crouchWalk, moving);
        }
        else
        {
            float toWalk = Mathf.Clamp01(speed / walkClipSpeed);
            float toRun = Mathf.Clamp01((speed - walkClipSpeed) / Mathf.Max(.01f, runClipSpeed - walkClipSpeed));
            float toSprint = Mathf.Clamp01((speed - runClipSpeed) / Mathf.Max(.01f, sprintClipSpeed - runClipSpeed));
            Mix(idle, 1f - toWalk);
            Mix(backward ? walkBack : walk, toWalk * (1f - toRun));
            Mix(backward ? runBack : run, toRun * (1f - toSprint));
            Mix(backward ? runBack : sprint, toSprint);
        }

        float weight = 0f, length = 0f, authored = 0f;
        foreach (var channel in channels)
        {
            if (!channel.Synced || channel.Target <= 0f) continue;
            weight += channel.Target;
            length += channel.Target * channel.Length;
            authored += channel.Target * channel.AuthoredSpeed;
        }
        if (weight <= 0f) return;
        float rate = Mathf.Clamp(speed / Mathf.Max(.1f, authored / weight), .6f, 1.6f);
        phase = Mathf.Repeat(phase + dt * rate / (length / weight), 1f);
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
    }

    private static void Restart(Channel channel)
    {
        if (channel == null || channel.Synced) return;
        channel.Playable.SetTime(0);
        channel.Playable.SetDone(false);
    }

    private float HorizontalSpeed() => new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;

    private void ApplyView()
    {
        var mode = thirdPerson ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        foreach (var part in headParts)
            if (part != null) part.shadowCastingMode = mode;
        if (!thirdPerson && view != null) view.localPosition = viewHome;
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
