using System;
using UnityEngine;

/// <summary>A sound the world could hear: where, how far it carries (m) and who made it.</summary>
public readonly struct NoiseEvent
{
    public readonly Vector3 Point;
    public readonly float Radius;
    public readonly GameObject Source;

    public NoiseEvent(Vector3 point, float radius, GameObject source)
    {
        Point = point;
        Radius = radius;
        Source = source;
    }
}

/// <summary>
/// What enemies will be able to hear. Every character sound reports here as it plays (CharacterSounds: footsteps,
/// landings, vaults, voice), with a reach from Tables/SoundCues. Nothing listens yet; enemy hearing subscribes to Made.
/// </summary>
public static class Noise
{
    public static event Action<NoiseEvent> Made;

    public static void Make(Vector3 point, float radius, GameObject source)
    {
        if (radius > 0f) Made?.Invoke(new NoiseEvent(point, radius, source));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Made = null;
}
