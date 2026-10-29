using UnityEngine;

/// <summary>The supplied vmanip_vault route and handoff, in metres, paced by TraversalTiming.</summary>
public sealed class ReferenceTraversal
{
    private readonly PlayerMovement ctx;
    private readonly ObstacleTarget target;
    private readonly ParkourDetector detector;
    private readonly float duration;
    private Vector3 lastClearPosition;
    private float progress, elapsed;
    private bool finishing;
    private float finishTime;
    private const float FinishDuration = 0.12f;
    private Vector3 finishStart, finishVelocity, lastVelocity;
    public bool Complete { get; private set; }
    public float TimeShare => Mathf.Clamp01(elapsed / duration);

    internal ReferenceTraversal(PlayerMovement context, ObstacleTarget obstacle, float seconds)
    {
        ctx = context; target = obstacle; detector = ctx.Abilities.Traversal.Detector;
        lastClearPosition = obstacle.Start;
        duration = Mathf.Max(0.05f, seconds);
    }

    internal bool Advance(float dt)
    {
        if (target.Surface == null || !target.Surface.enabled || !target.Surface.gameObject.activeInHierarchy) return false;
        float nextElapsed = elapsed + Mathf.Max(0f, dt);
        float next = TraversalTiming.Progress(nextElapsed / duration, target.IsVault);
        Vector3 position = Vector3.Lerp(target.Start, target.End, next);
        bool finish = progress >= TraversalTiming.EndProgress(target.IsVault)
            || (position - target.End).sqrMagnitude < TraversalTiming.EndDistanceSquared;
        if (!finishing && finish && !detector.ClearReferenceExit(position))
        {
            finishing = true; finishTime = 0f;
            finishStart = ctx.transform.position; finishVelocity = lastVelocity;
        }
        if (finishing)
        {
            // The reference snaps an embedded hull to End. Blend that last gap
            // with continuous position/velocity instead of teleporting the view.
            finishTime = Mathf.Min(FinishDuration, finishTime + Mathf.Max(0f, dt));
            float t = finishTime / FinishDuration, t2 = t * t, t3 = t2 * t;
            Vector3 exitVelocity = target.IsVault
                ? Vector3.ProjectOnPlane(target.End - target.Start, Vector3.up).normalized * 5f : Vector3.zero;
            position = finishStart + (-2f * t3 + 3f * t2) * (target.End - finishStart)
                + (t3 - 2f * t2 + t) * FinishDuration * finishVelocity
                + (t3 - t2) * FinishDuration * exitVelocity;
            finish = finishTime >= FinishDuration;
        }
        if (!detector.ClearReferencePath(ctx.transform.position, position, target.Surface)
            || (finish && !detector.ClearReferenceExit(position))) return false;
        progress = next; elapsed = nextElapsed; Complete = finish;
        if (detector.ClearReferenceExit(position)) lastClearPosition = position;
        Vector3 velocity = (position - ctx.transform.position) / Mathf.Max(dt, 0.001f);
        lastVelocity = velocity;
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up); ctx.VerticalVelocity = velocity.y;
        ctx.Motor.RequestTraversalPosition(position);
        return true;
    }

    internal void Exit()
    {
        // Ctrl, reset, death or a new obstruction may interrupt inside the target.
        // Restore the last free hull before ordinary collision movement resumes.
        if (!detector.ClearReferenceExit(ctx.transform.position))
            ctx.Motor.RecoverTraversalPosition(lastClearPosition);
    }
}
