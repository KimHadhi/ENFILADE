using UnityEngine;

/// <summary>
/// The feet-first slide on one of BIPER Female Runner's whole slides (Ability.slideClips: SlideToRun_5m, SlideToRun_10m: down
/// into it, along, and up running), on the experts' speed contract (2026-11-24):
/// - Which: as it begins, the clip whose own slide (from its body down to its body beginning to rise) is nearest how far this
///   one would go at the speed it comes in with, slowing as that clip does on the whole; one going further than both takes the
///   longer.
/// - Down into it [0, Down] on the clip's own time: the body at the clip's smoothed pace (ClipMotion.GlideAt) times the speed it
///   came in with over the clip's there (Ability.travelWarp), so a sprint's speed goes on into the slide.
/// - Along it [Down, Rises], keys held: on the clip's own time too, the body keeping that share of the clip's own speed as the
///   clip slows hard once down and then glides, the slope's pull on top, no faster than Move.slideMaxSpeed. While the slope
///   pulls it on harder than its clip slows, or it goes faster than travelWarp's most of the clip's own speed (down stairs, a
///   long slope, a boost), the body holds on the slide's loop (Ability.slideHold) at the middle of the clip's slide, slowing as
///   the clip does on the whole, and goes on in the clip from there once neither is so (Holds).
/// - With no keys, Move.slideFriction slows it and the body holds in the slide until it stops (PlayerBody.SlideStops).
/// - Up out of it [Rises, the first footfall once its legs are under it] on the clip's time, its share of the clip's speed going
///   from the slide's to the rate that brings its run-out to the gait the keys ask for (Ability.runOutRate), where the walk or
///   run takes over at its speed (MovementGait.Carry). The pose's pace comes into it from the slide's (Advance).
/// The other PCs play the same clip at the same moment (MovementMirror).
/// Until 0.9.459 the body slowed evenly along it (by its clip's slowing on the whole) and the clip's moment was matched to the
/// distance left to its getting up (distance matching, at half to twice its own pace): the clip slows hard just after its body
/// is down (7.7 to 4.4 m/s) and then glides at 3.4 to 4.3, so the pose went at 0.6 of its pace for the first half second, then
/// at up to twice it, while the body slowed evenly (the slide trace of 2026-11-26; the user, asked whether to end that:
/// "남은 것도 뭐 해 줘").
/// Until 0.9.395 a slide played SlideToRun_Enter, then a cut of SlideToRun_10m's middle swaying on a sine, then SlideToRun_Exit,
/// at a speed that eased in a line from the run's to the getting up's whatever the clips did; Shift with W stood it up at once
/// into a sprint; SlideToRun_5m went unused (the user, 2026-11-24: "슬라이딩은 5m 모션이랑 10m 모션 있잖아 … 계단에서 내려갈 때
/// 슬라이딩은 기니까 모션이 길 수도 … 속도 모션에 맞게 빨랐으면 좋겠어").
/// </summary>
public sealed class SlideMove
{
    /// <summary>Where in its clip the slide is: down into it, along it, held on the loop, up out of it, or over (got up).</summary>
    public enum Phase { Down, Along, Held, Up, Over }

    // The least a clip's own slide slows by on the whole (m/s²), so one that hardly slows still ends; a slope pulling to within
    // NoEnd of that holds the slide on its loop (Holds).
    private const float LeastSlowing = .5f, NoEnd = .2f;
    // The slope's pull is followed this quickly (s), so a stair's edges don't jolt the slide.
    private const float SlopeFollow = .2f;
    // The clip's pace (a share of its own) changes by no more than this a second, from one part of the slide to the next too
    // (Advance): from the slide's to the getting up's in a frame, the pose jolted (0.9.457).
    private const float PaceChange = 3f;
    // A copy's moment goes on between poses at the clip's pace, and is set again by one further off than this (s).
    private const float MirrorSlack = .15f;

    private readonly PlayerMovement ctx;
    // The share of the clip's pace it went down into it at, the rate its run-out plays at, the share of the clip's speed the
    // body had as it began to rise, the slope's pull followed (m/s²), and the clip's pace (a share of its own).
    private float warp = 1f, rate = 1f, risen = 1f, pull, along = 1f;

    internal SlideMove(PlayerMovement context) { ctx = context; }

    /// <summary>The clip's place in Ability.slideClips (-1 none), its name, its moment (s) and where in it the slide is.</summary>
    public int Clip { get; private set; } = -1;
    public string ClipName => NameAt(Clip);
    public float Moment { get; private set; }
    public Phase Now { get; private set; } = Phase.Over;
    /// <summary>The loop the body holds on while more of the slide is left than its clip shows.</summary>
    public string HoldClip => ctx.AbilitySettings.slideHold;

