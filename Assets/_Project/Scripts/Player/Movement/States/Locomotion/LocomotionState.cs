using UnityEngine;

public sealed class LocomotionState : MovementBaseState
{
    public LocomotionState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override string Name => "Locomotion";

    // Below this (m/s) a velocity has no way to turn: it starts or stops as a blend.
    private const float KeepsPaceFrom = .5f;
    // The frame the stride was last eased: one missed, something else moved the body meanwhile.
    private int strideFrame = -10;

    public override bool TryJump()
    {
        if (ctx.HasCeilingAbove(ctx.StandingColliderHeight)) return false;
        if (ctx.CurrentState == PlayerMovement.MovementState.Crouching)
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
        ctx.Actions.Jump(ctx.jumpHeight);
        return true;
    }

    public override void OnLanding()
    {
        bool crouchHeld = ctx.Input.CrouchHeld;
        // Faster than a run lands in a slide (the run's own speed was the floor before 0.9.245 too).
        float requiredSpeed = Mathf.Max(ctx.landingSlideMinSpeed, ctx.WalkSpeed + 0.1f);
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

        // The way the keys point. Its speed is the gait's that way (MovementGait); the side's 1.15x "so a side step doesn't
        // look slow" went in 0.9.245, the side clips being measured now.
        Vector3 inputDirection = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;

        float baseSpeed = ctx.StateMachine.Current.LocomotionSpeed;
        // The class's pace: an Assault's adrenaline, a CHARGE (PlayerClass; this PC's own player only).
        baseSpeed *= PlayerClass.MoveFactor(ctx);
        // Getting over a hard landing, the legs give only part of the usual speed.
        if (ctx.IsGrounded) baseSpeed *= ctx.Abilities.Landing.RecoveryScale;
        // Out of a landing, a slide or a dodge on the run, going on ahead, its clip's last steps carry the body at their own pace,
        // and from there it goes on to the gait's speed (MovementGait.Carry).
        float runsOut = ctx.IsGrounded && input.y > .1f ? ctx.RunOutPace : 0f;
        if (runsOut > 0f)
        {
            baseSpeed = runsOut;
            ctx.Actions.Gait.Carry(ctx.HorizontalVelocity.magnitude);
        }
        else if (input.y > .1f && ctx.Actions.Gait.Carried > 0f) baseSpeed = ctx.Actions.Gait.Carried;
        // The hands decide what the legs may do: rooted after a parry, a slow walk with the guard up (PlayerMeleeCombat).
        if (ctx.MeleeCombat != null) baseSpeed = ctx.MeleeCombat.LimitMoveSpeed(baseSpeed);
        Vector3 targetVelocity = inputDirection * (baseSpeed * inputMagnitude);

        // A run's turn back has the run while it goes (RunTurn); not while a one-off's clip runs out.
        var turn = ctx.Actions.RunTurn;
        if (ctx.IsGrounded && runsOut <= 0f && (turn.Turning || turn.TryStart(input, inputDirection, ctx.BodyFacingYaw)))
        {
            if (turn.Go(out Vector3 turning))
            {
                ctx.HorizontalVelocity = turning;
                return;
            }
        }
        else turn.Stop();

        // A surge (GroundSurge: toward a vault until the jump goes, or a Détente's run to the friend it doses) carries the
        // run on at its own pace, whatever the keys say.
        Vector3 surge = ctx.Abilities.Surge.Velocity;
        if (ctx.IsGrounded && surge != Vector3.zero)
        {
            ctx.HorizontalVelocity = surge;
            return;
        }

        if (ctx.IsGrounded)
        {
            // The stride: what the legs are asked to carry. A change of way keeps the pace: it turns toward the keys at the
            // ground's ease while its speed eases to the new way's on its own, so a strafe off a run runs on at a run's pace.
            // Blending the two straight cut the corner: halfway from W to D the speed fell to 70% (a sprint's 6.0 m/s to 4.35,
            // under the sprint's own thresholds, and the legs dropped to the jog's clips), and the strafe felt like braking (the
            // user, 2026-11-21: "달리면서 스트레이핑이 안 되고 속도가 줄어드는 느낌"). Setting off, stopping and a turn back
            // further than Move.groundKeepPaceTurn still go through the middle: a body that reverses slows down to turn. Taken
            // up from the way the body goes wherever something else moved it a moment ago (a run's turn, a surge, a slide, the air).
            if (strideFrame != Time.frameCount - 1) ctx.Stride = ctx.HorizontalVelocity;
            strideFrame = Time.frameCount;
            float t = 1f - Mathf.Exp(-ctx.GroundSharpness * dt);
            Vector3 now = ctx.Stride, stride;
            float speedNow = now.magnitude, speedWanted = targetVelocity.magnitude;
            if (speedNow > KeepsPaceFrom && speedWanted > KeepsPaceFrom && Vector3.Angle(now, targetVelocity) <= ctx.GroundKeepPaceTurn)
                stride = Vector3.Slerp(now / speedNow, targetVelocity / speedWanted, t) * Mathf.Lerp(speedNow, speedWanted, t);
            else
                stride = Vector3.Lerp(now, targetVelocity, t);
            if (stride.sqrMagnitude < 0.0001f) stride = Vector3.zero;
            // Into a wall the legs go along it or stand at it, not on the spot (MovementMotor.AlongWalls).
            ctx.Stride = ctx.Motor.AlongWalls(stride, dt);
            // The body goes as its legs carry it: as far and the way the walk or run's own feet carry it at their own pace
            // (PlayerBody.LegsCarry), so a foot that is down stays where it came down with nothing to hold it there. Where no
            // walk or run has the legs (a clip that plays out, no body) it goes as asked. Until 0.9.415 the body went at the
            // stride, the legs followed it at a pace fitted to it, and where the two parted (a turn, a flick of the view, a
            // start) the feet were held, dragged and caught up foot by foot (the user, 2026-11-25: "아무리 해결해도 문제가
            // 발생하는 건 근본이 잘못돼서 그런 거 아니에요").
            ctx.HorizontalVelocity = ctx.LegsCarry(out Vector3 carried) ? carried : ctx.Stride;
        }
        else if (!ctx.Abilities.Landing.IsKnockedBack) // A blow's throw flies on untouched.
        {
            // In the air the keys turn the flight as they turn a run (the user, 2026-11-22: "점프를 자유롭게", 공중 방향 전환):
            // up to Move.groundKeepPaceTurn round it comes about at its own pace, further it goes through a stop. A flight
            // faster than the keys' own pace (a run-up, a leap) keeps its speed; with no keys it flies on.
            float t = 1f - Mathf.Exp(-ctx.AirControlSharpness * dt);
            Vector3 now = ctx.HorizontalVelocity;
            float speedNow = now.magnitude, speedWanted = targetVelocity.magnitude;
            bool keys = inputMagnitude > 0.01f;
            if (keys && speedNow > KeepsPaceFrom && Vector3.Angle(now, inputDirection) <= ctx.GroundKeepPaceTurn)
                ctx.HorizontalVelocity = Vector3.Slerp(now / speedNow, inputDirection, t)
                    * (speedNow > speedWanted ? speedNow : Mathf.Lerp(speedNow, speedWanted, t));
            else if (keys || speedNow <= baseSpeed)
                ctx.HorizontalVelocity = Vector3.Lerp(now, targetVelocity, t);
        }
    }
}
