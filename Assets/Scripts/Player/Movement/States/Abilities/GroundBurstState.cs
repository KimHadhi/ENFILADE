using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>Finite ground movement shared by step dodge, landing roll and tackle.</summary>
public sealed class GroundBurstState : MovementBaseState
{
    private readonly State kind;
    private float initialSpeed;
    private float stepElapsed;
    private float stepExpectedAdvance;
    private Vector3 stepFrameStart;
    private bool interrupted;
    public GroundBurstState(PlayerMovement ctx, MovementBaseState parent, State kind) : base(ctx, parent) { this.kind = kind; }
    public override string Name => kind.ToString();
    public override MovementPosture Posture => kind == State.StepDodge ? ctx.Abilities.ActionPosture : MovementPosture.Crouching;
    private float Duration => kind == State.StepDodge ? ctx.AbilitySettings.stepDuration :
        kind == State.LandingRoll ? ctx.AbilitySettings.rollDuration : ctx.AbilitySettings.tackleDuration;
    public override void Enter() { interrupted = false; stepElapsed = stepExpectedAdvance = 0f; initialSpeed = ctx.HorizontalVelocity.magnitude; ctx.Actions.ClearJumpBuffer(); }
    internal void RestartStep()
    {
        // Restart only after a confirmed success; do not retain the burst speed
        // as normal locomotion momentum, or cancel the newly opened window.
        stepElapsed = stepExpectedAdvance = 0f;
        interrupted = false;
        ctx.StateMachine.Duration = 0f; ctx.Actions.ClearJumpBuffer();
    }
    public override void UpdateLogic(float dt)
    {
        if (kind == State.StepDodge && stepExpectedAdvance > 0f
            && (ctx.Motor.lastMoveFlags & CollisionFlags.Sides) != 0
            && Vector3.Dot(ctx.transform.position - stepFrameStart, ctx.Abilities.ActionDirection) < stepExpectedAdvance - 0.025f)
        {
            interrupted = true;
            ctx.Abilities.Dodge.CancelWindow(); ctx.HorizontalVelocity = Vector3.zero;
            ctx.Abilities.ReturnToLocomotion(); return;
        }
        bool expired = kind == State.StepDodge ? stepElapsed >= Mathf.Max(0.05f, Duration) - 0.00001f : ctx.CurrentStateDuration >= Duration;
        if (!ctx.IsGrounded || expired) ctx.Abilities.ReturnToLocomotion();
    }
    public override void UpdatePhysics(float dt)
    {
        float speed = kind == State.StepDodge ? ctx.AbilitySettings.stepDistance / Mathf.Max(0.05f, Duration) :
            kind == State.LandingRoll ? ctx.AbilitySettings.rollSpeed : ctx.AbilitySettings.tackleSpeed;
        ctx.HorizontalVelocity = ctx.Abilities.ActionDirection * speed;
        if (ctx.IsGrounded) ctx.VerticalVelocity = -2f;
        if (kind == State.StepDodge)
        {
            float duration = Mathf.Max(0.05f, Duration);
            float activeDt = Mathf.Min(Mathf.Max(0f, dt), duration - stepElapsed);
            stepExpectedAdvance = speed * activeDt; stepFrameStart = ctx.transform.position;
            stepElapsed = Mathf.Min(duration, stepElapsed + activeDt);
            // Integrate the final partial frame, preserving distance at any frame rate.
            ctx.Motor.RequestDisplacement(ctx.HorizontalVelocity * activeDt + Vector3.up * (ctx.VerticalVelocity * dt));
        }
        if (kind == State.Tackle) ctx.Abilities.Combat.HitForward(true);
    }
    public override void Exit()
    {
        ctx.HorizontalVelocity = Vector3.ClampMagnitude(ctx.HorizontalVelocity, Mathf.Max(initialSpeed, ctx.WalkSpeed));
        bool completedStep = kind == State.StepDodge && !interrupted && stepElapsed >= Mathf.Max(0.05f, Duration) - 0.00001f;
        bool sprintHeld = ctx.InputAvailable && ctx.Input.SprintButtonHeld;
        if (completedStep && ctx.IsGrounded && sprintHeld && ctx.CurrentInput.y > 0.1f
            && ctx.Abilities.ActionPosture == MovementPosture.Standing
            && !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            // Resume the held running direction, not the side/back direction of the dodge.
            float speed = Mathf.Lerp(ctx.WalkSpeed, ctx.SprintSpeed, ctx.SprintProgress);
            ctx.HorizontalVelocity = ctx.Abilities.InputDirection(ctx.transform.forward) * speed;
        }
        // Normal completion keeps the independently timed shared dodge window.
        // Wall interruption / loss of support still cancels it immediately.
        if (kind == State.StepDodge && stepElapsed < Mathf.Max(0.05f, Duration) - 0.00001f) ctx.Abilities.Dodge.CancelWindow();
    }
    public override void OnColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y >= Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) return;
        if (Vector3.Dot(ctx.Abilities.ActionDirection, -hit.normal) < 0.55f) return;
        if (kind == State.Tackle && hit.collider.GetComponentInParent<IBreakable>() != null) return;
        interrupted = true;
        if (kind == State.StepDodge) ctx.Abilities.Dodge.CancelWindow();
        ctx.HorizontalVelocity = Vector3.zero;
        ctx.Abilities.ReturnToLocomotion();
    }
}
