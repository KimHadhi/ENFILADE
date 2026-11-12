using UnityEngine;

/// <summary>
/// The real wall under a ledge, felt for where a hanging body needs it (PlayerBody.Hang's legs, the /hang readout). Below a
/// ledge the wall is seldom the ledge's own face carried straight down: it steps out (a sill, a lip, a thicker storey),
/// goes in (a recess, the underside of a balcony) or isn't there at all (an overhang). So at each height a leg needs it,
/// it is looked for afresh: level, straight in toward the ledge, from a place in front of it that the body itself takes up
/// (so free space), past the player's own colliders and the zombies'. Every PC asks the same world with the same pose, so
/// every PC finds the same wall. Before, the wall was taken to be the plane straight down from the ledge's edge, and a leg
/// went into a wall that stepped out under it or stood off one that went in (user, 2026-11-13: feet through the wall).
/// </summary>
internal static class HangWall
{
    /// <summary>A probe looks this far in past the point it asks about (m); a wall further in is no wall to it.</summary>
    internal const float Beyond = .6f;
    /// <summary>A probe starts at least this far out in front of the point (m).</summary>
    internal const float LeastBack = .05f;

    private static readonly RaycastHit[] Hits = new RaycastHit[8];
    private static int enemyLayer = -2;

    /// <summary>What a hanging body meets: the ground layers less the zombies (a zombie under a hang is no wall).</summary>
    internal static int Layers(LayerMask ground)
    {
        if (enemyLayer == -2) enemyLayer = LayerMask.NameToLayer("Enemy");
        return enemyLayer >= 0 ? ground & ~(1 << enemyLayer) : (int)ground;
    }

    /// <summary>
    /// How far out in front of <paramref name="point"/> to start looking in along <paramref name="inward"/> (level, unit) so
    /// the probe starts in free space: at least as far out as <paramref name="free"/>, a place the body takes up (the hip
    /// joint for a leg, the shoulder for a hand).
    /// </summary>
    internal static float BackFrom(Vector3 point, Vector3 free, Vector3 inward) => Mathf.Max(0f, Vector3.Dot(point - free, inward)) + LeastBack;

    /// <summary>
    /// The wall level with <paramref name="point"/>, looking in along <paramref name="inward"/> (level, unit) from
    /// <paramref name="back"/> out in front of it (BackFrom): how far the point stands out from it (m; below zero, how deep
    /// inside), and where it was met. False when there is no wall within <see cref="Beyond"/> past the point.
    /// <paramref name="owner"/>'s own colliders are no wall.
    /// </summary>
    internal static bool Probe(Vector3 point, Vector3 inward, float back, int layers, Transform owner, out float gap, out Vector3 surface)
    {
        gap = float.PositiveInfinity;
        surface = point;
        int count = Physics.RaycastNonAlloc(point - inward * back, inward, Hits, back + Beyond, layers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (Hits[i].distance >= nearest || (owner != null && Hits[i].collider.transform.IsChildOf(owner))) continue;
            nearest = Hits[i].distance;
            surface = Hits[i].point;
        }
        if (float.IsPositiveInfinity(nearest)) return false;
        gap = nearest - back;
        return true;
    }
}
