using UnityEngine;
using UnityEngine.InputSystem;

public sealed class StandingState : MovementBaseState
{
    public StandingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => Mathf.Lerp(ctx.WalkSpeed, ctx.SprintSpeed, ctx.SprintProgress);

    public override void ProcessInput(Vector2 input)
    {
        bool crouchPressed = ctx.InputAvailable && ctx.CrouchAction != null && ctx.CrouchAction.IsPressed();
        bool crouchJustPressed = ctx.InputAvailable && ctx.CrouchAction != null && ctx.CrouchAction.WasPressedThisFrame();
        bool diveJustPressed = ctx.InputAvailable && (
            (ctx.DiveAction != null && ctx.DiveAction.WasPressedThisFrame())
        );

        bool sprintHeld = ctx.InputAvailable && ctx.Input.SprintButtonHeld;
        float measuredSpeed = new Vector2(ctx.ActualVelocity.x, ctx.ActualVelocity.z).magnitude;
        bool isActuallySprinting = sprintHeld && input.y > 0.1f && (ctx.SprintProgress >= 0.7f) && (measuredSpeed >= ctx.SlideMinStartSpeed);

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
