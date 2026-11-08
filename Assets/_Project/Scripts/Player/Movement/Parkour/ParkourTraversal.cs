using UnityEngine;
using System.Collections.Generic;
using State = PlayerMovement.MovementState;

public sealed class ParkourTraversal
{
    private readonly PlayerMovement ctx;
    public ParkourDetector Detector { get; }
    internal ObstacleTarget Obstacle;
    internal LedgeTarget Ledge;
    internal RaycastHit Wall;
    internal SwingBar Bar;
    internal Vector3 LocalGrip;
    internal float Angle, AngularSpeed;
    internal Vector3 ReleaseVelocity;
    internal Vector3 LedgeJumpVelocity;
    /// <summary>Share of the current vault or mantle's time that has passed (0–1); the body plays its clip in step.</summary>
    public float ObstacleProgress { get; internal set; }
    /// <summary>A warped move's clip times (s) its hands are down from and until (WarpedTraversal); zero without one.</summary>
    public Vector2 ObstacleSupport { get; internal set; }
    /// <summary>Climbing off a ladder's top: how far short of the floor's edge the warp kept the hands (m) to keep the feet out of the wall.</summary>
    public float EdgeShortfall { get; internal set; }
    /// <summary>A warped move's hold for the hands' middle in the world (WarpedTraversal.Contact); for the probe.</summary>
    public Vector3 ObstacleContact { get; internal set; }
    /// <summary>
    /// The clip time (s) a vault carried by its clip's hands is at (WarpedTraversal); the body plays its clip there. Negative
    /// when the clip runs by itself.
    /// </summary>
    public float ObstacleClipTime { get; internal set; } = -1f;
    private float wallLock, barLock, ledgeLock;
    // The wall climbs of this time in the air, as Beatrun counts them: how many came one after another, when the last is
    // planned to end (on this clock), and which way its wall faces, since the same wall isn't climbed twice before the
    // ground or a ledge.
    private int wallClimbs;
    private float clock, wallClimbEnds = float.NegativeInfinity;
    private Vector3 climbedWall;
    private SwingBar lastBar;
    // A jump off the ground while it rises, for the low vaults (VaultFinder.TryAir): the height the feet left from, and the
    // time since in Beatrun's terms, the first airborne frame counting as its first tick.
    private float groundY, sinceJump = float.PositiveInfinity;
    private bool wasGrounded;
    // A surge toward a vault (SurgesToVault): its ground velocity, the time it may still take, and how many spots along
    // the run are looked at for where the vault catches. Below SurgeLeast (m/s) the player isn't running at anything.
    private Vector3 surge;
    private float surgeLeft, surgeFrom;
    private const int SurgeSamples = 8;
    private const float SurgeLeast = 2f, SurgeSpare = .1f;
    // The vault found at the spot the surge runs to, that spot, and the way the player looked when it was found. Reaching
    // the spot (within SurgeArrive, m), or held up within SurgeHeldUp of it when the time runs out (the obstacle itself
    // stops the capsule a little short), that vault goes even if the look from here misses it, while the player still
    // looks within SurgeFacing (cosine) of that way.
    private ObstacleTarget surgeVault;
    private Vector3 surgeSpot, surgeFacing;
    private const float SurgeArrive = .05f, SurgeHeldUp = .4f, SurgeFacing = .5f;
    /// <summary>A surge toward a vault under way: the ground velocity LocomotionState keeps; zero when there is none.</summary>
    internal Vector3 SurgeVelocity => surgeLeft > 0f ? surge : Vector3.zero;
    internal void StopSurge() => surgeLeft = 0f;
    // Turning around must not refresh the same obstacle's lift while airborne.
    // Keep distinct obstacles available for legitimate parkour chains.
    private readonly HashSet<Collider> traversedObstacles = new HashSet<Collider>();
    internal ParkourTraversal(PlayerMovement context) { ctx = context; Detector = new ParkourDetector(ctx); }
    internal void Tick(float dt)
    {
        clock += dt;
        wallLock = Mathf.Max(0f, wallLock - dt); barLock = Mathf.Max(0f, barLock - dt); ledgeLock = Mathf.Max(0f, ledgeLock - dt);
        bool grounded = ctx.IsGrounded, jumping = ctx.VerticalVelocity > 0f
            && (ctx.CurrentState == State.Standing || ctx.CurrentState == State.Crouching);
        if (grounded) groundY = ctx.transform.position.y;
        sinceJump = grounded || !jumping ? float.PositiveInfinity : wasGrounded ? VaultFinder.Tick : sinceJump + dt;
        wasGrounded = grounded;
        // A surge is a run on the ground: off it, or into any other move, it is over.
        if (!grounded || ctx.CurrentState != State.Standing)
        {
            if (surgeLeft > 0f) SurgeProbe($"surge ended without a vault: {(grounded ? $"state {ctx.CurrentState}" : "left the ground")}");
            StopSurge();
        }
        // The vaulted obstacles are kept only for the time in the air. A vault stopped in the frame it began never left the
        // ground, so no landing let go of its obstacle: the next surge at it ran on, the look there refused it as vaulted
        // already, and the press went as a jump (2026-11-09).
        if (grounded && !ctx.IsParkour) traversedObstacles.Clear();
    }
    internal void OnGrounded()
    {
        ForgetWallClimbs();
        if (!ctx.IsParkour) traversedObstacles.Clear();
    }
    /// <summary>On the ground or holding a ledge, the next wall climb is a first one again, on any wall.</summary>
    internal void ForgetWallClimbs() { wallClimbs = 0; climbedWall = Vector3.zero; }
    internal void RecordObstacle() { if (Obstacle.Surface != null) traversedObstacles.Add(Obstacle.Surface); }

