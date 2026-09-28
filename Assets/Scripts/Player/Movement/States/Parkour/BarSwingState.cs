using UnityEngine;

public sealed class BarGrabState : MovementBaseState
{
    private Vector3 start;
    public BarGrabState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override MovementPosture Posture => MovementPosture.Crouching;
    public override void Enter()
    { start = ctx.transform.position; ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f; }
    public override void ProcessInput(Vector2 input) => ctx.Abilities.Traversal.ProcessBarInput(input);
    public override void UpdateLogic(float dt)
    {
        if (ctx.Abilities.Traversal.Bar == null) { ctx.Abilities.ReturnToLocomotion(); return; }
        if (ctx.CurrentStateDuration >= ctx.AbilitySettings.barCatchDuration)
        {
            var traversal = ctx.Abilities.Traversal;
            Vector3 expected = traversal.Bar.transform.TransformPoint(traversal.LocalGrip) - Vector3.up * traversal.Bar.HangLength;
            if (Vector3.Distance(ctx.transform.position, expected) > 0.2f) { traversal.ReleaseBar(false); return; }
            ctx.Abilities.Landing.SupportedByTraversal();
            ctx.ChangeState(PlayerMovement.MovementState.BarSwing);
        }
    }
    public override void UpdatePhysics(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        if (traversal.Bar == null) return;
        Vector3 target = traversal.Bar.transform.TransformPoint(traversal.LocalGrip) - Vector3.up * traversal.Bar.HangLength;
        Vector3 point = Vector3.Lerp(start, target, Mathf.Clamp01((ctx.CurrentStateDuration + dt) / Mathf.Max(0.05f, ctx.AbilitySettings.barCatchDuration)));
        if (!traversal.Detector.ClearPath(ctx.transform.position, point, ctx.CrouchColliderHeight)) { traversal.ReleaseBar(false); return; }
        ctx.Motor.RequestDisplacement(point - ctx.transform.position);
    }
}

public sealed class BarSwingState : MovementBaseState
{
    public BarSwingState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override MovementPosture Posture => MovementPosture.Crouching;
    public override void ProcessInput(Vector2 input) => ctx.Abilities.Traversal.ProcessBarInput(input);
    public override void UpdateLogic(float dt)
    { if (ctx.Abilities.Traversal.Bar == null || !ctx.Abilities.Traversal.Bar.isActiveAndEnabled) ctx.Abilities.Traversal.ReleaseBar(false); }
    public override void UpdatePhysics(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        var bar = traversal.Bar;
        if (bar == null) return;
        // Integrate a controlled pendulum in small steps, independent of render frame rate.
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.016f));
        float slice = dt / steps;
        float pump = Vector3.Dot(ctx.Abilities.InputDirection(Vector3.zero), bar.SwingDirection) * ctx.CurrentInput.magnitude;
        for (int i = 0; i < steps; i++)
        {
            traversal.AngularSpeed += (-Mathf.Abs(ctx.gravity) / bar.HangLength * Mathf.Sin(traversal.Angle)
                + pump * ctx.AbilitySettings.swingPumpAcceleration - traversal.AngularSpeed * 0.15f) * slice;
            traversal.AngularSpeed = Mathf.Clamp(traversal.AngularSpeed, -ctx.AbilitySettings.swingMaxAngularSpeed, ctx.AbilitySettings.swingMaxAngularSpeed);
            traversal.Angle += traversal.AngularSpeed * slice;
            if (Mathf.Abs(traversal.Angle) > bar.MaxAngle)
            { traversal.Angle = Mathf.Clamp(traversal.Angle, -bar.MaxAngle, bar.MaxAngle); traversal.AngularSpeed *= -0.2f; }
        }
        Vector3 anchor = bar.transform.TransformPoint(traversal.LocalGrip);
        Vector3 destination = anchor + bar.SwingDirection * (Mathf.Sin(traversal.Angle) * bar.HangLength)
            - Vector3.up * (Mathf.Cos(traversal.Angle) * bar.HangLength);
        if (!traversal.Detector.ClearPath(ctx.transform.position, destination, ctx.CrouchColliderHeight))
        { traversal.ReleaseBar(false); return; }
        ctx.Motor.RequestDisplacement(destination - ctx.transform.position);
    }
}

public sealed class LacheReleaseState : MovementBaseState
{
    public LacheReleaseState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override bool CanApplyGravity => true;
    public override MovementPosture Posture => MovementPosture.Crouching;
    public override void Enter()
    {
        Vector3 velocity = ctx.Abilities.Traversal.ReleaseVelocity;
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
        ctx.Actions.LeaveGround(); ctx.Actions.ClearJumpBuffer();
    }
    public override void UpdateLogic(float dt) { if (ctx.CurrentStateDuration > 0f) ctx.Abilities.ReturnToLocomotion(); }
}
