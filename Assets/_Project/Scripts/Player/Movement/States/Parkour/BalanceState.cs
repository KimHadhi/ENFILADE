using UnityEngine;

/// <summary>
/// Walking a balance beam, Beatrun's fixture played as a rhythm. Stepping on starts the walk at once and it goes on by
/// itself, with actual left/right foot contacts to hit on A and D; hits quicken the continuous animation and with it the
/// walk. Coming on at speed carries on a little as it brakes (Ability.balanceEntryBraking). S turns round and starts the
/// notes over, Ctrl steps off to the side of the tilt, and either end of the beam hands back to ordinary walking.
/// Running along a beam is BeamRun's, and crouching along one is an ordinary crouch held to Ability.balanceCrouchSpeed.
/// </summary>
public sealed class BalanceState : MovementBaseState
{
    private readonly BalanceRhythm rhythm;
    private float distance, carry;
    private bool backHeld;
    public BalanceState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) => rhythm = new BalanceRhythm(ctx);
    public override void Enter()
    {
        var fixtures = ctx.Abilities.Fixtures;
        var settings = ctx.AbilitySettings;
        carry = Vector3.ProjectOnPlane(ctx.ActualVelocity, Vector3.up).magnitude;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Landing.SupportedByTraversal();
        distance = fixtures.Beam.DistanceOf(ctx.transform.position);
        backHeld = ctx.CurrentInput.y < -.1f;
        rhythm.Begin(new BalanceDifficulty
        {
            Beat = settings.balanceBeatTime, QuickestBeat = settings.balanceQuickestBeat, LeadBeats = settings.balanceLeadBeats,
            PerfectWindow = settings.balancePerfectWindow, GoodWindow = settings.balanceGoodWindow, SlipLimit = settings.balanceSlipLimit
        });
    }
    public override void Exit()
    {
        rhythm.End();
        ctx.Abilities.Fixtures.EndBalanceTurn();
    }
    public override void ProcessInput(Vector2 input)
    {
        var fixtures = ctx.Abilities.Fixtures;
        bool back = input.y < -.1f;
        if (back && !backHeld && !fixtures.BalanceTurning)
        {
            fixtures.TurnAround();
            rhythm.Lead(180f / ctx.AbilitySettings.balanceTurnSpeed);
            carry = 0f;
        }
        backHeld = back;
        if (fixtures.BalanceTurning) return;
        if (ctx.Input.MoveLeftPressed && rhythm.Press(-1)) { Fall(); return; }
        if (ctx.Input.MoveRightPressed && rhythm.Press(1)) Fall();
    }
    public override void UpdateLogic(float dt)
    {
        var fixtures = ctx.Abilities.Fixtures;
        if (fixtures.Beam == null || !fixtures.Beam.isActiveAndEnabled) { StepOff(); return; }
        fixtures.TickBalanceTurn(dt);
        if (rhythm.Tick(dt) || (ctx.InputAvailable && ctx.Input.CrouchPressed)) Fall();
    }
    public override void UpdatePhysics(float dt)
    {
        var fixtures = ctx.Abilities.Fixtures;
        var settings = ctx.AbilitySettings;
        var beam = fixtures.Beam;
        if (beam == null) return;
        carry *= Mathf.Exp(-settings.balanceEntryBraking * dt);
        float speed = (fixtures.BalanceTurning ? 0f : fixtures.BalanceSpeed) + carry;
        float next = distance + Mathf.Sign(Vector3.Dot(fixtures.BalanceFacing, beam.Along)) * speed * dt;
        if (next < settings.balanceEndMargin || next > beam.Length - settings.balanceEndMargin) { StepOff(); return; }
        Vector3 here = ctx.transform.position;
        // Eases onto the middle of the beam from wherever the step landed.
        Vector3 target = beam.PointAt(next) + Vector3.up * ctx.Motor.controller.skinWidth;
        Vector3 step = Vector3.ClampMagnitude(target - here, (fixtures.BalanceSpeed + carry) * 2f * dt);
        if (!ctx.Abilities.Traversal.Detector.ClearPath(here, here + step, ctx.StandingColliderHeight)) return;
        distance = next;
        ctx.Motor.RequestDisplacement(step);
    }
    private void Fall() => ctx.ChangeState(PlayerMovement.MovementState.BalanceFall);
    private void StepOff()
    {
        var fixtures = ctx.Abilities.Fixtures;
        ctx.Abilities.ReturnToLocomotion();
        ctx.HorizontalVelocity = fixtures.BalanceFacing * (fixtures.BalanceSpeed + carry);
        ctx.VerticalVelocity = -2f;
    }
}

/// <summary>Off the side of a beam: thrown clear of it, keeping the speed along it, then an ordinary fall.</summary>
public sealed class BalanceFallState : MovementBaseState
{
    public BalanceFallState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override bool CanApplyGravity => true;
    public override void Enter()
    {
        var fixtures = ctx.Abilities.Fixtures;
        Vector3 right = Vector3.Cross(Vector3.up, fixtures.BalanceFacing);
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(ctx.ActualVelocity, Vector3.up)
            + right * (fixtures.BalanceFallSide * ctx.AbilitySettings.balanceFallSpeed);
        ctx.VerticalVelocity = 0f;
        fixtures.LockBalance();
    }
    public override void UpdateLogic(float dt)
    {
        if (ctx.CurrentStateDuration >= ctx.AbilitySettings.balanceFallTime) ctx.Abilities.ReturnToLocomotion();
    }
}
