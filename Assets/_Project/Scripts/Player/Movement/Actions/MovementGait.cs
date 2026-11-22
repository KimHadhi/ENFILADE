using UnityEngine;

/// <summary>
/// How fast the legs carry the player: a gait (walk, jog or sprint) and the way it goes. Each gait plays its own clips and the
/// body goes as fast as those clips go that way (PlayerBody.LegSpeed, off the clips themselves), so at every gait and in every
/// way they play as they were made (the user, 2026-11-21: "이 애니메이션에 최대한 맞출 수 있는 스트레이프 시스템으로";
/// 2026-11-22: "이런 속도를 애니메이션에 맞춰 줄 수 없나?", "걍 애니메이션 하나만 있으면"). A clip's way is every body's alike,
/// so a race between two players is never won by a body. A gun out or aimed, the legs are that gun's own, and so is the
/// speed. Until 0.9.352 the speeds were rows of a table (Gaits.csv), measured with ClipBench and typed, and until 0.9.350
/// boosted over their clips.
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
    private const int Walk = 0, Jog = 1, Sprint = 2;
    // A body with no cycles of its own goes this fast (m/s): the walk, the jog, the sprint, and crouched.
    private static readonly float[] Bare = { 1.3f, 2.7f, 6f };
    private const float BareCrouch = 2f;
    // How long the legs take to go from one gait's clips to the next (s), as walk mode or Shift changes it.
    private const float GaitChange = .25f;

    private readonly PlayerMovement ctx;
    private float jogShare, position = Jog;

    internal MovementGait(PlayerMovement context) { ctx = context; }

    // The gait the keys ask for now: between the jog and the sprint (or the walk and the jog) as far as Shift has brought it.
    private float Asked => ctx.Input.WalkMode ? jogShare : Jog + ctx.SprintProgress;

    /// <summary>The gait's speed straight ahead now (m/s).</summary>
    public float Speed => Legs(0f, Asked, 0f);

    /// <summary>
    /// Which gait the legs go (0 walk, 1 jog, 2 sprint; between while Shift or walk mode changes it): the clips they play
    /// are that gait's own (PlayerBody.Locomotion), not the ones whose feet happen to match the speed. Kept by the player's
    /// own PC and sent with the pose (PlayerPose.Gait), so every PC plays the same.
    /// </summary>
    public float Position => position;

    /// <summary>Another PC's gait as its pose says (MovementMirror).</summary>
    internal void Mirror(float value) => position = Mathf.Clamp(value, 0f, Sprint);

    /// <summary>
    /// Where a speed lies on the gaits' ladder the way it goes (<paramref name="heading"/>, degrees from ahead): 0 at the
    /// walk's speed or slower, 1 at the jog's, 2 at the sprint's or faster, between in between. The legs never play a gait
    /// faster than this: setting off, the body goes through the slower gaits' clips to its own.
    /// </summary>
    internal float PlaceOf(float speed, float heading)
    {
        float below = Legs(heading, Walk, 0f);
        if (speed <= below) return 0f;
        for (int gait = Jog; gait <= Sprint; gait++)
        {
            float above = Mathf.Max(below, Legs(heading, gait, 0f));
            if (speed < above) return gait - 1 + (speed - below) / Mathf.Max(1e-3f, above - below);
            below = above;
        }
        return Sprint;
    }

    /// <summary>The jog's speed straight ahead (m/s): the pace a direction key alone gives.</summary>
    internal float JogAhead => Legs(0f, Jog, 0f);
    /// <summary>The sprint's speed straight ahead (m/s), the fastest the legs go: with a gun out, its own run's.</summary>
    internal float SprintAhead => Legs(0f, Sprint, 0f);

    /// <summary>The speed for this input standing (m/s): the gait now, the way the input points; the aim's as far as the
    /// sights are up.</summary>
    internal float SpeedFor(Vector2 input) => Legs(Heading(input), Asked, ctx.AimShare);

    /// <summary>The speed for this input crouched (m/s): the crouched walk's, walk mode or not.</summary>
    internal float CrouchSpeedFor(Vector2 input)
    {
        float speed = ctx.LegSpeed(Heading(input), Walk, true, ctx.AimShare);
        return speed > 0f ? speed : BareCrouch;
    }

    private float Legs(float heading, float gait, float aim)
    {
        float speed = ctx.LegSpeed(heading, gait, false, aim);
        return speed > 0f ? speed : gait < Jog ? Mathf.Lerp(Bare[Walk], Bare[Jog], gait) : Mathf.Lerp(Bare[Jog], Bare[Sprint], gait - Jog);
    }

    // Degrees from ahead, + to the right.
    private static float Heading(Vector2 input) => input.sqrMagnitude > 1e-6f ? Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg : 0f;

    internal void Reset() => jogShare = 0f;

    /// <summary>Walk mode's Shift brings the jog in as the sprint's does the sprint, standing on the ground and moving.</summary>
    internal void Tick(float dt, Vector2 input)
    {
        if (dt <= 0f) return;
        bool jog = ctx.Input.WalkMode && ctx.Input.SprintButtonHeld && ctx.InputAvailable && ctx.Motor.isGrounded
            && input.sqrMagnitude > .01f && ctx.CurrentState == PlayerMovement.MovementState.Standing;
        float ramp = Mathf.Max(.01f, ctx.sprintRampUpDuration);
        jogShare = Mathf.MoveTowards(jogShare, jog ? 1f : 0f, dt / (jog ? ramp : ramp * 1.5f));
        position = Mathf.MoveTowards(position, Asked, dt / GaitChange);
    }
}
