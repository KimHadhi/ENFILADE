using UnityEngine;

/// <summary>The camera chooses intent; the muzzle owns the actual shot and cover obstruction.</summary>
public static class WeaponAimSolver
{
    public static Vector3 AimPoint(GameObject owner, Transform view, float range, int layers)
        => AttackTrace.FirstExternalHit(owner, view.position, view.forward, range, layers, out var hit)
            ? hit.point : view.position + view.forward * range;

    public static bool IsObstructed(GameObject owner, Transform view, Vector3 muzzle, Vector3 target, int layers)
    {
        Vector3 toTarget = target - muzzle;
        // Never rotate toward the player's face or over-converge against an extremely close wall.
        if (Vector3.Dot(toTarget, view.forward) < .12f || Vector3.Angle(view.forward, toTarget) > 65f) return true;
        Vector3 barrel = muzzle - view.position;
        if (barrel.sqrMagnitude > .0001f && AttackTrace.FirstExternalHit(owner, view.position,
            barrel.normalized, barrel.magnitude + .035f, layers, out _)) return true;
        foreach (var c in Physics.OverlapSphere(muzzle, .045f, layers, QueryTriggerInteraction.Ignore))
            if (owner == null || !c.transform.IsChildOf(owner.transform)) return true;
        return AttackTrace.FirstExternalHit(owner, muzzle, toTarget.normalized, .12f, layers, out _);
    }

    public static Vector3 SpreadDirection(Vector3 direction, float degrees, Vector2 disk)
    {
        if (degrees <= 0f) return direction.normalized;
        Quaternion basis = Quaternion.LookRotation(direction);
        float radius = Mathf.Tan(Mathf.Min(45f, degrees) * Mathf.Deg2Rad);
        return (basis * new Vector3(disk.x * radius, disk.y * radius, 1f)).normalized;
    }
}
