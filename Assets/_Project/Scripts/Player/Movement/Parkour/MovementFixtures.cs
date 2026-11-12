using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// The world's ladders and ziplines, after Beatrun: takes hold of one when the player meets it, and
/// keeps what the body and the sounds need to know while on it. LadderState and ZiplineState do the moving.
/// - A ladder takes W, facing it, within reach, on the ground or in the air.
/// - From its top, walking off onto it takes it as Left 4 Dead 2 does: only once the body has reached it, moving out over
///   the edge (FindLadderTop). Interact takes it from a little further back.
/// - A zipline catches the player in the air they jumped into, or with jump held, once the cable is within the hands'
///   reach (TrackJumpedIntoAir).
/// Numbers come from Player.csv, row Ability.
/// </summary>
public sealed class MovementFixtures
{
    private readonly PlayerMovement ctx;
    private float ladderLock, ziplineLock;
    // Off the ground by the player's own jump: only then, or with jump held, do the hands go for a cable.
    private bool jumpedIntoAir;

    public Ladder Ladder { get; private set; }
    /// <summary>The ladder was caught in the air rather than stepped onto from the ground.</summary>
    public bool LadderCaught { get; private set; }
    /// <summary>The hand that last went up.</summary>
    public bool LadderLeftHand { get; private set; }
    /// <summary>Rungs climbed since taking hold; the body restarts its clip when it changes.</summary>
    public int LadderSteps { get; private set; }
    /// <summary>Actual movement progress of the current rung, shared with the hand IK.</summary>
    public float LadderStepProgress { get; internal set; }
    public bool LadderStepBlocked { get; internal set; }
    public bool LadderSliding { get; internal set; }

    public Zipline Zipline { get; private set; }
    /// <summary>The way along the cable this ride goes, and its ends in that order.</summary>
    public Vector3 ZiplineDirection { get; private set; }
    public Vector3 ZiplineFrom { get; private set; }
    public Vector3 ZiplineTo { get; private set; }
    public float ZiplineFraction { get; internal set; }
    public float ZiplineSpeed { get; internal set; }

    internal MovementFixtures(PlayerMovement context)
    {
        ctx = context;
        // A jump off the ground and the spring off a vault's top both go through MovementActions.Jump.
        ctx.Jumped += () => jumpedIntoAir = true;
    }

    /// <summary>The way a horizontal direction points (world yaw, degrees).</summary>
    public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

    internal void Tick(float dt)
    {
        ladderLock = Mathf.Max(0f, ladderLock - dt);
        ziplineLock = Mathf.Max(0f, ziplineLock - dt);
        TrackJumpedIntoAir();
        TrackClimbDown();
    }

    // A cable is taken only by a player who means it (the user, 2026-11-12: "사다리 올라가자마자 집라인이 타져서 사실
    // 사다리를 통해 위로 올라갈 수가 없음"). The hands used to go for any cable in reach whenever the player was off the
    // ground in an ordinary stance, whatever put them there. The climb off a ladder's top, like every climb onto a top,
    // sets the feet down a little over the floor (TryLeaveLadderTop's end, 0.1 m), and the hub's zipline sets off half a
    // metre beside its ladder's top: the few frames of that settle were enough to put the player on the cable instead of
    // the catwalk. Now the air counts only when the player jumped into it: Space off the ground or the spring off a
    // vault's top (Jumped), a ledge jump, a wall kick or a release off a bar (their launch states, a frame each). It
    // lasts until the feet are down or another move takes over. Settling after a climb, walking off an edge, being
    // knocked about and letting go of a ledge, a ladder, a bar or a cable never reach for one; holding jump does.
    private void TrackJumpedIntoAir()
    {
        switch (ctx.CurrentState)
        {
            case State.LedgeJump: case State.WallKick: case State.LacheRelease: jumpedIntoAir = true; break;
            case State.Standing: case State.Crouching: if (ctx.IsGrounded) jumpedIntoAir = false; break;
            default: jumpedIntoAir = false; break;
        }
    }

