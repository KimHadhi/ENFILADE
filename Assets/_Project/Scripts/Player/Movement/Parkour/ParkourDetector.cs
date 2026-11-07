using UnityEngine;

/// <summary>Beatrun's moves, each with its own clip and pace. None is a route of our own: a bar, a ladder's top.</summary>
public enum VaultKind : byte { None, StepUp, Onto, Over, Kong, OverHigh, OntoHigh, Heave }

public static class VaultKinds
{
    /// <summary>The quick vaults: the body goes over the top on its clip's own arc, legs clear of it.</summary>
    public static bool ClearsTop(this VaultKind kind) => kind == VaultKind.Over || kind == VaultKind.Kong;
}

public struct ObstacleTarget
{
    public Collider Surface;
    public VaultKind Kind;
    /// <summary>Where hands can rest: the near top edge and its outward side. A zero normal means none was found.</summary>
    public Vector3 Edge, EdgeNormal;
    /// <summary>The ladder this mantle climbs off; other vaults and mantles leave it null.</summary>
    public Ladder ExitLadder;
    /// <summary>The swing bar this climb goes up over (ParkourTraversal.MantleFromBar); other moves leave it null.</summary>
    public SwingBar FromBar;
    public Vector3 Start, RaisedStart, RaisedEnd, End;
    public bool IsVault;
    /// <summary>Carried over in a straight line (StraightLineTraversal), not along a rounded TraversalPath.</summary>
    public bool StraightLine;
    /// <summary>Begun during a reload: both hands were on the gun, so it is put away for the move (TraversalHands).</summary>
    public bool BareHanded;
    /// <summary>Top of the obstacle above the feet when it was found (m), for the moves that have no VaultKind.</summary>
    public float Height;
    /// <summary>Seconds the move takes, when it sets its own (off the top of a ladder); otherwise TraversalTiming decides.</summary>
    public float Duration;
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
    internal VaultFinder Vaults { get; }
    internal ParkourDetector(PlayerMovement context) { ctx = context; walls = new WallDetector(ctx); Vaults = new VaultFinder(ctx, this); }
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
    public bool ClearStraightExit(Vector3 feet)
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
    public bool ClearStraightPath(Vector3 start, Vector3 end, Collider surface)
    {
        if (!ClearStraightCapsule(start, surface) || !ClearStraightCapsule(end, surface)) return false;
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < 0.000001f) return true;
        Capsule(start, ctx.StandingColliderHeight, out Vector3 a, out Vector3 b, out float r);
        int count = Physics.CapsuleCastNonAlloc(a, b, r, delta.normalized, traversalHits, delta.magnitude,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);
        if (count == traversalHits.Length) return false;
        for (int i = 0; i < count; i++) if (traversalHits[i].collider != surface) return false;
        return true;
    }
    internal bool ClearStraightCapsule(Vector3 feet, Collider surface)
    {
        Capsule(feet, ctx.StandingColliderHeight, out Vector3 a, out Vector3 b, out float r);
        int count = Physics.OverlapCapsuleNonAlloc(a, b, r, traversalOverlaps, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        if (count == traversalOverlaps.Length) return false;
        for (int i = 0; i < count; i++) if (traversalOverlaps[i] != surface) return false;
        return true;
    }

    internal bool StraightTarget(Collider surface, Vector3 feet, Vector3 end, bool vault, out ObstacleTarget target)
    {
        target = default;
        if (!ClearStraightExit(end) || (vault && !BodyClearOf(surface, end)) || !ClearStraightPath(feet, end, surface)) return false;
        target = new ObstacleTarget { Surface = surface, Start = feet, End = end, IsVault = vault, StraightLine = true };
        return true;
    }

    /// <summary>
    /// The body standing at <paramref name="feet"/> keeps clear of this one obstacle, bottom and all, as Beatrun's hull
    /// test at vaultpos and its "touches nothing" leave do. The other straight-move tests lift their capsule off the floor
    /// (Capsule, ClearStraightExit), and a vault's end sits 35 units up, a few centimetres under a waist-high rail's top: a
    /// vault from the far edge of its reach ended with the body's back still over the rail, the player stood on its edge
    /// for a moment and dropped off it, and the vault looked cut short (user's videos, 2026-11-06).
    /// </summary>
    internal bool BodyClearOf(Collider surface, Vector3 feet)
    {
        float r = ctx.ControllerRadius + ctx.Motor.controller.skinWidth;
        Vector3 a = feet + Vector3.up * r, b = feet + Vector3.up * Mathf.Max(r, ctx.StandingColliderHeight - r);
        int count = Physics.OverlapCapsuleNonAlloc(a, b, r, traversalOverlaps, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (traversalOverlaps[i] == surface) return false;
        return true;
    }
    // climb.lua's heave: straight from the hang to the top, feet four units past the edge, or as far in as the capsule fits.
    // Under a ceiling too low to go straight, the crouched route of TryMantle.
    public bool TryHeave(Collider surface, Vector3 edge, Vector3 inward, Vector3 feet, out ObstacleTarget target)
    {
        for (float depth = 4f * ctx.HeightUnit; depth <= ctx.ControllerRadius + 0.5f; depth += 0.1f)
        {
            Vector3 probe = edge + inward * depth + Vector3.up * 0.2f;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit tread, 0.35f, ctx.groundLayers, QueryTriggerInteraction.Ignore)
                || tread.collider != surface || tread.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)) continue;
            if (!StraightTarget(surface, feet, tread.point + Vector3.up * 0.02f, false, out target)) continue;
            target.Kind = VaultKind.Heave;
            return true;
        }
        if (!TryMantle(surface, edge, inward, feet, out target)) return false;
        target.Kind = VaultKind.Heave;
        return true;
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
        // Climb the way the player faces along the swing (PlayerBody.BarFacingFor), not away from the feet: swung past the
        // bar's middle, the feet are on its far side and the body turned round to climb back (user, 2026-11-08).
        Vector3 swing = bar.SwingDirection;
        Vector3 outward = Vector3.Dot(ctx.transform.forward, swing) >= 0f ? -swing : swing;
        Vector3 approach = grip + outward * (ctx.ControllerRadius + 0.22f); approach.y = feet.y;
        Vector3 raised = approach; raised.y = end.y;
        if (!ClearPath(feet, approach, ctx.CrouchColliderHeight)
            || !ClearPath(approach, raised, ctx.CrouchColliderHeight)
            || !ClearPath(raised, end, ctx.CrouchColliderHeight)) return false;
        target = new ObstacleTarget { Surface = top.collider, FromBar = bar, Start = feet, HasApproach = true, Approach = approach,
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