    private ClipMotion Motion => ctx.MotionOf(ClipName);

    private string NameAt(int index)
    {
        var names = (ctx.AbilitySettings.slideClips ?? "").Split(';');
        return index >= 0 && index < names.Length ? names[index].Trim() : "";
    }

    /// <summary>A slide begins, the body coming in at <paramref name="carried"/> (m/s): its clip is chosen, and the speed it
    /// begins at (m/s) is given back.</summary>
    internal float Begin(float carried)
    {
        Moment = pull = 0f;
        rate = warp = risen = along = 1f;
        Now = Phase.Down;
        Clip = -1;
        int count = (ctx.AbilitySettings.slideClips ?? "").Split(';').Length;
        float best = float.PositiveInfinity, longest = 0f;
        for (int i = 0; i < count; i++)
        {
            var motion = ctx.MotionOf(NameAt(i));
            if (motion == null) continue;
            float k = Warp(carried, motion.GlideAt(0f));
            float shown = motion.GlidedAt(motion.Rises) - motion.GlidedAt(motion.Down);
            float going = Going(k * motion.GlideAt(motion.Down), motion);
            float score = shown <= .01f ? 1e6f : Mathf.Abs(Mathf.Log(Mathf.Max(.01f, going) / shown));
            if (score < best - 1e-3f || (Mathf.Abs(score - best) <= 1e-3f && shown > longest))
            {
                best = score;
                longest = shown;
                Clip = i;
                warp = k;
            }
        }
        var chosen = Motion;
        if (chosen == null)
        {
            Now = Phase.Over;
            return carried;
        }
        return warp * chosen.GlideAt(0f);
    }

    /// <summary>This frame's speed (m/s) from the speed it had, its clip's moment going on with it: <paramref name="keys"/> held
    /// on, a brake's slowing (m/s²), the slope's pull along the slide (m/s², + downhill) and how much of the slowing the ground
    /// gives (1 on it, less in the air).</summary>
    internal float Step(float dt, float speed, bool keys, float brake, float slope, float grip)
    {
        var motion = Motion;
        if (motion == null || dt <= 0f || Now == Phase.Over) return speed;
        pull = Mathf.Lerp(pull, slope, 1f - Mathf.Exp(-dt / SlopeFollow));
        // From one part of the slide to the next the moment goes on past where the part ends: held there, the pose stood still for
        // the rest of that frame.
        if (Now == Phase.Down)
        {
            Moment += dt;
            if (Moment >= motion.Down) Now = Phase.Along;
            return warp * motion.GlideAt(Moment);
        }
        if (Now == Phase.Up)
        {
            float share = motion.LegsUnder > motion.Rises ? Mathf.Clamp01((Moment - motion.Rises) / (motion.LegsUnder - motion.Rises)) : 1f;
            float r = Mathf.Lerp(risen, rate, share * share * (3f - 2f * share));
            // The getting up comes in from the pace the slide had (Advance), the body at the share of the clip's speed it had as
            // it began to rise, so its speed carries on from the slide's. Until 0.9.457 its pace went from the slide's to its own
            // in a frame, and the slide's last moments in slow motion (distance matching held at half pace) turned into the
            // getting up at full pace (the slide trace of 2026-11-26; the user: "슬라이딩 중과 슬라이딩 마무리 모션 전환").
            Advance(r, dt);
            if (Moment >= ExitAt(motion)) Now = Phase.Over;
            return r * motion.GlideAt(Moment);
        }
        float hold = (motion.Down + motion.Rises) * .5f;
        if (!keys)
        {
            // Let go: slowed by the slide's friction, on to the middle of the slide at the clip's own pace and held there until
            // it stops.
            speed = Mathf.Max(0f, speed - (ctx.SlideFriction * grip - pull + brake) * dt);
            if (Now == Phase.Held) return speed;
            if (Moment < hold)
            {
                Advance(1f, dt);
                Moment = Mathf.Min(hold, Moment);
                if (Moment >= hold) Now = Phase.Held;
            }
            return speed;
        }
        if (Now == Phase.Held)
        {
            // Held on the loop it slows as its clip does on the whole, the slope's pull on top.
            if (Holds(motion, speed, hold))
                return Mathf.Clamp(speed - (Slowing(motion) * grip - pull + brake) * dt, 0f, ctx.SlideMaxSpeed);
            Now = Phase.Along;
            Moment = hold;
            along = 1f;
        }
        // Along it: the clip on its own time, and the body keeping its share of the clip's speed as the clip slows hard once
        // down and then glides (off the ground, the grip's share of the clip's change), the slope's pull and a brake on top.
        float before = Moment, was = motion.GlideAt(Moment);
        Advance(1f, dt);
        float now = motion.GlideAt(Moment);
        if (was > .1f) speed *= 1f + grip * (now / was - 1f);
        speed = Mathf.Clamp(speed + (pull - brake) * dt, 0f, ctx.SlideMaxSpeed);
        if (before < hold && Moment >= hold && Holds(motion, speed, hold))
        {
            Now = Phase.Held;
            Moment = hold;
            return speed;
        }
        if (Moment >= motion.Rises)
        {
            Now = Phase.Up;
            rate = RunOutRate(motion);
            risen = now > .1f ? speed / now : 1f;
        }
        return speed;
    }

