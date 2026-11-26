using UnityEngine;

/// <summary>The feet-first slide: its clip and its speed are SlideMove's, its steering, slope and hits SlidingGroupState's.</summary>
public sealed class SlidingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Sliding;

    public SlidingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float SlideFriction => ctx.SlideFriction;

    public override void ProcessInput(Vector2 input)
    {
        // Got up out of its clip, or slowed to a stop: it ends. Until 0.9.395 Shift with W stood a slide up at once into a sprint
        // once its body was down (Beatrun's slide cancel), no getting up shown (the user, 2026-11-24: "슬라이딩도 달리면 빠르게
        // 끊고 달리는 기능이 있던 거 같은데 … 옛날 코드들 쓸데없으면 다 리팩토링").
        // Not getting up (the crouch key held, or no room to stand), it ends where its clip's body begins to rise, and goes
        // into the crouch from the slide's own pose (PlayerBody: Body.slideCrouchTime). From 0.9.395 it got up and ran out in
        // its clip whatever the keys, and only then crouched: the eyes went up from 0.62 to 1.37 m and back down to 0.94 m, the
        // body from 4.3 to 2.5 m/s in a frame (the slide trace of 2026-11-26). 0.9.266 had done away with the same once, when the
        // slide's end was a clip of its own (Body.slideEnd).
        var slide = ctx.Actions.SlideMove;
        if (slide.Now == SlideMove.Phase.Over || ctx.CurrentSlideSpeed <= ctx.SlideEndSpeed || (slide.Now == SlideMove.Phase.Up && !StandsUp))
            StopSlide();
    }

    // Out of the slide the body stands: the crouch key let go and room over it to stand.
    private bool StandsUp => !ctx.Input.CrouchHeld && !ctx.HasCeilingAbove(ctx.StandingColliderHeight);

    public override bool TryJump()
    {
        // A dive roll ahead; the pack has none for a body sliding back first.
        if (ctx.CurrentStateDuration < ctx.slideJumpMinDuration || ctx.Actions.SlideFacesBack) return false;
        Vector3 direction = Vector3.ProjectOnPlane(ctx.SlideDirection, Vector3.up).normalized;
        return direction.sqrMagnitude > .01f && ctx.Abilities.Landing.TryDiveRoll(direction);
    }

    private void StopSlide()
    {
        if (ctx.HasCeilingAbove(ctx.CrouchColliderHeight)) ctx.ChangeState(PlayerMovement.MovementState.Proning);
        else if (StandsUp)
        {
            // Got up running in its clip: the walk or run the keys ask for takes the slide's speed on from the clip's last step
            // and comes to its own from there, the legs blended to that speed (MovementGait.Carry), as out of every one-off that
            // runs on. From 0.9.395, when the getting up went into the slide's own clip, nothing handed it on: the legs took
            // their gait in a frame, W alone dropping the body from 4.3 to 3 m/s and Shift dipping it to 3.5 and up to 6.5 in
            // 0.3 s, the clip's leaning run turned into an upright jog in a tenth of a second and the eyes rose 10 cm (the slide
            // trace and pictures of 2026-11-26; the user: "슬라이딩 끝날 때 모션이 갑자기 바뀌어서 불편").
            if (ctx.Actions.SlideMove.Now == SlideMove.Phase.Over) ctx.Actions.Gait.Carry(ctx.CurrentSlideSpeed);
            ctx.ChangeState(PlayerMovement.MovementState.Standing);
        }
        else ctx.ChangeState(PlayerMovement.MovementState.Crouching);
    }

    /// <summary>
    /// Head-on into a wall the legs take it, as in the first-person games that slide (Mirror's Edge, Titanfall 2, Apex; the experts'
    /// review, 2026-11-24): into it slower than Ability.slideImpact's first number the slide stops there and ends as any slide;
    /// faster, the body stumbles down into a crouch with a hard landing's slowdown after it; from its second, down onto hands and
    /// knees (HeavyLanding). Until 0.9.395 the slide bounced back off the wall at half its speed and lay on its back from 7 m/s
    /// (the user, 2026-11-24: "속도 빠를 때 튕겨져 나가는 것부터(뒤로 밀려날 땐 모션이 이상하고 …)").
    /// </summary>
    public override void HandleWallCollision(Vector3 wallNormal, float impactDot)
    {
        float into = ctx.CurrentSlideSpeed * impactDot;
        Vector2 impact = ctx.AbilitySettings.slideImpact;
        ctx.CurrentSlideSpeed = 0f;
        ctx.HorizontalVelocity = Vector3.zero;
        ctx.SprintProgress = 0f;
        if (into >= impact.y) { ctx.Abilities.Landing.Slam(); return; }
        if (into >= impact.x)
        {
            ctx.Abilities.Landing.Stumble();
            ctx.ChangeState(ctx.HasCeilingAbove(ctx.CrouchColliderHeight) ? PlayerMovement.MovementState.Proning : PlayerMovement.MovementState.Crouching);
            return;
        }
        StopSlide();
    }
}
