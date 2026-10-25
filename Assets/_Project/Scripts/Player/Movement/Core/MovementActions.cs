using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// Shared action execution and timers. Eligibility is decided by the active state.
public sealed class MovementActions
{
    private readonly PlayerMovement ctx;
    public Vector3 SlideDirection;
    public float CurrentSlideSpeed;
    public bool SlideBrakeArmed;
    public Vector3 DiveBodyForward = Vector3.forward;
    public MovementDive Dive { get; }
    internal float sprintProgress, slideCooldownTimer, coyoteTimer, jumpBufferTimer;

    internal MovementActions(PlayerMovement context) { ctx = context; Dive = new MovementDive(ctx); }

    internal void TickTimers(float dt)
    {
        coyoteTimer -= dt;
        jumpBufferTimer -= dt;
        slideCooldownTimer -= dt;
        Dive.Tick(dt);
    }

    internal void Reset()
    {
        CurrentSlideSpeed = sprintProgress = 0f;
        coyoteTimer = jumpBufferTimer = slideCooldownTimer = 0f;
        SlideBrakeArmed = false;
        Dive.Reset();
    }

    public void StartSlide(float currentSpeed, bool applyBoost = true)
    {
        ctx.ChangeState(MovementState.Sliding);
        sprintProgress = 0f;
        Vector2 input = ctx.CurrentInput;
        Vector3 inputDir = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;
        Vector3 momentumDir = new Vector3(ctx.HorizontalVelocity.x, 0f, ctx.HorizontalVelocity.z).normalized;
        SlideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : (inputDir.sqrMagnitude > 0.01f ? inputDir : ctx.transform.forward);
        CurrentSlideSpeed = applyBoost ? Mathf.Max(currentSpeed, ctx.sprintSpeed) + ctx.slideKickBoost : currentSpeed;
        SlideBrakeArmed = !(ctx.InputAvailable && ctx.Input.crouchAction != null && ctx.Input.crouchAction.IsPressed());
        slideCooldownTimer = ctx.slideCooldown;
    }

    public void StartLandingSlide(MovementState slideState, float currentSpeed, Vector3 fallbackDir, bool applyBoost)
    {
        ctx.ChangeState(slideState);
        sprintProgress = 0f;
        SlideBrakeArmed = !(ctx.InputAvailable && ctx.Input.crouchAction != null && ctx.Input.crouchAction.IsPressed());
        Vector3 momentumDir = new Vector3(ctx.HorizontalVelocity.x, 0f, ctx.HorizontalVelocity.z).normalized;
        SlideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : fallbackDir;
        float boost = applyBoost ? ctx.diveSlideKickBoost : 0f;
        float baseTargetSpeed = currentSpeed + boost;
        CurrentSlideSpeed = (currentSpeed > ctx.maxLandingSlideSpeed) ? currentSpeed : Mathf.Min(baseTargetSpeed, ctx.maxLandingSlideSpeed);
        slideCooldownTimer = ctx.slideCooldown;
    }

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
        bool wantsToSprint = ctx.InputAvailable && ctx.Motor.isGrounded && sprintHeld && input.y > 0.1f && canSprintState;

        if (wantsToSprint) sprintProgress = Mathf.MoveTowards(sprintProgress, 1f, dt / Mathf.Max(0.01f, ctx.sprintRampUpDuration));
        else if (!ctx.IsSliding)
        {
            sprintProgress = Mathf.MoveTowards(sprintProgress, 0f, dt / (ctx.sprintRampUpDuration * 1.5f));
        }
    }

    internal void ProcessJump()
    {
        if (ctx.Abilities.InputConsumed || !ctx.StateMachine.Current.AllowsBufferedJump) { jumpBufferTimer = 0f; return; }
        if (ctx.InputAvailable && ctx.Input.JumpPressed) jumpBufferTimer = ctx.jumpBufferTime;
        if (jumpBufferTimer <= 0f || coyoteTimer <= 0f) return;
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
    }

    public float GetSpeedScaledPop(float speed) => Mathf.Min(Mathf.Max(0f, speed) * ctx.divePopPerSpeed, ctx.maxAdditionalDivePop);
}
