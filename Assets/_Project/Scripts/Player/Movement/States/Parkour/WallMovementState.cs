using UnityEngine;

public sealed class WallMovementState : MovementBaseState
{
    private float contactGrace;
    public WallMovementState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override string Name => "WallClimb";
    public override void Enter()
    {
        ctx.VerticalVelocity = 0f;
        ctx.Abilities.Landing.SupportedByTraversal();
        ctx.Actions.ClearJumpBuffer();
        contactGrace = ctx.AbilitySettings.wallContactGrace;
    }
    public override void ProcessInput(Vector2 input)
    {
        if (!WallDetector.CanUseSurface(ctx.Abilities.Traversal.Wall.collider)) { Drop(); return; }
        // Entry input commits to this action. Only a deliberate wall kick changes it.
        if (ctx.InputAvailable && ctx.Input.JumpPressed)
        {
            ctx.ChangeState(PlayerMovement.MovementState.WallKick);
            ctx.Abilities.InputConsumed = true;
        }
    }
    public override void UpdateLogic(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        if (!WallDetector.CanUseSurface(traversal.Wall.collider)) { Drop(); return; }
        if (traversal.TryCatchLedge(-traversal.Wall.normal)) return;
        if (!traversal.SpendWallTime(dt)) { Drop(); return; }
        if (!traversal.Detector.TryWall(-traversal.Wall.normal, out RaycastHit contact)
            || Vector3.Dot(contact.normal, traversal.Wall.normal) < 0.7f)
        {
            contactGrace -= dt;
            if (contactGrace <= 0f) Drop();
            return;
        }
        contactGrace = ctx.AbilitySettings.wallContactGrace;
        traversal.Wall = contact;
    }
    public override void UpdatePhysics(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        Vector3 normal = traversal.Wall.normal;
        ctx.HorizontalVelocity = -normal * 0.5f;
        ctx.VerticalVelocity = ctx.AbilitySettings.wallClimbSpeed;
    }
    public override void Exit() => ctx.Abilities.Traversal.LockWall();
    private void Drop() { ctx.Abilities.ReturnToLocomotion(); ctx.VerticalVelocity = Mathf.Min(ctx.VerticalVelocity, 0f); }
}

public sealed class WallKickState : MovementBaseState
{
    public WallKickState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override bool CanApplyGravity => true;
    public override void Enter()
    {
        Vector3 normal = ctx.Abilities.Traversal.Wall.normal;
        Vector3 tangent = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, normal) * 0.5f;
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(normal * ctx.AbilitySettings.wallKickOutSpeed + tangent, Vector3.up);
        ctx.VerticalVelocity = ctx.AbilitySettings.wallKickUpSpeed;
        ctx.Actions.LeaveGround(); ctx.Actions.ClearJumpBuffer();
    }
    public override void UpdateLogic(float dt) { if (ctx.CurrentStateDuration > 0f) ctx.Abilities.ReturnToLocomotion(); }
}
