using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// Shared action execution and timers. Eligibility is decided by the active state.
public sealed class MovementActions
{
    private readonly PlayerMovement ctx;
    private Vector3 slideDirection;
    /// <summary>
    /// The way a slide carries the player. Setting it keeps the sliding body's facing in the world: SlideFacesBack is
    /// worked out again from the way the body faced just before, so a steer, a glance off a wall or a bounce turns the
    /// body at most as far as the slide turned, and never round. A slide begins facing the way the player looks
    /// (StartSlide, StartLandingSlide).
    /// </summary>
    public Vector3 SlideDirection
    {
        get => slideDirection;
        set
        {
            float facing = SlideFacingYaw;
            slideDirection = value;
            FaceSlideFrom(facing);
        }
    }
    /// <summary>
    /// The sliding body faces back along SlideDirection rather than along it: a slide begun moving backwards, or bounced
    /// back off a wall it slid into. It holds for the whole slide. It used to be worked out every frame from where the
    /// player looked (more than 90° from the slide meant facing back), which a look round past 90° reached once the
    /// slide's view was widened to 120° (friend feedback C11): the legs and the body swung round 180° under the camera,
    /// and the view's arc went round with them (user, 2026-11-12: "슬라이딩하고 뒤 봤을 때 느낌 이상함").
    /// </summary>
    public bool SlideFacesBack;
    /// <summary>The way the sliding body faces (world yaw, degrees): along the slide, or back along it.</summary>
    public float SlideFacingYaw => Mathf.Atan2(slideDirection.x, slideDirection.z) * Mathf.Rad2Deg + (SlideFacesBack ? 180f : 0f);
    /// <summary>Faces the sliding body along the slide, or back along it when that is nearer the way given (world yaw, degrees).</summary>
    internal void FaceSlideFrom(float facingYaw)
        => SlideFacesBack = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(slideDirection.x, slideDirection.z) * Mathf.Rad2Deg, facingYaw)) > 90f;
    public float CurrentSlideSpeed;
    public bool SlideBrakeArmed;
    public Vector3 DiveBodyForward = Vector3.forward;
    public MovementDive Dive { get; }
    public MovementMomentum Momentum { get; }
    internal float sprintProgress, slideCooldownTimer, coyoteTimer, jumpBufferTimer, riseTimer;
    /// <summary>Getting up off the back: every key waits until the body is up (Ability.supineRiseTime).</summary>
    public bool IsRising => riseTimer > 0f;

    internal MovementActions(PlayerMovement context) { ctx = context; Dive = new MovementDive(ctx); Momentum = new MovementMomentum(ctx); }

    internal void TickTimers(float dt)
    {
        coyoteTimer -= dt;
        jumpBufferTimer -= dt;
        slideCooldownTimer -= dt;
        riseTimer -= dt;
        Dive.Tick(dt);
    }

    internal void Reset()
    {
        CurrentSlideSpeed = sprintProgress = 0f;
        coyoteTimer = jumpBufferTimer = slideCooldownTimer = riseTimer = 0f;
        SlideBrakeArmed = SlideFacesBack = false;
        Dive.Reset();
        Momentum.Reset();
    }

    public void StartSlide(float currentSpeed, bool applyBoost = true)
    {
        ctx.ChangeState(MovementState.Sliding);
        sprintProgress = 0f;
        Vector2 input = ctx.CurrentInput;
        Vector3 inputDir = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;
        Vector3 momentumDir = new Vector3(ctx.HorizontalVelocity.x, 0f, ctx.HorizontalVelocity.z).normalized;
        SlideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : (inputDir.sqrMagnitude > 0.01f ? inputDir : ctx.transform.forward);
        // The body drops into the slide facing the way the player looks: along the slide, or back along it when the run
        // carried them backwards. That holds for the slide; looking round in it doesn't turn it (SlideFacesBack).
        FaceSlideFrom(ctx.transform.eulerAngles.y);
        CurrentSlideSpeed = applyBoost ? Mathf.Max(currentSpeed, ctx.sprintSpeed) + ctx.slideKickBoost : currentSpeed;
        SlideBrakeArmed = !ctx.Input.CrouchHeld;
        slideCooldownTimer = ctx.slideCooldown;
    }

    public void StartLandingSlide(MovementState slideState, float currentSpeed, Vector3 fallbackDir, bool applyBoost)
    {
        ctx.ChangeState(slideState);
        sprintProgress = 0f;
        SlideBrakeArmed = !ctx.Input.CrouchHeld;
        Vector3 momentumDir = new Vector3(ctx.HorizontalVelocity.x, 0f, ctx.HorizontalVelocity.z).normalized;
        SlideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : fallbackDir;
        // As StartSlide. Down from a dive on the back the view is round toward the back (PlayerLook holds it there), so
        // the slide on the back faces back along the slide, feet trailing, as the dive did.
        FaceSlideFrom(ctx.transform.eulerAngles.y);
        float boost = applyBoost ? ctx.diveSlideKickBoost : 0f;
        float baseTargetSpeed = currentSpeed + boost;
        CurrentSlideSpeed = (currentSpeed > ctx.maxLandingSlideSpeed) ? currentSpeed : Mathf.Min(baseTargetSpeed, ctx.maxLandingSlideSpeed);
        slideCooldownTimer = ctx.slideCooldown;
    }

    /// <summary>When the player last got up off the ground (ProneState.Exit).</summary>
    internal float LeftProneAt = float.NegativeInfinity;
    /// <summary>Backlog 134: going down again right after getting up waits proneReentryDelay, so spamming can't flap the body.</summary>
    internal bool ProneReady => Time.time - LeftProneAt >= ctx.AbilitySettings.proneReentryDelay;

    public void LaunchDive(Vector2 input)
    {
        Dive.TryLaunch(input, true);
    }

    internal void ProcessSprintRampUp(float dt, Vector2 input)
    {
        bool sprintHeld = ctx.Input.SprintButtonHeld;
        // A short step interrupts movement, not the held sprint intent or its acceleration.
        bool canSprintState = ctx.CurrentState == MovementState.Standing ||
            (ctx.CurrentState == MovementState.StepDodge && ctx.Abilities.ActionPosture == MovementPosture.Standing);
        bool wantsToSprint = ctx.InputAvailable && ctx.Motor.isGrounded && sprintHeld && input.y > 0.1f && canSprintState
            && !ctx.Abilities.Landing.SprintBlocked;

        if (wantsToSprint) sprintProgress = Mathf.MoveTowards(sprintProgress, 1f, dt / Mathf.Max(0.01f, ctx.sprintRampUpDuration));
        else if (!ctx.IsSliding)
        {
            sprintProgress = Mathf.MoveTowards(sprintProgress, 0f, dt / (ctx.sprintRampUpDuration * 1.5f));
        }
    }

    internal void ProcessJump()
    {
        if (ctx.Abilities.InputConsumed || !ctx.StateMachine.Current.AllowsBufferedJump)
        {
            jumpBufferTimer = 0f;
            ctx.Abilities.Traversal.StopSurge();
            return;
        }
        if (ctx.InputAvailable && ctx.Input.JumpPressed) jumpBufferTimer = ctx.jumpBufferTime;
        // Running at a low obstacle, the press becomes the vault: begun at once in reach, or after the run surges there
        // (ParkourTraversal.SurgesToVault). A surge under way is looked after every frame until it ends, whatever is left
        // of the press's buffer: kept alive by two frames' time, one frame twice as long as the one before let it lapse,
        // and the surge ran on into the obstacle with nothing looking for its vault (2026-11-09).
        bool surging = ctx.Abilities.Traversal.SurgeVelocity != Vector3.zero;
        if ((jumpBufferTimer <= 0f && !surging) || coyoteTimer <= 0f) return;
        if (ctx.Abilities.Traversal.SurgesToVault(ctx.DeltaTime)) return;
        if (ctx.StateMachine.Current.TryJump()) jumpBufferTimer = 0f;
    }

    internal void ClearJumpBuffer() { jumpBufferTimer = 0f; }

    internal void LeaveGround()
    {
        ctx.Motor.jumpUngroundTimer = 0.15f;
        ctx.Motor.isGrounded = false;
        coyoteTimer = 0f;
    }

    internal void Jump(float height)
    {
        ctx.VerticalVelocity = Mathf.Sqrt(2f * height * Mathf.Abs(ctx.gravity));
        LeaveGround();
        ctx.RaiseJumped();
    }

    public float GetSpeedScaledPop(float speed) => Mathf.Min(Mathf.Max(0f, speed) * ctx.divePopPerSpeed, ctx.maxAdditionalDivePop);
}
