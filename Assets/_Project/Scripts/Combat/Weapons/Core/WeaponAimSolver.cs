using UnityEngine;

/// <summary>The camera chooses intent; the muzzle owns the actual shot and cover obstruction.</summary>
public static class WeaponAimSolver
{
    private static readonly Collider[] muzzleOverlaps = new Collider[32];

    public static Vector3 AimPoint(GameObject owner, Transform view, float range, int layers)
        => AttackTrace.FirstExternalHit(owner, view.position, view.forward, range, layers, out var hit)
            ? hit.point : view.position + view.forward * range;

    /// <summary>
    /// margin asks for that much more room (m) round the muzzle and ahead of it: a gun already raised comes down only once
    /// it is clear by that much, so a pose at the very edge doesn't flick it up and down.
    /// </summary>
    public static bool IsObstructed(GameObject owner, Transform view, Vector3 muzzle, Vector3 target, int layers, float margin = 0f)
    {
        Vector3 toTarget = target - muzzle;
        // Never rotate toward the player's face or over-converge against an extremely close wall.
        if (Vector3.Dot(toTarget, view.forward) < .12f + margin || Vector3.Angle(view.forward, toTarget) > 65f) return true;
        Vector3 barrel = muzzle - view.position;
        if (barrel.sqrMagnitude > .0001f && AttackTrace.FirstExternalHit(owner, view.position,
            barrel.normalized, barrel.magnitude + .035f + margin, layers, out _)) return true;
        float radius = .045f + margin;
        int count = Physics.OverlapSphereNonAlloc(muzzle, radius, muzzleOverlaps, layers, QueryTriggerInteraction.Ignore);
        bool obstructed = false;
        for (int i = 0; i < count; i++)
        {
            var collider = muzzleOverlaps[i];
            muzzleOverlaps[i] = null; // Do not retain scene objects in the shared query buffer.
            if (owner == null || !collider.transform.IsChildOf(owner.transform)) obstructed = true;
        }
        if (obstructed) return true;
        if (count == muzzleOverlaps.Length)
        {
            // A full buffer may contain only the owner's colliders and omit an embedded wall.
            foreach (var collider in Physics.OverlapSphere(muzzle, radius, layers, QueryTriggerInteraction.Ignore))
                if (owner == null || !collider.transform.IsChildOf(owner.transform)) return true;
        }
        return AttackTrace.FirstExternalHit(owner, muzzle, toTarget.normalized, .12f + margin, layers, out _);
    }

    public static Vector3 SpreadDirection(Vector3 direction, float degrees, Vector2 disk)
    {
        if (degrees <= 0f) return direction.normalized;
        Quaternion basis = Quaternion.LookRotation(direction);
        float radius = Mathf.Tan(Mathf.Min(45f, degrees) * Mathf.Deg2Rad);
        return (basis * new Vector3(disk.x * radius, disk.y * radius, 1f)).normalized;
    }
}
