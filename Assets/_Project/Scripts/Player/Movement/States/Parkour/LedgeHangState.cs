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
        // Without the warp (Ability.ledgeCatchWarp off, to compare by /hang) the catch goes as before 0.8.318 (Warp).
        if (settings.ledgeCatchWarp && WallDetector.CanUseSurface(traversal.Ledge.Surface))
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
            catchVelocity = NoOvershoot(catchVelocity, contact - catchStart, catchTime);
        }
        stepShare = 1f;
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

    // The reach's start velocity, kept from carrying the capsule past where the hands touch and back (user, 2026-11-22: a
    // jump onto a ledge floated up a little before it hung). A cubic Hermite that ends at rest stays between its ends on
    // each axis only while its start tangent points toward the end and is at most three times the straight slope
    // (Fritsch–Carlson); a jump still rising as the hands touch had more, so the capsule, and the body with it, rose past the
    // touch and came back down. Each axis keeps what it can of the speed it came in with, never more.
    private static Vector3 NoOvershoot(Vector3 velocity, Vector3 travel, float time)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            float most = 3f * travel[axis] / Mathf.Max(1e-4f, time);
            velocity[axis] = most >= 0f ? Mathf.Clamp(velocity[axis], 0f, most) : Mathf.Clamp(velocity[axis], most, 0f);
        }
        return velocity;
    }

    // A step along the ledge under way, as Beatrun's hangstrafe (climb.lua): how far through it (0-1; 1 is none), which way
    // along the wall (-1 left, 1 right as one faces it) and how long it is (m): the whole Ability.ledgeShimmyStep, or what of
    // it the ledge still has.
    private float stepShare = 1f, stepSide, stepLength;
    // Along the ledge the capsule goes in probes no longer than this (m), so it stops at a missing edge rather than crossing
    // a gap in one go.
    private const float ProbeSlice = .05f;

    /// <summary>A step along the ledge is under way (Beatrun's hangstrafe): no jump or climb starts until it is done.</summary>
    public bool Stepping => stepShare < 1f;

    public override void ProcessInput(Vector2 input)
    {
        // Only the crouch key lets go. The hands hold on whoever has the keys: with the chat or a menu up every key
        // reads as not pressed (MovementInput), so nothing below asks for a jump, a climb or a shimmy either. It used to
        // drop here whenever the keys were not the player's (PlayerMovement.InputAvailable).
        if (ctx.Input.CrouchPressed) { Drop(); return; }
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
        // A step along the ledge finishes first, as Beatrun only jumps or heaves from the hang itself.
        if (requestJump && Caught && !Stepping)
        { ctx.Abilities.Traversal.JumpFromLedge(); return; }
        if (requestMantle && Caught && !Stepping)
        {
            requestMantle = false;
            // Asked to climb where there is no way up (a ledge to hang from only: something stands on it, or the top is
            // too low under a ceiling): the hang goes on and the player is told (a sound, and the prompt says which keys
            // do work). It used to do nothing at all, which read as the game being stuck.
            if (!ctx.Abilities.Traversal.MantleFromLedge()) ctx.Abilities.Traversal.RefuseLedgeClimb();
        }
    }
    public override void UpdatePhysics(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        if (!WallDetector.CanUseSurface(traversal.Ledge.Surface)) { Drop(); return; }
        if (Caught) Step(dt);
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

    // Along the ledge a step at a time, as Beatrun's hangstrafe: A or D, with the hands on and the view on the wall, starts a
    // step that goes Ability.ledgeShimmyStep along in Ability.ledgeShimmyStepTime whatever the keys do meanwhile, and a key
    // still held as it ends starts the next at once, so the steps follow on with no stop between. The body plays a step's
    // clip from each start (PlayerBody.LedgePose). Before, the capsule slid along at the keys' speed under a clip looped to
    // that pace, and the clip was Mixamo's (user, 2026-11-22: moving on a ledge didn't use Beatrun's animation).
    // A tick is far shorter than a step, so it holds at most the rest of one step and the start of the next: two parts, a
    // fixed count. 0.9.301 looped while time was left, and a sliver of it too small to move a share near 1 by one float
    // step never ran out: the game froze holding A or D on a ledge (user, 2026-11-22: had to end it from Task Manager).
    private void Step(float dt)
    {
        float left = dt / Mathf.Max(.01f, ctx.AbilitySettings.ledgeShimmyStepTime);
        for (int part = 0; part < 2 && left > 0f; part++)
        {
            if (stepShare >= 1f && !StartStep()) return;
            float rest = 1f - stepShare;
            bool ends = left >= rest;
            float share = ends ? rest : left;
            left = ends ? left - rest : 0f;
            // The ledge stopped short of what was measured as the step began (it moved, or something came into the way).
            if (!Along(stepSide * stepLength * share)) { stepShare = 1f; return; }
            stepShare = ends ? 1f : stepShare + share;
        }
    }

    // A step starts the way the keys say, as far as the ledge goes within a step's length (Beatrun looks a step ahead before
    // it steps; here a ledge shorter than a step is still reached to its end).
    private bool StartStep()
    {
        float input = ctx.CurrentInput.x;
        if (Mathf.Abs(input) <= .1f || !ctx.Abilities.Traversal.FacingLedge()) return false;
        float side = Mathf.Sign(input), reach = Reach(side, ctx.AbilitySettings.ledgeShimmyStep);
        if (reach < ProbeSlice) return false;
        stepSide = side;
        stepLength = reach;
        stepShare = 0f;
        return true;
    }

    // How far along the ledge this way it goes on, up to this length (m), probed as the capsule would move.
    private float Reach(float side, float length)
    {
        var traversal = ctx.Abilities.Traversal;
        LedgeTarget ledge = traversal.Ledge;
        Vector3 right = Right(ledge);
        int slices = Mathf.Max(1, Mathf.CeilToInt(length / ProbeSlice));
        float slice = length / slices, gone = 0f;
        for (int i = 0; i < slices; i++)
        {
            if (!traversal.Detector.TryShimmy(ledge, right * (side * slice), out LedgeTarget next)) break;
            ledge = next;
            gone += slice;
        }
        return gone;
    }

    // Moves the hang this far along the ledge (m, right positive), in probes; false where the ledge ends before it.
    private bool Along(float distance)
    {
        var traversal = ctx.Abilities.Traversal;
        Vector3 right = Right(traversal.Ledge);
        int slices = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(distance) / ProbeSlice));
        for (int i = 0; i < slices; i++)
        {
            if (!traversal.Detector.TryShimmy(traversal.Ledge, right * (distance / slices), out LedgeTarget next)) return false;
            traversal.Ledge = next;
        }
        return true;
    }

    // Along the ledge to the right as one faces its wall.
    private static Vector3 Right(in LedgeTarget ledge)
        => Vector3.Cross(ledge.Surface.transform.TransformDirection(ledge.LocalNormal), Vector3.up).normalized;

    // Where the capsule is this far into the hang (s). Reaching, on a cubic from where the warp went from at its velocity to
    // where the hands touch, at rest there; settling, eased on from there to the hanging place; then the hanging place.
    private Vector3 Warp(Vector3 hang, float time)
    {
        // No warp: as before 0.8.318, the speed already cut, pulled to the hanging place in ledgeCatchDuration.
        if (catchTime <= 0f)
            return Vector3.Lerp(catchStart, hang, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / Mathf.Max(.01f, ctx.AbilitySettings.ledgeCatchDuration))));
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
