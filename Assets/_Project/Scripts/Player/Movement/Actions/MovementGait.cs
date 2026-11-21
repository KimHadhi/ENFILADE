using UnityEngine;

/// <summary>
/// How fast the legs carry the player: a gait (walk, jog, run or sprint) and the way it goes. The body goes as fast as the
/// gait's own clips carry it that way, so at every gait and in every way the feet stay where they come down (the user,
/// 2026-11-21: "이 애니메이션에 최대한 맞출 수 있는 스트레이프 시스템으로", and asked, "네 단계 모두 클립 속도", sprinting
/// any way). The speeds are rows of Tables/Player/Gaits.csv: each gait's clips (BIPER Female Runner) measured on the
/// reference body with ClipBench by their root's travel, the speed the performer moved at, which Unity scales to every
/// body alike, so a race between two players is never won by a body.
/// The keys (the user's): walk mode (Caps Lock, Player/WalkMode) walks and jogs with Shift; otherwise the player runs,
/// and sprints with Shift (PlayerMovement.SprintProgress, eased over Move.sprintRampUpDuration, as the walk to the jog is
/// here). Crouched, the crouched walk goes at its own speed, walk mode or not: a mode changes a motion only where it has a
/// motion of its own (the user, 2026-11-21: "걷기 모드라고 앉기 다르게 하지 마 따로 모션 없으면"; 0.9.253 slowed the crouched
/// walk to the walk's speed in walk mode).
/// The body follows the gait's speed and way as quickly as it always has (Move.groundSharpness). 0.9.253 slowed that to
/// the start clips' run-up (0.7-0.94 s to nine tenths) for every change, stopping and turning included, with no start, stop
/// or turn motion to show for it: the body coasted under legs that had already changed (the user: "굉장히 얼음장 위를
/// 걷는 느낌"). A run-up comes back with the start and stop clips themselves.
/// Until 0.9.245 a run built up the way Beatrun's move limit does (W alone 3.6 → 4.9 → 7.9 → 8.9 m/s, sideways and back
/// held at 4.9 x 1.15, cut by sharp turns and walls) and every cycle was stretched to whatever that speed was.
/// </summary>
public sealed partial class MovementGait
{
    private const string Table = "Gaits";
    // The ways a row gives a speed for, degrees from ahead (left and right alike).
    private static readonly float[] Angles = { 0f, 45f, 90f, 135f, 180f };
    private static readonly string[] Columns = { "ahead", "aheadSide", "side", "backSide", "back" };
    private const int Walk = 0, Jog = 1, Run = 2, Sprint = 3, Crouch = 4;
    private static readonly string[] Rows = { "walk", "jog", "run", "sprint", "crouch" };
    // Read before the table is: the clips' root travel measured on the reference body (0.9.253), the run 0.865 of it (0.9.258).
    private static readonly float[,] Fallback =
    {
        { 1.57f, 1.53f, 1.54f, 1.55f, 1.70f }, { 3.21f, 3.95f, 3.28f, 3.55f, 2.98f }, { 4.50f, 4.70f, 4.35f, 4.70f, 4.70f },
        { 7.16f, 6.99f, 6.89f, 6.39f, 5.42f }, { 2.34f, 2.30f, 2.25f, 2.23f, 1.86f }
    };

    private readonly PlayerMovement ctx;
    private readonly float[,] rows = new float[5, 5];
    private float jogShare;

    internal MovementGait(PlayerMovement context)
    {
        ctx = context;
        Read();
    }

    /// <summary>The gait's speed straight ahead now (m/s), between the run and the sprint (or the walk and the jog) as far
    /// as Shift has brought it.</summary>
    public float Speed => ctx.Input.WalkMode ? Mathf.Lerp(rows[Walk, 0], rows[Jog, 0], jogShare)
        : Mathf.Lerp(rows[Run, 0], rows[Sprint, 0], ctx.SprintProgress);

    /// <summary>The run's speed straight ahead (m/s): the pace a direction key alone gives.</summary>
    internal float RunAhead => rows[Run, 0];
    /// <summary>The sprint's speed straight ahead (m/s), the fastest the legs go.</summary>
    internal float SprintAhead => rows[Sprint, 0];
    /// <summary>The crouched walk's speed straight ahead (m/s).</summary>
    internal float CrouchAhead => rows[Crouch, 0];

    /// <summary>The speed for this input standing (m/s): the gait now, the way the input points.</summary>
    internal float SpeedFor(Vector2 input)
    {
        float angle = AngleOf(input);
        float gait = ctx.Input.WalkMode ? Mathf.Lerp(At(Walk, angle), At(Jog, angle), jogShare)
            : Mathf.Lerp(At(Run, angle), At(Sprint, angle), ctx.SprintProgress);
        // Setting off, as fast as the start clip's root goes by now (MovementGait.Moves).
        return gait * StartShare;
    }

    /// <summary>The speed for this input crouched (m/s): the crouched walk's, walk mode or not.</summary>
    internal float CrouchSpeedFor(Vector2 input) => At(Crouch, AngleOf(input));

    internal void Reset()
    {
        jogShare = 0f;
        ResetMoves();
    }

    /// <summary>Walk mode's Shift brings the jog in as the sprint's does the sprint, standing on the ground and moving.</summary>
    internal void Tick(float dt, Vector2 input)
    {
        if (dt <= 0f) return;
        bool jog = ctx.Input.WalkMode && ctx.Input.SprintButtonHeld && ctx.InputAvailable && ctx.Motor.isGrounded
            && input.sqrMagnitude > .01f && ctx.CurrentState == PlayerMovement.MovementState.Standing;
        float ramp = Mathf.Max(.01f, ctx.sprintRampUpDuration);
        jogShare = Mathf.MoveTowards(jogShare, jog ? 1f : 0f, dt / (jog ? ramp : ramp * 1.5f));
        TickMoves(dt, input);
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
            return Mathf.Lerp(rows[row, i], rows[row, i + 1], t);
        }
        return rows[row, Angles.Length - 1];
    }

    private void Read()
    {
        for (int r = 0; r < Rows.Length; r++)
        {
            GameTables.TryGetRow(Table, Rows[r], out var row);
            for (int c = 0; c < Columns.Length; c++)
                rows[r, c] = Mathf.Max(0f, row != null ? row.Number(Columns[c], Fallback[r, c]) : Fallback[r, c]);
        }
        ReadMoves();
    }
}
