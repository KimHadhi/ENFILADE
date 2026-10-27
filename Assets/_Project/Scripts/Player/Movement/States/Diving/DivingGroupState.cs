using UnityEngine;

public sealed class DivingGroupState : MovementBaseState
{
    public DivingGroupState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Dive";
    public override MovementPosture Posture => MovementPosture.Prone;
    public override bool TryJump() => false;
    public override void Enter() => ctx.Actions.Dive.BeginFlight();

    public override void OnLanding()
    {
        ctx.Actions.Dive.Land();
        float speed = new Vector2(ctx.HorizontalVelocity.x, ctx.HorizontalVelocity.z).magnitude;
        bool supine = ctx.CurrentState == PlayerMovement.MovementState.SupineDiving;
        if (speed > ctx.SlideEndSpeed)
            ctx.StartLandingSlide(supine ? PlayerMovement.MovementState.SupineSliding : PlayerMovement.MovementState.HeadFirstSliding,
                speed, supine ? -ctx.DiveBodyForward : ctx.DiveBodyForward, false);
        else
            ctx.ChangeState(supine ? PlayerMovement.MovementState.Supine : PlayerMovement.MovementState.Proning);
    }

    public override void UpdatePhysics(float dt)
    {
        ctx.Actions.Dive.UpdateFlight(dt);
    }

    public override void UpdateLogic(float dt)
    {
        // The apex and a wall stop are still airborne. Only support ends a dive.
        if (ctx.IsGrounded) OnLanding();
    }

    public override void ProcessInput(Vector2 input)
    {
        Vector3 camLookDir = ctx.transform.forward;
        camLookDir.y = 0f;
        camLookDir.Normalize();

        Vector3 bodyForward = ctx.DiveBodyForward;
        bodyForward.y = 0f;
        bodyForward.Normalize();

        float bodyDot = Vector3.Dot(camLookDir, bodyForward);

        if (bodyDot < -0.15f)
        {
            if (ctx.CurrentState != PlayerMovement.MovementState.SupineDiving)
            {
                ctx.ChangeState(PlayerMovement.MovementState.SupineDiving);
            }
        }
        else if (bodyDot > 0.15f)
        {
            if (ctx.CurrentState != PlayerMovement.MovementState.Diving)
            {
                ctx.ChangeState(PlayerMovement.MovementState.Diving);
            }
        }
    }

    public override void OnColliderHit(ControllerColliderHit hit)
    {
        // 바닥 충돌은 제외 (경사각 기준)
        float walkableNormalY = Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad);
        if (hit.normal.y >= walkableNormalY) return;

        IBreakable breakable = hit.gameObject.GetComponent<IBreakable>();
        if (breakable != null)
        {
            float speed = ctx.HorizontalVelocity.magnitude;

            // 속도 임계값 체크
            if (speed >= ctx.DoorBreakSpeedThreshold)
            {
                breakable.Break(hit.point, hit.normal, speed);

                // 문을 부수고 통과하면서 수평 속도 감속 유지율 적용
                ctx.HorizontalVelocity *= ctx.DoorBreakSpeedRetention;
                return;
            }
        }
        if (ctx.Motor.TryKickProp(hit.collider, hit.point, out Vector3 kept))
        {
            ctx.HorizontalVelocity = kept;
            return;
        }
        Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
        if (Vector3.Dot(ctx.HorizontalVelocity, normal) < 0f)
            ctx.HorizontalVelocity = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, normal);
    }
}
