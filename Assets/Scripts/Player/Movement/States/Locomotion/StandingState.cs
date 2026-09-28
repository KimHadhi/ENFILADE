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

        bool sprintHeld = ctx.InputAvailable && ctx.SprintAction != null && ctx.SprintAction.IsPressed();
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
                // 공중에 떠 있을 때: 즉시 에어 다이브 전환
                Vector3 diveDir = (input.sqrMagnitude > 0.01f)
                    ? (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized
                    : ctx.transform.forward;

                PlayerMovement.MovementState targetDiveState = (input.y < -0.2f)
                    ? PlayerMovement.MovementState.SupineDiving
                    : PlayerMovement.MovementState.Diving;

                ctx.DiveBodyForward = (targetDiveState == PlayerMovement.MovementState.SupineDiving) ? -diveDir : diveDir;

                // --- 점프 다이브 속도 계산 (8.5m/s 상한 캡 & 저점 보정) ---
                float currentSpeed = ctx.HorizontalVelocity.magnitude;
                float finalLaunchSpeed;

                if (currentSpeed < 5.5f)
                {
                    // [0 ~ 5.5m/s] 저속 구간: 자연스러운 점진 가속 (1.0 -> 8.5)
                    float t = currentSpeed / 5.5f;
                    finalLaunchSpeed = Mathf.Lerp(1.0f, 8.5f, t);
                }
                else if (currentSpeed <= 8.4f)
                {
                    // [5.5 ~ 8.4m/s] 일반 달리기 & 스프린트 구간:
                    // 의도적인 고점 인플레이션을 막고 8.5m/s로 깔끔하게 통일 (저점 보장)
                    finalLaunchSpeed = 8.5f;
                }
                else
                {
                    // [8.4m/s 초과] 내리막길/폭발/기믹 등 비정상적 고속 상태:
                    // 현재 속도 모멘텀을 깎지 않고 유지 (+0.1m/s 보너스)
                    finalLaunchSpeed = currentSpeed + 0.1f;
                }

                ctx.HorizontalVelocity = diveDir * finalLaunchSpeed;

                if (ctx.VerticalVelocity < 2.0f) ctx.VerticalVelocity = 2.0f; // 체공력 부여

                ctx.ChangeState(targetDiveState);
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