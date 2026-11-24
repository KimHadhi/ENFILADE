using UnityEngine;

public sealed class SlidingGroupState : MovementBaseState
{
    private const float HEAD_ON_COLLISION_DOT_THRESHOLD = 0.55f;
    // Up a slope the slide is held back this much per unit of the slope's rise along it (m/s²).
    private const float UphillPull = 15f;

    public SlidingGroupState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Slide";
    public override MovementPosture Posture => MovementPosture.Prone;
    public override bool TryJump() => false;
    public override void OnLanding() { }
    public override void Enter() => ctx.Abilities.Slide.Begin();
    public override void Exit() => ctx.Abilities.Slide.End();

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

        bool crouchHeld = ctx.Input.CrouchHeld;
        if (!crouchHeld) ctx.SlideBrakeArmed = true;
        bool wantsBrake = ctx.InputAvailable && ((ctx.SlideBrakeArmed && crouchHeld) || IsBrakeInput(input));

        // Down a stair's edge the contact's normal never turns the way along.
        float slopeDot = ctx.IsGrounded ? ctx.GetSlideSlopeDot(direction) : 0f;
        float brake = ctx.IsGrounded && wantsBrake ? ctx.SlideBrakeDeceleration + Mathf.Max(0f, slopeDot) * ctx.SlopeSlideGravity : 0f;
        if (ctx.CurrentState == PlayerMovement.MovementState.Sliding && ctx.Actions.SlideMove.Now != SlideMove.Phase.Over)
        {
            // The feet-first slide goes on its clip (SlideMove): the slope pulls it on down and holds it back up.
            float slope = slopeDot > .05f ? slopeDot * ctx.SlopeSlideGravity : slopeDot < -.05f ? slopeDot * UphillPull : 0f;
            bool keys = input.sqrMagnitude > .01f && !wantsBrake;
            ctx.CurrentSlideSpeed = ctx.Actions.SlideMove.Step(dt, ctx.CurrentSlideSpeed, keys, brake, slope,
                ctx.IsGrounded ? 1f : ctx.AirSlideFrictionMultiplier);
        }
        else
        {
            float activeFriction = ctx.IsGrounded ? ctx.StateMachine.Current.SlideFriction : ctx.StateMachine.Current.SlideFriction * ctx.AirSlideFrictionMultiplier;
            if (ctx.IsGrounded && slopeDot > 0.05f) ctx.CurrentSlideSpeed += slopeDot * ctx.SlopeSlideGravity * dt;
            else
            {
                float uphill = ctx.IsGrounded && slopeDot < -0.05f ? Mathf.Abs(slopeDot) * UphillPull : 0f;
                ctx.CurrentSlideSpeed = Mathf.Max(ctx.CurrentSlideSpeed - activeFriction * dt, 0f) - uphill * dt;
            }
            ctx.CurrentSlideSpeed -= brake * dt;
        }

        ctx.CurrentSlideSpeed = Mathf.Max(ctx.CurrentSlideSpeed, 0f);
        ctx.HorizontalVelocity = direction * ctx.CurrentSlideSpeed;

        // Held to the ground as it goes along it.
        if (ctx.IsGrounded) ctx.VerticalVelocity = -2f;
    }

    public override void OnColliderHit(ControllerColliderHit hit)
    {
        // A stair's edge touched while held down to the ground is no wall ahead.
        if (ctx.IsSlideGroundSnap || ctx.DodgePassThrough.IsResolving) return;
        if (hit.normal.y >= Mathf.Cos(ctx.Motor.SupportAngle * Mathf.Deg2Rad)) return;

        Vector3 wallNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        Vector3 travel = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        if (wallNormal.sqrMagnitude < 0.0001f || travel.sqrMagnitude < 0.0001f) return;
        wallNormal.Normalize();
        travel.Normalize();
        float impactDot = Vector3.Dot(travel, -wallNormal);
        if (impactDot <= 0.001f) return;

        // All slide postures share one attack, including transitions after a bounce.
        if (ctx.Abilities.Slide.Hit(hit.collider, hit.point, wallNormal, travel)) return;
        if (ctx.Motor.TryKickProp(hit.collider, hit.point, out Vector3 kept))
        {
            ctx.HorizontalVelocity = kept;
            ctx.CurrentSlideSpeed = kept.magnitude;
            return;
        }

        IBreakable breakable = hit.gameObject.GetComponent<IBreakable>();
        if (breakable != null && ctx.CurrentSlideSpeed >= ctx.DoorBreakSpeedThreshold)
        {
            breakable.Break(hit.point, hit.normal, ctx.CurrentSlideSpeed);
            ctx.CurrentSlideSpeed *= ctx.DoorBreakSpeedRetention;
            ctx.HorizontalVelocity = travel * ctx.CurrentSlideSpeed;
            return;
        }

        // A low wall is gone over only where there is a top to step up onto.
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
