using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// The world's ladders, ziplines and balance beams, after Beatrun: takes hold of one when the player meets it, and
/// keeps what the body and the sounds need to know while on it. LadderState, ZiplineState and BalanceState do the moving.
/// - A ladder takes W, facing it, within reach, on the ground or in the air.
/// - A zipline catches the player in the air, once the cable is within the hands' reach.
/// - A beam is crossed three ways. Stepping along it starts the rhythm walk; arriving at a run keeps running with the
///   same notes on top (BeamRun); crouching is safe and slowest (LimitOnBeam).
/// Numbers come from Player.csv, row Ability.
/// </summary>
public sealed class MovementFixtures
{
    private readonly PlayerMovement ctx;
    private float ladderLock, ziplineLock, balanceLock, turnLeft, lookTurn;
    private Vector3 turnTo;
    private BalanceRhythm balanceRhythm;

    public Ladder Ladder { get; private set; }
    /// <summary>The ladder was caught in the air rather than stepped onto from the ground.</summary>
    public bool LadderCaught { get; private set; }
    /// <summary>The hand that last went up.</summary>
    public bool LadderLeftHand { get; private set; }
    /// <summary>Rungs climbed since taking hold; the body restarts its clip when it changes.</summary>
    public int LadderSteps { get; private set; }
    public bool LadderSliding { get; internal set; }

    public Zipline Zipline { get; private set; }
    /// <summary>The way along the cable this ride goes, and its ends in that order.</summary>
    public Vector3 ZiplineDirection { get; private set; }
    public Vector3 ZiplineFrom { get; private set; }
    public Vector3 ZiplineTo { get; private set; }
    public float ZiplineFraction { get; internal set; }
    public float ZiplineSpeed { get; internal set; }

    public BalanceBeam Beam { get; private set; }
    /// <summary>Which way along the beam the player faces, level.</summary>
    public Vector3 BalanceFacing { get; private set; }
    /// <summary>How the body sways to the beat: -100 full left, 100 full right of the way they face.</summary>
    public float Balance { get; internal set; }
    /// <summary>Side of the failed foot placement, separate from the continuous visual sway.</summary>
    public int BalanceFallSide { get; internal set; } = 1;
    /// <summary>Desired average walking footstep interval. Hits shorten the continuous clip's pace; contacts own timing.</summary>
    public float BalanceBeatTime { get; internal set; }
    /// <summary>Walking speed along the beam at this tempo: one step a beat (m/s).</summary>
    public float BalanceSpeed => ctx.AbilitySettings.balanceStepLength / Mathf.Max(.05f, BalanceBeatTime);
    /// <summary>The foot awaiting input or previewing its next contact: -1 left, 1 right, 0 before calibration.</summary>
    public int BalanceNoteSide { get; internal set; }
    /// <summary>Each ring's approach: 1 before it starts, 0 exactly at Perfect, negative when late and inside the circle.</summary>
    public float BalanceLeftRing { get; internal set; }
    public float BalanceRightRing { get; internal set; }
    /// <summary>Notes missed on this beam and not yet made up for; at BalanceSlipLimit the player falls.</summary>
    public int BalanceSlips { get; internal set; }
    public int BalanceSlipLimit { get; internal set; }
    /// <summary>Roll of the view from the tilt (degrees), with the same sign as MovementLean.Roll.</summary>
    public float BalanceRoll => BalanceActive ? -Balance * .01f * ctx.AbilitySettings.balanceViewRoll : 0f;
    /// <summary>The notes are on: walking a beam, or running along one.</summary>
    public bool BalanceActive => ctx.CurrentState == State.Balance || Run.IsActive;
    public BeamRun Run { get; }

    internal void BeginBalanceRhythm(BalanceRhythm rhythm) => balanceRhythm = rhythm;
    internal void EndBalanceRhythm(BalanceRhythm rhythm)
    {
        if (balanceRhythm == rhythm) balanceRhythm = null;
    }

    /// <summary>Called by PlayerFootsteps after the visible foot lands on the beam.</summary>
    internal void BalanceFootDown(bool left)
    {
        if (!BalanceActive || BalanceTurning || balanceRhythm == null) return;
        ctx.RaiseBalanceStep();
        if (!balanceRhythm.Contact(left ? -1 : 1)) return;
        Run.End();
        ctx.ChangeState(State.BalanceFall);
    }

    internal void ObserveBalanceCycle(float phase)
    {
        if (BalanceActive && !BalanceTurning) balanceRhythm?.ObserveCycle(phase);
    }

    internal MovementFixtures(PlayerMovement context)
    {
        ctx = context;
        Run = new BeamRun(ctx);
    }

    /// <summary>The way a horizontal direction points (world yaw, degrees).</summary>
    public static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

    internal void Tick(float dt)
    {
        ladderLock = Mathf.Max(0f, ladderLock - dt);
        ziplineLock = Mathf.Max(0f, ziplineLock - dt);
        balanceLock = Mathf.Max(0f, balanceLock - dt);
        Run.Tick(dt);
    }

