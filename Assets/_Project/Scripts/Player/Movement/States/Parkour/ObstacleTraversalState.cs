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
    private ReferenceTraversal reference;
    public bool UsesReferenceMotion => reference != null;
    public bool IsVault => vault;
    public int EntrySerial { get; private set; }
    public float EntrySpeed => entryVelocity.magnitude;
    private readonly List<Vector3> framePath = new List<Vector3>();
    public ObstacleTraversalState(PlayerMovement ctx, MovementBaseState parent, bool vault) : base(ctx, parent) { this.vault = vault; }
    public override string Name => vault ? "Vault" : "Mantle";
    public override MovementPosture Posture => UsesReferenceMotion ? MovementPosture.Standing : MovementPosture.Crouching;
    public override void Enter()
    {
        blockedTime = progress = elapsed = 0f; finished = false;
        entryVelocity = ctx.HorizontalVelocity;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Traversal.Obstacle.Start = ctx.transform.position;
        var target = ctx.Abilities.Traversal.Obstacle;
        ctx.Abilities.Traversal.RecordObstacle();
        ctx.Abilities.Traversal.ObstacleProgress = 0f;
        path = target.ReferenceMotion ? null : new TraversalPath(target, ctx.Abilities.Traversal.Detector, ctx.CrouchColliderHeight);
        float route = path != null ? path.Length : Vector3.Distance(target.Start, target.End);
        duration = Mathf.Max(0.05f, TraversalTiming.Duration(ctx.AbilitySettings, target, route, entryVelocity.magnitude, ctx.WalkSpeed));
        reference = target.ReferenceMotion ? new ReferenceTraversal(ctx, target, duration) : null;
        EntrySerial++;
    }
    public override void ProcessInput(Vector2 input)
    {
        if (ctx.InputAvailable && ctx.Input.CrouchPressed) Abort();
    }
    public override void UpdateLogic(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        var target = traversal.Obstacle;
        if (target.Surface == null) { Abort(); return; }
        if (reference != null)
        {
            if (!reference.Complete) return;
            ctx.Abilities.Landing.SupportedByTraversal();
            ctx.Abilities.ReturnToLocomotion();
            // Looking back is allowed; it must not reverse the traversal's exit impulse.
            ctx.HorizontalVelocity = vault ? Vector3.ProjectOnPlane(target.End - target.Start, Vector3.up).normalized * 5f : Vector3.zero;
            ctx.VerticalVelocity = 0f;
            return;
        }
        Vector3 across = Vector3.ProjectOnPlane(target.End - target.Start, Vector3.up);
        if (finished)
        {
            if (Vector3.Distance(ctx.transform.position, finishPosition) > 0.15f) { Abort(); return; }
            ctx.Abilities.Landing.SupportedByTraversal();
            ctx.Abilities.ReturnToLocomotion();
            Vector3 forward = across.normalized;
            ctx.HorizontalVelocity = vault ? forward * Mathf.Max(entryVelocity.magnitude, ctx.WalkSpeed) : entryVelocity;
            ctx.VerticalVelocity = vault ? 0f : -2f;
        }
    }
    public override void UpdatePhysics(float dt)
    {
        if (reference != null)
        {
            if (!reference.Advance(dt)) Abort();
            else ctx.Abilities.Traversal.ObstacleProgress = reference.TimeShare;
            return;
        }
        float nextElapsed = elapsed + dt;
        float to = TraversalTiming.Progress(nextElapsed / duration, vault);
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        float from = progress;
        var target = ctx.Abilities.Traversal.Obstacle;
        Vector3 candidate = path.Evaluate(to);
        // Original exit conditions. Keep checking the body before handing control
        // back; unlike MOVETYPE_NOCLIP, this controller must not end inside a wall.
        bool wantsExit = progress >= TraversalTiming.EndProgress(vault)
            || (candidate - target.End).sqrMagnitude < TraversalTiming.EndDistanceSquared;
        bool safeExit = vault
            ? ctx.Abilities.Traversal.Detector.ClearCapsule(candidate, ctx.StandingColliderHeight)
            : Mathf.Abs(candidate.y - target.End.y) < 0.08f
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
        if (reference != null) ctx.Abilities.Traversal.LockLedge();
        ctx.Abilities.ReturnToLocomotion();
        ctx.HorizontalVelocity = entryVelocity;
        ctx.VerticalVelocity = Mathf.Min(ctx.VerticalVelocity, 0f);
    }
    public override void Exit() { reference?.Exit(); }
}
