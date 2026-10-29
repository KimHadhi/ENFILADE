using UnityEngine;
using UnityEngine.InputSystem;

public sealed class StandingState : MovementBaseState
{
    public StandingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.Actions.Momentum.SpeedFor(ctx.CurrentInput);

    public override void ProcessInput(Vector2 input)
    {
        bool crouchPressed = ctx.Input.CrouchHeld;
        bool crouchJustPressed = ctx.Input.CrouchPressed;
        bool diveJustPressed = ctx.InputAvailable && (
            (ctx.DiveAction != null && ctx.DiveAction.WasPressedThisFrame())
        );

        float measuredSpeed = new Vector2(ctx.ActualVelocity.x, ctx.ActualVelocity.z).magnitude;
        // Shift gets there in a moment; W alone once the run has built up enough speed.
        bool isActuallySprinting = input.y > 0.1f && measuredSpeed >= ctx.SlideMinStartSpeed;

        // 1. Z(다이브) 입력 처리 (지상 런칭 & 공중 점프 다이브 분기 통합)
        if (diveJustPressed)
        {
            if (ctx.IsGrounded)
            {
                // 지상일 때: 런칭 발사 vs 제자리 포복
                if (input.sqrMagnitude > 0.01f || isActuallySprinting)
                {
                    ctx.LaunchDive(input);
                }
                else
                {
                    ctx.ChangeState(PlayerMovement.MovementState.Proning);
                }
            }
            else
            {
                ctx.Actions.Dive.TryLaunch(input, false);
            }
            return;
        }

        // 2. 달리기 중 앉기 -> 슬라이딩
        if (!ctx.LandedThisFrame && crouchJustPressed && ctx.IsGrounded && isActuallySprinting && ctx.SlideCooldownTimer <= 0f)
        {
            ctx.StartSlide(measuredSpeed);
            return;
        }

        // 3. 일반 앉기
        if (crouchPressed && ctx.IsGrounded)
        {
            ctx.ChangeState(PlayerMovement.MovementState.Crouching);
        }
    }
}
