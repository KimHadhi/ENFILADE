using UnityEngine;

/// <summary>
/// Finds the walls the player climbs: one in front to start a wall climb on (TryEntry, Beatrun's eye-level traces), and
/// the wall against the torso while on it (TryContact).
/// </summary>
public sealed class WallDetector
{
    private readonly PlayerMovement ctx;
    private static readonly float[] EntryAngles = { 0f, -30f, 30f, -60f, 60f, -90f, 90f };
    internal WallDetector(PlayerMovement context) { ctx = context; }
    // Enemy bodies can still support short obstacle/head traversals, but never climbing or hanging.
    // Moving bodies (physics props, ragdolls) are never wall-run, hung from or vaulted: they would roll away mid-move.
    internal static bool CanUseSurface(Collider collider)
        => collider != null && collider.enabled && collider.gameObject.activeInHierarchy
            && collider.GetComponentInParent<SwingBar>() == null && !DodgeEnemyPassThrough.IsEnemy(collider)
            && (collider.attachedRigidbody == null || collider.attachedRigidbody.isKinematic);

    /// <summary>The wall this way against the torso, from 0.45 m up: what keeps a wall climb going on it.</summary>
    public bool TryContact(Vector3 direction, out RaycastHit hit)
    {
        Vector3 feet = ctx.transform.position;
        return Physics.CapsuleCast(feet + Vector3.up * 0.45f,
            feet + Vector3.up * (ctx.StandingColliderHeight * 0.85f), CastRadius,
            direction.normalized, out hit, ctx.AbilitySettings.wallReach, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(hit.normal.y) < 0.25f && CanUseSurface(hit.collider);
    }

    // Beatrun's vertical wall run (wallrunme.lua, the purist one WallClimbPace follows) starts only at a wall that stands
    // at the eyes, which are 64 units over the feet: a trace 15 units under them meets it within 25 units (about 9
    // beyond its hull, so the hands meet it), one 5 under them within 30, and one from there straight at the wall within
    // 40. The entry here looked with the torso capsule TryContact keeps the climb with, from 0.45 m up, so a waist-high
    // top was a wall too: a jump at a Support's barricade (1.15 m) that its vault missed, the W pressed a moment after
    // the jump, climbed it instead (the user, 2026-11-14: "바리케이드는 뛰어넘기가 아니라 벽오르기가 되는데"). Such a top is
    // the vaults' (VaultFinder), as in Beatrun. A thin cast, so a gap between boards doesn't hide the wall behind them.
    private const float EntryUnits = 25f, CastRadius = .14f;
    private const float EyeUnits = 64f, LowUnder = 15f, HighUnder = 5f, HighUnits = 30f, AtWallUnits = 40f;

    /// <summary>A wall in front to start a wall climb on: the nearest that stands at the eyes and that the player faces.</summary>
    public bool TryEntry(out RaycastHit best)
    {
        best = default;
        float bestScore = float.NegativeInfinity, u = ctx.HeightUnit;
        Vector3 feet = ctx.transform.position;
        Vector3 low = feet + Vector3.up * ((EyeUnits - LowUnder) * u), high = feet + Vector3.up * ((EyeUnits - HighUnder) * u);
        foreach (float angle in EntryAngles)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * ctx.transform.forward;
            if (!Probe(low, direction, EntryUnits * u, out RaycastHit hit)) continue;
            float facing = Vector3.Dot(ctx.transform.forward, -hit.normal);
            if (facing < ctx.AbilitySettings.wallClimbFacingDot) continue;
            Vector3 inward = Vector3.ProjectOnPlane(-hit.normal, Vector3.up).normalized;
            if (!Probe(high, direction, HighUnits * u, out _) || !Probe(high, inward, AtWallUnits * u, out _)) continue;
            float score = -hit.distance + facing * 0.15f;
            if (score <= bestScore) continue;
            bestScore = score; best = hit;
        }
        return best.collider != null;
    }

    // A wall to climb within reach (m) of a point, the thin cast's front included.
    private bool Probe(Vector3 from, Vector3 direction, float reach, out RaycastHit hit)
        => Physics.SphereCast(from, CastRadius, direction.normalized, out hit, Mathf.Max(.01f, reach - CastRadius),
                ctx.groundLayers, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(hit.normal.y) < 0.25f && CanUseSurface(hit.collider);
}
