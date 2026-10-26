using UnityEngine;

/// <summary>Searches the player's torso volume, and ranks all visible wall candidates.</summary>
public sealed class WallDetector
{
    private readonly PlayerMovement ctx;
    private static readonly float[] EntryAngles = { 0f, -30f, 30f, -60f, 60f, -90f, 90f };
    internal WallDetector(PlayerMovement context) { ctx = context; }
    // Enemy bodies can still support short obstacle/head traversals, but never climbing or hanging.
    internal static bool CanUseSurface(Collider collider)
        => collider != null && collider.enabled && collider.gameObject.activeInHierarchy
            && collider.GetComponentInParent<SwingBar>() == null && !DodgeEnemyPassThrough.IsEnemy(collider);
    public bool TryContact(Vector3 direction, out RaycastHit hit)
    {
        Vector3 feet = ctx.transform.position;
        return Physics.CapsuleCast(feet + Vector3.up * 0.45f,
            feet + Vector3.up * (ctx.StandingColliderHeight * 0.85f), 0.14f,
            direction.normalized, out hit, ctx.AbilitySettings.wallReach, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(hit.normal.y) < 0.25f && CanUseSurface(hit.collider);
    }
    public bool TryEntry(out RaycastHit best)
    {
        best = default;
        float bestScore = float.NegativeInfinity;
        foreach (float angle in EntryAngles)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * ctx.transform.forward;
            if (!TryContact(direction, out RaycastHit hit)) continue;
            float facing = Vector3.Dot(ctx.transform.forward, -hit.normal);
            if (facing < ctx.AbilitySettings.wallClimbFacingDot) continue;
            float score = -hit.distance + facing * 0.15f;
            if (score <= bestScore) continue;
            bestScore = score; best = hit;
        }
        return best.collider != null;
    }
}
