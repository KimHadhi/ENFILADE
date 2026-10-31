using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// Running along a balance beam. It stays an ordinary run in the Standing state, so the legs, the footsteps and the gun
/// all carry on, held to the middle of the beam. Its actual foot contacts are BalanceRhythm's notes, with stricter
/// windows and fewer allowed misses than walking. A and D hit left and right foot placements instead of steering. Letting
/// go of W, slowing down, leaving the ground or reaching the end of the beam ends it; moving on along the beam after
/// that is the walk (BalanceState).
/// </summary>
public sealed class BeamRun
{
    private readonly PlayerMovement ctx;
    private readonly BalanceRhythm rhythm;

    /// <summary>The player is running along a beam.</summary>
    public bool IsActive { get; private set; }
    /// <summary>Identifies a fresh run entry so the body can keep one coherent forward gait for that crossing.</summary>
    public int EntrySerial { get; private set; }

    internal BeamRun(PlayerMovement context)
    {
        ctx = context;
        rhythm = new BalanceRhythm(ctx);
    }

    internal void Begin()
    {
        EntrySerial++;
        IsActive = true;
        var settings = ctx.AbilitySettings;
        rhythm.Begin(new BalanceDifficulty
        {
            Beat = settings.balanceRunBeat, QuickestBeat = settings.balanceRunBeat, LeadBeats = settings.balanceRunLeadBeats,
            PerfectWindow = settings.balanceRunPerfectWindow, GoodWindow = settings.balanceRunGoodWindow, SlipLimit = settings.balanceRunSlipLimit
        });
    }

    internal void End()
    {
        IsActive = false;
        rhythm.End();
    }

    /// <summary>The run goes straight along the beam, however the view turns.</summary>
    internal Vector3 AlongBeam(Vector3 velocity) => ctx.Abilities.Fixtures.BalanceFacing * velocity.magnitude;

    internal void Tick(float dt)
    {
        if (!IsActive) return;
        var fixtures = ctx.Abilities.Fixtures;
        var settings = ctx.AbilitySettings;
        var beam = fixtures.Beam;
        float distance = beam != null ? beam.DistanceOf(ctx.transform.position) : 0f;
        if (beam == null || !beam.isActiveAndEnabled || ctx.CurrentState != State.Standing || !ctx.IsGrounded
            || ctx.Input.Move.y <= .1f || distance < settings.balanceEndMargin || distance > beam.Length - settings.balanceEndMargin
            || Vector3.ProjectOnPlane(ctx.ActualVelocity, Vector3.up).magnitude < settings.balanceRunSpeed * .5f)
        {
            End();
            return;
        }
        Vector3 toMiddle = Vector3.ProjectOnPlane(beam.PointAt(distance) - ctx.transform.position, Vector3.up);
        ctx.Motor.AddDisplacement(Vector3.ClampMagnitude(toMiddle, settings.balanceRunSpeed * dt));
        bool fell = ctx.Input.MoveLeftPressed && rhythm.Press(-1);
        if (!fell && ctx.Input.MoveRightPressed) fell = rhythm.Press(1);
        if (fell || rhythm.Tick(dt))
        {
            ctx.ChangeState(State.BalanceFall);
            End();
        }
    }
}
