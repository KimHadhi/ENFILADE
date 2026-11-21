using UnityEngine;

/// <summary>
/// How fast the legs carry the player: a gait (walk, jog, run or sprint) and the way it goes. The body goes as fast as the
/// gait's own clips carry it that way, so at every gait and in every way the feet stay where they come down (the user,
/// 2026-11-21: "이 애니메이션에 최대한 맞출 수 있는 스트레이프 시스템으로", and asked, "네 단계 모두 클립 속도", sprinting
/// any way). The speeds are rows of Tables/Player/Gaits.csv: each gait's clips (BIPER Female Runner) measured on the
/// reference body with ClipBench, one speed for every character, so a race between two players is never won by a body.
/// The keys (the user's): walk mode (Caps Lock, Player/WalkMode) walks and jogs with Shift; otherwise the player runs,
/// and sprints with Shift (PlayerMovement.SprintProgress, eased over Move.sprintRampUpDuration, as the walk to the jog is
/// here). Crouched, walk mode creeps (crouchWalk) and otherwise the crouch jogs (crouchJog).
/// Until 0.9.245 a run built up the way Beatrun's move limit does (W alone 3.6 → 4.9 → 7.9 → 8.9 m/s, sideways and back
/// held at 4.9 x 1.15, cut by sharp turns and walls) and every cycle was stretched to whatever that speed was.
/// </summary>
public sealed class MovementGait
{
    private const string Table = "Gaits";
    // The ways a row gives a speed for, degrees from ahead (left and right alike).
    private static readonly float[] Angles = { 0f, 45f, 90f, 135f, 180f };
    private static readonly string[] Columns = { "ahead", "aheadSide", "side", "backSide", "back" };
    private const int Walk = 0, Jog = 1, Run = 2, Sprint = 3, CrouchWalk = 4, CrouchJog = 5;
    private static readonly string[] Rows = { "walk", "jog", "run", "sprint", "crouchWalk", "crouchJog" };
    // Read before the table is: the reference body's measured speeds (0.9.245).
    private static readonly float[,] Fallback =
    {
        { 1.07f, 1.08f, 1.05f, 1.02f, 1.34f }, { 2.06f, 3.22f, 2.47f, 2.48f, 1.85f }, { 3.93f, 3.74f, 3.06f, 3.99f, 4.21f },
        { 5.61f, 5.68f, 5.55f, 4.10f, 4.37f }, { 1.99f, 1.96f, 1.71f, .58f, .99f }, { 2.49f, 3.04f, 2.64f, 2.60f, 2.51f }
    };

    private readonly PlayerMovement ctx;
    private readonly float[,] speeds = new float[6, 5];
    private float jogShare;

    internal MovementGait(PlayerMovement context)
    {
        ctx = context;
        Read();
    }

    /// <summary>The gait's speed straight ahead now (m/s), between the run and the sprint (or the walk and the jog) as far
    /// as Shift has brought it.</summary>
    public float Speed => ctx.Input.WalkMode ? Mathf.Lerp(speeds[Walk, 0], speeds[Jog, 0], jogShare)
        : Mathf.Lerp(speeds[Run, 0], speeds[Sprint, 0], ctx.SprintProgress);

    /// <summary>The run's speed straight ahead (m/s): the pace a direction key alone gives.</summary>
    internal float RunAhead => speeds[Run, 0];
    /// <summary>The sprint's speed straight ahead (m/s), the fastest the legs go.</summary>
    internal float SprintAhead => speeds[Sprint, 0];
    /// <summary>The crouch's jog straight ahead (m/s).</summary>
    internal float CrouchAhead => speeds[CrouchJog, 0];

    /// <summary>The speed for this input standing (m/s): the gait now, the way the input points.</summary>
    internal float SpeedFor(Vector2 input)
    {
        float angle = AngleOf(input);
        return ctx.Input.WalkMode ? Mathf.Lerp(At(Walk, angle), At(Jog, angle), jogShare)
            : Mathf.Lerp(At(Run, angle), At(Sprint, angle), ctx.SprintProgress);
    }

    /// <summary>The speed for this input crouched (m/s): creeping in walk mode, else the crouch's jog.</summary>
    internal float CrouchSpeedFor(Vector2 input) => At(ctx.Input.WalkMode ? CrouchWalk : CrouchJog, AngleOf(input));

    internal void Reset() => jogShare = 0f;

    /// <summary>Walk mode's Shift brings the jog in as the sprint's does the sprint, standing on the ground and moving.</summary>
    internal void Tick(float dt, Vector2 input)
    {
        if (dt <= 0f) return;
        bool jog = ctx.Input.WalkMode && ctx.Input.SprintButtonHeld && ctx.InputAvailable && ctx.Motor.isGrounded
            && input.sqrMagnitude > .01f && ctx.CurrentState == PlayerMovement.MovementState.Standing;
        float ramp = Mathf.Max(.01f, ctx.sprintRampUpDuration);
        jogShare = Mathf.MoveTowards(jogShare, jog ? 1f : 0f, dt / (jog ? ramp : ramp * 1.5f));
    }

    // Degrees from ahead, 0 to 180, left and right alike.
    private static float AngleOf(Vector2 input)
        => input.sqrMagnitude > 1e-6f ? Mathf.Abs(Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg) : 0f;

    // A row's speed at an angle: the two measured ways either side of it, joined by the angle.
    private float At(int row, float angle)
    {
        for (int i = 0; i < Angles.Length - 1; i++)
        {
            if (angle > Angles[i + 1]) continue;
            float t = (angle - Angles[i]) / (Angles[i + 1] - Angles[i]);
            return Mathf.Lerp(speeds[row, i], speeds[row, i + 1], t);
        }
        return speeds[row, Angles.Length - 1];
    }

    private void Read()
    {
        for (int r = 0; r < Rows.Length; r++)
        {
            GameTables.TryGetRow(Table, Rows[r], out var row);
            for (int c = 0; c < Columns.Length; c++)
                speeds[r, c] = Mathf.Max(0f, row != null ? row.Number(Columns[c], Fallback[r, c]) : Fallback[r, c]);
        }
    }
}
