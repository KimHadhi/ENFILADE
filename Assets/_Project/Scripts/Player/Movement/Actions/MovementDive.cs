using UnityEngine;

/// <summary>
/// A dive's committed flight, shared by both dive poses: a body lying on the ground that goes over an edge (ProneState). The
/// flight keeps its takeoff speed a moment, then eases toward its carry speed; the body folds down over it (PoseProgress).
/// Until 0.9.289 the Z key launched one too (the user, 2026-11-21: "Only remove Z and dive"), and until 0.9.335 a slide's
/// jump, which is a dive roll on the ground now (MovementLanding.TryDiveRoll).
/// </summary>
public sealed class MovementDive
{
    private readonly PlayerMovement ctx;
    private float flightTime;
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

    internal void BeginFlight()
    {
        flightTime = 0f;
        ctx.Posture.BeginDive();
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

    internal void Reset() { flightTime = 0f; }
}
