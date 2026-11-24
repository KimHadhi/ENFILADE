using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// A run's turn back: running at least Ability.runTurnSpeed with the keys ahead, the way the keys want at least
/// Ability.runTurnAngle from the way the body runs (the view flicked round), the body plants, brakes and pushes off the new way
/// on BIPER Female Runner's own turn (Jog_ and Sprint_Turn{L,R}_{L,R}foot, Ability.runTurns: the sprint's from half way between
/// the jog and the sprint, turning the way the body is behind the view, planting the foot that is down), entered where its
/// chest is within Ability.runTurnEntry of the way it ends, the view's as it began. The mouse is never held: the body only
/// lags the view, no further than the spine and neck turn back (as standing, TurnOnTheSpot).
/// While the clip's own root still goes the old way the run brakes evenly from the speed it came in at to a stop; from there it
/// pushes off along the keys at the clip's own pace, and the walk or run takes over at the clip's first footfall once its chest
/// has come round within AroundWithin (PlayerBody.StepFrom), the gait coming down from that pace (MovementGait.Carry). Jumping,
/// sliding, crouching or leaving the ground gives it up. The other PCs start the same turn on the pose's count and play it on
/// their own clock (MovementMirror).
/// Until 0.9.386 a flick of the view on the run swung the run round at full speed in a loop: the body turned with the view in
/// the frame and its legs ran the back and side sprints, then the walk's clips at 5 m/s, then banked into the arc (걸음 기록 of
/// 2026-11-24; the user: "달리다가 바로 반대편으로 돌면 캐릭터 모션이 굉장히 우스꽝스러워지는데 turn 애니메이션 넣어야 하나?",
/// the experts' review, then "Yes, all of it"). A sprint's turn back now takes the third to half a second a body's does.
/// Only a run that went ahead of the view a moment before turns back so (RanAheadWithin): running backwards and pressing ahead
/// is a strafe's reversal, the body already facing the way the keys want.
/// </summary>
public sealed class RunTurn
{
    // The keys count as ahead within this of straight on (degrees).
    private const float AheadWithin = 45f;
    // From here on the gait's ladder (MovementGait.Position: 1 the jog, 2 the sprint) it is the sprint's turn.
    private const float SprintFrom = 1.5f;
    // Its chest within this of the way it ends (degrees), the turn hands over at the clip's next footfall.
    private const float AroundWithin = 30f;
    // The brake takes at least this long (s), for a clip whose root has turned before the turn is entered.
    private const float BrakeLeast = .1f;
    // The clips are read at this many moments a second.
    private const float ReadRate = 60f;
    // A turn back is of a run that went ahead of the view this short a while ago (s): the view flicked round from it. Running
    // backwards and pressing ahead is no turn: the body already faces the way the keys want.
    private const float RanAheadWithin = .3f;

    private readonly PlayerMovement ctx;
    private float startedAt, inSpeed, brakeTo, outAt, ranAheadAt = float.NegativeInfinity;
    private Vector3 inWay;
    private bool active;

    internal RunTurn(PlayerMovement context) { ctx = context; }

    /// <summary>A turn is under way.</summary>
    public bool Turning => active && Moment < outAt;
    /// <summary>The turn's clip: its place in Ability.runTurns and its name.</summary>
    public int Index { get; private set; }
    public string Clip { get; private set; } = "";
    /// <summary>The clip's moment the turn began at (s).</summary>
    public float From { get; private set; }
    /// <summary>The way the turn ends facing (world yaw, degrees): the view's as it began.</summary>
    public float EndYaw { get; private set; }
    /// <summary>Turns begun, counted for the pose (MovementMirror).</summary>
    public byte Count { get; private set; }
    /// <summary>The clip's moment now (s), on this PC's own clock.</summary>
    public float Moment => From + (Time.time - startedAt);

    private ClipMotion Motion => ctx.MotionOf(Clip);

    // Which way the clip's chest faces at its moment against the way it began (degrees, + to the right), its root's turn and
    // its pose's together, whichever the clip has its turn in.
    private static float Faces(ClipMotion motion, float time) => motion.TurnedAt(time) + motion.FacingAt(time);

    /// <summary>Which way the body's root faces now (world yaw, degrees): the clip's own turn, set to end on EndYaw.</summary>
    public float RootYaw
    {
        get
        {
            var motion = Motion;
            return motion == null ? EndYaw : EndYaw - Faces(motion, motion.Length) + motion.TurnedAt(Moment);
        }
    }

    /// <summary>Which way the body's chest faces now (world yaw, degrees).</summary>
    public float ChestYaw
    {
        get
        {
            var motion = Motion;
            return motion == null ? EndYaw : EndYaw - Faces(motion, motion.Length) + Faces(motion, Moment);
        }
    }

    /// <summary>Which way the clip's pose has the chest against the body's root now (degrees, + to the right).</summary>
    public float PoseFacing => Motion is ClipMotion motion ? motion.FacingAt(Moment) : 0f;

