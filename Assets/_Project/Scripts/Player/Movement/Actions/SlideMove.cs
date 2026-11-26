using UnityEngine;

/// <summary>
/// The feet-first slide on one of BIPER Female Runner's whole slides (Ability.slideClips: SlideToRun_5m, SlideToRun_10m: down
/// into it, along, and up running), on the experts' speed contract (2026-11-24):
/// - Which: as it begins, the clip whose own slide (from its body down to its body beginning to rise) is nearest how far this
///   one will go at the speed it comes in with, slowing as that clip does; one going further than both takes the longer.
/// - Down into it [0, Down] on the clip's own time: the body at the clip's smoothed pace (ClipMotion.GlideAt) times the speed it
///   came in with over the clip's there (Ability.travelWarp), so a sprint's speed goes on into the slide.
/// - Along it [Down, Rises], keys held: the speed slows as the clip's own does on flat ground, the slope's pull and a brake on
///   top, no faster than Move.slideMaxSpeed; the clip's moment is matched to the distance left to its getting up (distance
///   matching, at half to twice its own pace, never back), so it begins to get up just as the speed has come down to the clip's
///   own there. While more is left than the clip can still show (down stairs, a long slope) the body holds on the slide's loop
///   (Ability.slideHold) at the middle of the clip's slide, and goes on in the clip from there once what is left fits.
/// - With no keys, Move.slideFriction slows it and the body holds in the slide until it stops (PlayerBody.SlideStops).
/// - Up out of it [Rises, the first footfall once its legs are under it] on the clip's time, its run-out at the rate that brings
///   its pace to the gait the keys ask for (Ability.runOutRate), where the walk or run takes over at its speed
///   (MovementGait.Carry).
/// The other PCs play the same clip at the same moment (MovementMirror).
/// Until 0.9.395 a slide played SlideToRun_Enter, then a cut of SlideToRun_10m's middle swaying on a sine, then SlideToRun_Exit,
/// at a speed that eased in a line from the run's to the getting up's whatever the clips did; Shift with W stood it up at once
/// into a sprint; SlideToRun_5m went unused (the user, 2026-11-24: "슬라이딩은 5m 모션이랑 10m 모션 있잖아 … 계단에서 내려갈 때
/// 슬라이딩은 기니까 모션이 길 수도 … 속도 모션에 맞게 빨랐으면 좋겠어").
/// </summary>
public sealed class SlideMove
{
    /// <summary>Where in its clip the slide is: down into it, along it, held on the loop, up out of it, or over (got up).</summary>
    public enum Phase { Down, Along, Held, Up, Over }

    // The least a clip's own slide slows by (m/s²), so one that hardly slows still ends; with the slope's pull below NoEnd the
    // distance left counts as more than any clip shows.
    private const float LeastSlowing = .5f, NoEnd = .2f;
    // The slope's pull is followed this quickly (s), so a stair's edges don't jolt the match.
    private const float SlopeFollow = .2f;
    // Along the slide the clip's moment goes at least and at most this many times its own pace, and that pace changes no
    // quicker than from one end to the other in MatchEase (s).
    private const float SlowestMatch = .5f, FastestMatch = 2f, MatchEase = .5f;
    // A copy's moment goes on between poses at the clip's pace, and is set again by one further off than this (s).
    private const float MirrorSlack = .15f;

