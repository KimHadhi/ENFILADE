using UnityEngine;

/// <summary>vmanip_vault progression: Lerp(rate / TargetTick, progress, 1).</summary>
public static class TraversalTiming
{
    public const float ReferenceHz = 66.66f;
    public const float SourceUnitScale = 0.025f;
    public static float Advance(float progress, float referenceLerp, float dt)
        => Mathf.Lerp(progress, 1f, Mathf.Clamp01(referenceLerp * ReferenceHz * Mathf.Max(0f, dt)));
    public static float EndProgress(bool vault) => vault ? 0.75f : 0.8f;
    public static float EndDistanceSquared => 280f * SourceUnitScale * SourceUnitScale;
}
