using UnityEngine;

/// <summary>
/// Whether a player stands on an enemy's head, judged from the shapes alone so the host and the rider's own PC reach the
/// same answer: the feet at the top of the enemy's body and over it. The enemy below never swings at its rider; it
/// shakes them off instead (HeadStandShakeOff), while the enemies around reach up to them (Enemies.csv reachAbove).
/// </summary>
public static class EnemyHead
{
    // The top is rounded on a capsule, so a rider off to the side stands a little lower; down to this share of the
    // body's radius below the top still counts. Above it, a little air between feet and head (a landing frame).
    private const float BelowTopShare = .8f, AboveTop = .3f;

    /// <summary>The body the feet would stand on: the enemy's first solid collider.</summary>
    public static Collider BodyOf(Component enemy)
    {
        if (enemy == null) return null;
        foreach (var collider in enemy.GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger) return collider;
        return null;
    }

    /// <summary>The player's feet are on top of this enemy.</summary>
    public static bool Carries(Component enemy, PlayerMovement player)
    {
        if (player == null || !player.IsInitialized) return false;
        var body = BodyOf(enemy);
        if (body == null) return false;
        Bounds bounds = body.bounds;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        Vector3 feet = player.transform.position;
        float rise = feet.y - bounds.max.y;
        if (rise < -radius * BelowTopShare || rise > AboveTop) return false;
        Vector2 across = new Vector2(feet.x - bounds.center.x, feet.z - bounds.center.z);
        return across.magnitude <= radius + RiderRadius(player) * .5f;
    }

    /// <summary>The player stands on some enemy's head.</summary>
    public static bool OnAnyHead(PlayerMovement player)
    {
        foreach (var target in MeleeCombatTarget.Active)
            if (target != null && target.IsAvailable && target.GetComponent<EnemyHealth>() != null && Carries(target, player)) return true;
        return false;
    }

    private static float RiderRadius(PlayerMovement player)
    {
        var capsule = player.GetComponent<CharacterController>();
        if (capsule == null) return .3f;
        Vector3 scale = player.transform.lossyScale;
        return capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
    }
}
