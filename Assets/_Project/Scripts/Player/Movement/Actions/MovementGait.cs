using UnityEngine;

/// <summary>
/// How fast the legs carry the player: a gait (walk, jog or sprint) and the way it goes. Each gait plays its own clips
/// (PlayerBody.Locomotion) and the body goes about as fast as their feet carry it that way, so at every gait and in every
/// way the feet stay where they come down (the user, 2026-11-21: "이 애니메이션에 최대한 맞출 수 있는 스트레이프 시스템으로",
/// sprinting any way). The speeds are rows of Tables/Player/Gaits.csv: what each gait's clips' (BIPER Female Runner) feet
/// carry the body at, measured with ClipBench on four characters, times the row's boost, the share the game goes faster
/// than that; the step's pace and the stride make up the boost (PlayerBody.Pace). The same for every body, so a race
/// between two players is never won by a body.
/// The keys (the user's): a direction key alone jogs, and sprints with Shift (PlayerMovement.SprintProgress, eased over
/// Move.sprintRampUpDuration); walk mode (Caps Lock, Player/WalkMode) walks, and jogs with Shift (eased the same way here).
/// Crouched, the crouched walk goes at its own speed, walk mode or not: a mode changes a motion only where it has a motion
/// of its own (the user, 2026-11-21: "걷기 모드라고 앉기 다르게 하지 마 따로 모션 없으면"; 0.9.253 slowed the crouched walk
/// to the walk's speed in walk mode).
/// Until 0.9.283 a key alone ran, a gait of its own between the jog and the sprint (BIPER's Run_Lfoot and StrafeRun_*), and
/// the jog was walk mode's Shift alone. The run and the sprint looked alike even on their own clips, the run went too fast
/// and the jog too slow, so the run went and the jog took its place, boosted, with the sprint a little faster (the user,
/// 2026-11-21: "달리기를 없애고 ... 조깅 좀 버프해 줘, 그리고 전력질주 속도 좀 살짝 버프").
/// The body follows the gait's speed and way as quickly as it always has (Move.groundSharpness). 0.9.253 slowed that to
/// the start clips' run-up (0.7-0.94 s to nine tenths) for every change, stopping and turning included, with no start, stop
/// or turn motion to show for it: the body coasted under legs that had already changed (the user: "굉장히 얼음장 위를
/// 걷는 느낌"). A run-up comes back with the start and stop clips themselves.
/// Starts and stops played as their clips (0.9.259) went again in 0.9.263: BIPER's start and stop clips go straight ahead
/// only (its other starts turn the whole body round to the way, for a body that faces where it goes), so setting off
/// ahead was slow while a step to the side first skipped it, and a stop ran on ahead (the user: "왼쪽 오른쪽 뒤 대각선
/// 시작하는 모션 없이 앞만 있는 거라면 이 기능 없애는 게 나을 듯").
/// Until 0.9.245 a run built up the way Beatrun's move limit does (W alone 3.6 → 4.9 → 7.9 → 8.9 m/s, sideways and back
/// held at 4.9 x 1.15, cut by sharp turns and walls) and every cycle was stretched to whatever that speed was.
/// </summary>
public sealed class MovementGait
{
    private const string Table = "Gaits";
    // The ways a row gives a speed for, degrees from ahead (left and right alike).
    private static readonly float[] Angles = { 0f, 45f, 90f, 135f, 180f };
    private static readonly string[] Columns = { "ahead", "aheadSide", "side", "backSide", "back" };
    private const int Walk = 0, Jog = 1, Sprint = 2, Crouch = 3;
    private static readonly string[] Rows = { "walk", "jog", "sprint", "crouch" };
    // Read before the table is: what the clips' feet carry the body at, the mean of four characters (0.9.282), and the
    // share each gait goes faster than that (0.9.283).
    private static readonly float[,] Fallback =
    {
        { 1.27f, 1.23f, 1.25f, 1.21f, 1.43f }, { 2.44f, 3.18f, 2.36f, 2.67f, 2.16f },
        { 5.53f, 5.79f, 5.87f, 4.50f, 4.46f }, { 2.34f, 2.30f, 2.25f, 2.23f, 1.86f }
    };
    private static readonly float[] FallbackBoost = { 1f, 1.23f, 1.08f, 1f };
    // How long the legs take to go from one gait's clips to the next (s), as walk mode or Shift changes it.
    private const float GaitChange = .25f;

