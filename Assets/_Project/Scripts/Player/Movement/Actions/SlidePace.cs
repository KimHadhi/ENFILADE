using UnityEngine;

/// <summary>
/// The feet-first slide at its clips' own pace (BIPER Female Runner SlideToRun: Ability.slideStart down into it, Ability.slide
/// along; ClipMotion): it begins as fast as the clip does, friction takes it no slower than the clip goes while the keys go
/// on, and it gets up where the clip's body does. Cancelled into a run as soon as it is down it is the pack's SlideToRun_5m,
/// held on it is SlideToRun_10m; either way the getting up runs out at its own clip's pace (PlayerBody.RunOutPace). With no
/// keys, or bounced off a wall, friction stops it as it always has.
/// Until 0.9.351 it began a kick over the sprint's speed and slowed at one rate to 1.5 m/s, so the body got up out of it with
/// steps made for 4.7 m/s (the user, 2026-11-22: "이런 속도를 애니메이션에 맞춰 줄 수 없나?").
/// </summary>
public sealed class SlidePace
{
    // Within this much of the clip's last pace (m/s) a slide that has come to its end gets up; a slope carries it on past it.
    private const float NearItsEnd = .3f;

    private readonly PlayerMovement ctx;
    private ClipMotion down, along;
    private float time;
    private bool thrown;

    internal SlidePace(PlayerMovement context) { ctx = context; }

    internal void Begin()
    {
        down = ctx.MotionOf(ctx.AbilitySettings.slideStart);
        along = ctx.MotionOf(ctx.AbilitySettings.slide);
        time = 0f;
        thrown = false;
    }

    internal void Tick(float dt) => time += Mathf.Max(0f, dt);

    /// <summary>Thrown back off a wall: it is no longer its clip's slide.</summary>
    internal void Throw() => thrown = true;

    private float Entry => down != null ? down.Length : 0f;
    private float Rises => along != null ? along.Rises : 0f;

    /// <summary>The body is down in it: from here it goes along, or up and on into the run.</summary>
    internal bool IsDown => thrown || time >= Entry;

    private bool Over => thrown || time >= Entry + Rises;

    /// <summary>The clip's pace now (m/s); 0 once its body gets up, or thrown.</summary>
    internal float Speed => Over ? 0f : time < Entry ? down.PaceAt(time) : along.PaceAt(time - Entry);

    /// <summary>The clip has come to its end and the slide goes no faster than it did.</summary>
    internal bool Ends(float speed) => !thrown && Over && speed <= (along != null ? along.PaceAt(Rises) : 0f) + NearItsEnd;
}
