using UnityEngine;

public sealed class SupineDivingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Supine;

    public SupineDivingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override void UpdateLogic(float dt)
    {
        float horizontalSpeed = ctx.HorizontalVelocity.magnitude;
        if (horizontalSpeed <= ctx.SlideEndSpeed && Mathf.Abs(ctx.VerticalVelocity) < 0.5f)
        {
            ctx.ChangeState(PlayerMovement.MovementState.Supine);
        }
    }
}