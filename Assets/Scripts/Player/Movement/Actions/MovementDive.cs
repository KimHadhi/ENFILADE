using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>Short evasive launch, committed flight, and recovery shared by both dive poses.</summary>
public sealed class MovementDive
{
    private readonly PlayerMovement ctx;
    private bool usedInAir;
    private float flightTime;
    public float RecoveryRemaining { get; private set; }
    public int EntrySerial { get; private set; }
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
        Vector3 direction = ctx.transform.right * input.x + ctx.transform.forward * input.y;
        direction = Vector3.ProjectOnPlane(direction.sqrMagnitude > 0.01f ? direction : ctx.transform.forward, Vector3.up).normalized;
        bool backward = Vector3.Dot(direction, ctx.transform.forward) < -0.2f;
        // This fixed axis classifies front/back relative to the flight direction.
        // Negating it for an air back-dive made the next input tick flip upright.
        ctx.DiveBodyForward = direction;
        float speed = Mathf.Max(ctx.HorizontalVelocity.magnitude * ctx.diveMomentumRetention, ctx.groundDiveLaunchSpeed);
        ctx.HorizontalVelocity = direction * speed;
        ctx.VerticalVelocity = fromGround ? ctx.groundDiveVerticalPop
            : Mathf.Max(ctx.VerticalVelocity, ctx.groundDiveVerticalPop * 0.35f);
        ctx.Actions.LeaveGround(); ctx.Actions.ClearJumpBuffer();
        ctx.ChangeState(backward ? State.SupineDiving : State.Diving);
        return true;
    }

    internal void BeginFlight()
    {
        flightTime = 0f; usedInAir = true; EntrySerial++;
    }

    internal void Land()
    {
        RecoveryRemaining = Mathf.Max(RecoveryRemaining, ctx.diveLandingRecovery);
        usedInAir = false;
    }

    internal void UpdateFlight(float dt)
    {
        float brakeTime = Mathf.Max(0f, flightTime + dt - Mathf.Max(flightTime, ctx.diveBurstDuration));
        flightTime += Mathf.Max(0f, dt);
        float speed = Mathf.Max(0f, ctx.HorizontalVelocity.magnitude - ctx.diveAirResistance * dt);
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
