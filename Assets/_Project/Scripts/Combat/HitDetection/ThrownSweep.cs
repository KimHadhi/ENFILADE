using UnityEngine;

/// <summary>
/// What something thrown meets on its way, frame by frame: the throwing knife (ThrownKnife), and later the thrown gun of
/// Gun Throw (L4D2 3782482636), whose own check is a sphere of 24 units (0.6 m) round the flying gun. A thin line finds
/// walls and bodies exactly; a sphere of the thrown thing's radius swept along the same way finds bodies only (a zombie's
/// bone hitboxes, anything else that takes damage), so a limb the line just misses is still hit while a wall beside the
/// way never stops it early. It passes what bullets pass (AttackTrace): the thrower, the capsule a zombie body stands in,
/// gore, dropped magazines, stones and knives, trigger volumes; and players, so a throw never hurts a teammate. Callers
/// leave the players' own layer out of the mask, which keeps corpses out too.
/// </summary>
public static class ThrownSweep
{
    private static readonly RaycastHit[] lines = new RaycastHit[32], balls = new RaycastHit[32];
    private static int syncedFrame = -1;

    /// <summary>
    /// Bone hitboxes move with their animation after the frame's scripts, and only reach physics when someone syncs them
    /// (Auto Sync Transforms is off). Once a frame, before the first check, they catch up with the picture, as
    /// AttackTrace does for bullets.
    /// </summary>
    public static void SyncBodies()
    {
        if (syncedFrame == Time.frameCount) return;
        syncedFrame = Time.frameCount;
        Physics.SyncTransforms();
    }

    /// <summary>
    /// The first thing met flying from <paramref name="from"/> to <paramref name="to"/>, for a thrown thing of this
    /// radius (m). A body the sphere is already touching where it starts counts as met there.
    /// </summary>
    public static bool First(GameObject thrower, Vector3 from, Vector3 to, float radius, int layers, out RaycastHit hit)
    {
        hit = default;
        Vector3 path = to - from;
        float length = path.magnitude;
        if (length < 1e-5f) return false;
        Vector3 direction = path / length;
        float nearest = float.PositiveInfinity;
        bool found = false;

        int count = Physics.RaycastNonAlloc(from, direction, lines, length, layers, QueryTriggerInteraction.Collide);
        var results = lines;
        // A full buffer may have left out the nearest wall: ask again for everything.
        if (count == lines.Length) { results = Physics.RaycastAll(from, direction, length, layers, QueryTriggerInteraction.Collide); count = results.Length; }
        for (int i = 0; i < count; i++)
        {
            var candidate = results[i];
            if (candidate.distance >= nearest || Passes(candidate.collider, thrower)) continue;
            nearest = candidate.distance;
            hit = candidate;
            found = true;
        }
        if (radius <= 0f) return found;

        count = Physics.SphereCastNonAlloc(from, radius, direction, balls, length, layers, QueryTriggerInteraction.Collide);
        results = balls;
        if (count == balls.Length) { results = Physics.SphereCastAll(from, radius, direction, length, layers, QueryTriggerInteraction.Collide); count = results.Length; }
        for (int i = 0; i < count; i++)
        {
            var candidate = results[i];
            if (candidate.distance >= nearest || !IsBody(candidate.collider) || Passes(candidate.collider, thrower)) continue;
            // Touching at the start: Unity gives no point or normal for that.
            if (candidate.distance <= 0f)
            {
                candidate.point = from;
                candidate.normal = -direction;
            }
            nearest = candidate.distance;
            hit = candidate;
            found = true;
        }
        return found;
    }

    /// <summary>Something a throw goes past, as a bullet does, and players.</summary>
    public static bool Passes(Collider collider, GameObject thrower)
    {
        if (collider == null) return true;
        if (thrower != null && collider.transform.IsChildOf(thrower.transform)) return true;
        if (ZombieBody.Replaces(collider) || GoreGib.IsPiece(collider) || DroppedMagazine.IsMagazine(collider)
            || ThrownStone.IsStone(collider) || ThrownKnife.IsKnife(collider)) return true;
        // Trigger volumes (a reverb box, a step's area) are no target; a zombie's bone hitbox is.
        if (collider.isTrigger && !ZombieBody.IsHitbox(collider)) return true;
        return collider.GetComponentInParent<PlayerMovement>() != null;
    }

    /// <summary>A body: a zombie's bone hitbox, or an enemy's own collider where it has no hitboxes.</summary>
    public static bool IsBody(Collider collider)
        => collider != null && (ZombieBody.IsHitbox(collider) || collider.GetComponentInParent<EnemyHealth>() != null);
}
