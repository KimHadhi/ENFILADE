using UnityEngine;

public sealed class HeadFirstSlidingState : MovementBaseState
{
    public HeadFirstSlidingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float SlideFriction => ctx.DiveSlideFriction;

    public override void ProcessInput(Vector2 input)
    {
        bool sprintHeld = ctx.InputAvailable && ctx.Input.SprintButtonHeld;
        bool wantsSprintForward = sprintHeld && input.y > 0.1f;
        bool canCancelDiveSlide = ctx.CurrentStateDuration >= ctx.SlideCancelMinDuration && ctx.CurrentSlideSpeed <= ctx.DiveSlideCancelMaxSpeed;

        if ((canCancelDiveSlide || ctx.CurrentSlideSpeed <= ctx.SlideEndSpeed) && wantsSprintForward && !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
            return;
        }

        if (ctx.CurrentSlideSpeed <= ctx.SlideEndSpeed)
        {
            ctx.ChangeState(PlayerMovement.MovementState.Proning);
        }
    }

    public override void HandleWallCollision(Vector3 wallNormal, float impactDot)
    {
        ctx.CurrentSlideSpeed = 0f;
        ctx.HorizontalVelocity = Vector3.zero;
        ctx.SprintProgress = 0f;
        ctx.ChangeState(PlayerMovement.MovementState.Proning);
    }
}
