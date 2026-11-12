using UnityEngine;

/// <summary>
/// Persistent support: catching never implicitly commits to climbing over.
/// The catch is warped as KINEMATION Motion Warping warps a move onto its target (WarpedTraversal does it for the vaults):
/// - Reach: the capsule goes from where the ledge was caught to the height at which the catch clip's hands, on the frame
///   they touch, rest on the edge (PlayerBody.TryLedgeContact), out from the wall as it hangs, on a curve that starts at the
///   velocity the player came in with, less what ran into the wall, and comes to rest there, in as long as getting there at
///   that speed takes (Ability.ledgeCatchTimes, ledgeCatchLeastSpeed); a fall carries on a little before the hands stop it.
///   That time is ParkourTraversal.LedgeCatchTime: the body holds the catch clip on that frame, the hands reach for their
///   holds and take the body over by then, on every PC.
/// - Settle: the body hanging from the hands, the capsule goes on to its hanging place, where shimmying and the climb up
///   start from, in the shortest catch time; nothing of it shows.
/// Before, the speed was cut to nothing at once and the capsule lerped to the hang in 0.14 s, while the body, the hands and
/// the clip each snapped in over 0.15 s (user, 2026-11-13: the catch popped). Jumps, climbs and shimmies wait for the hands.
/// </summary>
public sealed class LedgeHangState : MovementBaseState
{
    private bool forwardWasHeld, requestMantle;
    private bool requestJump;
    // The warp: where it went from and its velocity there (m/s), from when (s into the hang), to where the hands touch, and
    // when they do.
    private Vector3 catchStart, catchVelocity, contact;
    private float catchFrom, catchTime;
    // contact in the space of the ledge it was caught on, so a ledge that moves carries it; the hang may shimmy on to
    // another collider meanwhile.
    private Collider contactSurface;
    private Vector3 localContact;
    public LedgeHangState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }

    // The hands are on the edge: jumps, climbs and shimmies wait for it.
    private bool Caught => ctx.CurrentStateDuration >= Mathf.Max(ctx.AbilitySettings.ledgeCatchDuration, catchTime);

    public override void Enter()
    {
        var traversal = ctx.Abilities.Traversal;
        var settings = ctx.AbilitySettings;
        catchStart = ctx.transform.position;
        catchFrom = 0f;
        Vector3 velocity = ctx.HorizontalVelocity + Vector3.up * ctx.VerticalVelocity;
        catchVelocity = Vector3.zero;
        catchTime = 0f;
        contact = catchStart;
        contactSurface = null;
        if (WallDetector.CanUseSurface(traversal.Ledge.Surface))
        {
            Transform support = traversal.Ledge.Surface.transform;
            Vector3 hang = support.TransformPoint(traversal.Ledge.LocalHang);
            Vector3 outward = Vector3.ProjectOnPlane(support.TransformDirection(traversal.Ledge.LocalNormal), Vector3.up).normalized;
            // What ran into the wall stops there; along it, out from it, up or down carries on into the catch.
            float into = -Vector3.Dot(velocity, outward);
            if (into > 0f) velocity += outward * into;
            catchVelocity = velocity;
            contact = Contact(hang);
            contactSurface = traversal.Ledge.Surface;
            localContact = support.InverseTransformPoint(contact);
            float speed = Mathf.Max(velocity.magnitude, settings.ledgeCatchLeastSpeed);
            Vector2 times = settings.ledgeCatchTimes;
            catchTime = Mathf.Clamp(Vector3.Distance(catchStart, contact) / speed, Mathf.Min(times.x, times.y), Mathf.Max(times.x, times.y));
        }
        traversal.LedgeCatchTime = catchTime;
        forwardWasHeld = ctx.CurrentInput.y > 0.1f;
        requestMantle = requestJump = false;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Actions.ClearJumpBuffer();
        ctx.Abilities.Landing.SupportedByTraversal();
        ctx.Abilities.Traversal.ForgetWallClimbs();
    }

    // Where the capsule is when the hands touch: at the height the body measures (PlayerBody.TryLedgeContact), out from the
    // wall as it hangs, since the capsule can't go into the wall wherever the clip has its hands, and the body is held out
    // from it by the hands anyway (PlayerBody.Grip). Not when that is no place for the capsule or far from the hanging
    // place (a clip whose hands sit oddly can't send it off): then the hanging place itself.
    private Vector3 Contact(Vector3 hang)
    {
        var body = ctx.ObstacleMotions;
        if (body == null || !body.TryLedgeContact(out Vector3 feet)) return hang;
        Vector3 touch = hang + Vector3.up * (feet.y - hang.y);
        if (Mathf.Abs(feet.y - hang.y) > MostContactShift || !ctx.Abilities.Traversal.Detector.ClearPath(hang, touch, ctx.StandingColliderHeight))
            return hang;
        return touch;
    }

    // How far above or below its hanging place the capsule may be when the hands touch (m): about an arm's length.
    private const float MostContactShift = .8f;

    public override void ProcessInput(Vector2 input)
    {
        if (!ctx.InputAvailable || ctx.Input.CrouchPressed) { Drop(); return; }
        bool forward = input.y > 0.1f;
        bool facing = ctx.Abilities.Traversal.FacingLedge();
        // Looking away prepares a directional jump, never a delayed mantle.
        if (facing) requestJump = false;
        else requestMantle = false;
        if (ctx.Input.JumpPressed && !facing) { requestJump = true; requestMantle = false; }
        else requestMantle |= facing && (ctx.Input.JumpPressed || (forward && !forwardWasHeld));
        forwardWasHeld = forward;
    }
    public override void UpdateLogic(float dt)
    {
        var ledge = ctx.Abilities.Traversal.Ledge;
        if (!WallDetector.CanUseSurface(ledge.Surface)) { Drop(); return; }
        if (requestJump && Caught)
        { ctx.Abilities.Traversal.JumpFromLedge(); return; }
        if (requestMantle && Caught)
        {
            requestMantle = false;
            ctx.Abilities.Traversal.MantleFromLedge();
        }
    }
    public override void UpdatePhysics(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        if (!WallDetector.CanUseSurface(traversal.Ledge.Surface)) { Drop(); return; }
        if (Caught && traversal.FacingLedge() && Mathf.Abs(ctx.CurrentInput.x) > 0.1f)
        {
            Vector3 normal = traversal.Ledge.Surface.transform.TransformDirection(traversal.Ledge.LocalNormal);
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            float remaining = ctx.CurrentInput.x * ctx.AbilitySettings.ledgeShimmySpeed * dt;
            // Small probes stop at missing edges rather than crossing a gap in one frame.
            int slices = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(remaining) / 0.05f));
            for (int i = 0; i < slices; i++)
            {
                if (!traversal.Detector.TryShimmy(traversal.Ledge, right * (remaining / slices), out LedgeTarget nextLedge)) break;
                traversal.Ledge = nextLedge;
            }
        }
        Vector3 hang = traversal.Ledge.Surface.transform.TransformPoint(traversal.Ledge.LocalHang);
        float time = ctx.CurrentStateDuration + dt;
        Vector3 next = Warp(hang, time);
        if (!traversal.Detector.ClearPath(ctx.transform.position, next, ctx.StandingColliderHeight))
        {
            // The catch's swing met something (a sill below, a wall beside): from here straight on, the hands touching when
            // they were to, so the body's catch keeps its time.
            if (time >= catchTime || (catchVelocity == Vector3.zero && catchFrom > 0f)) { Drop(); return; }
            catchStart = ctx.transform.position;
            catchVelocity = Vector3.zero;
            catchFrom = ctx.CurrentStateDuration;
            next = Warp(hang, time);
            if (!traversal.Detector.ClearPath(ctx.transform.position, next, ctx.StandingColliderHeight)) { Drop(); return; }
        }
        ctx.Motor.RequestDisplacement(next - ctx.transform.position);
    }

    // Where the capsule is this far into the hang (s). Reaching, on a cubic from where the warp went from at its velocity to
    // where the hands touch, at rest there; settling, eased on from there to the hanging place; then the hanging place.
    private Vector3 Warp(Vector3 hang, float time)
    {
        Vector3 touch = contactSurface != null ? contactSurface.transform.TransformPoint(localContact) : contact;
        if (time < catchTime)
        {
            float span = catchTime - catchFrom;
            if (span <= 1e-4f) return touch;
            float s = Mathf.Clamp01((time - catchFrom) / span), s2 = s * s, s3 = s2 * s;
            // Hermite: the start, its tangent (the velocity over the span) and the end; the end's tangent is zero.
            return catchStart * (2f * s3 - 3f * s2 + 1f) + catchVelocity * (span * (s3 - 2f * s2 + s)) + touch * (3f * s2 - 2f * s3);
        }
        Vector2 times = ctx.AbilitySettings.ledgeCatchTimes;
        float settle = Mathf.Min(times.x, times.y);
        if (catchTime <= 0f || settle <= 0f || time >= catchTime + settle) return hang;
        return Vector3.Lerp(touch, hang, Mathf.SmoothStep(0f, 1f, (time - catchTime) / settle));
    }

    private void Drop()
    {
        ctx.Abilities.Traversal.LockLedge();
        ctx.Abilities.ReturnToLocomotion();
        ctx.VerticalVelocity = -2f;
    }
}

public sealed class LedgeJumpState : MovementBaseState
{
    public LedgeJumpState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override bool CanApplyGravity => true;
    public override void Enter()
    {
        Vector3 velocity = ctx.Abilities.Traversal.LedgeJumpVelocity;
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
        ctx.Actions.LeaveGround(); ctx.Actions.ClearJumpBuffer();
    }
    public override void UpdateLogic(float dt)
    { if (ctx.CurrentStateDuration > 0f) ctx.Abilities.ReturnToLocomotion(); }
}