    /// <summary>
    /// Running on the ground with the keys ahead (<paramref name="input"/>, their way <paramref name="wanted"/> in the world)
    /// turned far enough from the way the body runs: the turn begins. <paramref name="bodyYaw"/>: the way the body faces (world
    /// yaw), which tells the side the view went round.
    /// </summary>
    internal bool TryStart(Vector2 input, Vector3 wanted, float bodyYaw)
    {
        var settings = ctx.AbilitySettings;
        Vector3 now = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        float speed = now.magnitude;
        if (speed >= settings.runTurnSpeed && Vector3.Angle(now, Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up)) <= AheadWithin)
            ranAheadAt = Time.time;
        if (active || ctx.CurrentState != State.Standing || !ctx.IsGrounded || input.y <= .1f
            || Vector2.Angle(input, Vector2.up) > AheadWithin || wanted.sqrMagnitude < .01f) return false;
        if (speed < settings.runTurnSpeed || Vector3.Angle(now, wanted) < settings.runTurnAngle
            || Time.time - ranAheadAt > RanAheadWithin) return false;
        // Round the way the body is behind the view (the way the view went round), else the short way.
        float view = ctx.transform.eulerAngles.y, behind = Mathf.DeltaAngle(view, bodyYaw);
        bool left = Mathf.Abs(behind) > 1f ? behind > 0f : Vector3.SignedAngle(now, wanted, Vector3.up) < 0f;
        bool sprint = ctx.Actions.Gait.Position >= SprintFrom, leftFoot = Mathf.Repeat(ctx.StridePhase, 1f) < .5f;
        int index = (sprint ? 4 : 0) + (left ? 0 : 2) + (leftFoot ? 0 : 1);
        string clip = ClipAt(index);
        var motion = ctx.MotionOf(clip);
        if (motion == null) return false;
        Begin(index, clip, EntryOf(motion, settings.runTurnEntry), view, motion);
        inWay = now / speed;
        inSpeed = speed;
        Count++;
        return true;
    }

    /// <summary>Another PC's turn, begun on its pose's count: the same clip from the same moment, ending the same way.</summary>
    internal void Mirror(int index, float from, float endYaw)
    {
        string clip = ClipAt(index);
        var motion = ctx.MotionOf(clip);
        if (motion != null) Begin(index, clip, from, endYaw, motion);
    }

    /// <summary>
    /// This frame's velocity while the turn goes on (<paramref name="wanted"/>: the way the keys want, in the world): braking the
    /// old way, then pushing off along the keys at the clip's pace. False once it is over or given up; the gait takes the pace
    /// over (Stop).
    /// </summary>
    internal bool Go(Vector3 wanted, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        var motion = Motion;
        if (!Turning || motion == null || ctx.CurrentState != State.Standing || !ctx.IsGrounded)
        {
            Stop();
            return false;
        }
        float t = Moment;
        if (t < brakeTo)
        {
            velocity = inWay * (inSpeed * Mathf.Clamp01(1f - (t - From) / Mathf.Max(.01f, brakeTo - From)));
            return true;
        }
        Vector3 way = wanted.sqrMagnitude > .01f ? wanted.normalized : Quaternion.Euler(0f, EndYaw, 0f) * Vector3.forward;
        velocity = way * motion.PaceAt(t);
        return true;
    }

    /// <summary>The turn is over or given up: the gait takes the pace over and comes down from it (MovementGait.Carry).</summary>
    internal void Stop()
    {
        if (!active) return;
        active = false;
        if (ctx.IsGrounded) ctx.Actions.Gait.Carry(Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up).magnitude);
    }

    /// <summary>Gone with nothing handed over (a new life).</summary>
    internal void Reset() => active = false;

    private void Begin(int index, string clip, float from, float endYaw, ClipMotion motion)
    {
        active = true;
        Index = index;
        Clip = clip;
        From = from;
        EndYaw = endYaw;
        startedAt = Time.time;
        float end = Faces(motion, motion.Length), step = 1f / ReadRate;
        // Where the clip's root comes to its turn: furthest on the way it began.
        float turnsAt = 0f, furthest = float.NegativeInfinity;
        for (float t = 0f; t <= motion.Length; t += step)
        {
            float ahead = motion.WayAt(t).y;
            if (ahead > furthest) { furthest = ahead; turnsAt = t; }
        }
        brakeTo = Mathf.Max(turnsAt, from + BrakeLeast);
        float round = motion.Length;
        for (float t = from; t <= motion.Length; t += step)
            if (Mathf.Abs(end - Faces(motion, t)) <= AroundWithin) { round = t; break; }
        outAt = motion.StepAfter(Mathf.Max(round, brakeTo), out float footfall) ? footfall : motion.Length;
    }

    // The first moment the clip's chest is within `within` degrees of the way it ends.
    private static float EntryOf(ClipMotion motion, float within)
    {
        float end = Faces(motion, motion.Length);
        for (float t = 0f; t < motion.Length; t += 1f / ReadRate)
            if (Mathf.Abs(end - Faces(motion, t)) <= within) return t;
        return 0f;
    }

    // The clip in this place of Ability.runTurns; "" for none.
    private string ClipAt(int index)
    {
        var clips = (ctx.AbilitySettings.runTurns ?? "").Split(';');
        return index >= 0 && index < clips.Length ? clips[index].Trim() : "";
    }
}
