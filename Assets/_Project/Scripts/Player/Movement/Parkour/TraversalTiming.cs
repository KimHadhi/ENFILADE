using UnityEngine;

/// <summary>
/// How long a mantle along a route of its own takes: off a bar, off a ladder's top, or under a ceiling too low for the
/// straight heave. The body's clip plays over that same time. It eases in and out like a pull-up and takes longer the higher
/// it climbs. Every other vault and climb moves at Beatrun's pace, see VaultMotion.
/// </summary>
public static class TraversalTiming
{
    public const float SourceUnitScale = 0.025f;
    public const float EndProgress = 0.8f;
    public static float Duration(MovementAbilitySettings settings, ObstacleTarget target)
        => target.Duration > 0f ? target.Duration
            : settings.mantleDuration + settings.mantleDurationPerMeter * Mathf.Max(0f, target.Height);
    /// <summary>Share of the route covered once this share of the time has passed.</summary>
    public static float Progress(float time) => Mathf.SmoothStep(0f, 1f, time);
    public static float EndDistanceSquared => 280f * SourceUnitScale * SourceUnitScale;
}