    // The clip's moment goes on this frame at the pace this part of the slide asks for (a share of the clip's own): along it and
    // let go on to the hold its own, getting up the run-out's. The pace comes to it no quicker than PaceChange, from one part to
    // the next too, so the pose never changes pace in a frame.
    private void Advance(float pace, float dt)
    {
        along = Mathf.MoveTowards(along, pace, PaceChange * dt);
        Moment += along * dt;
    }

    // Whether the slide holds on the loop at the middle of its clip (hold): the slope pulls it on harder than its clip slows on
    // the whole, or it goes faster than travelWarp's most of the clip's own speed there, more than the clip can carry.
    private bool Holds(ClipMotion motion, float speed, float hold)
    {
        Vector2 warps = ctx.AbilitySettings.travelWarp;
        return Slowing(motion) - pull < NoEnd || speed > Mathf.Max(warps.x, warps.y) * motion.GlideAt(hold);
    }

    /// <summary>Another PC's slide as its pose says: the same clip, where in it and its moment, going on between poses at the
    /// clip's pace (<paramref name="fresh"/>: the slide has just begun there).</summary>
    internal void Mirror(int clip, Phase phase, float moment, bool fresh)
    {
        if (fresh || clip != Clip || phase == Phase.Held || Now == Phase.Held)
        {
            Clip = clip;
            Now = phase;
            Moment = moment;
            return;
        }
        Now = phase;
        float on = Moment + Time.deltaTime;
        Moment = Mathf.Abs(on - moment) > MirrorSlack ? moment : on;
    }

    // The share of a clip's pace the body comes in at (Ability.travelWarp).
    private float Warp(float carried, float pace)
    {
        if (pace < .1f) return 1f;
        Vector2 warps = ctx.AbilitySettings.travelWarp;
        return Mathf.Clamp(carried / pace, Mathf.Min(warps.x, warps.y), Mathf.Max(warps.x, warps.y));
    }

    // How far a slide coming down at this speed would go before it gets up, slowing as the clip does on the whole (m): what
    // chooses the clip (Begin).
    private static float Going(float speed, ClipMotion motion)
    {
        float exit = motion.GlideAt(motion.Rises);
        return Mathf.Max(0f, (speed * speed - exit * exit) / (2f * Slowing(motion)));
    }

    // How fast the clip's own slide slows (m/s²): from its pace where its body is down to its pace as it begins to rise, over the
    // distance between.
    private static float Slowing(ClipMotion motion)
    {
        float from = motion.GlideAt(motion.Down), to = motion.GlideAt(motion.Rises);
        float shown = motion.GlidedAt(motion.Rises) - motion.GlidedAt(motion.Down);
        return shown > .01f ? Mathf.Max(LeastSlowing, (from * from - to * to) / (2f * shown)) : LeastSlowing;
    }

    // Where the slide is over: the clip's first footfall once its legs are under it, or where its body is up.
    private static float ExitAt(ClipMotion motion) => motion.StepAfter(motion.LegsUnder, out float step) ? step : motion.Up;

    // The run-out's rate: its own pace from its legs under it to its first footfall brought to the gait the keys ask for.
    private float RunOutRate(ClipMotion motion)
    {
        float exit = ExitAt(motion);
        if (exit <= motion.LegsUnder) return 1f;
        float pace = (motion.GlidedAt(exit) - motion.GlidedAt(motion.LegsUnder)) / (exit - motion.LegsUnder);
        Vector2 rates = ctx.AbilitySettings.runOutRate;
        return pace > .1f ? Mathf.Clamp(ctx.Actions.Gait.SpeedFor(ctx.CurrentInput) / pace, Mathf.Min(rates.x, rates.y), Mathf.Max(rates.x, rates.y)) : 1f;
    }
}
