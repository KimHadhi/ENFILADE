using UnityEngine;

public sealed class SlidingGroupState : MovementBaseState
{
    private const float HEAD_ON_COLLISION_DOT_THRESHOLD = 0.55f;

    public SlidingGroupState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Slide";
    public override MovementPosture Posture => MovementPosture.Prone;
    public override bool TryJump() => false;
    public override void OnLanding() { }
    public override void Enter() => ctx.Abilities.Combat.BeginSlideImpact();

    private bool IsBrakeInput(Vector2 input)
    {
        if (input.sqrMagnitude < 0.01f) return false;
        Vector3 inputWorldDir = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;
        return Vector3.Dot(inputWorldDir, ctx.SlideDirection) < -0.2f;
    }

    public override void UpdatePhysics(float dt)
    {
        Vector2 input = ctx.InputAvailable ? ctx.CurrentInput : Vector2.zero;
        Vector3 direction = Vector3.ProjectOnPlane(ctx.SlideDirection, Vector3.up).normalized;

        if (Mathf.Abs(input.x) > 0.1f)
        {
            Vector3 camRight = Vector3.ProjectOnPlane(ctx.transform.right, Vector3.up).normalized;
            Vector3 target = (direction + camRight * input.x * 0.5f).normalized;
            direction = Vector3.Slerp(direction, target, ctx.SlideSteerSharpness * dt).normalized;
        }
        ctx.SlideDirection = direction;

        // 계단 접촉 법선으로 수평 진행 방향을 꺾지 않습니다.
        float slopeDot = ctx.IsGrounded ? ctx.GetSlideSlopeDot(direction) : 0f;
        float activeFriction = ctx.IsGrounded ? ctx.StateMachine.Current.SlideFriction : ctx.StateMachine.Current.SlideFriction * ctx.AirSlideFrictionMultiplier;

        if (ctx.IsGrounded && slopeDot > 0.05f)
        {
            ctx.CurrentSlideSpeed += slopeDot * ctx.SlopeSlideGravity * dt;
        }
        else
        {
            float uphillResistance = ctx.IsGrounded && slopeDot < -0.05f ? Mathf.Abs(slopeDot) * 15f : 0f;
            ctx.CurrentSlideSpeed -= (activeFriction + uphillResistance) * dt;
        }

        bool crouchHeld = ctx.InputAvailable && ctx.CrouchAction != null && ctx.CrouchAction.IsPressed();
        if (!crouchHeld) ctx.SlideBrakeArmed = true;
        bool wantsBrake = ctx.InputAvailable && ((ctx.SlideBrakeArmed && crouchHeld) || IsBrakeInput(input));
        if (ctx.IsGrounded && wantsBrake)
        {
            float downhillAcceleration = Mathf.Max(0f, slopeDot) * ctx.SlopeSlideGravity;
            ctx.CurrentSlideSpeed -= (ctx.SlideBrakeDeceleration + downhillAcceleration) * dt;
        }

        ctx.CurrentSlideSpeed = Mathf.Max(ctx.CurrentSlideSpeed, 0f);
        ctx.HorizontalVelocity = direction * ctx.CurrentSlideSpeed;

        // 수평 이동과 하향 밀착을 PlayerMovement에서 분리합니다.
        if (ctx.IsGrounded) ctx.VerticalVelocity = -2f;
    }

    public override void OnColliderHit(ControllerColliderHit hit)
    {
        // 순수 하향 밀착 중의 계단 모서리 접촉은 정면 벽 충돌이 아닙니다.
        if (ctx.IsSlideGroundSnap) return;
        if (hit.normal.y >= Mathf.Cos(ctx.Motor.SupportAngle * Mathf.Deg2Rad)) return;

        Vector3 wallNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        Vector3 travel = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        if (wallNormal.sqrMagnitude < 0.0001f || travel.sqrMagnitude < 0.0001f) return;
        wallNormal.Normalize();
        travel.Normalize();
        float impactDot = Vector3.Dot(travel, -wallNormal);
        if (impactDot <= 0.001f) return;

        // All slide postures share one attack, including transitions after a bounce.
        if (ctx.Abilities.Combat.HitSlide(hit.collider, hit.point, hit.normal, travel)) return;

        IBreakable breakable = hit.gameObject.GetComponent<IBreakable>();
        if (breakable != null && ctx.CurrentSlideSpeed >= ctx.DoorBreakSpeedThreshold)
        {
            breakable.Break(hit.point, hit.normal, ctx.CurrentSlideSpeed);
            ctx.CurrentSlideSpeed *= ctx.DoorBreakSpeedRetention;
            ctx.HorizontalVelocity = travel * ctx.CurrentSlideSpeed;
            return;
        }

        // 높이만으로 모든 낮은 벽을 무시하지 않고, 실제 걸어 올라갈 면을 확인합니다.
        if (ctx.IsSlideStepContact(hit)) return;

        if (impactDot < HEAD_ON_COLLISION_DOT_THRESHOLD)
        {
            Vector3 velocityAlongWall = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, wallNormal);
            ctx.HorizontalVelocity = velocityAlongWall;
            ctx.CurrentSlideSpeed = velocityAlongWall.magnitude;
            if (ctx.CurrentSlideSpeed > 0.001f)
                ctx.SlideDirection = velocityAlongWall / ctx.CurrentSlideSpeed;
            return;
        }

        ctx.StateMachine.Current.HandleWallCollision(wallNormal, impactDot);
    }


}