    private bool ReachesForCable => jumpedIntoAir || (ctx.InputAvailable && ctx.Input.JumpHeld);

    internal bool TryStart()
    {
        bool ground = ctx.Abilities.CanUseGroundActions, air = ctx.Abilities.IsOrdinaryAirborne;
        if ((!ground && !air) || ctx.ControlLocked || ctx.IsGettingUp || ctx.Input.CrouchHeld) return false;
        if (TryLadder()) return true;
        return air && TryZipline();
    }

    /// <summary>The way the view stays around while on a fixture (world yaw, degrees).</summary>
    public bool TryLookCentre(out float yaw)
    {
        yaw = 0f;
        switch (ctx.CurrentState)
        {
            case State.Ladder when Ladder != null: yaw = Ladder.ClimberYaw; return true;
            case State.Zipline: yaw = YawOf(ZiplineDirection); return true;
            default: return false;
        }
    }

    /// <summary>Getting on from the top (backlog 141): out over the edge to here, then down to LadderTopHold.</summary>
    public bool LadderFromTop { get; private set; }
    internal Vector3 LadderTopOver { get; private set; }
    internal Vector3 LadderTopHold { get; private set; }
    /// <summary>Got on from the top by walking off it rather than by Interact: the player was already moving out.</summary>
    internal bool LadderTopWalked { get; private set; }
    /// <summary>Seconds this getting on from the top takes, for the movement, the body and the hands alike.</summary>
    public float LadderTopEnterTime { get; private set; }

    private bool TryLadder()
    {
        if (ladderLock > 0f) return false;
        if (TryLadderFromTop()) return true;
        if (ctx.CurrentInput.y <= .1f) return false;
        var settings = ctx.AbilitySettings;
        Vector3 feet = ctx.transform.position, facing = Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized;
        float minFacing = Mathf.Cos(settings.ladderFacingAngle * Mathf.Deg2Rad);
        foreach (var candidate in Ladder.Active)
        {
            if (Vector3.Dot(facing, -candidate.Outward) < minFacing) continue;
            Vector3 offset = feet - candidate.transform.position;
            float away = Vector3.Dot(offset, candidate.Outward), side = Vector3.Dot(offset, candidate.transform.right);
            // Reachable from below while the eyes are level with its foot, and up to where the top exit starts.
            if (away < 0f || away > settings.ladderReach || Mathf.Abs(side) > candidate.Width * .5f + settings.ladderSideReach
                || offset.y < -ctx.StandingColliderHeight * .85f || offset.y > candidate.Height - settings.ladderTopGap) continue;
            Vector3 hold = LadderHold(candidate, feet.y);
            if (!ctx.Abilities.Traversal.Detector.ClearPath(feet, hold, ctx.StandingColliderHeight)) continue;
            Ladder = candidate;
            LadderFromTop = LadderTopWalked = false;
            LadderCaught = !ctx.IsGrounded;
            LadderLeftHand = !LadderCaught;
            LadderSteps = 0;
            LadderStepProgress = 0f;
            LadderStepBlocked = false;
            ctx.ChangeState(State.Ladder);
            return true;
        }
        return false;
    }

    // Backlog 141: from the floor the ladder leads up to, out over the edge, then down until the hands are on the top
    // rungs, at the height the climb up hands over to climbing off (ladderTopGap) less a rung. Walking off onto it does it
    // by itself, as in Left 4 Dead 2; Interact does it from a little further back (FindLadderTop).
    private bool TryLadderFromTop()
    {
        Ladder candidate;
        Vector3 over, hold;
        bool walked;
        if (ctx.Input.InteractPressed && FindLadderTop(false, out candidate, out over, out hold)) walked = false;
        else if (FindLadderTop(true, out candidate, out over, out hold)) walked = true;
        else return false;
        Ladder = candidate;
        LadderFromTop = true;
        LadderTopOver = over;
        LadderTopHold = hold;
        LadderTopWalked = walked;
        // The player walking off it is on the move already, so it is quicker than getting on from standing with the key.
        LadderTopEnterTime = walked ? ctx.AbilitySettings.ladderTopWalkTime : ctx.AbilitySettings.ladderTopEnterTime;
        LadderCaught = false;
        LadderLeftHand = true;
        LadderSteps = 0;
        LadderStepProgress = 0f;
        LadderStepBlocked = false;
        ctx.ChangeState(State.Ladder);
        return true;
    }

