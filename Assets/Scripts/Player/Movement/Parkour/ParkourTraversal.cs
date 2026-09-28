using UnityEngine;
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
    private float wallBudget;
    private float wallLock, barLock, ledgeLock;
    private SwingBar lastBar;
    internal ParkourTraversal(PlayerMovement context)
    { ctx = context; Detector = new ParkourDetector(ctx); wallBudget = ctx.AbilitySettings.wallDuration; }
    internal void Tick(float dt)
    { wallLock = Mathf.Max(0f, wallLock - dt); barLock = Mathf.Max(0f, barLock - dt); ledgeLock = Mathf.Max(0f, ledgeLock - dt); }
    internal void OnGrounded() { wallBudget = ctx.AbilitySettings.wallDuration; }
    internal bool SpendWallTime(float dt) { wallBudget -= dt; return wallBudget > 0f; }
    internal bool CanGrab(SwingBar candidate) => barLock <= 0f || candidate != lastBar;

    internal bool TryObstacle(bool airborne = false)
    {
        if (ctx.CurrentInput.sqrMagnitude < 0.01f) return false;
        if (!Detector.TryObstacle(ctx.Abilities.InputDirection(ctx.transform.forward), out Obstacle, airborne)) return false;
        ctx.ChangeState(Obstacle.IsVault ? State.Vault : State.Mantle);
        ctx.Actions.ClearJumpBuffer();
        return true;
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
            && ledgeLock <= 0f && TryObstacle(true)) return true;
        if (ctx.VerticalVelocity <= 0f && TryCatchLedge()) return true;
        if (wallBudget <= 0f || wallLock > 0f || ctx.CurrentInput.y <= 0.1f || !ctx.Input.JumpHeld) return false;
        if (!Detector.TryWallEntry(out Wall)) return false;
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
    internal bool TryCatchLedge(Vector3? facing = null)
    {
        if (ledgeLock > 0f || !Detector.TryLedge(facing ?? ctx.transform.forward, out Ledge)) return false;
        ctx.ChangeState(State.LedgeHang);
        return true;
    }
    internal bool MantleFromLedge()
    {
        if (Ledge.Surface == null) return false;
        Transform support = Ledge.Surface.transform;
        if (!Detector.TryMantle(Ledge.Surface, support.TransformPoint(Ledge.LocalEdge),
            -support.TransformDirection(Ledge.LocalNormal), ctx.transform.position, out Obstacle)) return false;
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
    }
}
