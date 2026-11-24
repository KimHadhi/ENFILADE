using UnityEngine;

/// <summary>
/// The feet-first slide on its clips (BIPER Female Runner SlideToRun: Ability.slideStart down into it, Ability.slide along;
/// ClipMotion): it begins as fast as the body came into it, or as the clip does if that is faster, and while the keys go on it
/// slows evenly to the pace its getting up (Body.slideEnd) starts at, there as the clip's body begins to rise, where it gets
/// up; the getting up runs out at its own clip's pace (PlayerBody.RunOutPace) until the walk or run takes over at its first
/// footfall (PlayerBody.HasStepped). Gliding on the floor the feet don't step, so the speed carried in shows nowhere as a foot
/// sliding. With no keys, or bounced off a wall, friction stops it as it always has.
/// Until 0.9.351 it began a kick over the sprint's speed and slowed at one rate to 1.5 m/s, so the body got up out of it with
/// steps made for 4.7 m/s (the user, 2026-11-22: "이런 속도를 애니메이션에 맞춰 줄 수 없나?"). From then until 0.9.382 friction
/// took it down to the slide clip's own pace, 3.7 m/s, within 0.6 s: slower than the getting up after it and than any run,
/// so out of a sprint (6.1) every slide braked and the sprint sped it up again (the user, 2026-11-24: "슬라이딩 속도도 느리고 …
/// 전반적으로 통일성이 안 느껴지는", the experts' review).
/// </summary>
public sealed class SlidePace
{
    // Within this much of the clip's last pace (m/s) a slide that has come to its end gets up; a slope carries it on past it.
    private const float NearItsEnd = .3f;

    private readonly PlayerMovement ctx;
    private ClipMotion down, along;
    // How long it has gone (s), how fast it began, and the pace its getting up starts at (m/s).
    private float time, start, exit;
    private bool thrown;

    internal SlidePace(PlayerMovement context) { ctx = context; }

    /// <summary>A slide begins, the body coming into it at <paramref name="carried"/> (m/s).</summary>
    internal void Begin(float carried)
    {
        down = ctx.MotionOf(ctx.AbilitySettings.slideStart);
        along = ctx.MotionOf(ctx.AbilitySettings.slide);
        time = 0f;
        thrown = false;
        start = Mathf.Max(carried, down != null ? down.PaceAt(0f) : 0f);
        exit = ctx.SlideExitPace;
        if (exit <= 0f && along != null) exit = along.PaceAt(Rises);
    }

    /// <summary>How fast the slide begins (m/s): as the body came in, or as its clip does if that is faster.</summary>
    internal float Start => start;

    internal void Tick(float dt) => time += Mathf.Max(0f, dt);

    /// <summary>Thrown back off a wall: it is no longer its clip's slide.</summary>
    internal void Throw() => thrown = true;

    private float Entry => down != null ? down.Length : 0f;
    private float Rises => along != null ? along.Rises : 0f;

    /// <summary>The body is down in it: from here it goes along, or up and on into the run.</summary>
    internal bool IsDown => thrown || time >= Entry;

    private bool Over => thrown || time >= Entry + Rises;

    /// <summary>The slowest the keys let it go now (m/s): from its start evenly down to its getting up's pace as the clip's body
    /// begins to rise; 0 once it has, or thrown.</summary>
    internal float Speed => Over ? 0f : Mathf.Lerp(start, exit, Entry + Rises > 0f ? time / (Entry + Rises) : 1f);

    /// <summary>The clip has come to its end and the slide goes no faster than its getting up begins.</summary>
    internal bool Ends(float speed) => !thrown && Over && speed <= exit + NearItsEnd;
}
