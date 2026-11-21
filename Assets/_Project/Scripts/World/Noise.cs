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
    /// <summary>
    /// A lure that goes on and on (a map's TV, a radio, an airport's announcement: MapGimmicks' GimmickLure): every calm
    /// zombie that hears it walks over and stays round it, not only the nearest one, so the way it drew them from is clear.
    /// Only with Lure.
    /// </summary>
    public readonly bool Gather;
    /// <summary>
    /// Where a gathering lure's hearers go: where it was heard from (Point), or a place the sound calls them to (an
    /// airport's announcement heard at every speaker, paging the far wing: GimmickLure's gatherAt).
    /// </summary>
    public readonly Vector3 GatherPoint;

    public NoiseEvent(Vector3 point, float radius, GameObject source, bool lure = false, bool gather = false)
        : this(point, radius, source, lure, gather, point) { }

    public NoiseEvent(Vector3 point, float radius, GameObject source, bool lure, bool gather, Vector3 gatherPoint)
    {
        Point = point;
        Radius = radius;
        Source = source;
        Lure = lure;
        Gather = lure && gather;
        GatherPoint = gatherPoint;
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

    public static void Make(Vector3 point, float radius, GameObject source, bool lure = false, bool gather = false)
    {
        if (radius > 0f) Made?.Invoke(new NoiseEvent(point, radius, source, lure, gather));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Made = null;
}
