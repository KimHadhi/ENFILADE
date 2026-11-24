using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// A run's turn back: running at least Ability.runTurnSpeed with the keys ahead, the way the keys want at least
/// Ability.runTurnAngle from the way the body runs (the view flicked round), the body plants, brakes and pushes off the new way
/// on BIPER Female Runner's own turn (Jog_ and Sprint_Turn{L,R}_{L,R}foot, Ability.runTurns: the sprint's from half way between
/// the jog and the sprint, turning the way the body is behind the view, planting the foot that is down), entered at its last
/// moment before its root turns where its chest is twisted no more than Ability.runTurnEntry against its root (EntryOf).
/// The clip moves the body (root motion): the capsule goes the way and at the pace the clip's own root goes, turned into the
/// world from where the body faced (StartYaw), and the body's root turns as the clip's root turns, so the body moves as the
/// clip's feet were made to and a foot that is down stays down where the clip put it. The one bend (motion warping): so the
/// turn ends with the chest where the view looked as it began (EndYaw), the root turns up to MostWarp more or less than the
/// clip's own turn, spread over the clip's own turning, the way it goes bending with it: a body that had already turned part
/// of the way after a flicked view (as far as the head lets it lag, PlayerBody) turns the rest. A turn the clip can't make
/// within that is no turn back, and the run comes round keeping its pace as on any other change of way. The speed the body
/// came in at goes over to the clip's own in EntryBlend. 0.9.412 laid the clip from the way the run went instead, which
/// turned a body already half round back away from the view at once, and the head, the neck and the arms swung round 105 to
/// 175 degrees in a frame (the pops record of 2026-11-24 23:40; the user: "더 괴랄해졌는데요 … 튕기고 터지고").
/// It hands over at the clip's first footfall once its root is done turning and its chest is back over its root
/// (ChestAligned): the walk or run takes the stride there (PlayerBody.StepFrom) at the clip's own pace (MovementGait.Carry),
/// with nothing to turn back. Jumping, sliding, crouching or leaving the ground gives it up. The other PCs start the same turn
/// on the pose's count and play it on their own clock (MovementMirror).
/// Until 0.9.412 the turn made up its own path: the capsule braked in a straight line the old way, then pushed off along the
/// keys at the clip's pace, the root turned up to 1.25 times the clip's turn, and at the end the root was put under the chest
/// at once. The body then went otherwise than the clip's feet, and what kept them on the ground (a planted foot held, a let-go
/// foot caught up, the root's jump eased away) threw them about at every turn: 75 cm and 171 degrees in a frame, a knee
/// flipped, 15 to 25 cm at the hand-over (the user, 2026-11-24: "그냥 클립 모션대로 하면 되는 거 아닌가?"; the animation and
/// gameplay experts' review: the turn's own path was where it all began).
/// Until 0.9.386 a flick of the view on the run swung the run round at full speed in a loop: the body turned with the view in
/// the frame and its legs ran the back and side sprints, then the walk's clips at 5 m/s, then banked into the arc (the user:
/// "달리다가 바로 반대편으로 돌면 캐릭터 모션이 굉장히 우스꽝스러워지는데 turn 애니메이션 넣어야 하나?").
/// Only a run that went ahead of the view a moment before turns back so (RanAheadWithin): running backwards and pressing ahead
/// is a strafe's reversal, the body already facing the way the keys want.
/// </summary>
public sealed class RunTurn
{
    // The keys count as ahead within this of straight on (degrees).
    private const float AheadWithin = 45f;
    // From here on the gait's ladder (MovementGait.Position: 1 the jog, 2 the sprint) it is the sprint's turn.
    private const float SprintFrom = 1.5f;
    // Its root done turning, its chest within this of its root (degrees), the turn hands over at the clip's next footfall.
    private const float ChestAligned = 10f;
    // The clips are read at this many moments a second.
    private const float ReadRate = 60f;
    // A clip's root has not begun, or is done, turning while it is within this of where it began or ends (degrees).
    private const float RootStill = 2f;
    // The most the root turns past or short of the clip's own turn to end where the view looked (degrees).
    private const float MostWarp = 90f;
    // The speed the body came in at goes over to the clip's own in this long (s).
    private const float EntryBlend = .15f;
    // The clip's root velocity is read over this long either side of its moment (s): the root sways over the feet.
    private const float WayOver = .08f;
    // A turn back is of a run that went ahead of the view this short a while ago (s): the view flicked round from it. Running
    // backwards and pressing ahead is no turn: the body already faces the way the keys want.
    private const float RanAheadWithin = .3f;

    private readonly PlayerMovement ctx;
    private float startedAt, outAt, warp, frameYaw, ranAheadAt = float.NegativeInfinity;
    /// <summary>Turns begun on this PC, its own or mirrored: a new one, even on the same clip (PlayerBody.RunTurnPose).</summary>
    public int Serial { get; private set; }
    // How much faster and which other way than the clip at its entry the body came in (world, m/s), fading over EntryBlend.
    private Vector3 entryOff;
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
    /// <summary>The way the body faced as the turn began (world yaw, degrees): where its root comes in.</summary>
    public float StartYaw { get; private set; }
    /// <summary>Turns begun, counted for the pose (MovementMirror).</summary>
    public byte Count { get; private set; }
    /// <summary>The clip's moment now (s), on this PC's own clock.</summary>
    public float Moment => From + (Time.time - startedAt);

    private ClipMotion Motion => ctx.MotionOf(Clip);

    // How far the clip's own root has turned since the turn began (degrees, + to the right).
    private float ClipTurned(ClipMotion motion, float time) => motion.TurnedAt(time) - motion.TurnedAt(From);

