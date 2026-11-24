using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>Finite ground movement shared by the landing roll and the dash push. The step dodge has a state of its own (StepDodgeState).</summary>
public sealed class GroundBurstState : MovementBaseState
{
    // Moving less than this share of what it asked to against something beside it, a roll is up against a wall.
    private const float HeldBackShare = .3f;

    private readonly State kind;
    private float initialSpeed, expected;
    private Vector3 frameStart;
    // Backlog 155: a landing roll that meets a wall stops moving but plays to its end. Ending it at the wall stood or
    // crouched the player at once while the body still played the roll out, so the view and the body disagreed.
    private bool rollStopped;
    public GroundBurstState(PlayerMovement ctx, MovementBaseState parent, State kind) : base(ctx, parent) { this.kind = kind; }
    public override string Name => kind.ToString();
    // A dash push stays upright, so a run that carries on after it never dips the body.
    public override MovementPosture Posture => kind == State.DashPush ? MovementPosture.Standing : MovementPosture.Crouching;
    private bool Over => kind == State.LandingRoll ? ctx.Abilities.Landing.RollOver : ctx.CurrentStateDuration >= ctx.AbilitySettings.dashPushDuration;
    public override void Enter() { rollStopped = false; expected = 0f; initialSpeed = ctx.HorizontalVelocity.magnitude; ctx.Actions.ClearJumpBuffer(); }
    public override void UpdateLogic(float dt)
    {
        // A dash push stops dead on the first body it meets, before this frame's move.
        if (kind == State.DashPush && ctx.Abilities.Combat.DashInto(ctx.Abilities.Combat.DashSpeed * dt))
        {
            ctx.HorizontalVelocity = Vector3.zero;
            ctx.Abilities.ReturnToLocomotion(); return;
        }
        if (kind == State.LandingRoll && expected > .01f && (ctx.Motor.lastMoveFlags & CollisionFlags.Sides) != 0
            && Vector3.Dot(ctx.transform.position - frameStart, ctx.Abilities.ActionDirection) < expected * HeldBackShare) StopRoll();
        if (!ctx.IsGrounded || Over) ctx.Abilities.ReturnToLocomotion();
    }
    public override void UpdatePhysics(float dt)
    {
        // A roll goes on its speed contract (MovementLanding.RollSpeed), its clip's moment with it even when a wall has stopped it
        // or it lands on the spot.
        float speed = kind != State.LandingRoll ? ctx.Abilities.Combat.DashSpeed : ctx.Abilities.Landing.RollSpeed(dt);
        if (rollStopped) speed = 0f;
        ctx.HorizontalVelocity = ctx.Abilities.ActionDirection * speed;
        if (ctx.IsGrounded) ctx.VerticalVelocity = -2f;
        expected = speed * dt;
        frameStart = ctx.transform.position;
    }
    public override void Exit()
    {
        if (kind == State.DashPush) ctx.Abilities.Combat.EndDashPush();
        ctx.HorizontalVelocity = Vector3.ClampMagnitude(ctx.HorizontalVelocity, Mathf.Max(initialSpeed, ctx.WalkSpeed));
        // A roll that runs on hands its speed to the gait, which goes on from it to the keys' (MovementGait.Carry).
        if (kind == State.LandingRoll && ctx.Abilities.Landing.RunsOut) ctx.Actions.Gait.Carry(ctx.HorizontalVelocity.magnitude);
    }
    public override void OnColliderHit(ControllerColliderHit hit)
    {
        if (ctx.DodgePassThrough.OrNull()?.IsResolving == true) return;
        if (hit.normal.y >= Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) return;
        if (Vector3.Dot(ctx.Abilities.ActionDirection, -hit.normal) < 0.55f) return;
        if (kind == State.DashPush && hit.collider.GetComponentInParent<IBreakable>() != null) return;
        // A light physics prop is kicked away instead of ending the dash or the roll.
        if (ctx.Motor.TryKickProp(hit.collider, hit.point, out Vector3 kept)) { ctx.HorizontalVelocity = kept; return; }
        if (kind == State.LandingRoll)
        {
            // A step it goes up is no wall; one it can't is seen by how little it moved (UpdateLogic).
            if (!ctx.IsSlideStepContact(hit)) StopRoll();
            return;
        }
        ctx.HorizontalVelocity = Vector3.zero;
        ctx.Abilities.ReturnToLocomotion();
    }
    private void StopRoll()
    {
        rollStopped = true;
        expected = 0f;
        ctx.HorizontalVelocity = Vector3.zero;
    }
}
