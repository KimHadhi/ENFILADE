using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a player can see of a place a zombie might stand on, and whether a body fits there: the one measure of a
/// zombie's body for the spawn model. The horde asks it of every spawn point before making anyone (CampaignHorde), and
/// the ground asks it to know when someone has looked at a place from near (SpawnGround: a place seen empty is
/// cleared).
/// A body is the capsule a zombie stands in. It is seen when a line from a player's eyes reaches its crown, its middle,
/// its shins or either shoulder: it was one ray to the head, so a body whose head a lamp post or a door frame covered
/// counted as hidden while the rest of it stood in plain view.
/// </summary>
internal static class SpawnSight
{
    /// <summary>A zombie's body as a capsule standing on a point: its lower and upper centres over the feet and its radius (m).</summary>
    public const float BodyLow = .35f, BodyHigh = 1.55f, BodyRadius = .28f;
    /// <summary>A standing player's eyes over their feet (m).</summary>
    public const float EyeHeight = 1.6f;

    // What blocks a player's sight: everything but players (layer 31) and enemies. Found on first use.
    private static int mask;

    public static int Mask => mask != 0 ? mask : mask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");

    /// <summary>A body fits there: nothing of the world inside a zombie standing on the spot (user, 2026-11-11 playtest: a
    /// runner came out of a wall during the alley hold).</summary>
    public static bool Roomy(Vector3 feet)
        => !Physics.CheckCapsule(feet + Vector3.up * BodyLow, feet + Vector3.up * BodyHigh, BodyRadius, Mask, QueryTriggerInteraction.Ignore);

    /// <summary>Someone of these players has a clear line from their eyes to a body standing there (players' own capsules and enemies aside).</summary>
    public static bool AnyoneSees(Vector3 feet, List<PlayerMovement> players)
    {
        foreach (var player in players)
            if (player != null && Sees(player, feet)) return true;
        return false;
    }

    /// <summary>This player has a clear line from their eyes to a body standing there: to its crown, its middle, its shins or either shoulder.</summary>
    public static bool Sees(PlayerMovement player, Vector3 feet)
    {
        Vector3 eye = player.transform.position + Vector3.up * EyeHeight, middle = feet + Vector3.up * ((BodyLow + BodyHigh) * .5f),
            shoulders = feet + Vector3.up * BodyHigh;
        Vector3 across = Vector3.Cross(Vector3.up, middle - eye);
        across = across.sqrMagnitude > 1e-6f ? across.normalized * BodyRadius : Vector3.zero;
        return Open(eye, feet + Vector3.up * (BodyHigh + BodyRadius)) || Open(eye, middle) || Open(eye, feet + Vector3.up * BodyLow)
            || Open(eye, shoulders + across) || Open(eye, shoulders - across);
    }

    private static bool Open(Vector3 eye, Vector3 at) => !Physics.Linecast(eye, at, Mask, QueryTriggerInteraction.Ignore);
}