    internal bool TryStart()
    {
        bool ground = ctx.Abilities.CanUseGroundActions, air = ctx.Abilities.IsOrdinaryAirborne;
        if ((!ground && !air) || ctx.ControlLocked || ctx.Input.CrouchHeld) return false;
        if (TryLadder()) return true;
        if (air) return TryZipline();
        return ctx.CurrentState == State.Standing && !Run.IsActive && TryBeam();
    }

    /// <summary>A crouched walk's speed, held down while on a beam.</summary>
    internal float LimitOnBeam(float speed)
        => ctx.CurrentState == State.Crouching && IsOnBeam() ? Mathf.Min(speed, ctx.AbilitySettings.balanceCrouchSpeed) : speed;

    private bool IsOnBeam()
    {
        var settings = ctx.AbilitySettings;
        Vector3 feet = ctx.transform.position;
        foreach (var candidate in BalanceBeam.Active)
        {
            float distance = candidate.DistanceOf(feet);
            if (distance < 0f || distance > candidate.Length) continue;
            Vector3 offset = feet - candidate.PointAt(distance);
            if (Mathf.Abs(offset.y) <= settings.balanceStepHeight && new Vector2(offset.x, offset.z).magnitude <= settings.balanceReach) return true;
        }
        return false;
    }

    /// <summary>The way the view stays around while on a fixture (world yaw, degrees).</summary>
    public bool TryLookCentre(out float yaw)
    {
        yaw = 0f;
        switch (ctx.CurrentState)
        {
            case State.Ladder when Ladder != null: yaw = Ladder.ClimberYaw; return true;
            case State.Zipline: yaw = YawOf(ZiplineDirection); return true;
            case State.Balance: yaw = YawOf(BalanceFacing); return true;
            default:
                yaw = YawOf(BalanceFacing);
                return Run.IsActive;
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
        ctx.Abilities.Traversal.Obstacle = new ObstacleTarget { Surface = floor.collider, Start = feet, RaisedStart = raised,
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

    private bool TryBeam()
    {
        if (balanceLock > 0f) return false;
        var settings = ctx.AbilitySettings;
        Vector3 feet = ctx.transform.position, travel = Vector3.ProjectOnPlane(ctx.ActualVelocity, Vector3.up);
        if (travel.magnitude < settings.balanceMinSpeed) return false;
        foreach (var candidate in BalanceBeam.Active)
        {
            float distance = candidate.DistanceOf(feet);
            if (distance < settings.balanceEnterMargin || distance > candidate.Length - settings.balanceEnterMargin) continue;
            Vector3 offset = feet - candidate.PointAt(distance);
            if (Mathf.Abs(offset.y) > settings.balanceStepHeight || new Vector2(offset.x, offset.z).magnitude > settings.balanceReach) continue;
            Vector3 along = Vector3.ProjectOnPlane(candidate.Along, Vector3.up).normalized;
            float heading = Vector3.Dot(travel.normalized, along);
            // Crossing it sideways is not walking it.
            if (Mathf.Abs(heading) < .5f) continue;
            Beam = candidate;
            BalanceFacing = heading >= 0f ? along : -along;
            bool running = ctx.Input.Move.y > .1f && (ctx.Input.SprintHeld || travel.magnitude >= settings.balanceRunSpeed);
            if (running) { Run.Begin(); return false; }
            ctx.ChangeState(State.Balance);
            return true;
        }
        return false;
    }

    /// <summary>The player is turning round on the beam and doesn't walk meanwhile.</summary>
    public bool BalanceTurning => turnLeft > 0f;

    internal void TurnAround()
    {
        if (BalanceTurning) return;
        turnTo = -BalanceFacing;
        turnLeft = 180f;
    }

    internal void TickBalanceTurn(float dt)
    {
        if (!BalanceTurning) return;
        float step = Mathf.Min(turnLeft, ctx.AbilitySettings.balanceTurnSpeed * dt);
        turnLeft -= step;
        lookTurn += step;
        BalanceFacing = BalanceTurning ? Quaternion.AngleAxis(step, Vector3.up) * BalanceFacing : turnTo;
    }

    internal void EndBalanceTurn() => turnLeft = lookTurn = 0f;

    /// <summary>Degrees the view turns with the body since last asked (turning round on a beam).</summary>
    public float TakeLookTurn()
    {
        float turn = lookTurn;
        lookTurn = 0f;
        return turn;
    }

    internal void LockBalance() => balanceLock = ctx.AbilitySettings.balanceRegrabDelay;

    internal void Reset()
    {
        Run.End();
        balanceRhythm = null;
        ladderLock = ziplineLock = balanceLock = turnLeft = lookTurn = 0f;
        Ladder = null;
        Zipline = null;
        Beam = null;
        LadderSteps = 0;
        LadderSliding = false;
        Balance = 0f;
        BalanceSlips = 0;
    }
}