    /// <summary>The ladder whose top the player could climb down from now, for the prompt (LadderPromptHUD); else null.</summary>
    public Ladder ClimbDownLadder { get; private set; }

    /// <summary>Kept each frame for the prompt: only standing about on the ground, never mid-move.</summary>
    internal void TrackClimbDown()
    {
        bool free = ctx.IsLocal && ctx.Abilities.CanUseGroundActions && !ctx.ControlLocked && !ctx.IsGettingUp && !ctx.Input.CrouchHeld
            && ladderLock <= 0f && (ctx.CurrentState == State.Standing || ctx.CurrentState == State.Crouching);
        ClimbDownLadder = free && FindLadderTop(false, out Ladder found, out _, out _) ? found : null;
    }

    // Where the player could get on a ladder from its top, standing on the floor it leads up to.
    // - With Interact (walking false): within ladderTopEnterReach of it, looking out over it.
    // - Walking off onto it (walking true), as Left 4 Dead 2 does it (the user, 2026-11-12: "사다리는 걍 알아서 내려가는 게
    //   좋은 듯. 근데 전에 내가 F 눌러서 내려가자 했던 이유가 너무 범위가 넓어서 걍 걸어가다 사다리쪽으로 납치됐어서 그럼.
    //   딱 레포데만큼이 좋은 듯"). L4D2 takes a ladder when the body touches its thin volume, as wide as the ladder, while
    //   moving into it. Here that is a body that has reached it: the capsule's front at the plane of the rungs
    //   (ladderTopWalkReach), the centre inside the rails (ladderTopWalkSideReach), and the move keys pointing out over the
    //   edge (ladderTopWalkAngle). The keys, not the view, so backing onto it works too. Walking along the edge, standing
    //   at it, crossing past the top and jumping at the edge never take it; holding crouch never does (TryStart). The
    //   reach the key had before (0.9 m back, any walk toward it) was what pulled people in.
    private bool FindLadderTop(bool walking, out Ladder ladder, out Vector3 over, out Vector3 hold)
    {
        ladder = null; over = hold = Vector3.zero;
        if (!ctx.IsGrounded) return false;
        var settings = ctx.AbilitySettings;
        Vector3 feet = ctx.transform.position, heading;
        float minHeading, back, sideReach;
        if (walking)
        {
            // A jump pressed at the edge is a jump off it.
            if (ctx.Input.JumpPressed || ctx.Actions.jumpBufferTimer > 0f) return false;
            Vector2 keys = ctx.CurrentInput;
            if (keys.sqrMagnitude < .04f) return false;
            heading = Vector3.ProjectOnPlane(ctx.transform.right * keys.x + ctx.transform.forward * keys.y, Vector3.up).normalized;
            minHeading = Mathf.Cos(settings.ladderTopWalkAngle * Mathf.Deg2Rad);
            back = ctx.ControllerRadius + settings.ladderTopWalkReach;
            sideReach = settings.ladderTopWalkSideReach;
        }
        else
        {
            heading = Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized;
            minHeading = Mathf.Cos(settings.ladderFacingAngle * 1.5f * Mathf.Deg2Rad);
            back = settings.ladderTopEnterReach;
            sideReach = settings.ladderSideReach;
        }
        foreach (var candidate in Ladder.Active)
        {
            if (candidate == null || !candidate.isActiveAndEnabled) continue;
            if (Vector3.Dot(heading, candidate.Outward) < minHeading) continue;
            Vector3 offset = feet - candidate.transform.position;
            float away = Vector3.Dot(offset, candidate.Outward), side = Vector3.Dot(offset, candidate.transform.right);
            float top = candidate.Height;
            // On the floor up there: behind the plane of the rungs, at the height the climb up comes off onto.
            if (away > .05f || away < -back || Mathf.Abs(side) > candidate.Width * .5f + sideReach
                || offset.y < top - settings.ladderTopGap || offset.y > top + .3f) continue;
            Vector3 down = LadderHold(candidate, candidate.transform.position.y + TopHoldHeight(candidate));
            Vector3 out_ = down;
            out_.y = feet.y;
            var detector = ctx.Abilities.Traversal.Detector;
            if (!detector.ClearPath(feet, out_, ctx.StandingColliderHeight) || !detector.ClearPath(out_, down, ctx.StandingColliderHeight))
                continue;
            ladder = candidate; over = out_; hold = down;
            return true;
        }
        return false;
    }

