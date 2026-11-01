using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// The world's ladders and ziplines, after Beatrun: takes hold of one when the player meets it, and
/// keeps what the body and the sounds need to know while on it. LadderState and ZiplineState do the moving.
/// - A ladder takes W, facing it, within reach, on the ground or in the air.
/// - A zipline catches the player in the air, once the cable is within the hands' reach.
/// Numbers come from Player.csv, row Ability.
/// </summary>
public sealed class MovementFixtures
{
    private readonly PlayerMovement ctx;
    private float ladderLock, ziplineLock;

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

    internal MovementFixtures(PlayerMovement context) => ctx = context;

    /// <summary>The way a horizontal direction points (world yaw, degrees).</summary>
    public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

    internal void Tick(float dt)
    {
        ladderLock = Mathf.Max(0f, ladderLock - dt);
        ziplineLock = Mathf.Max(0f, ziplineLock - dt);
    }

    internal bool TryStart()
    {
        bool ground = ctx.Abilities.CanUseGroundActions, air = ctx.Abilities.IsOrdinaryAirborne;
        if ((!ground && !air) || ctx.ControlLocked || ctx.Input.CrouchHeld) return false;
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

    private bool TryLadder()
    {
        if (ladderLock > 0f || ctx.CurrentInput.y <= .1f) return false;
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
            RaisedEnd = end, End = end, Height = end.y - feet.y, Duration = settings.ladderExitDuration };
        ctx.ChangeState(State.Mantle);
        ctx.Actions.ClearJumpBuffer();
        return true;
    }

    private bool TryZipline()
    {
        if (ziplineLock > 0f) return false;
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

    /// <summary>Shows the ladder this character is on, on another PC.</summary>
    internal void MirrorLadder(Ladder ladder, bool caught, bool leftHand, int steps, float stepProgress, bool blocked, bool sliding)
    {
        Ladder = ladder;
        LadderCaught = caught;
        LadderLeftHand = leftHand;
        LadderSteps = steps;
        LadderStepProgress = stepProgress;
        LadderStepBlocked = blocked;
        LadderSliding = sliding;
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
        Ladder = null;
        Zipline = null;
        LadderSteps = 0;
        LadderStepProgress = 0f;
        LadderStepBlocked = false;
        LadderSliding = false;
    }
}
