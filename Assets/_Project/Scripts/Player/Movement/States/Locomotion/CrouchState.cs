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

        // 2. Lower the body before the entrance, only if the prone route is clear.
        bool moving = input.sqrMagnitude > 0.01f;
        if (ctx.IsGrounded && (moving || ctx.HasCeilingAbove(ctx.CrouchColliderHeight)))
        {
            Vector3 moveDir = moving
                ? (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized : ctx.transform.forward;
            if (ctx.HasVentCeilingAhead(moveDir, moving ? ctx.proneEntryLookAhead : 0f))
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