    // Getting on from the top ends with the feet this far over the ladder's foot: where climbing up hands over to climbing
    // off (ladderTopGap), a rung lower.
    private float TopHoldHeight(Ladder ladder)
        => Mathf.Max(0f, ladder.Height - ctx.AbilitySettings.ladderTopGap - ctx.AbilitySettings.ladderRungHeight);

    private Vector3 LadderHold(Ladder ladder, float feetY)
    {
        Vector3 hold = ladder.transform.position + ladder.Outward * ctx.AbilitySettings.ladderStandOff;
        hold.y = Mathf.Max(hold.y, feetY);
        return hold;
    }

    /// <summary>Where the player stands on the ladder at this height.</summary>
    internal Vector3 LadderHold(float feetY) => LadderHold(Ladder, feetY);

    internal void LadderStepped()
    {
        LadderFromTop = false;
        LadderLeftHand = !LadderLeftHand;
        LadderSteps++;
        LadderStepProgress = 0f;
        LadderStepBlocked = false;
        ctx.RaiseLadderStepped(LadderLeftHand);
    }

    internal void LockLadder() => ladderLock = ctx.AbilitySettings.ladderRegrabDelay;

    /// <summary>Over the top of the ladder onto what lies beyond it, if there is somewhere to stand there.</summary>
    internal bool TryLeaveLadderTop()
    {
        var settings = ctx.AbilitySettings;
        var detector = ctx.Abilities.Traversal.Detector;
        Vector3 feet = ctx.transform.position;
        Vector3 origin = feet + Vector3.up * settings.ladderExitProbeHeight - Ladder.Outward * settings.ladderExitReach;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit floor, settings.ladderExitProbeHeight, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || floor.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) return false;
        Vector3 end = floor.point + Vector3.up * .1f, raised = feet;
        raised.y = end.y;
        if (!detector.ClearPath(feet, raised, ctx.CrouchColliderHeight) || !detector.ClearPath(raised, end, ctx.CrouchColliderHeight)
            || !detector.ClearCapsule(end, ctx.StandingColliderHeight)) return false;
        ctx.Abilities.Traversal.Obstacle = new ObstacleTarget { Surface = floor.collider, ExitLadder = Ladder, Start = feet, RaisedStart = raised,
            RaisedEnd = end, End = end, Height = end.y - feet.y, Duration = settings.ladderExitDuration,
            // The floor's near edge, where the climb-off's clip puts its hands down (WarpedTraversal).
            Edge = NearEdge(floor, feet), EdgeNormal = Ladder.Outward };
        ctx.ChangeState(State.Mantle);
        ctx.Actions.ClearJumpBuffer();
        return true;
    }

    // Where the floor up there begins, in from the ladder in front of the climber: probed down every few cm from the
    // plane of the rungs inward until it is met at the floor's height. The floor's point found further in, failing that.
    private Vector3 NearEdge(in RaycastHit floor, Vector3 feet)
    {
        Vector3 inward = -Ladder.Outward;
        Vector3 plane = feet - Ladder.Outward * Vector3.Dot(feet - Ladder.transform.position, Ladder.Outward);
        plane.y = floor.point.y;
        float reach = ctx.AbilitySettings.ladderExitReach + .3f;
        for (float into = 0f; into <= reach; into += .03f)
        {
            Vector3 probe = plane + inward * into;
            if (floor.collider.Raycast(new Ray(probe + Vector3.up * .3f, Vector3.down), out RaycastHit hit, .45f)
                && Mathf.Abs(hit.point.y - floor.point.y) < .08f)
                return hit.point;
        }
        return floor.point;
    }

    private bool TryZipline()
    {
        if (ziplineLock > 0f || !ReachesForCable) return false;
        var settings = ctx.AbilitySettings;
        Vector3 feet = ctx.transform.position;
        Vector3 hands = feet + Vector3.up * (settings.ziplineReachHeight.x + settings.ziplineReachHeight.y) * .5f;
        foreach (var candidate in Zipline.Active)
        {
            Vector3 from = candidate.From, to = candidate.To;
            float length = Vector3.Distance(from, to);
            if (length < .1f) continue;
            if (candidate.TwoWay && Vector3.Dot(ctx.transform.forward, to - from) < 0f) (from, to) = (to, from);
            Vector3 direction = (to - from) / length;
            float along = Mathf.Clamp(Vector3.Dot(hands - from, direction), 0f, length);
            Vector3 cable = from + direction * along, reach = cable - feet;
            if (along >= length || reach.y < settings.ziplineReachHeight.x || reach.y > settings.ziplineReachHeight.y
                || new Vector2(reach.x, reach.z).magnitude > settings.ziplineReach) continue;
            if (!ctx.Abilities.Traversal.Detector.ClearCapsule(cable - Vector3.up * settings.ziplineHangLength, ctx.StandingColliderHeight)) continue;
            Zipline = candidate;
            ZiplineFrom = from;
            ZiplineTo = to;
            ZiplineDirection = direction;
            ZiplineFraction = along / length;
            ZiplineSpeed = Mathf.Min(ctx.HorizontalVelocity.magnitude, settings.ziplineMaxSpeed);
            ctx.ChangeState(State.Zipline);
            return true;
        }
        return false;
    }

    internal void LockZipline() => ziplineLock = ctx.AbilitySettings.ziplineRegrabDelay;

    /// <summary>Shows the ladder this character is on, on another PC. topEnterTime: how long getting on from its top takes,
    /// while it does (0 otherwise).</summary>
    internal void MirrorLadder(Ladder ladder, bool caught, bool leftHand, int steps, float stepProgress, bool blocked, bool sliding,
        float topEnterTime = 0f)
    {
        Ladder = ladder;
        LadderCaught = caught;
        LadderLeftHand = leftHand;
        LadderSteps = steps;
        LadderStepProgress = stepProgress;
        LadderStepBlocked = blocked;
        LadderSliding = sliding;
        // The copy plays getting on from the top as the player's own PC does (the climb off played backwards), and its hands
        // take the rungs for where the body ends up, which the ladder alone gives.
        LadderFromTop = topEnterTime > 0f && ladder != null;
        if (!LadderFromTop) return;
        LadderTopEnterTime = topEnterTime;
        LadderTopHold = LadderHold(ladder, ladder.transform.position.y + TopHoldHeight(ladder));
    }

    /// <summary>Shows the cable this character rides, on another PC.</summary>
    internal void MirrorZipline(Vector3 from, Vector3 direction, float speed)
    {
        ZiplineFrom = from;
        ZiplineDirection = direction;
        ZiplineSpeed = speed;
    }

    internal void Reset()
    {
        ladderLock = ziplineLock = 0f;
        jumpedIntoAir = false;
        Ladder = null;
        LadderFromTop = LadderTopWalked = false;
        Zipline = null;
        LadderSteps = 0;
        LadderStepProgress = 0f;
        LadderStepBlocked = false;
        LadderSliding = false;
    }
}
