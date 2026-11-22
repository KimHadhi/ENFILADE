using UnityEngine;

public sealed class CrouchingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Crouching;

    public CrouchingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.Actions.Gait.CrouchSpeedFor(ctx.CurrentInput);

    public override void ProcessInput(Vector2 input)
    {
        bool crouchPressed = ctx.Input.CrouchHeld;

        // 1. Z lay the body down from a crouch until 0.9.289; low ceilings still do (below).
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
