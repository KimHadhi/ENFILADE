using UnityEngine;

public sealed class SupineSlidingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Supine;

    public SupineSlidingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float SlideFriction => ctx.SupineSlideFriction;

    // Getting up used to wait for the slide to stop, then for the body to settle on its back (Ability.proneMinHold), then
    // for the get-up off the back (Ability.supineRiseTime): far too slow (user, 2026-11-10). Shift with a move key (the
    // lying states' quick get-up) or Space now gets up straight out of the slide once it has lasted
    // slideCancelMinDuration and slowed to supineSlideCancelMaxSpeed, as the other slides' cancel does; into a crouch
    // with Ctrl held or under a low ceiling. Still asked for as the slide comes to rest, it gets up there too instead of
    // lying down first. W alone stays out of it, as before: it is too easily still held from the run.
    private bool SlowEnough => ctx.CurrentStateDuration >= ctx.SlideCancelMinDuration && ctx.CurrentSlideSpeed <= ctx.SupineSlideCancelMaxSpeed;
    private bool CanRise => ctx.IsGrounded && !ctx.HasCeilingAbove(ctx.CrouchColliderHeight);

    public override void ProcessInput(Vector2 input)
    {
        bool stopped = ctx.CurrentSlideSpeed <= ctx.SlideEndSpeed;
        bool wantsUp = ctx.InputAvailable && ctx.Input.SprintButtonHeld && input.sqrMagnitude > 0.01f;
        if ((stopped || SlowEnough) && wantsUp && CanRise)
        {
            Rise();
            return;
        }
        if (stopped) ctx.ChangeState(PlayerMovement.MovementState.Supine);
    }

    public override bool TryJump()
    {
        if (!SlowEnough || !CanRise) return false;
        Rise();
        return true;
    }

    private void Rise()
    {
        bool crouch = ctx.Input.CrouchHeld || ctx.HasCeilingAbove(ctx.StandingColliderHeight);
        // Up into a run, as the slide's cancel carries it.
        if (!crouch) ctx.SprintProgress = Mathf.Max(ctx.SprintProgress, 0.5f);
        ctx.ChangeState(crouch ? PlayerMovement.MovementState.Crouching : PlayerMovement.MovementState.Standing);
    }

    public override void HandleWallCollision(Vector3 wallNormal, float impactDot)
    {
        ctx.CurrentSlideSpeed = 0f;
        ctx.HorizontalVelocity = Vector3.zero;
        ctx.SprintProgress = 0f;
        ctx.ChangeState(PlayerMovement.MovementState.Supine);
    }
}