    // How much of the root's bend past the clip's turn is done by the clip's moment: as much as the clip's own turn to the
    // hand-over is.
    private float Warped(ClipMotion motion, float time)
    {
        float whole = ClipTurned(motion, outAt);
        return Mathf.Abs(whole) > 1f ? warp * Mathf.Clamp01(ClipTurned(motion, time) / whole) : warp;
    }

    /// <summary>Which way the body's root faces now (world yaw, degrees): from where the body faced, the clip's own turn and its
    /// bend.</summary>
    public float RootYaw
    {
        get
        {
            var motion = Motion;
            return motion == null ? EndYaw : StartYaw + ClipTurned(motion, Moment) + Warped(motion, Moment);
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
        // The turn is the clip's only if its own turn, bent by no more than MostWarp, brings the chest round to the view.
        float from = EntryOf(motion, settings.runTurnEntry);
        if (Mathf.Abs(Bend(motion, from, HandOver(motion, from), bodyYaw, view)) > MostWarp) return false;
        Begin(index, clip, from, view, bodyYaw, motion);
        entryOff = now - ClipVelocity(motion, From);
        Count++;
        return true;
    }

    /// <summary>Another PC's turn, begun on its pose's count: the same clip from the same moment, from the same facing to the
    /// same end.</summary>
    internal void Mirror(int index, float from, float endYaw, float startYaw)
    {
        string clip = ClipAt(index);
        var motion = ctx.MotionOf(clip);
        if (motion != null) Begin(index, clip, from, endYaw, startYaw, motion);
    }

    /// <summary>This frame's velocity while the turn goes on: the clip's own root's, the speed the body came in at fading into
    /// it. False once it is over or given up; the gait takes the pace over (Stop).</summary>
    internal bool Go(out Vector3 velocity)
    {
        velocity = Vector3.zero;
        var motion = Motion;
        if (!Turning || motion == null || ctx.CurrentState != State.Standing || !ctx.IsGrounded)
        {
            Stop();
            return false;
        }
        float t = Moment, fade = 1f - Mathf.Clamp01((t - From) / EntryBlend);
        velocity = ClipVelocity(motion, t) + entryOff * (fade * fade * (3f - 2f * fade));
        return true;
    }

    // The clip's root velocity at its moment in the world (m/s): its way in the frame it began in, read over WayOver either side,
    // turned to where that frame lies for this turn and by the root's bend so far.
    private Vector3 ClipVelocity(ClipMotion motion, float time)
    {
        float from = Mathf.Clamp(time - WayOver, 0f, motion.Length), to = Mathf.Clamp(time + WayOver, 0f, motion.Length);
        Vector2 way = to - from > 1e-3f ? (motion.WayAt(to) - motion.WayAt(from)) / (to - from) : Vector2.zero;
        return Quaternion.Euler(0f, frameYaw + Warped(motion, time), 0f) * new Vector3(way.x, 0f, way.y);
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

    private void Begin(int index, string clip, float from, float endYaw, float startYaw, ClipMotion motion)
    {
        active = true;
        Serial++;
        Index = index;
        Clip = clip;
        From = from;
        EndYaw = endYaw;
        StartYaw = startYaw;
        startedAt = Time.time;
        entryOff = Vector3.zero;
        // The world's yaw of the frame the clip began in: the root at From faces where the body faced.
        frameYaw = startYaw - motion.TurnedAt(from);
        outAt = HandOver(motion, from);
        warp = Mathf.Clamp(Bend(motion, from, outAt, startYaw, endYaw), -MostWarp, MostWarp);
    }

    // The hand-over (s): the clip's first footfall once its root is done turning and its chest is back over its root.
    private static float HandOver(ClipMotion motion, float from)
    {
        float step = 1f / ReadRate, end = motion.TurnedAt(motion.Length), turned = motion.Length, aligned = motion.Length;
        for (float t = from; t <= motion.Length; t += step)
            if (Mathf.Abs(Mathf.DeltaAngle(end, motion.TurnedAt(t))) <= RootStill) { turned = t; break; }
        for (float t = turned; t <= motion.Length; t += step)
            if (Mathf.Abs(motion.FacingAt(t)) <= ChestAligned) { aligned = t; break; }
        return motion.StepAfter(aligned, out float footfall) ? footfall : motion.Length;
    }

    // The bend (degrees): what the root turns past or short of the clip's own turn from `from` to `to` for the chest to end
    // facing `endYaw` from a root that began at `startYaw` (the long way round where the clip turns that way).
    private static float Bend(ClipMotion motion, float from, float to, float startYaw, float endYaw)
    {
        float turn = motion.TurnedAt(to) - motion.TurnedAt(from);
        float need = Mathf.DeltaAngle(startYaw, endYaw - motion.FacingAt(to));
        if (need * turn < 0f && Mathf.Abs(need) > 90f) need -= 360f * Mathf.Sign(need);
        return need - turn;
    }

    // The last moment before the clip's root begins to turn at which its chest is twisted no more than `within` degrees against
    // its root (its start if none): the running body comes into it as it was, root and chest, with nothing to swing round.
    private static float EntryOf(ClipMotion motion, float within)
    {
        float entry = 0f, start = motion.TurnedAt(0f);
        for (float t = 0f; t < motion.Length; t += 1f / ReadRate)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(start, motion.TurnedAt(t))) > RootStill) break;
            if (Mathf.Abs(motion.FacingAt(t)) <= within) entry = t;
        }
        return entry;
    }

    // The clip in this place of Ability.runTurns; "" for none.
    private string ClipAt(int index)
    {
        var clips = (ctx.AbilitySettings.runTurns ?? "").Split(';');
        return index >= 0 && index < clips.Length ? clips[index].Trim() : "";
    }
}
