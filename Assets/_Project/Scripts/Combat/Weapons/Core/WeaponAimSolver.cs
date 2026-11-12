using UnityEngine;

/// <summary>The camera chooses intent; the muzzle owns the actual shot and cover obstruction.</summary>
public static class WeaponAimSolver
{
    private static readonly Collider[] muzzleOverlaps = new Collider[32];

    public static Vector3 AimPoint(GameObject owner, Transform view, float range, int layers)
        => AttackTrace.FirstExternalHit(owner, view.position, view.forward, range, layers, out var hit)
            ? hit.point : view.position + view.forward * range;

    /// <summary>
    /// Whether the gun must go to high ready: the aim point behind the muzzle or far off to the side (the gun would turn to
    /// the face or over-converge), something between the eyes and the muzzle, the muzzle inside something, or something just
    /// ahead of it. Low cover meant to be shot over whose top is under the eyes (ShootOverCover: a Support's barricade,
    /// sandbags) doesn't count, so the player stands behind it and aims over it; anything else, a wall stood against
    /// included, does. margin asks for that much more room (m) round the muzzle and ahead of it: a gun already raised comes
    /// down only once it is clear by that much, so a pose at the very edge doesn't flick it up and down.
    /// </summary>
    public static bool IsObstructed(GameObject owner, Transform view, Vector3 muzzle, Vector3 target, int layers, float margin = 0f)
    {
        Vector3 toTarget = target - muzzle;
        // Never rotate toward the player's face or over-converge against an extremely close wall.
        if (Vector3.Dot(toTarget, view.forward) < .12f + margin || Vector3.Angle(view.forward, toTarget) > 65f) return true;
        float eyes = view.position.y;
        Vector3 barrel = muzzle - view.position;
        if (barrel.sqrMagnitude > .0001f && Blocked(owner, view.position, barrel.normalized, barrel.magnitude + .035f + margin, layers, eyes))
            return true;
        float radius = .045f + margin;
        int count = Physics.OverlapSphereNonAlloc(muzzle, radius, muzzleOverlaps, layers, QueryTriggerInteraction.Ignore);
        bool obstructed = false;
        for (int i = 0; i < count; i++)
        {
            var collider = muzzleOverlaps[i];
            muzzleOverlaps[i] = null; // Do not retain scene objects in the shared query buffer.
            if (Walls(collider, owner, eyes)) obstructed = true;
        }
        if (obstructed) return true;
        if (count == muzzleOverlaps.Length)
        {
            // A full buffer may contain only the owner's colliders and omit an embedded wall.
            foreach (var collider in Physics.OverlapSphere(muzzle, radius, layers, QueryTriggerInteraction.Ignore))
                if (Walls(collider, owner, eyes)) return true;
        }
        return Blocked(owner, muzzle, toTarget.normalized, .12f + margin, layers, eyes);
    }

    // Something round the muzzle that holds the gun back: not the player's own body, and not low cover looked over.
    private static bool Walls(Collider collider, GameObject owner, float eyes)
        => (owner == null || !collider.transform.IsChildOf(owner.transform)) && !ShootOverCover.LookedOver(collider, eyes);

    // The first thing along the way that holds the gun back, looking past low cover the eyes are over (a few in a row at
    // most): each time from just inside the cover it met, which a ray from inside a collider doesn't meet again.
    private static bool Blocked(GameObject owner, Vector3 origin, Vector3 direction, float range, int layers, float eyes)
    {
        for (int i = 0; i < 4 && range > 0f; i++)
        {
            if (!AttackTrace.FirstExternalHit(owner, origin, direction, range, layers, out RaycastHit hit)) return false;
            if (!ShootOverCover.LookedOver(hit.collider, eyes)) return true;
            float past = hit.distance + .005f;
            origin += direction * past;
            range -= past;
        }
        return false;
    }

    public static Vector3 SpreadDirection(Vector3 direction, float degrees, Vector2 disk)
    {
        if (degrees <= 0f) return direction.normalized;
        Quaternion basis = Quaternion.LookRotation(direction);
        float radius = Mathf.Tan(Mathf.Min(45f, degrees) * Mathf.Deg2Rad);
        return (basis * new Vector3(disk.x * radius, disk.y * radius, 1f)).normalized;
    }
}
