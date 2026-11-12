using UnityEngine;

/// <summary>
/// How far another body is from a player across the ground, from the middle of the player's capsule to the other's
/// surface, and which way. The dashes that close in on an enemy (the left-click deathblow, the counter) stop by it.
/// </summary>
public static class BodyGap
{
    public static float Across(PlayerMovement player, Collider other, out Vector3 toward)
    {
        toward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        if (other == null) return 0f;
        var capsule = player.GetComponent<CharacterController>();
        Vector3 center = capsule != null ? player.transform.TransformPoint(capsule.center) : player.transform.position + Vector3.up;
        Vector3 delta = Vector3.ProjectOnPlane(other.ClosestPoint(center) - center, Vector3.up);
        float gap = delta.magnitude;
        if (gap > .001f) toward = delta / gap;
        return gap;
    }
}
