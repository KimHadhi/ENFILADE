using UnityEngine;

/// <summary>
/// How fast the legs carry the player: a gait (walk, jog or sprint) and the way it goes. Each gait plays its own clips
/// (PlayerBody.Locomotion) and the body goes as fast as their feet carry it that way, so at every gait and in every way the
/// clips play as they were made (the user, 2026-11-21: "이 애니메이션에 최대한 맞출 수 있는 스트레이프 시스템으로"; 2026-11-22:
/// "이런 속도를 애니메이션에 맞춰 줄 수 없나?", "전부 맞추기"). The speeds are rows of Tables/Player/Gaits.csv: what each gait's
/// clips' (BIPER Female Runner) feet carry the body at, measured with ClipBench on four characters, the same for every body,
/// so a race between two players is never won by a body. A gun out, the legs are that gun's own walk, jog, run and crouched
/// walk ahead (Combat Girls), and its sights up, its aim walk, each with speeds of its own (rows gun.* and aim*); where the gun
/// has no clip the legs, and the speed, are the empty hands'. Until 0.9.350 the jog went 1.23 and the sprint 1.08 times their
/// clips, a gun jogged, ran and crouched at the empty hands' pace (its run a third faster than it is made) and aimed at a
/// share of it that each gun had of its own (Weapons.csv adsMoveMultiplier, 0.45-0.8), and the step's pace and the stride
/// made up the rest.
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
    private const int Walk = 0, Jog = 1, Sprint = 2, Crouch = 3, Gun = 4, Aim = 8, AimCrouch = 9;
    private static readonly string[] Rows =
    {
        "walk", "jog", "sprint", "crouch", "gun.walk", "gun.jog", "gun.sprint", "gun.crouch", "aim", "aim.crouch"
    };
    // The row whose clips, and speed, a gun's or an aim's legs have where its own cell is empty.
    private static readonly int[] Under = { Walk, Jog, Sprint, Crouch, Walk, Jog, Sprint, Crouch, Walk, Crouch };
    // Read before the table is: what the clips' feet carry the body at, the mean of four characters (0.9.350).
    private static readonly float[,] Fallback =
    {
        { 1.33f, 1.26f, 1.31f, 1.38f, 1.50f }, { 2.50f, 3.36f, 2.66f, 2.79f, 2.69f },
        { 6.01f, 5.79f, 6.18f, 4.93f, 4.46f }, { 2.21f, 2.15f, 1.86f, 2.09f, 1.66f }
    };
    // How long the legs take to go from one gait's clips to the next (s), as walk mode or Shift changes it.
    private const float GaitChange = .25f;

    private readonly PlayerMovement ctx;
    private readonly float[,] rows = new float[Rows.Length, Columns.Length];
    private float jogShare, position = Jog;

    internal MovementGait(PlayerMovement context)
    {
        ctx = context;
        Read();
    }

    /// <summary>The gait's speed straight ahead now (m/s), between the jog and the sprint (or the walk and the jog) as far
    /// as Shift has brought it.</summary>
    public float Speed => ctx.Input.WalkMode ? Mathf.Lerp(At(Walk, 0f), At(Jog, 0f), jogShare)
        : Mathf.Lerp(At(Jog, 0f), At(Sprint, 0f), ctx.SprintProgress);

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
        float below = Aimed(At(Walk, angle), Aim, angle);
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
    internal float JogAhead => At(Jog, 0f);
    /// <summary>The sprint's speed straight ahead (m/s), the fastest the legs go: with a gun out, its own run's.</summary>
    internal float SprintAhead => At(Sprint, 0f);

    /// <summary>The speed for this input standing (m/s): the gait now, the way the input points; the aim's as far as the
    /// sights are up.</summary>
    internal float SpeedFor(Vector2 input)
    {
        float angle = AngleOf(input);
        return Aimed(ctx.Input.WalkMode ? Mathf.Lerp(At(Walk, angle), At(Jog, angle), jogShare)
            : Mathf.Lerp(At(Jog, angle), At(Sprint, angle), ctx.SprintProgress), Aim, angle);
    }

    /// <summary>The speed for this input crouched (m/s): the crouched walk's, walk mode or not.</summary>
    internal float CrouchSpeedFor(Vector2 input)
    {
        float angle = AngleOf(input);
        return Aimed(At(Crouch, angle), AimCrouch, angle);
    }

    private float Aimed(float speed, int aim, float angle) => Mathf.Lerp(speed, RowAt(aim, angle), ctx.AimShare);

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

    // A gait's speed at an angle: the gun's in the hands, or the empty hands'.
    private float At(int gait, float angle) => RowAt(ctx.GunHasLegs ? gait + Gun : gait, angle);

    // A row's speed at an angle: the two measured ways either side of it, joined by the angle.
    private float RowAt(int row, float angle)
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
            {
                float under = r == Under[r] ? Fallback[r, c] : rows[Under[r], c];
                rows[r, c] = Mathf.Max(0f, row != null ? row.Number(Columns[c], under) : under);
            }
        }
    }
}
