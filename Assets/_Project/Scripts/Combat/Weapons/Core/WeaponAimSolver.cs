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
    /// passFoes: a pistol in its close hold (FPSBody.CloseHold), which fires into an enemy however close: living enemies
    /// (IsFoe) hold it back nowhere, and with foeAimed (the crosshair is on one) neither does the aim point being at or
    /// behind the muzzle. Walls, props and friends still do.
    /// </summary>
    public static bool IsObstructed(GameObject owner, Transform view, Vector3 muzzle, Vector3 target, int layers, float margin = 0f,
        bool passFoes = false, bool foeAimed = false)
    {
        Vector3 toTarget = target - muzzle;
        // Never rotate toward the player's face or over-converge against an extremely close wall.
        if (!(passFoes && foeAimed) && (Vector3.Dot(toTarget, view.forward) < .12f + margin || Vector3.Angle(view.forward, toTarget) > 65f))
            return true;
        float eyes = view.position.y;
        Vector3 barrel = muzzle - view.position;
        if (barrel.sqrMagnitude > .0001f && Blocked(owner, view.position, barrel.normalized, barrel.magnitude + .035f + margin, layers, eyes, passFoes))
            return true;
        float radius = .045f + margin;
        int count = Physics.OverlapSphereNonAlloc(muzzle, radius, muzzleOverlaps, layers, QueryTriggerInteraction.Ignore);
        bool obstructed = false;
        for (int i = 0; i < count; i++)
        {
            var collider = muzzleOverlaps[i];
            muzzleOverlaps[i] = null; // Do not retain scene objects in the shared query buffer.
            if (Walls(collider, owner, eyes, passFoes)) obstructed = true;
        }
        if (obstructed) return true;
        if (count == muzzleOverlaps.Length)
        {
            // A full buffer may contain only the owner's colliders and omit an embedded wall.
            foreach (var collider in Physics.OverlapSphere(muzzle, radius, layers, QueryTriggerInteraction.Ignore))
                if (Walls(collider, owner, eyes, passFoes)) return true;
        }
        return toTarget.sqrMagnitude > 1e-8f && Blocked(owner, muzzle, toTarget.normalized, .12f + margin, layers, eyes, passFoes);
    }

    /// <summary>
    /// A living enemy's collider: a zombie's bone hitbox or the capsule it stands in (ZombieBody), or anything under a living
    /// EnemyHealth. Every PC has its copies of them, so a friend's PC tells the same.
    /// </summary>
    public static bool IsFoe(Collider collider)
    {
        if (collider == null) return false;
        if (ZombieBody.IsHitbox(collider) || ZombieBody.Replaces(collider)) return true;
        var health = collider.GetComponentInParent<EnemyHealth>();
        return health != null && health.IsAlive;
    }

    // How far short of an enemy pressed against the gun a shot held in close leaves from (m).
    private const float ContactGap = .02f;

    /// <summary>
    /// Where a shot from a pistol in its close hold leaves from: the muzzle, or, with an enemy between the eyes and the
    /// muzzle (the gun pressed into it), just short of that enemy on the eyes' way to the muzzle. A ray from inside a
    /// collider doesn't meet it, so from the muzzle buried in a zombie the bullet would start past its hitboxes and miss
    /// the zombie it was pressed into; from there it strikes what the crosshair is on.
    /// </summary>
    public static Vector3 ShortOfFoe(GameObject owner, Transform view, Vector3 muzzle, int layers)
    {
        Vector3 way = muzzle - view.position;
        float length = way.magnitude;
        if (length < 1e-4f) return muzzle;
        way /= length;
        if (!AttackTrace.FirstExternalHit(owner, view.position, way, length, layers, out RaycastHit hit) || !IsFoe(hit.collider))
            return muzzle;
        return view.position + way * Mathf.Max(0f, hit.distance - ContactGap);
    }

    // Something round the muzzle that holds the gun back: not the player's own body, not low cover looked over, and with
    // passFoes not a living enemy.
    private static bool Walls(Collider collider, GameObject owner, float eyes, bool passFoes)
        => (owner == null || !collider.transform.IsChildOf(owner.transform)) && !ShootOverCover.LookedOver(collider, eyes)
            && !(passFoes && IsFoe(collider));

    // The first thing along the way that holds the gun back, looking past low cover the eyes are over (and with passFoes
    // past living enemies; a few in a row at most): each time from just inside what it met, which a ray from inside a
    // collider doesn't meet again.
    private static bool Blocked(GameObject owner, Vector3 origin, Vector3 direction, float range, int layers, float eyes, bool passFoes)
    {
        for (int i = 0; i < 4 && range > 0f; i++)
        {
            if (!AttackTrace.FirstExternalHit(owner, origin, direction, range, layers, out RaycastHit hit)) return false;
            if (!ShootOverCover.LookedOver(hit.collider, eyes) && !(passFoes && IsFoe(hit.collider))) return true;
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
