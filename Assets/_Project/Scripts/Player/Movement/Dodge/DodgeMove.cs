using UnityEngine;

/// <summary>
/// The step dodge under way: which clip it is, how far into it the body is and how far the clip carries it (ClipTravel).
/// The movement goes by it (StepDodgeState), the body plays it (PlayerBody.Dodge), the world shows it (DodgeDash) and every
/// other PC picks the same one from the way, the kind and the number it is sent (MovementMirror).
/// Rebuilt for its motion (the user, 2026-11-22: "회피는 모션을 위해 개편하고(너무 일찍 끝나고 이펙트가 구림)", then
/// "QuickShift 끝까지"): until 0.9.334 a dodge was 2.6 m in 0.14 s of a move of its own, BIPER's 0.8 s QuickShift showed its
/// first fifth of a second over it and was dropped as soon as the keys moved the body, and with a gun out only the legs had it.
/// - Upright: QuickShift_*, the way the keys point: a burst, down low, and up again, as far as the clip's own body goes
///   (3.3-3.4 m) however this one was going (2.6 m from 0.9.341 to 0.9.350). The legs are the clip's until
///   Ability.dodgeUntil; the rest of getting up plays out if the body stays.
/// - Out of a jog or a sprint still asked for, the run goes on sooner (Ability.dodgeRunAt, StepDodgeState). Begun at a sprint
///   (by the gait, MovementGait.Position, the same on every PC, not the speed now: the burst is faster than any gait) and
///   aside, the body comes up into it with ShiftToSprint_* from the moment it is as low as the dodge (RunOutToward), at that
///   clip's own pace (PlayerBody.RunOutPace). Until 0.9.341
///   a sprint's dodge was that clip whole, and the whole length of its way, which shifts aside and then runs on ahead, went
///   aside: the legs ran ahead under a body still going sideways, at the pace of a run and not of a dodge (the user,
///   2026-11-22: "달리면서 회피하면 평소보다 회피 거리가 짧고", "오른쪽 회피 이후에 잠깐 매우 빠르게 달리는 모션").
/// - Crouched: the low part of the same clip, from its push to before it rises (dodgeLowFrom, dodgeLowUntil), into the crouch.
///   Until 0.9.334 it slid along in the still crouch pose.
/// - Chained on a confirmed dodge it starts from that push too: the body is low already.
/// </summary>
public sealed class DodgeMove
{
    private const int Standing = 0, Low = 1, Sprint = 2;
    // The gait past which a dodge shifts into the sprint: halfway from the jog (1) to the sprint (2).
    private const float SprintsFrom = 1.5f;
    // A dodge comes up into the sprint with the clip whose way is this near its own (degrees): straight ahead or back, none.
    private const float SprintWithin = 30f;
    // A dodge clip with no baked travel goes this far (m) in this long (s).
    private const float PlainDistance = 3.3f, PlainSeconds = .25f;
    // A run-out goes ahead: it is for a run this near ahead (degrees).
    private const float RunOutWithin = 30f;
    // The body has slid out this far into its way (share): a jump may cut in, and it comes out of whoever it slid into.
    private const float ShiftedShare = .85f;

    private readonly PlayerMovement ctx;
    private readonly ClipTravel travel = new ClipTravel();
    private int kind, mirrored = -1;

    internal DodgeMove(PlayerMovement context) { ctx = context; }

    /// <summary>The clip (the PlayerBody profile's name), empty before the first dodge.</summary>
    public string Clip { get; private set; } = "";
    /// <summary>The clip's moment it begins at (s).</summary>
    public float From { get; private set; }
    /// <summary>It began at a sprint.</summary>
    public bool Runs => kind == Sprint;
    // The clip it comes up into a sprint ahead with, empty for none.
    private string runOut = "";
    /// <summary>The clip's moment it is at (s).</summary>
    public float Time => travel.Time;
    /// <summary>Counts the dodges, so one chained on another is seen to begin.</summary>
    public int Serial { get; private set; }
    /// <summary>How far its clip goes (m).</summary>
    public float Distance => travel.Distance;
    /// <summary>The slide is done; what is left is getting up.</summary>
    public bool Shifted => travel.Share >= ShiftedShare;
    /// <summary>The legs are the player's again.</summary>
    public bool Over
    {
        get
        {
            var settings = ctx.AbilitySettings;
            return travel.Time >= (kind == Low ? settings.dodgeLowUntil : settings.dodgeUntil);
        }
    }

    internal int Kind => kind;

    /// <summary>A dodge this way begins, crouched or not; <paramref name="chained"/> on one just confirmed.</summary>
    internal void Begin(Vector3 direction, bool crouched, bool chained)
    {
        float heading = Heading(direction, ctx.transform.eulerAngles.y);
        Set(heading, crouched ? Low : ctx.Actions.Gait.Position > SprintsFrom ? Sprint : Standing, chained);
        Serial++;
    }

    /// <summary>Another PC's dodge as its pose says: its way, its kind and its number (MovementMirror).</summary>
    internal void Mirror(Vector3 direction, float yaw, int kind, int serial)
    {
        if (serial == mirrored) return;
        mirrored = serial;
        Set(Heading(direction, yaw), Mathf.Clamp(kind, Standing, Sprint), false);
        Serial++;
    }

    private void Set(float heading, int kind, bool chained)
    {
        var settings = ctx.AbilitySettings;
        this.kind = kind;
        Clip = ClipWays.Nearest(settings.dodgeWays, heading, 180f) ?? "";
        runOut = kind == Sprint ? ClipWays.Nearest(settings.dodgeSprintWays, heading, SprintWithin) ?? "" : "";
        From = kind == Low || chained ? settings.dodgeLowFrom : 0f;
        if (!travel.Begin(Clip, From, settings.clipTravelMost))
            travel.BeginPlain(PlainDistance, PlainSeconds, From, settings.clipTravelMost);
    }

    /// <summary>The clip it comes up into a run this way with (from Ability.dodgeRunFrom): aside out of a sprint and on ahead.
    /// Empty where there is none, the run goes another way, or a gun is out: its own run has the body then (PlayerBody.GunRuns),
    /// and this clip's trunk would carry the gun down with it.</summary>
    internal string RunOutToward(Vector3 way)
        => runOut.Length == 0 || ctx.GunHasLegs || Mathf.Abs(Heading(way, ctx.transform.eulerAngles.y)) > RunOutWithin ? "" : runOut;

    /// <summary>How fast the body goes on that way out of it (m/s): the run-out's own pace, or the legs'.</summary>
    internal float PaceInto(Vector3 way)
    {
        float pace = ClipTravel.PaceAt(RunOutToward(way), ctx.AbilitySettings.dodgeRunFrom);
        return pace > 0f ? pace : ctx.Actions.Gait.SpeedFor(ctx.CurrentInput);
    }

    // Degrees from the way they face, + to the right.
    private static float Heading(Vector3 direction, float yaw)
        => direction.sqrMagnitude > 1e-6f ? Mathf.DeltaAngle(yaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg) : 180f;

    /// <summary>How far the body goes in this step (m); the clip's moment goes on.</summary>
    internal float Go(float dt) => travel.Go(dt);
    /// <summary>The clip's moment goes on while the body stays where it is.</summary>
    internal void Pass(float dt) => travel.Pass(dt);

    internal void Reset() => mirrored = -1;
}
