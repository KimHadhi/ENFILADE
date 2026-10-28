using UnityEngine;

public sealed class SlidingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Sliding;

    public SlidingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float SlideFriction => ctx.SlideFriction;

    public override void ProcessInput(Vector2 input)
    {
        bool sprintHeld = ctx.InputAvailable && ctx.SprintAction != null && ctx.SprintAction.IsPressed();
        bool wantsSprintForward = sprintHeld && input.y > 0.1f;

        // 1. 달리기 캔슬: 최소 유지 시간 충족 + 캔슬 허용 속도(5m/s) 이하일 때 즉시 기상
        if (ctx.CurrentStateDuration >= ctx.SlideCancelMinDuration &&
            ctx.CurrentSlideSpeed <= ctx.SlideCancelMaxSpeed &&
            wantsSprintForward &&
            !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            // 기상 시 부드럽게 가속이 이어지도록 모멘텀 주입
            ctx.SprintProgress = Mathf.Max(ctx.SprintProgress, 0.5f);
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
            return;
        }

        // 2. 속도가 다 빠지면 정지
        if (ctx.CurrentSlideSpeed <= ctx.SlideEndSpeed)
        {
            StopSlide();
        }
    }

    public override bool TryJump()
    {
        if (ctx.CurrentStateDuration < ctx.slideJumpMinDuration || ctx.HasCeilingAbove(ctx.StandingColliderHeight)) return false;
        Vector3 direction = new Vector3(ctx.SlideDirection.x, 0f, ctx.SlideDirection.z).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = ctx.transform.forward;
        float speed = Mathf.Max(ctx.slideJumpMinSpeed, ctx.CurrentSlideSpeed + ctx.slideJumpSpeedBonus);
        ctx.HorizontalVelocity = direction * speed;
        ctx.VerticalVelocity = Mathf.Sqrt(2f * ctx.slideKickJumpHeight * Mathf.Abs(ctx.gravity)) + ctx.GetSpeedScaledPop(speed);
        ctx.DiveBodyForward = direction;
        ctx.ChangeState(PlayerMovement.MovementState.Diving);
        ctx.Actions.LeaveGround();
        return true;
    }

    private void StopSlide()
    {
        bool crouchPressed = ctx.Input.CrouchHeld;
        bool sprintHeld = ctx.InputAvailable && ctx.SprintAction != null && ctx.SprintAction.IsPressed();
        bool wantsSprintForward = sprintHeld && ctx.CurrentInput.y > 0.1f;

        if (ctx.HasCeilingAbove(ctx.CrouchColliderHeight))
        {
            ctx.ChangeState(PlayerMovement.MovementState.Proning);
        }
        else if ((wantsSprintForward || !crouchPressed) && !ctx.HasCeilingAbove(ctx.StandingColliderHeight))
        {
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
        }
        else
        {
            ctx.ChangeState(PlayerMovement.MovementState.Crouching);
        }
    }

    public override void HandleWallCollision(Vector3 wallNormal, float impactDot)
    {
        float impactSpeed = ctx.CurrentSlideSpeed * impactDot;
        float bounceSpeed = impactSpeed * ctx.WallBounceBounciness;

        if (bounceSpeed <= ctx.SlideEndSpeed + 0.5f)
        {
            ctx.CurrentSlideSpeed = 0f;
            ctx.HorizontalVelocity = Vector3.zero;
            ctx.SprintProgress = 0f;
            StopSlide();
            return;
        }

        Vector3 reflectDir = Vector3.Reflect(ctx.SlideDirection, wallNormal).normalized;
        ctx.SlideDirection = Vector3.Lerp(reflectDir, wallNormal, 0.6f).normalized;

        ctx.CurrentSlideSpeed = bounceSpeed;
        ctx.HorizontalVelocity = ctx.SlideDirection * ctx.CurrentSlideSpeed;

        ctx.SlideBrakeArmed = true;

        // 7.0f 이상 속도로 튕겨져 나올 때만 SupineSliding으로 전환
        if (bounceSpeed >= ctx.SupineBounceThreshold)
        {
            ctx.ChangeState(PlayerMovement.MovementState.SupineSliding);
        }
        else
        {
            ctx.ChangeState(PlayerMovement.MovementState.Sliding);
        }

        ctx.VerticalVelocity = -2f;
        ctx.SprintProgress = 0f;

        ctx.SlideCooldownTimer = ctx.SlideCooldown * 1.5f;

        // 💡 [추가] 벽에 박고 튕겨져 나왔으니, 슬라이딩 지속 시간을 0초로 리셋!
        // 이렇게 하면 ProcessInput의 SlideCancelMinDuration(0.5초)에 걸려서 0.5초 동안 일어설 수 없습니다.
        ctx.CurrentStateDuration = 0f;
    }
}
