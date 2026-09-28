using UnityEngine;

public sealed class LocomotionState : MovementBaseState
{
    public LocomotionState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Locomotion";

    public override bool TryJump()
    {
        if (ctx.Abilities.Traversal.TryObstacle()) return true;
        if (ctx.HasCeilingAbove(ctx.StandingColliderHeight)) return false;
        if (ctx.CurrentState == PlayerMovement.MovementState.Crouching)
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
        ctx.Actions.Jump(ctx.jumpHeight);
        return true;
    }

    public override void OnLanding()
    {
        bool crouchHeld = ctx.InputAvailable && ctx.CrouchAction != null && ctx.CrouchAction.IsPressed();
        float requiredSpeed = Mathf.Max(ctx.landingSlideMinSpeed, ctx.WalkSpeed * ctx.StrafeCompensation + 0.1f);
        float landingSpeed = new Vector2(ctx.HorizontalVelocity.x, ctx.HorizontalVelocity.z).magnitude;
        float measuredSpeed = new Vector2(ctx.ActualVelocity.x, ctx.ActualVelocity.z).magnitude;
        float entrySpeed = Mathf.Min(landingSpeed, measuredSpeed);
        if (crouchHeld && entrySpeed >= requiredSpeed && ctx.SlideCooldownTimer <= 0f)
            ctx.StartSlide(entrySpeed, false);
    }

    public override void UpdatePhysics(float dt)
    {
        Vector2 input = ctx.CurrentInput;
        float inputMagnitude = Mathf.Clamp01(input.magnitude);

        // 입력 방향 및 Strafe 횡이동 보정
        Vector3 inputDirection = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;
        float lateralWeight = Mathf.Abs(input.x);
        float visualBoost = Mathf.Lerp(1f, ctx.StrafeCompensation, lateralWeight);

        float baseSpeed = ctx.StateMachine.Current.LocomotionSpeed * visualBoost;
        Vector3 targetVelocity = inputDirection * (baseSpeed * inputMagnitude);

        if (ctx.IsGrounded)
        {
            // 지상: 지면 마찰 및 목표 속도로 가속 보간
            float t = 1f - Mathf.Exp(-ctx.GroundSharpness * dt);
            ctx.HorizontalVelocity = Vector3.Lerp(ctx.HorizontalVelocity, targetVelocity, t);

            if (ctx.HorizontalVelocity.sqrMagnitude < 0.0001f)
            {
                ctx.HorizontalVelocity = Vector3.zero;
            }
        }
        else
        {
            // 공중: 점프로 얻은 고속 모멘텀 보존 및 공중 제어(Air Control)
            float currentAirSpeed = ctx.HorizontalVelocity.magnitude;

            if (currentAirSpeed > baseSpeed)
            {
                // 이미 달리기 등으로 가속된 상태라면 속도 크기를 줄이지 않고 입력 방향으로만 조향
                if (inputMagnitude > 0.01f)
                {
                    ctx.HorizontalVelocity = Vector3.MoveTowards(
                        ctx.HorizontalVelocity,
                        inputDirection * currentAirSpeed,
                        ctx.AirControlSharpness * currentAirSpeed * dt
                    );
                }
            }
            else
            {
                // 저속 체공 시 부드러운 공중 이동 보간
                float t = 1f - Mathf.Exp(-ctx.AirControlSharpness * dt);
                ctx.HorizontalVelocity = Vector3.Lerp(ctx.HorizontalVelocity, targetVelocity, t);
            }
        }
    }
}
