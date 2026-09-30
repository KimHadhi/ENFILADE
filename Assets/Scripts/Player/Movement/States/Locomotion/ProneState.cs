using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ProneState : MovementBaseState
{
    public ProneState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Prone";
    public override MovementPosture Posture => MovementPosture.Prone;

    public override bool TryJump()
    {
        if (ctx.HasCeilingAbove(ctx.CrouchColliderHeight)) return false;
        ctx.ChangeState(ctx.HasCeilingAbove(ctx.StandingColliderHeight)
            ? PlayerMovement.MovementState.Crouching : PlayerMovement.MovementState.Standing);
        return true;
    }

    public override void ProcessInput(Vector2 input)
    {
        // 포복 중 실제로 떨어지기 시작하면 다이빙으로 복귀
        if (!ctx.IsGrounded && ctx.ActualVelocity.y < -0.15f)
        {
            ctx.ChangeState(
                ctx.CurrentState == PlayerMovement.MovementState.Supine
                    ? PlayerMovement.MovementState.SupineDiving
                    : PlayerMovement.MovementState.Diving
            );
            return;
        }

        bool crouchPressed = ctx.InputAvailable
            && ctx.CrouchAction != null
            && ctx.CrouchAction.IsPressed();

        bool crouchJustPressed = ctx.InputAvailable
            && ctx.CrouchAction != null
            && ctx.CrouchAction.WasPressedThisFrame();

        bool diveJustPressed = ctx.InputAvailable && (
            (ctx.DiveAction != null && ctx.DiveAction.WasPressedThisFrame())
        );

        bool sprintHeld = ctx.InputAvailable && ctx.Input.SprintButtonHeld;

        bool sprintJustPressed = ctx.InputAvailable
            && ctx.SprintAction != null
            && ctx.SprintAction.WasPressedThisFrame();

        bool hasMoveInput = input.sqrMagnitude > 0.01f;
        bool wantsFastGetUp =
            (sprintHeld && hasMoveInput) || (sprintJustPressed && hasMoveInput);

        // 1. 달리기 기상 캔슬
        if (wantsFastGetUp && !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            ctx.SprintProgress = Mathf.Max(ctx.SprintProgress, 0.6f);

            Vector3 moveDirection =
                (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;

            float getUpSpeed = Mathf.Max(ctx.HorizontalVelocity.magnitude, ctx.WalkSpeed);
            ctx.HorizontalVelocity = moveDirection * getUpSpeed;

            ctx.ChangeState(PlayerMovement.MovementState.Standing);
            return;
        }

        // 2. 앉기 또는 다이빙 키를 다시 눌러 일어서기
        if ((crouchJustPressed || diveJustPressed)
            && !ctx.HasCeilingAbove(ctx.CrouchColliderHeight))
        {
            ctx.ChangeState(
                crouchJustPressed || crouchPressed
                    || ctx.HasCeilingAbove(ctx.StandingColliderHeight)
                    ? PlayerMovement.MovementState.Crouching
                    : PlayerMovement.MovementState.Standing
            );
            return;
        }

        // 3. 앉기 키 유지 중 환풍구 탈출 시 앉기로 복귀
        if (crouchPressed && ctx.CurrentStateDuration > 0.15f)
        {
            Vector3 moveDir = hasMoveInput
                ? (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized
                : ctx.transform.forward;

            bool isStillHeadingIntoVent = hasMoveInput && ctx.HasVentCeilingAhead(moveDir, ctx.proneEntryLookAhead);

            // 현재 위치 + 0.35m 앞쪽 여유 공간까지 모두 확보되었을 때만 Crouching으로 전이
            bool canSafelyRise = !ctx.HasCeilingAbove(ctx.CrouchColliderHeight)
                              && (!hasMoveInput || !ctx.HasCeilingAhead(moveDir, 0.35f, ctx.CrouchColliderHeight));

            if (!isStillHeadingIntoVent && canSafelyRise)
            {
                ctx.ChangeState(PlayerMovement.MovementState.Crouching);
            }
        }
    }
}
