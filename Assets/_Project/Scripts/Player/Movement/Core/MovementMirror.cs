using UnityEngine;
using Marks = PlayerPose.Marks;
using State = PlayerMovement.MovementState;

/// <summary>
/// Turns a character's movement into a PlayerPose on the PC that plays it, and back again on every other PC, so the
/// copy's body, hands and sounds read what they always read. The copy's states never run and nothing here moves it.
/// A pose's Detail and Extent mean: the way the body faces, down after a heavy landing; a vault or mantle's progress and
/// height; a bar's angle and swing speed; the rung's progress on a ladder; the ride's speed on a cable.
/// </summary>
public sealed class MovementMirror
{
    // LookRate only smooths the steps between two poses: high enough that it adds no lag of its own, and it never overshoots.
    private const float FollowRate = 20f, LookRate = 45f, LedgeProbe = .1f, LadderMatch = .01f;

    private readonly PlayerMovement ctx;
    private readonly Transform lookPivot;
    private readonly Collider[] nearby = new Collider[8];
    private Vector3 ledgeAnchor;
    private float pitch, landImpact;
    private byte jumps, landings, dashHits;
    private bool applied;
    private PlayerBody body;

    // Asked for late: the body is added after this.
    private PlayerBody Body => body != null ? body : body = ctx.GetComponent<PlayerBody>();

    internal MovementMirror(PlayerMovement context)
    {
        ctx = context;
        lookPivot = ctx.transform.Find("CrouchPivot/BobPivot/LookPivot");
        if (lookPivot == null) lookPivot = ctx.transform.Find("CrouchPivot/LookPivot");
        ctx.Jumped += () => jumps++;
        ctx.DashPushHit += _ => dashHits++;
        ctx.Landed += impact => { landings++; landImpact = impact; };
    }

    internal PlayerPose Capture()
    {
        var abilities = ctx.Abilities;
        var landing = abilities.Landing;
        var traversal = abilities.Traversal;
        var fixtures = abilities.Fixtures;
        var pose = new PlayerPose
        {
            State = (byte)ctx.CurrentState, LandingTier = (byte)landing.LastTier, HeavyPose = (byte)landing.HeavyPose,
            Jumps = jumps, Landings = landings, DashHits = dashHits, LadderSteps = (byte)fixtures.LadderSteps,
            Velocity = ctx.ActualVelocity, RunIntent = ctx.RunIntent, FallSpeed = ctx.FallSpeed, LandImpact = landImpact,
            Lean = abilities.Lean.Share, Yaw = ctx.transform.eulerAngles.y,
            Pitch = lookPivot != null ? Mathf.DeltaAngle(0f, lookPivot.localEulerAngles.x) : 0f
        };
        pose.Tuck = ctx.Posture.AirTuck;
        if (Body != null) pose.Step = Body.Step;
        var marks = Marks.None;
        if (ctx.IsGrounded) marks |= Marks.Grounded;
        if (ctx.IsAirCrouching) marks |= Marks.AirCrouching;
        if (ctx.IsBigFall) marks |= Marks.BigFall;
        switch (ctx.CurrentState)
        {
            case State.Sliding:
            case State.HeadFirstSliding:
            case State.SupineSliding: pose.Direction = ctx.SlideDirection; break;
            case State.StepDodge: pose.Direction = abilities.ActionDirection; break;
            case State.HeavyLanding: pose.Detail = landing.HeavyYaw; break;
            case State.LandingRoll: pose.Detail = landing.LandsInPlace ? 1f : 0f; break;
            case State.Vault:
            case State.Mantle:
                pose.Detail = traversal.ObstacleProgress;
                pose.Extent = traversal.Obstacle.Height;
                pose.Vault = (byte)traversal.Obstacle.Kind;
                if (traversal.Obstacle.ExitLadder != null) marks |= Marks.LeavesLadder;
                if (traversal.Obstacle.Kind != VaultKind.None) WriteLedge(ref pose, traversal.Ledge);
                break;
            case State.LedgeHang: WriteLedge(ref pose, traversal.Ledge); break;
            case State.BarSwing:
                pose.Detail = traversal.Angle;
                pose.Extent = traversal.AngularSpeed;
                break;
            case State.Ladder:
                if (fixtures.Ladder != null) pose.Anchor = fixtures.Ladder.transform.position;
                pose.Detail = fixtures.LadderStepProgress;
                if (fixtures.LadderCaught) marks |= Marks.LadderCaught;
                if (fixtures.LadderLeftHand) marks |= Marks.LadderLeftHand;
                if (fixtures.LadderSliding) marks |= Marks.LadderSliding;
                if (fixtures.LadderStepBlocked) marks |= Marks.LadderBlocked;
                break;
            case State.Zipline:
                pose.Anchor = fixtures.ZiplineFrom;
                pose.Direction = fixtures.ZiplineDirection;
                pose.Extent = fixtures.ZiplineSpeed;
                break;
        }
        pose.Flags = marks;
        return pose;
    }

    private static void WriteLedge(ref PlayerPose pose, in LedgeTarget ledge)
    {
        if (ledge.Surface == null) return;
        pose.Anchor = ledge.Surface.transform.TransformPoint(ledge.LocalEdge);
        pose.Direction = ledge.Surface.transform.TransformDirection(ledge.LocalNormal);
    }

