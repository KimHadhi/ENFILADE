using UnityEngine;

public sealed class DivingState : MovementBaseState
{
    private float groundedStuckTimer;

    public DivingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override void Enter()
    {
        base.Enter();
        groundedStuckTimer = 0f;
    }

    public override void UpdateLogic(float dt)
    {
        bool physicallyStopped =
            ctx.IsGrounded || Mathf.Abs(ctx.ActualVelocity.y) < 0.15f;

        groundedStuckTimer = physicallyStopped
            ? groundedStuckTimer + dt
            : 0f;

        if (groundedStuckTimer >= 0.1f)
        {
            ctx.ChangeState(PlayerMovement.MovementState.Proning);
        }
    }
}