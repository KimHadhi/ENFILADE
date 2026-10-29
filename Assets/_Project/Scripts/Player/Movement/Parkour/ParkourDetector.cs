using UnityEngine;

public struct ObstacleTarget
{
    public Collider Surface;
    public Vector3 Start, RaisedStart, RaisedEnd, End;
    public bool IsVault;
    public bool ReferenceMotion;
    /// <summary>Top of the obstacle above the feet when it was found (m); the body picks its vault or mantle clip by it.</summary>
    public float Height;
    public bool HasApproach;
    public Vector3 Approach;
}

public struct LedgeTarget
{
    public Collider Surface;
    public Vector3 LocalHang, LocalEdge, LocalNormal;
}

/// <summary>Geometry queries only; no state changes or player movement.</summary>
public sealed class ParkourDetector
{
    private readonly PlayerMovement ctx;
    private readonly WallDetector walls;
    private readonly Collider[] traversalOverlaps = new Collider[32];
    private readonly RaycastHit[] traversalHits = new RaycastHit[32];
    internal ParkourDetector(PlayerMovement context) { ctx = context; walls = new WallDetector(ctx); }
    private static bool IsBar(Collider collider) => collider != null && collider.GetComponentInParent<SwingBar>() != null;
    private float Radius(float height) => Mathf.Max(0.05f, Mathf.Min(ctx.ControllerRadius, height * 0.5f) - 0.025f);
    private void Capsule(Vector3 feet, float height, out Vector3 bottom, out Vector3 top, out float radius)
    {
        radius = Radius(height);
        bottom = feet + Vector3.up * (radius + 0.04f);
        top = feet + Vector3.up * Mathf.Max(radius + 0.04f, height - radius);
    }
    public bool ClearCapsule(Vector3 feet, float height)
    {
        Capsule(feet, height, out Vector3 a, out Vector3 b, out float r);
        return !Physics.CheckCapsule(a, b, r, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }
    public bool ClearReferenceExit(Vector3 feet)
    {
        // Recovery must fit the actual controller, including its contact margin;
        // the slightly smaller query hull used for route planning is insufficient.
        float r = ctx.ControllerRadius + ctx.Motor.controller.skinWidth;
        Vector3 a = feet + Vector3.up * (r + 0.04f);
        Vector3 b = feet + Vector3.up * Mathf.Max(r + 0.04f, ctx.StandingColliderHeight - r);
        return !Physics.CheckCapsule(a, b, r, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }
    public bool ClearPath(Vector3 start, Vector3 end, float height)
    {
        if (!ClearCapsule(end, height)) return false;
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < 0.0001f) return true;
        Capsule(start, height, out Vector3 a, out Vector3 b, out float r);
        return !Physics.CapsuleCast(a, b, r, delta.normalized, delta.magnitude,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }
    public bool HasTread(Vector3 feet, Collider surface)
        => Physics.Raycast(feet + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 0.3f,
            ctx.groundLayers, QueryTriggerInteraction.Ignore) && hit.collider == surface
            && hit.normal.y >= Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad);

    // Source temporarily uses NOCLIP. Exempt only the selected obstacle here;
    // the standing body must still fit past every other collider along the line.
    public bool ClearReferencePath(Vector3 start, Vector3 end, Collider surface)
    {
        if (!ClearReferenceCapsule(start, surface) || !ClearReferenceCapsule(end, surface)) return false;
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < 0.000001f) return true;
        Capsule(start, ctx.StandingColliderHeight, out Vector3 a, out Vector3 b, out float r);
        int count = Physics.CapsuleCastNonAlloc(a, b, r, delta.normalized, traversalHits, delta.magnitude,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);
        if (count == traversalHits.Length) return false;
        for (int i = 0; i < count; i++) if (traversalHits[i].collider != surface) return false;
        return true;
    }
    private bool ClearReferenceCapsule(Vector3 feet, Collider surface)
    {
        Capsule(feet, ctx.StandingColliderHeight, out Vector3 a, out Vector3 b, out float r);
        int count = Physics.OverlapCapsuleNonAlloc(a, b, r, traversalOverlaps, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        if (count == traversalOverlaps.Length) return false;
        for (int i = 0; i < count; i++) if (traversalOverlaps[i] != surface) return false;
        return true;
    }

    private bool ReferenceTarget(Collider surface, Vector3 feet, Vector3 end, bool vault, out ObstacleTarget target)
    {
        target = default;
        if (!ClearReferenceExit(end) || !ClearReferencePath(feet, end, surface)) return false;
        target = new ObstacleTarget { Surface = surface, Start = feet, End = end, IsVault = vault, ReferenceMotion = true };
        return true;
    }
    public bool TryObstacle(Vector3 direction, out ObstacleTarget target, bool airborne = false)
    {
        target = default;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.01f) return false;
        var settings = ctx.AbilitySettings;
        Vector3 feet = ctx.transform.position;
        if (!Physics.SphereCast(feet + Vector3.up * (airborne ? 0.16f : settings.stepHeight + 0.18f), 0.12f, direction,
            out RaycastHit front, settings.obstacleReach, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || IsBar(front.collider) || Mathf.Abs(front.normal.y) > 0.45f || Vector3.Dot(direction, -front.normal) < 0.4f) return false;
        Vector3 topOrigin = front.point + direction * 0.12f;
        topOrigin.y = feet.y + settings.mantleMaxHeight + 0.15f;
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top,
            settings.mantleMaxHeight + 0.15f, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || top.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad) || top.collider != front.collider) return false;
        float height = top.point.y - feet.y;
        if (height <= (airborne ? 0.05f : settings.stepHeight + 0.03f) || height > settings.mantleMaxHeight) return false;
        if (airborne && height > settings.vaultMaxHeight && !ctx.Input.JumpPressed) return false;

