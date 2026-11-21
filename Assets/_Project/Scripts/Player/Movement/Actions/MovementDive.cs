using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>Short evasive launch, committed flight, and recovery shared by both dive poses.</summary>
public sealed class MovementDive
{
    private readonly PlayerMovement ctx;
    private bool usedInAir;
    private float flightTime;
    public float RecoveryRemaining { get; private set; }
    internal float PoseProgress
    {
        get
        {
            float t = Mathf.Clamp01((flightTime - ctx.divePoseDelay) / Mathf.Max(0.05f, ctx.divePoseDuration));
            // Zero slope at either end: takeoff, folding and landing share one continuous pose.
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
    public float BurstWeight => ctx.IsDiving ? Mathf.Clamp01(1f - flightTime / Mathf.Max(0.01f, ctx.diveBurstDuration)) : 0f;
    internal MovementDive(PlayerMovement context) { ctx = context; }

    internal void Tick(float dt)
    {
        RecoveryRemaining = Mathf.Max(0f, RecoveryRemaining - dt);
        if (ctx.IsGrounded && !ctx.IsDiving) usedInAir = false;
    }

    internal bool TryLaunch(Vector2 input, bool fromGround)
    {
        if (ctx.IsDiving || RecoveryRemaining > 0f || (!fromGround && usedInAir)) return false;
        // As in Beatrun, where a dive needs the crouch jump and that needs a fall slower than 350 units/s: no diving
        // out of a fast fall, nor out of a big fall's flailing.
        if (!fromGround && (ctx.IsBigFall || -ctx.VerticalVelocity > ctx.airDiveMaxFallSpeed)) return false;
        // Ahead, or back with S: never to the side. BIPER's side dive rolls are its forward roll turned that way (the body
        // swings round 90 degrees and back), and the side dive (backlog 135) laid the body on its side; the user had both go
        // (2026-11-21: "옆 다이브롤 모션이 따로 없다면 옆 다이빙과 옆 다이브롤은 삭제"). A or D with Z goes ahead.
        bool backward = input.y < -0.1f;
        Vector3 direction = Vector3.ProjectOnPlane(backward ? -ctx.transform.forward : ctx.transform.forward, Vector3.up).normalized;
        // On the ground ahead it is a dive roll (MovementLanding.TryDiveRoll, 0.9.285): the user's pick for Z, "Z 다이브를
        // 구르기로 교체". Back stays the dive onto the back; in the air, the dive.
        if (fromGround && !backward && ctx.Abilities.Landing.TryDiveRoll(direction)) return true;
        // This fixed axis classifies front/back relative to the flight direction.
        // Negating it for an air back-dive made the next input tick flip upright.
        ctx.DiveBodyForward = direction;
        float speed = Mathf.Max(ctx.HorizontalVelocity.magnitude * ctx.diveMomentumRetention, ctx.groundDiveLaunchSpeed);
        // Chaining from an impulse must not turn dive into a faster travel mode.
        speed = Mathf.Min(speed, ctx.groundDiveLaunchSpeed * 1.1f);
        ctx.HorizontalVelocity = direction * speed;
        ctx.VerticalVelocity = fromGround ? ctx.groundDiveVerticalPop
            : Mathf.Max(ctx.VerticalVelocity, ctx.groundDiveVerticalPop * 0.35f);
        ctx.Actions.LeaveGround(); ctx.Actions.ClearJumpBuffer();
        ctx.ChangeState(backward ? State.SupineDiving : State.Diving);
        return true;
    }

    internal void BeginFlight()
    {
        flightTime = 0f; usedInAir = true; 
        ctx.Posture.BeginDive();
    }

    internal void Land()
    {
        RecoveryRemaining = Mathf.Max(RecoveryRemaining, ctx.diveLandingRecovery);
        usedInAir = false;
    }

    internal void UpdateFlight(float dt)
    {
        dt = Mathf.Max(0f, dt);
        // Preserve takeoff speed, then smoothly ramp drag. Integrate only the portion
        // after the hold so crossing its boundary is independent of frame rate.
        float ramp = Mathf.Max(0.01f, ctx.diveBurstDuration);
        float DragTime(float time) => time < ramp ? time * time / (2f * ramp) : time - ramp * 0.5f;
        float hold = Mathf.Max(0f, ctx.diveSpeedHoldDuration);
        float start = Mathf.Max(0f, flightTime - hold);
        float end = Mathf.Max(0f, flightTime + dt - hold);
        float brakeTime = DragTime(end) - DragTime(start);
        flightTime += dt;
        float speed = Mathf.Max(0f, ctx.HorizontalVelocity.magnitude - ctx.diveAirResistance * (end - start));
        if (speed <= 0.001f) { ctx.HorizontalVelocity = Vector3.zero; return; }
        Vector3 direction = ctx.HorizontalVelocity.normalized;
        // Looking around never redirects the impulse. Only a small explicit side correction is allowed.
        float side = ctx.CurrentInput.x;
        if (Mathf.Abs(side) > 0.1f)
        {
            Vector3 target = (direction + ctx.transform.right * (side * 0.3f)).normalized;
            direction = Vector3.RotateTowards(direction, target, 18f * Mathf.Deg2Rad * dt, 0f);
        }
        if (speed > ctx.diveCarrySpeed)
            speed = ctx.diveCarrySpeed + (speed - ctx.diveCarrySpeed) * Mathf.Exp(-ctx.diveBurstBraking * brakeTime);
        ctx.HorizontalVelocity = direction * speed;
    }

    internal void Reset() { usedInAir = false; flightTime = RecoveryRemaining = 0f; }
}