    internal void Apply(in PlayerPose pose, float dt)
    {
        var state = (State)pose.State;
        var motor = ctx.Motor;
        var abilities = ctx.Abilities;
        float follow = 1f - Mathf.Exp(-FollowRate * dt);
        motor.actualVelocity = Vector3.Lerp(motor.actualVelocity, pose.Velocity, follow);
        motor.HorizontalVelocity = Vector3.ProjectOnPlane(pose.Velocity, Vector3.up);
        motor.VerticalVelocity = pose.Velocity.y;
        ctx.Actions.sprintProgress = pose.RunIntent;
        ctx.Posture.airCrouching = Has(pose, Marks.AirCrouching);
        ctx.Posture.mirroredTuck = pose.Tuck;
        if (Body != null) Body.Step = pose.Step;
        abilities.Lean.Share = Mathf.Lerp(abilities.Lean.Share, pose.Lean, follow);
        abilities.Landing.Mirror((MovementLanding.Tier)pose.LandingTier, (MovementLanding.Pose)pose.HeavyPose,
            state == State.HeavyLanding ? pose.Detail : abilities.Landing.HeavyYaw, Has(pose, Marks.BigFall), pose.FallSpeed);
        ApplyStateDetails(state, pose);
        // The details come first: entering the state is what starts its clip and its sound.
        ctx.StateMachine.Mirror(state);
        ctx.StateMachine.Duration += dt;
        motor.isGrounded = Has(pose, Marks.Grounded);
        float look = 1f - Mathf.Exp(-LookRate * dt);
        ctx.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(ctx.transform.eulerAngles.y, pose.Yaw, look), 0f);
        if (lookPivot != null)
        {
            pitch = Mathf.Lerp(pitch, pose.Pitch, look);
            lookPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
        RaiseEvents(pose);
    }

    private void ApplyStateDetails(State state, in PlayerPose pose)
    {
        var abilities = ctx.Abilities;
        var traversal = abilities.Traversal;
        var fixtures = abilities.Fixtures;
        switch (state)
        {
            case State.Sliding:
            case State.HeadFirstSliding:
            case State.SupineSliding: ctx.SlideDirection = pose.Direction; break;
            case State.StepDodge: abilities.ActionDirection = pose.Direction; break;
            case State.LandingRoll: abilities.Landing.LandsInPlace = pose.Detail > .5f; break;
            case State.Vault:
            case State.Mantle:
                traversal.ObstacleProgress = pose.Detail;
                traversal.Obstacle.Height = pose.Extent;
                traversal.Obstacle.Kind = (VaultKind)pose.Vault;
                traversal.Obstacle.ExitLadder = Has(pose, Marks.LeavesLadder) ? fixtures.Ladder : null;
                if (pose.Direction != Vector3.zero) MirrorLedge(pose);
                break;
            case State.LedgeHang: MirrorLedge(pose); break;
            case State.BarSwing:
                traversal.Angle = pose.Detail;
                traversal.AngularSpeed = pose.Extent;
                break;
            case State.Ladder:
                if (pose.LadderSteps != 0 && pose.LadderSteps != fixtures.LadderSteps) ctx.RaiseLadderStepped(Has(pose, Marks.LadderLeftHand));
                fixtures.MirrorLadder(LadderAt(pose.Anchor), Has(pose, Marks.LadderCaught), Has(pose, Marks.LadderLeftHand),
                    pose.LadderSteps, pose.Detail, Has(pose, Marks.LadderBlocked), Has(pose, Marks.LadderSliding));
                break;
            case State.Zipline: fixtures.MirrorZipline(pose.Anchor, pose.Direction, pose.Extent); break;
        }
    }

    // The hands read the ledge from its collider, and a collider can't travel, so this PC finds its own at the same edge.
    private void MirrorLedge(in PlayerPose pose)
    {
        ref var ledge = ref ctx.Abilities.Traversal.Ledge;
        if (ledge.Surface != null && pose.Anchor == ledgeAnchor) return;
        ledgeAnchor = pose.Anchor;
        int count = Physics.OverlapSphereNonAlloc(pose.Anchor, LedgeProbe, nearby, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        Collider surface = count > 0 ? nearby[0] : null;
        for (int i = 0; i < count; i++)
        {
            Vector3 above = pose.Anchor - pose.Direction * LedgeProbe + Vector3.up * .25f;
            if (!nearby[i].Raycast(new Ray(above, Vector3.down), out _, .5f)) continue;
            surface = nearby[i];
            break;
        }
        if (surface == null) { ledge = default; return; }
        var support = surface.transform;
        ledge = new LedgeTarget
        {
            Surface = surface, LocalEdge = support.InverseTransformPoint(pose.Anchor),
            LocalNormal = support.InverseTransformDirection(pose.Direction), LocalHang = support.InverseTransformPoint(ctx.transform.position)
        };
    }

    private Ladder LadderAt(Vector3 foot)
    {
        var held = ctx.Abilities.Fixtures.Ladder;
        if (held != null && (held.transform.position - foot).sqrMagnitude < LadderMatch) return held;
        foreach (var ladder in Ladder.Active)
            if ((ladder.transform.position - foot).sqrMagnitude < LadderMatch) return ladder;
        return null;
    }

    // A counter that went up is an event. The first pose only sets the count: it may be someone's old jumps. The counts
    // are set last because raising an event also counts it here, which would read as another one next frame.
    private void RaiseEvents(in PlayerPose pose)
    {
        if (applied && pose.Jumps != jumps) ctx.RaiseJumped();
        if (applied && pose.Landings != landings) ctx.RaiseLanded(pose.LandImpact);
        if (applied && pose.DashHits != dashHits) ctx.RaiseDashPushHit(ctx.transform.position);
        applied = true;
        jumps = pose.Jumps;
        landings = pose.Landings;
        dashHits = pose.DashHits;
    }

    private static bool Has(in PlayerPose pose, Marks mark) => (pose.Flags & mark) != 0;
}
