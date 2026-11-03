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
    private float wallBudget;
    private float wallLock, barLock, ledgeLock;
    private SwingBar lastBar;
    // Turning around must not refresh the same obstacle's lift while airborne.
    // Keep distinct obstacles available for legitimate parkour chains.
    private readonly HashSet<Collider> traversedObstacles = new HashSet<Collider>();
    internal ParkourTraversal(PlayerMovement context)
    { ctx = context; Detector = new ParkourDetector(ctx); wallBudget = ctx.AbilitySettings.wallDuration; }
    internal void Tick(float dt)
    { wallLock = Mathf.Max(0f, wallLock - dt); barLock = Mathf.Max(0f, barLock - dt); ledgeLock = Mathf.Max(0f, ledgeLock - dt); }
    internal void OnGrounded()
    {
        wallBudget = ctx.AbilitySettings.wallDuration;
        if (!ctx.IsParkour) traversedObstacles.Clear();
    }
    internal void RecordObstacle() { if (Obstacle.Surface != null) traversedObstacles.Add(Obstacle.Surface); }
    internal bool SpendWallTime(float dt) { wallBudget -= dt; return wallBudget > 0f; }
    internal bool CanGrab(SwingBar candidate) => barLock <= 0f || candidate != lastBar;

    // Airborne, or at a wall with high = true. The move looks where the player looks, as Beatrun's do.
    internal bool TryVault(bool high)
    {
        Vector3 forward = ctx.transform.forward;
        ObstacleTarget candidate;
        bool found = high ? Detector.Vaults.TryHigh(forward, ctx.VerticalVelocity, out candidate)
            : Detector.Vaults.TryAir(forward, out candidate);
        if (!found || traversedObstacles.Contains(candidate.Surface) || WaitsForReload(candidate)) return false;
        Begin(candidate);
        return true;
    }

    // A reload has both hands on the gun: a vault or a climb that puts a hand on the obstacle waits for it to finish. A step
    // up needs no hand, so a reload doesn't leave the player stuck at a low ledge.
    private bool WaitsForReload(in ObstacleTarget candidate) => candidate.Kind != VaultKind.StepUp && ctx.HandsOnReload;

    private void Begin(in ObstacleTarget candidate)
    {
        Obstacle = candidate;
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
            AngularSpeed = Mathf.Clamp(Vector3.Dot(ctx.HorizontalVelocity, bar.SwingDirection) / bar.HangLength,
                -ctx.AbilitySettings.swingMaxAngularSpeed, ctx.AbilitySettings.swingMaxAngularSpeed);
            ctx.ChangeState(State.BarGrab);
            return true;
        }
        if (ctx.CurrentInput.y > 0.1f && ctx.VerticalVelocity >= -ctx.AbilitySettings.airTraversalMaxFallSpeed
            && ledgeLock <= 0f && (TryVault(false) || TryVault(true))) return true;
        if (ctx.VerticalVelocity <= 0f && TryCatchLedge()) return true;
        if (wallBudget <= 0f || wallLock > 0f || ctx.CurrentInput.y <= 0.1f || !ctx.Input.JumpHeld) return false;
        if (!Detector.TryWallEntry(out Wall) || VaultComing()) return false;
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
        if (!Detector.Vaults.TryHighBelow(edge, ctx.transform.forward, ctx.VerticalVelocity, out ObstacleTarget candidate)
            || traversedObstacles.Contains(candidate.Surface) || WaitsForReload(candidate)) return false;
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
                Vector3 forward = Vector3.ProjectOnPlane(ReleaseVelocity, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = ctx.transform.forward;
                ReleaseVelocity += forward * ctx.AbilitySettings.lacheForwardBoost + Vector3.up * ctx.AbilitySettings.lacheUpBoost;
            }
            lastBar = Bar; barLock = 0.45f;
        }
        else ReleaseVelocity = ctx.Velocity;
        ctx.ChangeState(State.LacheRelease);
    }
    internal void Reset()
    {
        wallBudget = ctx.AbilitySettings.wallDuration;
        wallLock = barLock = ledgeLock = 0f; Bar = lastBar = null; Obstacle = default; Ledge = default; Wall = default;
        Angle = AngularSpeed = 0f; ReleaseVelocity = LedgeJumpVelocity = Vector3.zero;
        traversedObstacles.Clear();
    }
}
