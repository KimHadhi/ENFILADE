using UnityEngine;

public sealed class StandingState : MovementBaseState
{
    public StandingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.Actions.Gait.SpeedFor(ctx.CurrentInput);

    public override void ProcessInput(Vector2 input)
    {
        bool crouches = ctx.Actions.CrouchKey.Crouches(ctx.Input.CrouchHeld, false);
        bool crouchJustPressed = ctx.Input.CrouchPressed;

        float measuredSpeed = new Vector2(ctx.ActualVelocity.x, ctx.ActualVelocity.z).magnitude;
        // Shift gets there in a moment; W alone once the run has built up enough speed.
        bool isActuallySprinting = input.y > 0.1f && measuredSpeed >= ctx.SlideMinStartSpeed;

        // No Z (Player/Dive) since 0.9.289: its dive, dive roll and lying down went (the user: "Only remove Z and dive").
        // 1. 달리기 중 앉기 -> 슬라이딩
        if (!ctx.LandedThisFrame && crouchJustPressed && ctx.IsGrounded && isActuallySprinting && ctx.SlideCooldownTimer <= 0f)
        {
            ctx.StartSlide(measuredSpeed);
            return;
        }

        // 2. 일반 앉기
        if (crouches && ctx.IsGrounded)
        {
            ctx.Actions.CrouchKey.Goes(true);
            ctx.ChangeState(PlayerMovement.MovementState.Crouching);
        }
    }
}