    private readonly PlayerMovement ctx;
    // The share of the clip's pace it went down into it at, the rate its run-out plays at, the slope's pull followed (m/s²),
    // and the pace along it (a share of the clip's own).
    private float warp = 1f, rate = 1f, pull, along = 1f;

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
        rate = warp = along = 1f;
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
            float going = Left(k * motion.GlideAt(motion.Down), motion, 0f);
            float score = float.IsPositiveInfinity(going) || shown <= .01f ? 1e6f : Mathf.Abs(Mathf.Log(Mathf.Max(.01f, going) / shown));
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
        if (Now == Phase.Down)
        {
            Moment = Mathf.Min(Moment + dt, motion.Down);
            if (Moment >= motion.Down) Now = Phase.Along;
            return warp * motion.GlideAt(Moment);
        }
        if (Now == Phase.Up)
        {
            float share = motion.LegsUnder > motion.Rises ? Mathf.Clamp01((Moment - motion.Rises) / (motion.LegsUnder - motion.Rises)) : 1f;
            float r = Mathf.Lerp(1f, rate, share * share * (3f - 2f * share));
            Moment += dt * r;
            if (Moment >= ExitAt(motion)) Now = Phase.Over;
            return r * motion.GlideAt(Moment);
        }
        // Along it, or held on the loop.
        speed = Mathf.Max(0f, speed - ((keys ? Slowing(motion) : ctx.SlideFriction) * grip - pull + brake) * dt);
        if (keys) speed = Mathf.Min(speed, ctx.SlideMaxSpeed);
        float hold = (motion.Down + motion.Rises) * .5f, end = motion.GlidedAt(motion.Rises), fits = end - motion.GlidedAt(hold);
        if (!keys)
        {
            // Let go: on to the middle of the slide at the clip's own pace and held there until it stops.
            if (Now == Phase.Held) return speed;
            if (Moment < hold)
            {
                Moment = Mathf.Min(hold, Moment + dt);
                if (Moment >= hold) Now = Phase.Held;
            }
            return speed;
        }
        float left = Left(speed, motion, pull);
        if (Now == Phase.Held)
        {
            if (left > fits) return speed;
            Now = Phase.Along;
            Moment = hold;
            along = 1f;
        }
        float before = Moment;
        // The pace that matches the distance left, eased into from the pace it had: down into the slide on the clip's own
        // time and then at once to the distance's (half its pace, coming in faster than the clip), the pose went into slow
        // motion in a frame as the body came down (the slide trace of 2026-11-26: the eyes dropping 3.5 m/s, then 1.8).
        float wanted = Mathf.Clamp((MomentLeft(motion, left) - Moment) / dt, SlowestMatch, FastestMatch);
        along = Mathf.MoveTowards(along, wanted, (FastestMatch - SlowestMatch) / MatchEase * dt);
        Moment += along * dt;
        if (before < hold && Moment >= hold && left > fits)
        {
            Now = Phase.Held;
            Moment = hold;
            return speed;
        }
        if (Moment >= motion.Rises)
        {
            Moment = motion.Rises;
            Now = Phase.Up;
            rate = RunOutRate(motion);
        }
        return speed;
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

    // How far the slide still goes before it gets up, at this speed, slowing as the clip does less the slope's pull (m);
    // infinite where that barely slows it.
    private static float Left(float speed, ClipMotion motion, float slope)
    {
        float exit = motion.GlideAt(motion.Rises), slowing = Slowing(motion) - slope;
        return slowing < NoEnd ? float.PositiveInfinity : Mathf.Max(0f, (speed * speed - exit * exit) / (2f * slowing));
    }

    // How fast the clip's own slide slows (m/s²): from its pace where its body is down to its pace as it begins to rise, over the
    // distance between.
    private static float Slowing(ClipMotion motion)
    {
        float from = motion.GlideAt(motion.Down), to = motion.GlideAt(motion.Rises);
        float shown = motion.GlidedAt(motion.Rises) - motion.GlidedAt(motion.Down);
        return shown > .01f ? Mathf.Max(LeastSlowing, (from * from - to * to) / (2f * shown)) : LeastSlowing;
    }

    // The clip's moment along its slide with this much of it left to its getting up.
    private static float MomentLeft(ClipMotion motion, float left)
    {
        float end = motion.GlidedAt(motion.Rises);
        if (left >= end - motion.GlidedAt(motion.Down)) return motion.Down;
        if (left <= 0f) return motion.Rises;
        float low = motion.Down, high = motion.Rises;
        for (int i = 0; i < 14; i++)
        {
            float middle = (low + high) * .5f;
            if (end - motion.GlidedAt(middle) > left) low = middle;
            else high = middle;
        }
        return (low + high) * .5f;
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