    /// <summary>
    /// Starts a climb up Wall and returns how long it lasts (s), from how it began (WallClimbPace). The chain counts it,
    /// and its wall is the one that can't be climbed again before the ground or a ledge.
    /// </summary>
    internal float BeginWallClimb()
    {
        float u = ctx.HeightUnit;
        float share = WallClimbPace.RiseShare(ctx.VerticalVelocity / u);
        if (clock - wallClimbEnds < WallClimbPace.ChainSeconds) share = WallClimbPace.ChainShare(wallClimbs);
        else wallClimbs = 0;
        float duration = ctx.AbilitySettings.wallClimbTime * share * WallClimbPace.SpeedShare(ctx.HorizontalVelocity.magnitude / u);
        wallClimbs++;
        wallClimbEnds = clock + duration;
        climbedWall = Wall.normal;
        return duration;
    }

    // Beatrun compares the walls' normals axis by axis, a quarter apart at most (IsEqualTol).
    private bool ClimbedBefore(Vector3 normal)
        => climbedWall != Vector3.zero && Mathf.Abs(normal.x - climbedWall.x) <= .25f
            && Mathf.Abs(normal.y - climbedWall.y) <= .25f && Mathf.Abs(normal.z - climbedWall.z) <= .25f;
    internal bool CanGrab(SwingBar candidate) => barLock <= 0f || candidate != lastBar;

    // Airborne, or at a wall with high = true. The move looks where the player looks, as Beatrun's do.
    internal bool TryVault(bool high)
    {
        Vector3 forward = ctx.transform.forward;
        ObstacleTarget candidate;
        bool found = high ? Detector.Vaults.TryHigh(forward, ctx.VerticalVelocity, ctx.CurrentState == State.WallClimb, out candidate)
            : Detector.Vaults.TryAir(forward, sinceJump, groundY, out candidate);
        if (!found || traversedObstacles.Contains(candidate.Surface)) return false;
        Begin(candidate);
        return true;
    }

