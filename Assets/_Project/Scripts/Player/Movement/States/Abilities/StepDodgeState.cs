using UnityEngine;

/// <summary>
/// The step dodge (Alt) while it has the legs: the body goes the way it was pointed at its clip's own pace (DodgeMove,
/// ClipTravel) and the keys have it back once it is nearly up again (Ability.dodgeUntil), so the motion is seen. The next
/// dodge cuts in by the dodge's own rules (MovementAbilities), a jump once the slide is done. A wall stops the body and ends
/// the dodge's window, not the motion: it ended the dodge itself until 0.9.334, and the body snapped upright against the
/// wall. Nothing solid to it is an enemy (DodgeEnemyPassThrough); slid into one it is carried on out of them.
/// </summary>
public sealed class StepDodgeState : MovementBaseState
{
    // Moving less than it asked to by more than this (m) against something beside it, the body is up against a wall.
    private const float HeldBack = .025f;

    private float entrySpeed, pushedOut, expected;
    private Vector3 frameStart;
    private bool stopped;

    public StepDodgeState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }

    public override string Name => "StepDodge";
    public override MovementPosture Posture => ctx.Abilities.ActionPosture;
    public override bool AllowsBufferedJump => true;

    private DodgeMove Move => ctx.Abilities.DodgeMove;
    private bool Slid => stopped || Move.Shifted;

    public override void Enter()
    {
        entrySpeed = ctx.HorizontalVelocity.magnitude;
        Begin();
    }

    /// <summary>A dodge chained on this one begins: only after a confirmed success, so the window just opened stays.</summary>
    internal void Restart()
    {
        ctx.StateMachine.Duration = 0f;
        Begin();
    }

    private void Begin()
    {
        stopped = false;
        pushedOut = expected = 0f;
        ctx.Actions.ClearJumpBuffer();
    }

    public override void UpdateLogic(float dt)
    {
        if (expected > 0f && (ctx.Motor.lastMoveFlags & CollisionFlags.Sides) != 0
            && Vector3.Dot(ctx.transform.position - frameStart, ctx.Abilities.ActionDirection) < expected - HeldBack) Stop();
        if (!ctx.IsGrounded || (Move.Over && !PushesOut(out _))) ctx.Abilities.ReturnToLocomotion();
    }

    public override void UpdatePhysics(float dt)
    {
        if (ctx.IsGrounded) ctx.VerticalVelocity = -2f;
        float step = ctx.DodgePassThrough.MovementDelta(Mathf.Max(0f, dt));
        Vector3 way = ctx.Abilities.ActionDirection;
        float go = 0f;
        expected = 0f;
        if (Slid && PushesOut(out Vector3 exit))
        {
            // On through the body it slid into at full pace, in real time, walls and the floor's support still counting.
            float real = Time.timeScale > 0f ? Mathf.Min(.05f, dt / Time.timeScale) : 0f;
            pushedOut += real;
            Move.Pass(step);
            Vector3 leave = Vector3.ClampMagnitude(exit - ctx.transform.position, ctx.AbilitySettings.clipTravelMost * real);
            go = leave.magnitude;
            if (go > 1e-5f) way = leave / go;
        }
        else if (stopped) Move.Pass(step);
        else expected = go = Move.Go(step);
        frameStart = ctx.transform.position;
        // This frame's whole way, however long the frame: the clip's, not a speed's.
        ctx.HorizontalVelocity = dt > 0f ? way * (go / dt) : Vector3.zero;
    }

    // Its end comes inside an enemy's body: the way on out of it, for as long as the help lasts.
    private bool PushesOut(out Vector3 exit)
    {
        exit = ctx.transform.position;
        return ctx.IsGrounded && pushedOut < ctx.AbilitySettings.dodgeExitAssistTime && ctx.DodgePassThrough.HasEnemyOverlap
            && ctx.DodgePassThrough.TryGetForwardExit(out exit);
    }

    private void Stop()
    {
        stopped = true;
        expected = 0f;
        ctx.Abilities.Dodge.CancelWindow();
        ctx.HorizontalVelocity = Vector3.zero;
    }

    public override bool TryJump()
    {
        if (!Slid || ctx.HasCeilingAbove(ctx.StandingColliderHeight)) return false;
        ctx.Abilities.ReturnToLocomotion();
        if (ctx.CurrentState != PlayerMovement.MovementState.Standing) ctx.ChangeState(PlayerMovement.MovementState.Standing);
        ctx.Actions.Jump(ctx.jumpHeight);
        return true;
    }

    public override void Exit()
    {
        ctx.DodgePassThrough.OrNull()?.Finish();
        bool whole = Move.Over && !stopped;
        // Cut short (a wall, the ground gone, a jump), what is left of its window goes; played to here it ran out long ago.
        if (!whole) ctx.Abilities.Dodge.CancelWindow();
        ctx.HorizontalVelocity = Vector3.ClampMagnitude(ctx.HorizontalVelocity, Mathf.Max(entrySpeed, ctx.WalkSpeed));
        if (!whole || !Move.Runs || !ctx.IsGrounded || !ctx.InputAvailable || !ctx.Input.SprintButtonHeld
            || ctx.CurrentInput.sqrMagnitude < .01f || !ctx.SprintsToward(ctx.CurrentInput)
            || ctx.HasCeilingAbove(ctx.StandingColliderHeight)) return;
        // The sprint it shifted aside in goes on, the way the keys point now.
        float speed = Mathf.Max(ctx.RunSpeed, Mathf.Lerp(ctx.WalkSpeed, ctx.SprintSpeed, ctx.SprintProgress));
        ctx.HorizontalVelocity = ctx.Abilities.InputDirection(ctx.transform.forward) * speed;
    }

    public override void OnColliderHit(ControllerColliderHit hit)
    {
        if (ctx.DodgePassThrough.OrNull()?.IsResolving == true) return;
        if (hit.normal.y >= Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) return;
        if (Vector3.Dot(ctx.Abilities.ActionDirection, -hit.normal) < .55f) return;
        // A light physics prop is kicked away instead of stopping it.
        if (ctx.Motor.TryKickProp(hit.collider, hit.point, out Vector3 kept)) { ctx.HorizontalVelocity = kept; return; }
        // A step it goes up is no wall; one it can't is seen by how little it moved (UpdateLogic).
        if (!ctx.IsSlideStepContact(hit)) Stop();
    }
}
