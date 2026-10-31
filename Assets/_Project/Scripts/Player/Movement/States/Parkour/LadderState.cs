using UnityEngine;

/// <summary>
/// On a ladder, after Beatrun: W goes up it hand over hand a rung at a time, S slides down fast, Ctrl lets go. At the
/// top it climbs off through the Mantle state when there is somewhere to stand; at the bottom a slide steps off.
/// </summary>
public sealed class LadderState : MovementBaseState
{
    private const float GroundReach = .25f;
    private Vector3 from, to;
    private float progress, moveTime, nextStep;
    public LadderState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override void Enter()
    {
        var fixtures = ctx.Abilities.Fixtures;
        var settings = ctx.AbilitySettings;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Landing.SupportedByTraversal();
        from = ctx.transform.position;
        to = fixtures.LadderHold(from.y);
        progress = 0f;
        moveTime = fixtures.LadderCaught ? settings.ladderCatchTime : settings.ladderEnterTime;
        nextStep = fixtures.LadderCaught ? settings.ladderCatchDelay : settings.ladderEnterTime;
    }
    public override void Exit() => ctx.Abilities.Fixtures.LadderSliding = false;
    public override void UpdateLogic(float dt)
    {
        var fixtures = ctx.Abilities.Fixtures;
        var ladder = fixtures.Ladder;
        if (ladder == null || !ladder.isActiveAndEnabled || (ctx.InputAvailable && ctx.Input.CrouchPressed)) { LetGo(); return; }
        float climbed = ctx.transform.position.y - ladder.transform.position.y, input = ctx.CurrentInput.y;
        if (fixtures.LadderSliding)
        {
            if (input >= -.1f) StopSliding();
            return;
        }
        if (progress < 1f) return;
        if (climbed >= ladder.Height - ctx.AbilitySettings.ladderTopGap && fixtures.TryLeaveLadderTop()) return;
        if (ctx.CurrentStateDuration < nextStep) return;
        if (input > .1f && climbed < ladder.Height - ctx.AbilitySettings.ladderTopGap) StepUp();
        else if (input < -.1f && climbed > .05f) fixtures.LadderSliding = true;
    }
    public override void UpdatePhysics(float dt)
    {
        var fixtures = ctx.Abilities.Fixtures;
        if (fixtures.Ladder == null) return;
        var detector = ctx.Abilities.Traversal.Detector;
        Vector3 here = ctx.transform.position;
        if (fixtures.LadderSliding)
        {
            float bottom = fixtures.Ladder.transform.position.y;
            Vector3 down = here + Vector3.down * Mathf.Min(ctx.AbilitySettings.ladderSlideSpeed * dt, here.y - bottom);
            if (HasGroundBelow(down)) { fixtures.LockLadder(); ctx.Abilities.ReturnToLocomotion(); ctx.VerticalVelocity = -2f; return; }
            if (here.y - bottom <= .001f || !detector.ClearPath(here, down, ctx.StandingColliderHeight)) { StopSliding(); return; }
            ctx.Motor.RequestDisplacement(down - here);
            return;
        }
        if (progress >= 1f) return;
        progress = Mathf.Min(1f, progress + dt / Mathf.Max(.01f, moveTime));
        Vector3 next = Vector3.Lerp(from, to, progress);
        // Something overhead stops the climb; the hands stay where they are.
        if (!detector.ClearPath(here, next, ctx.StandingColliderHeight)) { from = to = here; progress = 1f; return; }
        ctx.Motor.RequestDisplacement(next - here);
    }
    private bool HasGroundBelow(Vector3 feet)
        => Physics.Raycast(feet + Vector3.up * .1f, Vector3.down, .1f + GroundReach, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    private void StepUp()
    {
        var settings = ctx.AbilitySettings;
        ctx.Abilities.Fixtures.LadderStepped();
        from = ctx.transform.position;
        to = from + Vector3.up * settings.ladderRungHeight;
        progress = 0f;
        moveTime = settings.ladderRungTime;
        nextStep = ctx.CurrentStateDuration + settings.ladderRungInterval;
    }
    private void StopSliding()
    {
        ctx.Abilities.Fixtures.LadderSliding = false;
        from = to = ctx.transform.position;
        ctx.RaiseLadderSlideStopped();
    }
    private void LetGo()
    {
        var fixtures = ctx.Abilities.Fixtures;
        Vector3 outward = fixtures.Ladder != null ? fixtures.Ladder.Outward : -ctx.transform.forward;
        fixtures.LockLadder();
        ctx.Abilities.ReturnToLocomotion();
        ctx.HorizontalVelocity = outward * ctx.AbilitySettings.ladderDropSpeed;
    }
}
