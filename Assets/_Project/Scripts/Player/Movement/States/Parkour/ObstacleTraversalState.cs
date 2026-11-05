using UnityEngine;
using System.Collections.Generic;

public sealed class ObstacleTraversalState : MovementBaseState
{
    private readonly bool vault;
    private float blockedTime;
    private float progress, elapsed, duration;
    private bool finished;
    private Vector3 finishPosition;
    private TraversalPath path;
    private Vector3 entryVelocity;
    private StraightLineTraversal straightLine;
    public bool MovesInStraightLine => straightLine != null;
    public bool IsVault => vault;
    public int EntrySerial { get; private set; }
    public float EntrySpeed => entryVelocity.magnitude;
    private readonly List<Vector3> framePath = new List<Vector3>();
    // A vault ends in the air, chest high beyond the obstacle (Beatrun's vaultpos). It stays a vault until the feet are
    // down: handed to the fall at once, the move ended abruptly in mid-air, which the user found rushed and choppy
    // (2026-11-06). Ground further below than LandingReach (units) is left to the fall, with its landing and roll, as
    // before; LandingTime (s) bounds the wait.
    private bool landing;
    private float landingLeft;
    private const float LandingReach = 60f, LandingTime = .6f;
    public ObstacleTraversalState(PlayerMovement ctx, MovementBaseState parent, bool vault) : base(ctx, parent) { this.vault = vault; }
    public override string Name => vault ? "Vault" : "Mantle";
    public override MovementPosture Posture => MovesInStraightLine ? MovementPosture.Standing : MovementPosture.Crouching;
    public override bool CanApplyGravity => landing || base.CanApplyGravity;
    /// <summary>
    /// Coming down after the vault. Still a move to the motor, which leaves its ground alone, so the capsule's edge on the
    /// obstacle behind can't pass for a landing; the feet find the floor (FeetDown). MovementLanding measures the fall, so
    /// the landing is heard and felt as any other.
    /// </summary>
    public bool IsLanding => landing;
    public override void Enter()
    {
        blockedTime = progress = elapsed = 0f; finished = landing = false;
        entryVelocity = ctx.HorizontalVelocity;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Traversal.Obstacle.Start = ctx.transform.position;
        var target = ctx.Abilities.Traversal.Obstacle;
        ctx.Abilities.Traversal.RecordObstacle();
        ctx.Abilities.Traversal.ObstacleProgress = 0f;
        straightLine = target.StraightLine ? new StraightLineTraversal(ctx, target, entryVelocity.magnitude) : null;
        path = straightLine != null ? null : new TraversalPath(target, ctx.Abilities.Traversal.Detector, ctx.CrouchColliderHeight);
        duration = Mathf.Max(0.05f, TraversalTiming.Duration(ctx.AbilitySettings, target));
        EntrySerial++;
    }
    public override void ProcessInput(Vector2 input)
    {
        if (!ctx.InputAvailable || !ctx.Input.CrouchPressed) return;
        // Coming down after the vault, Ctrl hands over to the fall as it is, for a landing slide or roll.
        if (landing) ctx.Abilities.ReturnToLocomotion();
        else Abort();
    }
    public override void UpdateLogic(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        var target = traversal.Obstacle;
        if (landing)
        {
            landingLeft -= dt;
            if (!FeetDown() && landingLeft > 0f) return;
            ctx.Abilities.ReturnToLocomotion();
            return;
        }
        if (target.Surface == null) { Abort(); return; }
        if (straightLine != null)
        {
            if (!straightLine.Complete) return;
            ctx.Abilities.Landing.SupportedByTraversal();
            if (vault && !SpringsOff(target.Kind) && LandsWithin(LandingReach * ctx.HeightUnit))
            {
                Leave(target.Kind);
                landing = true;
                landingLeft = LandingTime;
                return;
            }
            ctx.Abilities.ReturnToLocomotion();
            Leave(target.Kind);
            return;
        }
        if (finished)
        {
            if (Vector3.Distance(ctx.transform.position, finishPosition) > 0.15f) { Abort(); return; }
            ctx.Abilities.Landing.SupportedByTraversal();
            ctx.Abilities.ReturnToLocomotion();
            ctx.HorizontalVelocity = entryVelocity;
            ctx.VerticalVelocity = -2f;
        }
    }
    // Forward is the way the player looks now, so looking round in a vault steers what comes after it.
    private void Leave(VaultKind kind)
    {
        float unit = ctx.HeightUnit, entry = entryVelocity.magnitude / unit;
        Vector3 forward = ctx.transform.forward;
        ctx.HorizontalVelocity = forward * (VaultMotion.ExitSpeed(kind, entry) * unit);
        ctx.VerticalVelocity = 0f;
        if (!ctx.Input.JumpHeld || !VaultMotion.CanSpring(kind)) return;
        ctx.Actions.Jump(VaultMotion.SpringHeight * unit);
        ctx.HorizontalVelocity = forward * (VaultMotion.SpringSpeed(entry) * unit);
    }
    // Holding jump as the move ends springs off it (Leave), a jump of its own that the fall carries.
    private bool SpringsOff(VaultKind kind) => ctx.Input.JumpHeld && VaultMotion.CanSpring(kind);
    // Ground under the feet, further down than a step and within reach: the vault comes down onto it before handing over.
    private bool LandsWithin(float reach)
        => Physics.Raycast(ctx.transform.position + Vector3.up * .05f, Vector3.down, out RaycastHit ground, reach + .05f,
               ctx.groundLayers, QueryTriggerInteraction.Ignore) && ground.distance > .1f;
    // Falling, with walkable ground right under the middle of the feet: an edge the capsule's side touches doesn't count.
    private bool FeetDown()
        => ctx.VerticalVelocity <= 0f && Physics.Raycast(ctx.transform.position + Vector3.up * .05f, Vector3.down,
               out RaycastHit ground, .05f + Mathf.Max(.06f, ctx.Motor.controller.skinWidth * 2f), ctx.groundLayers,
               QueryTriggerInteraction.Ignore) && ground.normal.y >= Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad);
    public override void UpdatePhysics(float dt)
    {
        if (straightLine != null)
        {
            // Coming down: gravity and the motor carry the exit speed Leave set, with collisions, to the ground.
            if (landing) return;
            if (!straightLine.Advance(dt)) Abort();
            else ctx.Abilities.Traversal.ObstacleProgress = straightLine.Progress;
            return;
        }
        float nextElapsed = elapsed + dt;
        float to = TraversalTiming.Progress(nextElapsed / duration);
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        float from = progress;
        var target = ctx.Abilities.Traversal.Obstacle;
        Vector3 candidate = path.Evaluate(to);
        // Original exit conditions. Keep checking the body before handing control
        // back; unlike MOVETYPE_NOCLIP, this controller must not end inside a wall.
        bool wantsExit = progress >= TraversalTiming.EndProgress
            || (candidate - target.End).sqrMagnitude < TraversalTiming.EndDistanceSquared;
        bool safeExit = Mathf.Abs(candidate.y - target.End.y) < 0.08f
            && ctx.Abilities.Traversal.Detector.ClearCapsule(candidate, ctx.StandingColliderHeight)
            && ctx.Abilities.Traversal.Detector.HasTread(candidate, target.Surface);
        bool complete = wantsExit && safeExit;
        if (to > 0.995f) { to = 1f; complete = true; }
        int slices = Mathf.Max(1, Mathf.CeilToInt((to - from) * path.Length / 0.06f));
        framePath.Clear();
        Vector3 destination = ctx.transform.position;
        for (int i = 1; i <= slices; i++)
        {
            Vector3 next = path.Evaluate(Mathf.Lerp(from, to, i / (float)slices));
            if (!ctx.Abilities.Traversal.Detector.ClearPath(destination, next, ctx.CrouchColliderHeight))
            { blockedTime += dt; if (blockedTime > 0.08f) Abort(); return; }
            framePath.Add(next); destination = next;
        }
        blockedTime = 0f;
        progress = to;
        elapsed = nextElapsed;
        ctx.Abilities.Traversal.ObstacleProgress = Mathf.Clamp01(elapsed / duration);
        finished = complete;
        finishPosition = destination;
        Vector3 velocity = (destination - ctx.transform.position) / Mathf.Max(dt, 0.001f);
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
        ctx.Motor.RequestPath(framePath);
    }
    private void Abort()
    {
        // Held W must not immediately turn a blocked vault into a wall action
        // or another automatic grab before the body has recovered.
        if (straightLine != null) ctx.Abilities.Traversal.LockLedge();
        ctx.Abilities.ReturnToLocomotion();
        ctx.HorizontalVelocity = entryVelocity;
        ctx.VerticalVelocity = Mathf.Min(ctx.VerticalVelocity, 0f);
    }
    public override void Exit() { straightLine?.Exit(); }
}
