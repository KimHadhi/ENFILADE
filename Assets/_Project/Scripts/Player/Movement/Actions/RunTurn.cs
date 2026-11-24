using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// A run's turn back: running at least Ability.runTurnSpeed with the keys ahead, the way the keys want at least
/// Ability.runTurnAngle from the way the body runs, the view gone round ahead of the body by ViewLedLeast (the view flicked
/// round on the run), the body plants, brakes and pushes off the new way
/// on BIPER Female Runner's own turn (Jog_ and Sprint_Turn{L,R}_{L,R}foot, Ability.runTurns: the sprint's from half way between
/// the jog and the sprint, turning the way the body is behind the view), come into where its stride stands as the run's does:
/// of the way round's two clips (the pivot on either foot) and their moments before the root turns with the chest twisted no
/// more than Ability.runTurnEntry against it, the one whose own stride (ClipMotion.StepAt) stands nearest the run's now, the
/// latest of those as near (EntryOf). The legs go on into the clip as they were. Until 0.9.419 the clip was the one pivoting
/// on the foot that was down, come into at its last such moment whatever its legs did there: the run's legs went over to a
/// pose up to half a stride off within the 0.15 s blend (BodyInertia), and the planted foot swept the ground at 10 to 12 m/s
/// for a tenth of a second at every turn (the feet record of 2026-11-25 02:19; the user: "turn 애니메이션 나올 때 … 한 프레임
/// 정도 … 갑작스럽게 움직이는 느낌").
/// The clip moves the body (root motion): the capsule goes the way and at the pace the clip's own root goes, turned into the
/// world from where the body faced (StartYaw), and the body's root turns as the clip's root turns, so the body moves as the
/// clip's feet were made to and a foot that is down stays down where the clip put it. The one bend (motion warping): so the
/// turn ends with the chest where the view looked as it began (EndYaw), the root's whole turn is what the body needs from where
/// it faced, the clip's own turn drawn out or in to it over the clip's own turning, the way it goes bending with it: a body
/// that had already turned part of the way after a flicked view (as far as the head lets it lag, PlayerBody) turns the rest,
/// on the clip's own footwork. 0.9.412 and 0.9.413 took the clip only where its turn, bent by no more than 30 and then 90
/// degrees, came round to the view: by then the body had turned 45 to 90 degrees of a 100 to 140 degree flick, the clip's
/// 180 never fitted, and the run came round as before 0.9.386, the legs sprinting sideways and backwards (StrafeSprint_R135,
/// _B) while the body swung up to 65 degrees a frame (the gait and pops records of 2026-11-24 23:53; the user: "모션이
/// 전반적으로 … 우스꽝스러워졌는데"). The speed the body came in at goes over to the clip's own in EntryBlend. 0.9.412 laid
/// the clip from the way the run went instead, which
/// turned a body already half round back away from the view at once, and the head, the neck and the arms swung round 105 to
/// 175 degrees in a frame (the pops record of 2026-11-24 23:40; the user: "더 괴랄해졌는데요 … 튕기고 터지고").
/// It hands over at the clip's first footfall once its root is done turning and its chest is back over its root
/// (ChestAligned): the walk or run takes the stride there (PlayerBody.StepFrom) at the clip's own pace (MovementGait.Carry),
/// with nothing to turn back. Jumping, sliding, crouching or leaving the ground gives it up, and so does the player changing
/// their mind: the keys asking a way more than GiveUpWithin off the one it turns to (the view flicked back the way it came),
/// and the run goes on the way they ask from where the turn had got to. Until 0.9.424 it played on to its end, turning the
/// body round to where the view no longer was, and the head was left to turn back past the shoulder to the view behind it
/// (the user, 2026-11-25: "돌리고 나서 잉 구라야 사실 그쪽으로 안 갈 거야 하고 다시 원방향을 볼 때 문제가 발생하는 건데").
/// The other PCs start the same turn on the pose's count, play it on their own clock and give it up as the pose stops
/// saying it goes on (MovementMirror, PlayerPose.Marks.Turning).
/// Until 0.9.412 the turn made up its own path: the capsule braked in a straight line the old way, then pushed off along the
/// keys at the clip's pace, the root turned up to 1.25 times the clip's turn, and at the end the root was put under the chest
/// at once. The body then went otherwise than the clip's feet, and what kept them on the ground (a planted foot held, a let-go
/// foot caught up, the root's jump eased away) threw them about at every turn: 75 cm and 171 degrees in a frame, a knee
/// flipped, 15 to 25 cm at the hand-over (the user, 2026-11-24: "그냥 클립 모션대로 하면 되는 거 아닌가?"; the animation and
/// gameplay experts' review: the turn's own path was where it all began).
/// Until 0.9.386 a flick of the view on the run swung the run round at full speed in a loop: the body turned with the view in
/// the frame and its legs ran the back and side sprints, then the walk's clips at 5 m/s, then banked into the arc (the user:
/// "달리다가 바로 반대편으로 돌면 캐릭터 모션이 굉장히 우스꽝스러워지는데 turn 애니메이션 넣어야 하나?").
/// Only after the view went round: a body that faces the view (keys rolled round, running backwards and pressing ahead) has
/// nothing to turn, and goes through a stop the other way. Until 0.9.427 the keys alone started it, the clip's 180 degrees
/// were bent back to nothing to end facing the view, and its legs pivoted on the spot, the planted foot sweeping the ground at
/// 3 to 13 m/s for up to 0.6 s (10 of 12 turns in the feet record of 2026-11-25 06:46; the user: "이게 뭐냐고 시발
/// 날라다니잖아"); from 0.9.427 to 0.9.428 the hips' facing told it, a run ahead's hips facing the way it ran.
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
    // Moments whose stride stands within this share of a stride of the nearest are as near: the latest is come into (EntryOf).
    private const float PhaseTie = .04f;
    // The keys asking a way further than this off the one the turn turns to (degrees) give it up (Go).
    private const float GiveUpWithin = 90f;
    // The view gone round at least this far ahead of the body (degrees): it was flicked, not the keys turned (TryStart).
    private const float ViewLedLeast = 30f;
    // The speed the body came in at goes over to the clip's own in this long (s).
    private const float EntryBlend = .15f;
    // The clip's root velocity is read over this long either side of its moment (s): the root sways over the feet.
    private const float WayOver = .08f;

    private readonly PlayerMovement ctx;
    private float startedAt, outAt, warp, frameYaw;
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
    /// turned far enough from the way the body runs, and the view gone round ahead of the way the body faces
    /// (<paramref name="bodyYaw"/>, world yaw): the turn begins, round the side the view went.
    /// </summary>
    internal bool TryStart(Vector2 input, Vector3 wanted, float bodyYaw)
    {
        var settings = ctx.AbilitySettings;
        Vector3 now = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        float speed = now.magnitude;
        if (active || ctx.CurrentState != State.Standing || !ctx.IsGrounded || input.y <= .1f
            || Vector2.Angle(input, Vector2.up) > AheadWithin || wanted.sqrMagnitude < .01f) return false;
        // A run that goes ahead on its legs (the way it goes within AheadWithin of the way the body faces): a turn back is the
        // turn of a run ahead, not of a strafe or a backpedal.
        float view = ctx.transform.eulerAngles.y, behind = Mathf.DeltaAngle(view, bodyYaw);
        if (speed < settings.runTurnSpeed || Vector3.Angle(now, wanted) < settings.runTurnAngle
            || Mathf.Abs(behind) < ViewLedLeast
            || Vector3.Angle(now, Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.forward) > AheadWithin) return false;
        // Round the way the body is behind the view (the way the view went round), else the short way.
        bool left = Mathf.Abs(behind) > 1f ? behind > 0f : Vector3.SignedAngle(now, wanted, Vector3.up) < 0f;
        // Of the way round's two clips (the pivot on the left foot or the right), the one and the moment whose stride stands
        // nearest where the run's stands now (EntryOf), so the legs go on into it as they were.
        bool sprint = ctx.Actions.Gait.Position >= SprintFrom;
        int first = (sprint ? 4 : 0) + (left ? 0 : 2), index = -1;
        float from = 0f, apart = float.MaxValue, phase = Mathf.Repeat(ctx.StridePhase, 1f);
        ClipMotion motion = null;
        for (int foot = 0; foot < 2; foot++)
        {
            var candidate = ctx.MotionOf(ClipAt(first + foot));
            if (candidate == null) continue;
            float at = EntryOf(candidate, settings.runTurnEntry, phase, out float off);
            if (off >= apart) continue;
            apart = off;
            from = at;
            index = first + foot;
            motion = candidate;
        }
        if (motion == null) return false;
        string clip = ClipAt(index);
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
    /// it. False once it is over or given up, <paramref name="wanted"/> (the keys' way in the world, none for no keys) gone
    /// back more than GiveUpWithin off the way it turns to included; the gait takes the pace over (Stop).</summary>
    internal bool Go(Vector3 wanted, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        var motion = Motion;
        bool changedMind = wanted.sqrMagnitude > .01f
            && Vector3.Angle(wanted, Quaternion.Euler(0f, EndYaw, 0f) * Vector3.forward) > GiveUpWithin;
        if (!Turning || motion == null || ctx.CurrentState != State.Standing || !ctx.IsGrounded || changedMind)
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
        warp = Bend(motion, from, outAt, startYaw, endYaw);
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

    // The moment to come into the clip at: before its root begins to turn, its chest twisted no more than `within` degrees
    // against its root (the running body comes in as it was, root and chest, nothing to swing round), the one whose own stride
    // (ClipMotion.StepAt) stands nearest `phase`, the run's now (0 the left foot about to come down, .5 the right), the latest
    // within PhaseTie of that (the sooner it turns). `apart`: how far that one's stride is off the run's (a share of a stride).
    // A clip whose feet never step comes in at its last such moment.
    private static float EntryOf(ClipMotion motion, float within, float phase, out float apart)
    {
        float start = motion.TurnedAt(0f), nearest = float.MaxValue, step = 1f / ReadRate, entry = 0f;
        apart = 1f;
        for (int pass = 0; pass < 2; pass++)
            for (float t = 0f; t < motion.Length; t += step)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(start, motion.TurnedAt(t))) > RootStill) break;
                if (Mathf.Abs(motion.FacingAt(t)) > within) continue;
                float off = motion.StrideOff(t, phase);
                if (pass == 0) nearest = Mathf.Min(nearest, off);
                else if (off <= nearest + PhaseTie)
                {
                    entry = t;
                    apart = off;
                }
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