    private void Begin(in ObstacleTarget candidate)
    {
        // A wall climb that ends in a vault or a climb onto the top ends its chain too (Beatrun clears the run's time).
        if (ctx.CurrentState == State.WallClimb) wallClimbEnds = float.NegativeInfinity;
        // Temporary (ParkourProbeLog, 2026-11-07): which move a long low top got, and where it was found.
        if (ctx.IsLocal)
        {
            Vector3 feet = ctx.transform.position;
            ParkourProbeLog.Line($"move {candidate.Kind} on {(candidate.Surface != null ? candidate.Surface.name : "-")} "
                + $"size {(candidate.Surface != null ? candidate.Surface.bounds.size.ToString("F2") : "-")} feet {feet.ToString("F2")} "
                + $"edge {candidate.Edge.ToString("F2")} ({Vector3.Distance(feet, candidate.Edge):0.00} m) end {candidate.End.ToString("F2")} "
                + $"facing {ctx.transform.forward.ToString("F2")} in air {!ctx.IsGrounded}");
        }
        Obstacle = candidate;
        // A reload has both hands on the gun. A move that puts a hand on the obstacle still goes: the gun is put away for it,
        // as for a roll, and the reload stops. A step up needs no hand, so the reload carries on.
        Obstacle.BareHanded = candidate.Kind != VaultKind.StepUp && ctx.HandsOnReload;
        Ledge = default;
        if (candidate.EdgeNormal != Vector3.zero)
        {
            Transform support = candidate.Surface.transform;
            Ledge = new LedgeTarget { Surface = candidate.Surface, LocalEdge = support.InverseTransformPoint(candidate.Edge),
                LocalNormal = support.InverseTransformDirection(candidate.EdgeNormal), LocalHang = support.InverseTransformPoint(ctx.transform.position) };
        }
        ctx.ChangeState(Obstacle.IsVault ? State.Vault : State.Mantle);
        ctx.Actions.ClearJumpBuffer();
    }
    internal bool TryStartAirAction()
    {
        if (ctx.Input.CrouchPressed || ctx.Input.CrouchHeld) return false;
        if ((ctx.Input.JumpHeld || ctx.Input.JumpPressed || ctx.CurrentInput.y > 0.1f) && Detector.TryBar(out SwingBar bar, out Vector3 grip))
        {
            Bar = bar; LocalGrip = bar.transform.InverseTransformPoint(grip);
            Angle = 0f;
            barTurnStarted = float.NegativeInfinity;
            AngularSpeed = Mathf.Clamp(Vector3.Dot(ctx.HorizontalVelocity, bar.SwingDirection) / bar.HangLength,
                -ctx.AbilitySettings.swingMaxAngularSpeed, ctx.AbilitySettings.swingMaxAngularSpeed);
            ctx.ChangeState(State.BarGrab);
            return true;
        }
        if (ctx.CurrentInput.y > 0.1f && ctx.VerticalVelocity >= -ctx.AbilitySettings.airTraversalMaxFallSpeed
            && ledgeLock <= 0f && (TryVault(false) || TryVault(true))) return true;
        if (ctx.VerticalVelocity <= 0f && TryCatchLedge()) return true;
        // A wall climb starts only on the way up or at the top of a jump, never falling (WallClimbPace).
        if (wallLock > 0f || ctx.CurrentInput.y <= 0.1f || !ctx.Input.JumpHeld
            || !WallClimbPace.CanStart(ctx.VerticalVelocity / ctx.HeightUnit)) return false;
        if (!Detector.TryWallEntry(out Wall) || ClimbedBefore(Wall.normal) || VaultComing()) return false;
        float facing = Vector3.Dot(ctx.transform.forward, -Wall.normal);
        float alongSpeed = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Wall.normal).magnitude;
        bool travellingAlongWall = alongSpeed > Mathf.Max(2f, ctx.HorizontalVelocity.magnitude * 0.6f);
        if (ctx.Input.JumpHeld && facing >= ctx.AbilitySettings.wallClimbFacingDot && !travellingAlongWall)
        {
            ctx.ChangeState(State.WallClimb);
            return true;
        }
        return false;
    }
    /// <summary>
    /// Running at a low obstacle with W held, a jump pressed before the vault can catch it (Beatrun's comes only from a jump
    /// within about 1.2 m of the near face; an earlier one sailed over) surges instead: the run speeds up to
    /// Ability.vaultSurgeSpeed along the ground to where the vault catches the obstacle (VaultFinder.CatchesLow), with a
    /// camera push, and the vault starts right there. Up to Ability.vaultSurgeDistance short of that spot. True when the
    /// press was taken: a surge under way (the jump held back) or a vault begun. Not in Beatrun: the user asked to vault
    /// what is ahead of a running jump quickly, with speed rather than a teleport, without timing it (2026-11-06).
    /// </summary>
    internal bool SurgesToVault(float dt)
    {
        var settings = ctx.AbilitySettings;
        Vector3 run = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        bool surging = surgeLeft > 0f;
        // W starts a surge; once it is under way it runs to its vault whatever the keys do. Read every frame, W came up
        // released within a tenth of a second of most surges (Logs/body-probe.txt, 2026-11-09) and the press went as a
        // jump over the obstacle instead.
        if (!ctx.IsGrounded || ctx.CurrentState != State.Standing || (!surging && ctx.CurrentInput.y <= 0.1f) || ledgeLock > 0f
            || settings.vaultSurgeDistance <= 0f)
        {
            if (surging)
                SurgeProbe("surge ended without a vault: " + (!ctx.IsGrounded ? "left the ground" : ctx.CurrentState != State.Standing
                    ? $"state {ctx.CurrentState}" : ledgeLock > 0f ? "ledge lock" : "surge off"));
            StopSurge();
            return false;
        }
        Vector3 forward = ctx.transform.forward, feet = ctx.transform.position, way = (surging ? surge : run).normalized;
        float speed = surging ? surge.magnitude : run.magnitude;
        // Close enough: the vault starts here and now, from the ground. Left to the next frame's first look, a frame a little
        // longer or shorter could carry a body at surge speed past the rail's narrow band (about 0.65 m), and the move came
        // out a kong vault or a bare jump at 11 m/s. It goes at the pace the player was running at: entered at the surge's
        // speed, it ran and left at that speed and its clip was cut short, which looked fast-forwarded (user, 2026-11-06).
        if (way.sqrMagnitude > .5f && Detector.Vaults.CatchesLow(feet, forward, out ObstacleTarget vault)
            && !traversedObstacles.Contains(vault.Surface))
        {
            if (surging) ctx.HorizontalVelocity = way * surgeFrom;
            StopSurge();
            Begin(vault);
            return true;
        }
        if (surging)
        {
            surgeLeft -= dt;
            // The look from here missed the vault the surge was run for, though it caught it from this very spot a moment
            // ago: the look turned a little, or the obstacle stopped the capsule just short of the spot. Ending the surge
            // as a jump then sailed over what was to be vaulted (user, 2026-11-09). At the spot, or held up next to it
            // when the time is up, the vault found there goes from where the feet are; Begin's move starts from here.
            float shortOf = Vector3.Dot(surgeSpot - feet, way);
            bool arrived = shortOf <= SurgeArrive || (surgeLeft <= 0f && shortOf <= SurgeHeldUp);
            if (!arrived) return surgeLeft > 0f || SurgeMissed($"ran out {shortOf:0.00} m short of the spot");
            if (Vector3.Dot(forward, surgeFacing) < SurgeFacing) return SurgeMissed("looked away from the vault");
            var found = surgeVault;
            if (found.Surface == null || !found.Surface.enabled || !found.Surface.gameObject.activeInHierarchy
                || traversedObstacles.Contains(found.Surface)) return SurgeMissed("the obstacle is gone");
            SurgeProbe($"surge took the {found.Kind} found ahead, {shortOf:0.00} m short of the spot");
            ctx.HorizontalVelocity = way * surgeFrom;
            StopSurge();
            Begin(found);
            return true;
        }
        if (speed < SurgeLeast) return false;
        float gap = GapToVault(feet, way, forward, settings.vaultSurgeDistance, out surgeVault);
        if (gap <= 0f) return false;
        surgeSpot = feet + way * gap;
        surgeFacing = forward;
        surgeFrom = speed;
        speed = Mathf.Max(settings.vaultSurgeSpeed, speed);
        surge = way * speed;
        surgeLeft = gap / speed + SurgeSpare;
        SurgeProbe($"surge to {surgeVault.Kind} on {(surgeVault.Surface != null ? surgeVault.Surface.name : "-")}: {gap:0.00} m at {speed:0.0} m/s");
        // The push sells the burst: the view dips forward a little and widens, and the speed widens it further on its own.
        ctx.GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(
            new Vector3(settings.vaultSurgeKick.x, 0f, 0f), settings.vaultSurgeKick.y);
        return true;
    }

    // A surge that won't end in its vault: the press goes as a jump (ProcessJump), as it would have without the surge.
    private bool SurgeMissed(string why)
    {
        SurgeProbe($"surge ended without a vault: {why}");
        StopSurge();
        return false;
    }

    // Temporary (ParkourProbeLog, 2026-11-09): how each surge started and ended, until surges reliably end in their vault.
    private void SurgeProbe(string text)
    {
        if (ctx.IsLocal) ParkourProbeLog.Line(text);
    }

    // How far along the run (m) the feet go before a jump from there would be vaulted, and that vault, looking up to reach
    // beyond where the body is now, and no further than it gets before it meets something (the obstacle itself). Zero when
    // nothing is.
    private float GapToVault(Vector3 feet, Vector3 way, Vector3 forward, float reach, out ObstacleTarget vault)
    {
        vault = default;
        var controller = ctx.Motor.controller;
        float radius = controller.radius, half = Mathf.Max(0f, controller.height * .5f - radius);
        Vector3 centre = ctx.transform.TransformPoint(controller.center), lift = Vector3.up * controller.stepOffset;
        if (Physics.CapsuleCast(centre + Vector3.up * half, centre - Vector3.up * half + lift, radius - .01f, way,
                out RaycastHit blocked, reach, ctx.groundLayers, QueryTriggerInteraction.Ignore))
            reach = blocked.distance;
        for (int step = 1; step <= SurgeSamples; step++)
        {
            float along = reach * step / SurgeSamples;
            if (Detector.Vaults.CatchesLow(feet + way * along, forward, out vault) && !traversedObstacles.Contains(vault.Surface))
                return along;
        }
        vault = default;
        return 0f;
    }

    // A top this jump will clear in a moment is a vault on its way, not a wall to climb.
    private bool VaultComing()
    {
        if (ctx.VerticalVelocity <= 0f) return false;
        float rise = ctx.VerticalVelocity * ctx.VerticalVelocity / (2f * Mathf.Abs(ctx.gravity));
        return Detector.Vaults.TryLaterInJump(ctx.transform.forward, rise, out ObstacleTarget vault)
            && !traversedObstacles.Contains(vault.Surface);
    }
    internal bool TryCatchLedge(Vector3? facing = null)
    {
        if (ledgeLock > 0f || !Detector.TryLedge(facing ?? ctx.transform.forward, out Ledge)) return false;
        // As Beatrun's ClimbingCheck: holding W, a caught ledge is climbed straight over or onto when there's room, and
        // only hung from when there isn't.
        if (ctx.CurrentInput.y > 0.1f && ClimbCaughtLedge()) return true;
        ctx.ChangeState(State.LedgeHang);
        return true;
    }

    private bool ClimbCaughtLedge()
    {
        if (Ledge.Surface == null) return false;
        Vector3 edge = Ledge.Surface.transform.TransformPoint(Ledge.LocalEdge);
        if (!Detector.Vaults.TryHighBelow(edge, ctx.transform.forward, ctx.VerticalVelocity, ctx.CurrentState == State.WallClimb,
                out ObstacleTarget candidate) || traversedObstacles.Contains(candidate.Surface)) return false;
        Begin(candidate);
        return true;
    }
    internal bool MantleFromLedge()
    {
        if (!WallDetector.CanUseSurface(Ledge.Surface)) return false;
        Transform support = Ledge.Surface.transform;
        Vector3 edge = support.TransformPoint(Ledge.LocalEdge), feet = ctx.transform.position;
        Vector3 inward = Vector3.ProjectOnPlane(-support.TransformDirection(Ledge.LocalNormal), Vector3.up).normalized;
        Vector3 along = Vector3.Cross(Vector3.up, inward);
        edge += along * Vector3.Dot(feet - edge, along);
        if (!Detector.TryHeave(Ledge.Surface, edge, inward, feet, out Obstacle)) return false;
        // The hands stay where they gripped; a shimmy may have left the stored edge point behind.
        Ledge.LocalEdge = support.InverseTransformPoint(edge);
        ctx.ChangeState(State.Mantle);
        ctx.Actions.ClearJumpBuffer();
        return true;
    }
    internal bool FacingLedge()
    {
        if (Ledge.Surface == null) return false;
        Vector3 inward = -Ledge.Surface.transform.TransformDirection(Ledge.LocalNormal);
        return Vector3.Dot(Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized, inward)
            >= Mathf.Cos(ctx.AbilitySettings.ledgeJumpLookAngle * Mathf.Deg2Rad);
    }
    /// <summary>
    /// The way the current mantle climbs, flat: into the wall from a hang, else over the edge the hands rest on, else from
    /// where it began to where it ends. Zero when there is no telling.
    /// </summary>
    public Vector3 MantleFacing
    {
        get
        {
            Vector3 way = Obstacle.Kind == VaultKind.Heave && Ledge.Surface != null
                ? -Ledge.Surface.transform.TransformDirection(Ledge.LocalNormal)
                : Obstacle.EdgeNormal != Vector3.zero ? -Obstacle.EdgeNormal : Obstacle.End - Obstacle.Start;
            way = Vector3.ProjectOnPlane(way, Vector3.up);
            return way.sqrMagnitude > .0001f ? way.normalized : Vector3.zero;
        }
    }
    internal void JumpFromLedge()
    {
        if (Ledge.Surface == null) return;
        Vector3 normal = Ledge.Surface.transform.TransformDirection(Ledge.LocalNormal);
        Vector3 direction = Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized;
        // Keep a small outward separation for side jumps; never launch into the wall.
        float outward = Vector3.Dot(direction, normal);
        if (outward < 0.15f) direction = (direction + normal * (0.15f - outward)).normalized;
        LedgeJumpVelocity = direction * ctx.AbilitySettings.ledgeJumpSpeed + Vector3.up * ctx.AbilitySettings.ledgeJumpUpSpeed;
        LockLedge();
        ctx.ChangeState(State.LedgeJump);
        ctx.Abilities.InputConsumed = true;
    }
    internal bool MantleFromBar()
    {
        if (Bar == null || !Detector.TryBarMantle(Bar, Bar.transform.TransformPoint(LocalGrip), out Obstacle)) return false;
        // Up over the bar the way a ledge hang climbs up: the hands stay on the bar's top and the body heaves over them.
        // Without an edge the hands followed the full mantle clip and pressed down on the air in front (backlog 121), and a
        // stale Ledge from an earlier hang could even pull the body toward that old edge.
        Obstacle.Kind = VaultKind.Heave;
        Transform support = Obstacle.Surface.transform;
        Vector3 outward = Vector3.ProjectOnPlane(Obstacle.Approach - Obstacle.End, Vector3.up);
        outward = outward.sqrMagnitude > .0001f ? outward.normalized : -Bar.SwingDirection;
        Vector3 edge = Obstacle.End - Vector3.up * 0.1f;
        Ledge = new LedgeTarget { Surface = Obstacle.Surface, LocalEdge = support.InverseTransformPoint(edge),
            LocalNormal = support.InverseTransformDirection(outward), LocalHang = support.InverseTransformPoint(ctx.transform.position) };
        lastBar = Bar; barLock = 0.45f;
        ctx.ChangeState(State.Mantle);
        ctx.Actions.ClearJumpBuffer();
        Bar = null;
        return true;
    }
    internal void ProcessBarInput(Vector2 input)
    {
        if (!ctx.InputAvailable) return;
        if (ctx.Input.CrouchPressed) ReleaseBar(false);
        else if (ctx.Input.JumpPressed)
        {
            if (input.sqrMagnitude <= 0.01f) MantleFromBar();
            else ReleaseBar(true);
        }
    }

    // Backlog 123: hanging nearly still, the mouse pushed on past the bar's look limit turns the player round to face
    // the bar's other side, from where a jump goes that way (PlayerLook.TryMouseTurnOnBar). The view is carried the
    // half turn toward the side pushed; the body follows the view's side. The movement keys stay for pumping the swing.
    private float barTurnStarted = float.NegativeInfinity, barTurnFrom, barTurnAmount = 180f;
    internal bool BarTurning => Bar != null && Time.time - barTurnStarted < ctx.AbilitySettings.barTurnDuration;
    /// <summary>Where the view is turned to on the bar right now; only meaningful while BarTurning.</summary>
    internal float BarTurnYaw => barTurnFrom + barTurnAmount * Mathf.SmoothStep(0f, 1f,
        Mathf.Clamp01((Time.time - barTurnStarted) / ctx.AbilitySettings.barTurnDuration));
    /// <summary>Starts the turn round on the bar by this many degrees (+ right, - left); false when the swing is too big.</summary>
    internal bool TryTurnOnBar(float degrees)
    {
        if (Bar == null || ctx.CurrentState != State.BarSwing || BarTurning) return false;
        if (Mathf.Abs(Angle) > 20f * Mathf.Deg2Rad || Mathf.Abs(AngularSpeed) > 1.2f) return false;
        barTurnFrom = ctx.transform.eulerAngles.y;
        barTurnAmount = degrees;
        barTurnStarted = Time.time;
        return true;
    }

    /// <summary>Backlog 122: sideways input moves the grip along the bar, hand over hand, kept within its ends.</summary>
    internal void ShimmyOnBar(float dt)
    {
        if (Bar == null || BarTurning) return;
        Vector3 axis = Bar.transform.right;
        float along = Vector3.Dot(ctx.Abilities.InputDirection(Vector3.zero), axis) * Mathf.Min(1f, ctx.CurrentInput.magnitude);
        if (Mathf.Abs(along) < 0.1f) return;
        float pace = ctx.AbilitySettings.barShimmySpeed * Mathf.Clamp01(1f - Mathf.Abs(AngularSpeed) / 2f);
        if (pace <= 0f) return;
        Vector3 grip = Bar.transform.TransformPoint(LocalGrip);
        LocalGrip = Bar.transform.InverseTransformPoint(Bar.ClosestGrip(grip + axis * (along * pace * dt)));
    }
    internal void LockLedge() { ledgeLock = ctx.AbilitySettings.ledgeRegrabDelay; LockWall(); }
    internal void LockWall() { wallLock = ctx.AbilitySettings.wallReattachDelay; }
    internal void ReleaseBar(bool jump)
    {
        if (Bar != null)
        {
            Vector3 tangent = Bar.SwingDirection * Mathf.Cos(Angle) + Vector3.up * Mathf.Sin(Angle);
            ReleaseVelocity = tangent * (AngularSpeed * Bar.HangLength);
            if (jump)
            {
                // The jump goes the way the player pushes (backlog 123: turned round, it goes the other way). Of the
                // swing's own speed only what runs that way is kept; a swing running against it is let go of.
                Vector3 flat = Vector3.ProjectOnPlane(ReleaseVelocity, Vector3.up);
                Vector3 fallback = flat.sqrMagnitude > 0.01f ? flat : Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up);
                Vector3 forward = ctx.Abilities.InputDirection(fallback);
                ReleaseVelocity = forward * Mathf.Max(0f, Vector3.Dot(flat, forward)) + Vector3.up * ReleaseVelocity.y
                    + forward * ctx.AbilitySettings.lacheForwardBoost + Vector3.up * ctx.AbilitySettings.lacheUpBoost;
            }
            lastBar = Bar; barLock = 0.45f;
        }
        else ReleaseVelocity = ctx.Velocity;
        ctx.ChangeState(State.LacheRelease);
    }
    internal void Reset()
    {
        ForgetWallClimbs();
        wallClimbEnds = float.NegativeInfinity;
        sinceJump = float.PositiveInfinity;
        wasGrounded = false;
        StopSurge();
        wallLock = barLock = ledgeLock = 0f; Bar = lastBar = null; Obstacle = default; Ledge = default; Wall = default;
        Angle = AngularSpeed = 0f; ReleaseVelocity = LedgeJumpVelocity = Vector3.zero;
        traversedObstacles.Clear();
    }
}
