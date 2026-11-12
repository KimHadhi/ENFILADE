using UnityEngine;

/// <summary>
/// On a ladder, after Beatrun: W goes up it hand over hand a rung at a time, S slides down fast, Ctrl lets go. At the
/// top it climbs off through the Mantle state when there is somewhere to stand; at the bottom a slide steps off.
/// </summary>
public sealed class LadderState : MovementBaseState
{
    private const float GroundReach = .25f;
    private Vector3 from, via, to;
    private bool twoLegs, walkedOn;
    private float progress, moveTime, nextStep;
    // Getting on from the top: the share of the move spent going out over the edge before going down.
    private const float OverShare = .35f;
    public LadderState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override void Enter()
    {
        var fixtures = ctx.Abilities.Fixtures;
        var settings = ctx.AbilitySettings;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Landing.SupportedByTraversal();
        from = ctx.transform.position;
        progress = 0f;
        twoLegs = fixtures.LadderFromTop;
        if (twoLegs)
        {
            // From the top (backlog 141): out over the edge, then down onto the top rungs. Walked off onto, it takes
            // ladderTopWalkTime rather than ladderTopEnterTime (MovementFixtures.LadderTopEnterTime).
            via = fixtures.LadderTopOver;
            to = fixtures.LadderTopHold;
            walkedOn = fixtures.LadderTopWalked;
            moveTime = nextStep = fixtures.LadderTopEnterTime;
            return;
        }
        to = fixtures.LadderHold(from.y);
        moveTime = fixtures.LadderCaught ? settings.ladderCatchTime : settings.ladderEnterTime;
        nextStep = fixtures.LadderCaught ? settings.ladderCatchDelay : settings.ladderEnterTime;
    }
    private Vector3 Along(float share)
    {
        if (!twoLegs) return Vector3.Lerp(from, to, share);
        if (share >= OverShare) return Vector3.Lerp(via, to, Mathf.SmoothStep(0f, 1f, (share - OverShare) / (1f - OverShare)));
        float leg = share / OverShare;
        // Walked off onto it, the player is already moving out: the leg starts at speed and eases to a stop over the edge
        // (twice its length over its time: 0.6 m in 0.26 s is 4.7 m/s, about the walk's 4.9), where a stop to start from
        // would hitch. Interact starts from standing, so that leg eases in as well.
        return Vector3.Lerp(from, via, walkedOn ? 1f - (1f - leg) * (1f - leg) : Mathf.SmoothStep(0f, 1f, leg));
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
        Vector3 next = Along(progress);
        // Something overhead stops the climb; the hands stay where they are. A zombie clawing from below is no ceiling.
        if (!detector.ClearPathPastEnemies(here, next, ctx.StandingColliderHeight))
        {
            from = via = to = here;
            twoLegs = false;
            progress = 1f;
            fixtures.LadderStepBlocked = true;
            return;
        }
        fixtures.LadderStepProgress = progress;
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
        twoLegs = false;
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
