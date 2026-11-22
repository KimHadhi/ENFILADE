using UnityEngine;

/// <summary>
/// A move under way that goes over the ground as its clip does (ClipMotion): the step dodge, the landing rolls, the slide's
/// dive roll. One object is one move under way. A clip that throws its body further in a frame than the most speed it is
/// begun with owes the rest and pays it in the frames after: BIPER's QuickShift covers 1.2 m in a thirtieth of a second, a
/// jump cut seen from the eyes.
/// </summary>
public sealed class ClipTravel
{
    private float most, owed, plainDistance, plainSeconds;

    /// <summary>The clip's own motion; null for a move at an even pace.</summary>
    public ClipMotion Motion { get; private set; }
    /// <summary>The clip's moment the body is at (s).</summary>
    public float Time { get; private set; }
    /// <summary>How far the whole clip goes (m).</summary>
    public float Distance => Motion != null ? Motion.Distance : plainDistance;
    /// <summary>The share of the way gone (0-1), not counting what is owed.</summary>
    public float Share => Distance > 0f ? Mathf.Clamp01((GoneAt(Time) - owed) / Distance) : 1f;

    /// <summary>The way of a clip from its moment <paramref name="from"/> (s), as far as its own body goes and never faster than
    /// <paramref name="mostSpeed"/> (m/s). False for a clip that goes nowhere.</summary>
    public bool Begin(ClipMotion motion, float from, float mostSpeed)
    {
        Motion = motion;
        most = mostSpeed > 0f ? mostSpeed : float.PositiveInfinity;
        Time = Mathf.Max(0f, from);
        owed = plainDistance = plainSeconds = 0f;
        return motion != null && motion.Distance > .05f;
    }

    /// <summary>A way at one even pace, for a body without the clip.</summary>
    public void BeginPlain(float distance, float seconds, float from, float mostSpeed)
    {
        Begin(null, from, mostSpeed);
        plainDistance = Mathf.Max(0f, distance);
        plainSeconds = Mathf.Max(.05f, seconds);
    }

    /// <summary>How far the body goes in this step (m); the clip's moment goes on with it.</summary>
    public float Go(float dt)
    {
        if (dt <= 0f) return 0f;
        float before = GoneAt(Time);
        Time += dt;
        float want = GoneAt(Time) - before + owed;
        float go = Mathf.Min(want, most * dt);
        owed = want - go;
        return go;
    }

    /// <summary>The clip's moment goes on while the body stays (stopped by a wall): what it would have gone is let go.</summary>
    public void Pass(float dt)
    {
        Time += Mathf.Max(0f, dt);
        owed = 0f;
    }

    private float GoneAt(float time) => Motion != null ? Motion.GoneAt(time)
        : plainSeconds > 0f ? plainDistance * Mathf.Clamp01(time / plainSeconds) : 0f;
}
