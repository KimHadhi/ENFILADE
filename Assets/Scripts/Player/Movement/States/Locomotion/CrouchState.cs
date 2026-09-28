using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CrouchingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Crouching;

    public CrouchingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.CrouchSpeed;

    public override void ProcessInput(Vector2 input)
    {
        bool crouchPressed = ctx.InputAvailable && ctx.CrouchAction != null && ctx.CrouchAction.IsPressed();
        bool diveJustPressed = ctx.InputAvailable && (
            (ctx.DiveAction != null && ctx.DiveAction.WasPressedThisFrame())
        );

        // 1. 앉은 상태에서 Z를 누르면 즉시 포복 자세로 진입
        if (diveJustPressed && ctx.IsGrounded)
        {
            ctx.ChangeState(PlayerMovement.MovementState.Proning);
            return;
        }

        // 2. [환풍구 자동 포복] 완벽한 캡슐 볼륨 검사를 통해 환풍구 턱을 통과함
        if (input.sqrMagnitude > 0.01f)
        {
            Vector3 moveDir = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;
            if (ctx.HasVentCeilingAhead(moveDir, 0.45f)) // 조금 더 좁은 간격에서 정밀 스캔
            {
                ctx.ChangeState(PlayerMovement.MovementState.Proning);
                return;
            }
        }

        // 3. 앉기 키를 놓았을 때만 천장 확인 후 기립
        if (!crouchPressed && !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
        }
    }
}