    private readonly PlayerMovement ctx;
    private readonly float[,] rows = new float[4, 5];
    private float jogShare, position = Jog;

    internal MovementGait(PlayerMovement context)
    {
        ctx = context;
        Read();
    }

    /// <summary>The gait's speed straight ahead now (m/s), between the jog and the sprint (or the walk and the jog) as far
    /// as Shift has brought it.</summary>
    public float Speed => ctx.Input.WalkMode ? Mathf.Lerp(rows[Walk, 0], rows[Jog, 0], jogShare)
        : Mathf.Lerp(rows[Jog, 0], rows[Sprint, 0], ctx.SprintProgress);

    /// <summary>
    /// Which gait the legs go (0 walk, 1 jog, 2 sprint; between while Shift or walk mode changes it): the clips they play
    /// are that gait's own (PlayerBody.Locomotion), not the ones whose feet happen to match the speed. Kept by the player's
    /// own PC and sent with the pose (PlayerPose.Gait), so every PC plays the same.
    /// </summary>
    public float Position => position;

    /// <summary>Another PC's gait as its pose says (MovementMirror).</summary>
    internal void Mirror(float value) => position = Mathf.Clamp(value, 0f, Sprint);

    /// <summary>
    /// Where a speed lies on the gaits' ladder the way it goes (<paramref name="angle"/>, degrees from ahead): 0 at the
    /// walk's speed or slower, 1 at the jog's, 2 at the sprint's or faster, between in between. The legs never play a gait
    /// faster than this: setting off, the body goes through the slower gaits' clips to its own.
    /// </summary>
    internal float PlaceOf(float speed, float angle)
    {
        float below = At(Walk, angle);
        if (speed <= below) return 0f;
        for (int gait = Jog; gait <= Sprint; gait++)
        {
            float above = Mathf.Max(below, At(gait, angle));
            if (speed < above) return gait - 1 + (speed - below) / Mathf.Max(1e-3f, above - below);
            below = above;
        }
        return Sprint;
    }

    /// <summary>The jog's speed straight ahead (m/s): the pace a direction key alone gives.</summary>
    internal float JogAhead => rows[Jog, 0];
    /// <summary>The sprint's speed straight ahead (m/s), the fastest the legs go.</summary>
    internal float SprintAhead => rows[Sprint, 0];

    /// <summary>The speed for this input standing (m/s): the gait now, the way the input points.</summary>
    internal float SpeedFor(Vector2 input)
    {
        float angle = AngleOf(input);
        return ctx.Input.WalkMode ? Mathf.Lerp(At(Walk, angle), At(Jog, angle), jogShare)
            : Mathf.Lerp(At(Jog, angle), At(Sprint, angle), ctx.SprintProgress);
    }

    /// <summary>The speed for this input crouched (m/s): the crouched walk's, walk mode or not.</summary>
    internal float CrouchSpeedFor(Vector2 input) => At(Crouch, AngleOf(input));

    internal void Reset() => jogShare = 0f;

    /// <summary>Walk mode's Shift brings the jog in as the sprint's does the sprint, standing on the ground and moving.</summary>
    internal void Tick(float dt, Vector2 input)
    {
        if (dt <= 0f) return;
        bool jog = ctx.Input.WalkMode && ctx.Input.SprintButtonHeld && ctx.InputAvailable && ctx.Motor.isGrounded
            && input.sqrMagnitude > .01f && ctx.CurrentState == PlayerMovement.MovementState.Standing;
        float ramp = Mathf.Max(.01f, ctx.sprintRampUpDuration);
        jogShare = Mathf.MoveTowards(jogShare, jog ? 1f : 0f, dt / (jog ? ramp : ramp * 1.5f));
        float gait = ctx.Input.WalkMode ? jogShare : Jog + ctx.SprintProgress;
        position = Mathf.MoveTowards(position, gait, dt / GaitChange);
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
            float boost = Mathf.Max(0f, row != null ? row.Number("boost", FallbackBoost[r]) : FallbackBoost[r]);
            for (int c = 0; c < Columns.Length; c++)
                rows[r, c] = Mathf.Max(0f, row != null ? row.Number(Columns[c], Fallback[r, c]) : Fallback[r, c]) * boost;
        }
    }
}