        Vector3 raisedStart = feet; raisedStart.y = top.point.y + 0.07f;
        float bodyHeight = ctx.CrouchColliderHeight;
        if (!ClearPath(feet, raisedStart, bodyHeight)) return false;
        if (height <= settings.vaultMaxHeight)
        {
            for (float depth = 0.3f; depth <= settings.vaultMaxDepth + ctx.ControllerRadius; depth += 0.15f)
            {
                Vector3 probe = top.point + direction * depth + Vector3.up * 0.2f;
                float landingDepth = height + (airborne ? ctx.AbilitySettings.safeFallDistance + 0.4f : 0.8f);
                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit landing, landingDepth,
                    ctx.groundLayers, QueryTriggerInteraction.Ignore) || landing.point.y > top.point.y - 0.15f
                    || landing.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) continue;
                Vector3 end = landing.point + Vector3.up * 0.04f;
                Vector3 raisedEnd = end; raisedEnd.y = raisedStart.y;
                if (ClearPath(raisedStart, raisedEnd, bodyHeight) && ClearPath(raisedEnd, end, bodyHeight))
                {
                    // End above the far edge, then hand back normal air movement.
                    // Landing is physics, not a scripted descent to the sampled floor.
                    // Vault2: chest32 + forward70, independent of approach speed.
                    Vector3 referenceEnd = feet + Vector3.up * (32f * TraversalTiming.SourceUnitScale)
                        + direction * (70f * TraversalTiming.SourceUnitScale);
                    if (ReferenceTarget(front.collider, feet, referenceEnd, true, out target)) { target.Height = height; return true; }
                    // Larger Unity obstacles retain their measured clearance destination,
                    // but use the same straight interpolation, never a lift-first curve.
                    if (ReferenceTarget(front.collider, feet, raisedEnd, true, out target)) { target.Height = height; return true; }
                }
            }
        }
        if (height <= settings.vaultMaxHeight)
        {
            // Vault1: land on the tread with the reference's 16-unit vertical margin.
            Vector3 treadProbe = feet + direction * (50f * TraversalTiming.SourceUnitScale);
            treadProbe.y = top.point.y + 0.2f;
            if (Physics.Raycast(treadProbe, Vector3.down, out RaycastHit tread, 0.4f, ctx.groundLayers,
                QueryTriggerInteraction.Ignore) && tread.collider == front.collider
                && ReferenceTarget(front.collider, feet, tread.point + Vector3.up * (16f * TraversalTiming.SourceUnitScale), false, out target))
            {
                target.Height = height;
                return true;
            }
        }
        return TryMantle(front.collider, top.point, -front.normal, feet, out target);
    }

    // Sample inward from the edge instead of requiring a full capsule radius of
    // tread beyond an already inset ray. This also supports narrow wall tops.
    public bool TryMantle(Collider surface, Vector3 edge, Vector3 inward, Vector3 feet, out ObstacleTarget target)
    {
        target = default;
        Vector3 raised = feet; raised.y = edge.y + 0.1f;
        if (!ClearPath(feet, raised, ctx.CrouchColliderHeight)) return false;
        for (float depth = 0.15f; depth <= ctx.ControllerRadius + 0.5f; depth += 0.1f)
        {
            Vector3 probe = edge + inward * depth + Vector3.up * 0.2f;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit tread, 0.35f,
                ctx.groundLayers, QueryTriggerInteraction.Ignore) || tread.collider != surface
                || tread.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) continue;
            Vector3 end = tread.point + Vector3.up * 0.1f;
            if (!ClearPath(raised, end, ctx.CrouchColliderHeight) || !ClearCapsule(end, ctx.StandingColliderHeight)) continue;
            target = new ObstacleTarget { Surface = surface, Start = feet, RaisedStart = raised,
                RaisedEnd = end, End = end, IsVault = false, Height = edge.y - feet.y };
            return true;
        }
        return false;
    }

    public bool TryLedge(Vector3 facing, out LedgeTarget ledge)
        => TryLedgeAt(ctx.transform.position, facing, out ledge);

    private bool TryLedgeAt(Vector3 feet, Vector3 facing, out LedgeTarget ledge)
    {
        ledge = default;
        facing = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
        float hands = ctx.StandingColliderHeight - 0.15f;
        if (!Physics.SphereCast(feet + Vector3.up * (hands - 0.45f), 0.1f, facing,
            out RaycastHit wall, ctx.AbilitySettings.ledgeReach, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || !WallDetector.CanUseSurface(wall.collider) || Mathf.Abs(wall.normal.y) > 0.25f || Vector3.Dot(facing, -wall.normal) < 0.4f) return false;
        Vector3 origin = wall.point - wall.normal * 0.08f;
        origin.y = feet.y + hands + ctx.AbilitySettings.ledgeReachAboveHands;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit top,
            ctx.AbilitySettings.ledgeReachAboveHands + 0.4f, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || top.collider != wall.collider || top.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) return false;
        Vector3 edge = top.point + wall.normal * 0.08f;
        Vector3 hang = edge + wall.normal * (ctx.ControllerRadius + 0.06f) - Vector3.up * hands;
        if (!ClearPath(feet, hang, ctx.StandingColliderHeight)) return false;
        Transform support = wall.collider.transform;
        ledge = new LedgeTarget { Surface = wall.collider, LocalHang = support.InverseTransformPoint(hang),
            LocalEdge = support.InverseTransformPoint(edge), LocalNormal = support.InverseTransformDirection(wall.normal) };
        return true;
    }
    public bool TryShimmy(LedgeTarget current, Vector3 displacement, out LedgeTarget next)
    {
        next = current;
        if (!WallDetector.CanUseSurface(current.Surface)) return false;
        Transform support = current.Surface.transform;
        Vector3 normal = support.TransformDirection(current.LocalNormal);
        Vector3 feet = support.TransformPoint(current.LocalHang) + displacement;
        if (!TryLedgeAt(feet, -normal, out LedgeTarget candidate)) return false;
        Vector3 nextFeet = candidate.Surface.transform.TransformPoint(candidate.LocalHang);
        Vector3 nextNormal = candidate.Surface.transform.TransformDirection(candidate.LocalNormal);
        if (Vector3.Dot(normal, nextNormal) < 0.95f || Mathf.Abs(nextFeet.y - feet.y) > 0.15f
            || Vector3.Distance(nextFeet, feet) > 0.15f
            || !ClearPath(ctx.transform.position, nextFeet, ctx.StandingColliderHeight)) return false;
        next = candidate;
        return true;
    }

    internal bool TryBarMantle(SwingBar bar, Vector3 grip, out ObstacleTarget target)
    {
        target = default;
        if (bar == null || !bar.isActiveAndEnabled) return false;
        if (!Physics.Raycast(grip + Vector3.up * 0.5f, Vector3.down, out RaycastHit top, 1f,
            ctx.groundLayers, QueryTriggerInteraction.Ignore) || top.collider.GetComponentInParent<SwingBar>() != bar
            || top.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) return false;
        Vector3 feet = ctx.transform.position, end = top.point + Vector3.up * 0.1f;
        if (!ClearCapsule(end, ctx.StandingColliderHeight)) return false;
        Vector3 outward = Vector3.ProjectOnPlane(feet - grip, Vector3.up);
        if (outward.sqrMagnitude < 0.01f) outward = -bar.SwingDirection;
        outward.Normalize();
        Vector3 approach = grip + outward * (ctx.ControllerRadius + 0.22f); approach.y = feet.y;
        Vector3 raised = approach; raised.y = end.y;
        if (!ClearPath(feet, approach, ctx.CrouchColliderHeight)
            || !ClearPath(approach, raised, ctx.CrouchColliderHeight)
            || !ClearPath(raised, end, ctx.CrouchColliderHeight)) return false;
        target = new ObstacleTarget { Surface = top.collider, Start = feet, HasApproach = true, Approach = approach,
            RaisedStart = raised, RaisedEnd = end, End = end, Height = end.y - feet.y };
        return true;
    }
    public bool TryWall(Vector3 direction, out RaycastHit hit)
        => walls.TryContact(direction, out hit);
    public bool TryWallEntry(out RaycastHit hit) => walls.TryEntry(out hit);
    internal bool TryBar(out SwingBar bar, out Vector3 grip)
    {
        bar = null; grip = Vector3.zero;
        Vector3 hands = ctx.transform.position + Vector3.up * ctx.StandingColliderHeight;
        float best = ctx.AbilitySettings.barGrabReach;
        foreach (var candidate in SwingBar.ActiveBars)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || !ctx.Abilities.Traversal.CanGrab(candidate)) continue;
            Vector3 point = candidate.ClosestGrip(hands);
            float distance = Vector3.Distance(hands, point);
            if (distance >= best || point.y < ctx.transform.position.y + ctx.StandingColliderHeight * 0.55f) continue;
            if (Physics.Linecast(hands, point, out RaycastHit obstruction, ctx.groundLayers, QueryTriggerInteraction.Ignore)
                && !obstruction.transform.IsChildOf(candidate.transform)) continue;
            Vector3 destination = point - Vector3.up * candidate.HangLength;
            if (!ClearPath(ctx.transform.position, destination, ctx.CrouchColliderHeight)) continue;
            best = distance; bar = candidate; grip = point;
        }
        return bar != null;
    }
}
