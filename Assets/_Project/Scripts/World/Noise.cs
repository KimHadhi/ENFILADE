using System;
using UnityEngine;

/// <summary>A sound the world could hear: where, how far it carries (m) and who made it.</summary>
public readonly struct NoiseEvent
{
    public readonly Vector3 Point;
    public readonly float Radius;
    public readonly GameObject Source;
    /// <summary>
    /// Something thrown to draw them off (PlayerThrow's stone landing): zombies that hear it go and look where it fell,
    /// but it never tells them who threw it nor wakes them by itself.
    /// </summary>
    public readonly bool Lure;

    public NoiseEvent(Vector3 point, float radius, GameObject source, bool lure = false)
    {
        Point = point;
        Radius = radius;
        Source = source;
        Lure = lure;
    }
}

/// <summary>
/// What enemies will be able to hear. Every character sound reports here as it plays (CharacterSounds: footsteps,
/// landings, vaults, voice), with a reach from Tables/SoundCues, and so do shots and thrown stones. Zombies hear it:
/// ZombieMind subscribes to Made. The stealth view draws the reach of the local player's own (StealthView).
/// </summary>
public static class Noise
{
    public static event Action<NoiseEvent> Made;

    public static void Make(Vector3 point, float radius, GameObject source, bool lure = false)
    {
        if (radius > 0f) Made?.Invoke(new NoiseEvent(point, radius, source, lure));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Made = null;
}
