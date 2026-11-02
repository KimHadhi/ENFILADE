using UnityEngine;

/// <summary>Carries the player in a straight line from Start to End at Beatrun's pace (VaultMotion), through the obstacle only.</summary>
public sealed class ReferenceTraversal
{
    private readonly PlayerMovement ctx;
    private readonly ObstacleTarget target;
    private readonly ParkourDetector detector;
    private readonly float entryUnits;
    private Vector3 lastClearPosition;
    private float lerp, clock;
    private bool finishing;
    private float finishTime;
    private const float FinishDuration = 0.12f;
    private Vector3 finishStart, finishVelocity, lastVelocity;
    public bool Complete { get; private set; }
    public float Progress => lerp;

    internal ReferenceTraversal(PlayerMovement context, ObstacleTarget obstacle, float entrySpeed)
    {
        ctx = context; target = obstacle; detector = ctx.Abilities.Traversal.Detector;
        lastClearPosition = obstacle.Start;
        entryUnits = entrySpeed / ctx.HullUnit;
    }

    internal bool Advance(float dt)
    {
        if (target.Surface == null || !target.Surface.enabled || !target.Surface.gameObject.activeInHierarchy) return false;
        float next = VaultMotion.Step(target.Kind, lerp, Mathf.Max(0f, dt), entryUnits, ctx.AbilitySettings);
        clock += Mathf.Max(0f, dt);
        // The high moves start at the wall, whatever the jump left of the distance to it.
        Vector3 from = target.HasApproach ? Vector3.Lerp(target.Start, target.Approach, clock / VaultMotion.ApproachTime) : target.Start;
        Vector3 position = Vector3.Lerp(from, target.End, next);
        bool clear = detector.ClearReferenceExit(position);
        // Beatrun leaves when its hull touches nothing, which its width makes true only past the obstacle. A narrower body
        // can touch nothing just before reaching it, so the rest of the way must be open too, for a body of any size.
        bool past = clear && VaultMotion.MayLeave(target.Kind, next)
            && detector.ClearPath(position, target.End, ctx.StandingColliderHeight);
        bool finish = VaultMotion.Arrived(target.Kind, next) || past;
        if (!finishing && finish && !clear)
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
            Vector3 exitVelocity = Vector3.ProjectOnPlane(target.End - target.Start, Vector3.up).normalized
                * (VaultMotion.ExitSpeed(target.Kind, entryUnits) * ctx.HullUnit);
            position = finishStart + (-2f * t3 + 3f * t2) * (target.End - finishStart)
                + (t3 - 2f * t2 + t) * FinishDuration * finishVelocity
                + (t3 - t2) * FinishDuration * exitVelocity;
            finish = finishTime >= FinishDuration;
        }
        if (!detector.ClearReferencePath(ctx.transform.position, position, target.Surface)
            || (finish && !detector.ClearReferenceExit(position))) return false;
        lerp = next; Complete = finish;
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
