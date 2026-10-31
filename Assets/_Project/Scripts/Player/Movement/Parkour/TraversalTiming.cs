using UnityEngine;

/// <summary>
/// How long a vault or mantle takes; the body's clip plays over that same time. Vaults keep the run-up speed (never
/// slower than walking) at an even pace, as the vault clip's body moves evenly. Mantles ease in and out like a
/// pull-up and take longer the higher they climb.
/// </summary>
public static class TraversalTiming
{
    public const float SourceUnitScale = 0.025f;
    public static float Duration(MovementAbilitySettings settings, ObstacleTarget target, float routeLength, float entrySpeed, float walkSpeed)
        => target.Duration > 0f ? target.Duration
            : target.IsVault ? routeLength / Mathf.Max(0.1f, entrySpeed, walkSpeed)
            : settings.mantleDuration + settings.mantleDurationPerMeter * Mathf.Max(0f, target.Height);
    /// <summary>Share of the route covered once this share of the time has passed.</summary>
    public static float Progress(float time, bool vault) => vault ? Mathf.Clamp01(time) : Mathf.SmoothStep(0f, 1f, time);
    public static float EndProgress(bool vault) => vault ? 0.75f : 0.8f;
    public static float EndDistanceSquared => 280f * SourceUnitScale * SourceUnitScale;
}
