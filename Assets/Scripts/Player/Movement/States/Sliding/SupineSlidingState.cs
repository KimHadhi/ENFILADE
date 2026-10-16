using UnityEngine;

public sealed class SupineSlidingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Supine;

    public SupineSlidingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float SlideFriction => ctx.SupineSlideFriction;

    public override void ProcessInput(Vector2 input)
    {
        if (ctx.CurrentSlideSpeed <= ctx.SlideEndSpeed)
        {
            ctx.ChangeState(PlayerMovement.MovementState.Supine);
        }
    }

    public override void HandleWallCollision(Vector3 wallNormal, float impactDot)
    {
        ctx.CurrentSlideSpeed = 0f;
        ctx.HorizontalVelocity = Vector3.zero;
        ctx.SprintProgress = 0f;
        ctx.ChangeState(PlayerMovement.MovementState.Supine);
    }
}
