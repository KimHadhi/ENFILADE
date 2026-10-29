using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The run speed, built up the way Beatrun's move limit is.
/// - W alone starts a run at runStartSpeed and builds it through walkSpeed and sprintSpeed to topSpeed. Shift jumps to
///   sprintSpeed within sprintRampUpDuration and builds on from there. Only a forward run builds past walkSpeed;
///   sideways and backwards stay at walkSpeed at most.
/// - Letting go of forward lets it fall back. A sharp turn of the view, a wall in the way or a hard landing cuts it.
/// - It holds in the air. Coming out of a slide, a roll, a vault or another move, it carries on from the speed the
///   player is actually moving at; a dodge or a dash push that missed keeps the run they interrupted.
/// Numbers: Player.csv row Move.
/// </summary>
public sealed class MovementMomentum
{
    // Input this far forward counts as running forward (diagonals included), like the sprint rule.
    private const float ForwardInput = .1f;
    // A wall in the way: the player moved less than this share of the built speed while touching something sideways.
    private const float BlockedShare = .5f;

    private readonly PlayerMovement ctx;
    private float lastYaw;
    private bool hasYaw;

    /// <summary>The built-up run speed (m/s).</summary>
    public float Speed { get; private set; }
    /// <summary>The player is pushing a run forward on the ground this frame.</summary>
    public bool Driving { get; private set; }

    internal MovementMomentum(PlayerMovement context)
    {
        ctx = context;
        Speed = Start;
        ctx.StateChanged += OnStateChanged;
        ctx.Landed += OnLanded;
    }

    private float Start => Mathf.Min(ctx.runStartSpeed, ctx.walkSpeed);
    internal float Top => Mathf.Max(ctx.topSpeed, ctx.sprintSpeed);

    /// <summary>The speed to move at for this input: the built speed running forward, at most walkSpeed otherwise.</summary>
    internal float SpeedFor(Vector2 input) => input.y > ForwardInput ? Speed : Mathf.Min(Speed, ctx.walkSpeed);

    internal void Reset()
    {
        Speed = Start;
        Driving = hasYaw = false;
    }

    internal void Tick(float dt, Vector2 input)
    {
        Driving = false;
        float yaw = ctx.transform.eulerAngles.y;
        float turned = hasYaw ? Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) : 0f;
        lastYaw = yaw;
        hasYaw = true;
        if (dt <= 0f || ctx.CurrentState != MovementState.Standing || !ctx.Motor.isGrounded) return;

        bool moving = input.sqrMagnitude > .01f;
        if (input.y > ForwardInput)
        {
            Driving = true;
            Speed = Mathf.Max(Speed, Mathf.Lerp(Start, ctx.sprintSpeed, ctx.SprintProgress));
            Speed = Mathf.Min(Top, Speed + BuildRate() * dt);
        }
        else if (moving && Speed < ctx.walkSpeed) Speed = Mathf.Min(ctx.walkSpeed, Speed + BuildRate() * dt);
        else Speed = Mathf.MoveTowards(Speed, moving ? ctx.walkSpeed : Start, ctx.momentumFalloff * dt);

        if (!moving) return;
        if (turned / dt > ctx.turnCutRate) Cut(Speed - turned * ctx.turnCutPerDegree);
        Vector3 actual = ctx.Motor.actualVelocity;
        float actualSpeed = new Vector2(actual.x, actual.z).magnitude;
        if ((ctx.Motor.lastMoveFlags & CollisionFlags.Sides) != 0 && actualSpeed < Speed * BlockedShare) Cut(actualSpeed);
    }

    // The build follows the table's timeline: start → walk in walkBuildTime, → sprint by sprintBuildTime, → top in topBuildTime.
    private float BuildRate()
    {
        if (Speed < ctx.walkSpeed) return (ctx.walkSpeed - Start) / Mathf.Max(.01f, ctx.walkBuildTime);
        if (Speed < ctx.sprintSpeed)
            return (ctx.sprintSpeed - ctx.walkSpeed) / Mathf.Max(.01f, ctx.sprintBuildTime - ctx.walkBuildTime);
        return (Top - ctx.sprintSpeed) / Mathf.Max(.01f, ctx.topBuildTime);
    }

    private void Cut(float speed) => Speed = Mathf.Clamp(speed, Start, Speed);

    /// <summary>The run stops dead (a dash push that met someone).</summary>
    internal void Stop() => Speed = Start;

    private void OnStateChanged(MovementState from, MovementState to)
    {
        if (to != MovementState.Standing || from == MovementState.Standing
            || from == MovementState.StepDodge || from == MovementState.DashPush) return;
        Vector3 actual = ctx.Motor.actualVelocity;
        Speed = Mathf.Clamp(new Vector2(actual.x, actual.z).magnitude, Start, Top);
    }

    // A roll or a landing slide carries the speed on; landing hard on the feet stops the run.
    private void OnLanded(float impact)
    {
        var state = ctx.CurrentState;
        if (impact >= ctx.hardLandingSpeed && (state == MovementState.Standing || state == MovementState.Crouching))
            Speed = Start;
    }
}
