using UnityEngine;

/// <summary>
/// The easing curves the game uses, in one place: HUD pieces, cameras and effects each carried their own copy. Each
/// takes a share from 0 to 1 (held to that range) and gives a share back.
/// </summary>
public static class Ease
{
    /// <summary>Fast at first, settling at the end: 1 - (1 - x)³.</summary>
    public static float Out(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    /// <summary>Slow, fast, slow: a cubic on each half.</summary>
    public static float InOut(float x) { x = Mathf.Clamp01(x); return x < .5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * .5f; }

    /// <summary>Gentle at both ends (smoothstep).</summary>
    public static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// The share of the way still to go that a follower covers in this much time at this rate (per second), the same
    /// at any frame rate; all of it at a rate of 0 or less.
    /// </summary>
    public static float Share(float rate, float dt) => rate <= 0f ? 1f : 1f - Mathf.Exp(-rate * dt);
}
