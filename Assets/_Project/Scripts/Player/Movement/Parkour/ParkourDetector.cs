using UnityEngine;

/// <summary>Beatrun's moves, each with its own clip and pace. None is a route of our own: a bar, a ladder's top.</summary>
// Clamber (0.9.285): onto a top from a walk, slowly, on its clip (ParkourTraversal.TryClamber).
public enum VaultKind : byte { None, StepUp, Onto, Over, Kong, OverHigh, OntoHigh, Heave, Clamber }

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
    /// <summary>The climb ends where only a crouched body fits (a top under a low ceiling): it comes off it crouched.</summary>
    public bool EndsCrouched;
}

/// <summary>
/// A ledge the body holds or could hold (LedgeFinder finds it), in the space of the collider its top is on, so a ledge
/// that moves carries it: where the fingers go over (LocalEdge), the way the face looks (LocalNormal, outward), the way
/// where the feet are when the body hangs (LocalHang), and how the body gets
/// from the hang onto the top: where it then stands (LocalStand) and in what posture (Climb; None for a ledge to hang
/// from only). The climb is planned again when it is asked for, so Climb and LocalStand say what was so when the ledge
/// was last looked at (caught, or shimmied along).
/// </summary>
public struct LedgeTarget
{
    public Collider Surface;
    public Vector3 LocalHang, LocalEdge, LocalNormal;
    public Vector3 LocalStand;
    public LedgeClimb Climb;
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

    /// <summary>This player as the ledge finder asks of a body: its capsule, what is world to it, and itself.</summary>
    public LedgeFinder.Body Body => new LedgeFinder.Body
    {
        Radius = ctx.ControllerRadius, Height = ctx.StandingColliderHeight, CrouchHeight = ctx.CrouchColliderHeight,
        SlopeLimit = ctx.SlopeLimit, Layers = ctx.groundLayers, Self = ctx.transform
    };

    /// <summary>The ledge's design numbers as the table has them now (Player.csv Ability.ledge*).</summary>
    public LedgeFinder.Rules LedgeRules
    {
        get
        {
            var settings = ctx.AbilitySettings;
            return new LedgeFinder.Rules
            {
                Reach = settings.ledgeReach, EdgeBelowHead = settings.ledgeEdgeBelowHead, WallBelowHands = settings.ledgeWallBelowHands,
                BelowHands = settings.ledgeReachBelowHands, AboveHands = settings.ledgeReachAboveHands, Inset = settings.ledgeProbeInset,
                HandRoom = settings.ledgeHandRoom
            };
        }
    }

    // The body's room is asked in one place (LedgeFinder), so a ledge found there and a move planned here see the same capsule.
    private void Capsule(Vector3 feet, float height, out Vector3 bottom, out Vector3 top, out float radius)
        => LedgeFinder.Capsule(Body, feet, height, out bottom, out top, out radius);
    public bool ClearCapsule(Vector3 feet, float height) => ClearCapsule(feet, height, ctx.groundLayers);
    private bool ClearCapsule(Vector3 feet, float height, int layers) => LedgeFinder.ClearCapsule(Body, feet, height, layers);
    public bool ClearStraightExit(Vector3 feet)
    {
        // Recovery must fit the actual controller, including its contact margin;
        // the slightly smaller query hull used for route planning is insufficient.
        float r = ctx.ControllerRadius + ctx.Motor.controller.skinWidth;
        Vector3 a = feet + Vector3.up * (r + 0.04f);
        Vector3 b = feet + Vector3.up * Mathf.Max(r + 0.04f, ctx.StandingColliderHeight - r);
        return !Physics.CheckCapsule(a, b, r, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }
    public bool ClearPath(Vector3 start, Vector3 end, float height) => ClearPath(start, end, height, ctx.groundLayers);

    /// <summary>
    /// ClearPath with the living enemies left out: on a ladder a zombie pressed against the climber (close enough to hit
    /// them) is no ceiling, and counting it held every step up for as long as it stayed there.
    /// </summary>
    public bool ClearPathPastEnemies(Vector3 start, Vector3 end, float height)
        => ClearPath(start, end, height, ctx.groundLayers & ~LayerMask.GetMask("Enemy"));

    private bool ClearPath(Vector3 start, Vector3 end, float height, int layers)
    {
        if (!ClearCapsule(end, height, layers)) return false;
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < 0.0001f) return true;
        Capsule(start, height, out Vector3 a, out Vector3 b, out float r);
        return !Physics.CapsuleCast(a, b, r, delta.normalized, delta.magnitude,
            layers, QueryTriggerInteraction.Ignore);
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
    // Where on a ledge's top the body can stand is looked for from free space, on whatever is there (LedgeFinder.Tread):
    // it used to have to be the very collider the hands hold, and the look started 0.2 m up without asking what it
    // started in, so under a slab it found the wall's top through the slab and then could not fit the body there.
    public bool TryHeave(Collider surface, Vector3 edge, Vector3 inward, Vector3 feet, out ObstacleTarget target)
    {
        var body = Body;
        for (float depth = 4f * ctx.HeightUnit; depth <= ctx.ControllerRadius + 0.5f; depth += 0.1f)
        {
            Vector3 probe = edge + inward * depth + Vector3.up * 0.2f;
            if (!LedgeFinder.Tread(body, probe, 0.35f, out RaycastHit tread)) continue;
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
    // The way up and in is a crouched body's, and it may end where only a crouched body fits (EndsCrouched: a top under a
    // low ceiling, a duct's mouth): the move comes off it crouched. It used to refuse any top without standing room.
    public bool TryMantle(Collider surface, Vector3 edge, Vector3 inward, Vector3 feet, out ObstacleTarget target)
    {
        target = default;
        var body = Body;
        Vector3 raised = feet; raised.y = edge.y + 0.1f;
        if (!ClearPath(feet, raised, ctx.CrouchColliderHeight)) return false;
        for (float depth = 0.15f; depth <= ctx.ControllerRadius + 0.5f; depth += 0.1f)
        {
            Vector3 probe = edge + inward * depth + Vector3.up * 0.2f;
            if (!LedgeFinder.Tread(body, probe, 0.35f, out RaycastHit tread)) continue;
            Vector3 end = tread.point + Vector3.up * 0.1f;
            if (!ClearPath(raised, end, ctx.CrouchColliderHeight)) continue;
            target = new ObstacleTarget { Surface = surface, Start = feet, RaisedStart = raised,
                RaisedEnd = end, End = end, IsVault = false, Height = edge.y - feet.y,
                EndsCrouched = !ClearCapsule(end, ctx.StandingColliderHeight) };
            return true;
        }
        return false;
    }

    /// <summary>
    /// A top to clamber onto from the ground, walking up to it (ParkourTraversal.TryClamber): a face straight ahead within
    /// <paramref name="reach"/> of the body's side, its top <paramref name="lowest"/> to <paramref name="highest"/> over the
    /// feet, walkable, and with room on it to stand or crouch (TryMantle's own checks); its near edge and which way it faces,
    /// where the clip's hands go (WarpedTraversal).
    /// </summary>
    public bool TryClamber(Vector3 feet, Vector3 forward, float reach, float lowest, float highest, out ObstacleTarget target)
    {
        target = default;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) return false;
        forward.Normalize();
        // The face: straight ahead at knee height, below the lowest top there may be.
        Vector3 knee = feet + Vector3.up * Mathf.Clamp(lowest * .5f, .2f, .4f);
        if (!Physics.Raycast(knee, forward, out RaycastHit face, ctx.ControllerRadius + reach, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || Mathf.Abs(face.normal.y) > .3f) return false;
        Vector3 inward = Vector3.ProjectOnPlane(-face.normal, Vector3.up).normalized;
        // Facing it, not passing along it.
        if (Vector3.Dot(inward, forward) < ClamberFacing) return false;
        // Its top: straight down a little in from the face, from over the highest top that may be climbed. A face that goes
        // on up past that is a wall: the ray starts inside it and finds nothing.
        Vector3 over = face.point + inward * ClamberTopIn;
        over.y = feet.y + highest + .2f;
        if (!Physics.Raycast(over, Vector3.down, out RaycastHit top, highest + .25f - lowest, ctx.groundLayers, QueryTriggerInteraction.Ignore))
            return false;
        float height = top.point.y - feet.y;
        if (height < lowest || height > highest || top.normal.y < Mathf.Cos(ctx.SlopeLimit * Mathf.Deg2Rad)
            || !WallDetector.CanUseSurface(top.collider)) return false;
        // The near edge, under the hands: where the face meets the top.
        Vector3 edge = face.point + Vector3.up * (top.point.y - face.point.y);
        if (!TryMantle(top.collider, edge, inward, feet, out target)) return false;
        target.Kind = VaultKind.Clamber;
        target.Edge = edge;
        target.EdgeNormal = -inward;
        return true;
    }

    // A face to clamber onto is faced within about 45 degrees (cosine); its top is looked for this far in from it (m).
    private const float ClamberFacing = .7f, ClamberTopIn = .15f;

    /// <summary>As TryLedge, from any place and with what the search came to (the console's /ledge, the records).</summary>
    public bool TryLedgeAt(Vector3 feet, Vector3 facing, out LedgeTarget ledge, out LedgeFinder.Probe probe)
    {
        if (!LedgeFinder.Find(Body, LedgeRules, feet, facing, out ledge, out probe)) return false;
        PlanClimb(ref ledge);
        return true;
    }

    /// <summary>
    /// Writes into the ledge how a body hanging on it gets onto its top, by the very moves the climb would use (TryHeave):
    /// standing, crouched, or not at all. Asked when a ledge is found and again as the body moves along it, since what is
    /// on top differs from place to place.
    /// </summary>
    public void PlanClimb(ref LedgeTarget ledge) => PlanClimb(ref ledge, out _);

    /// <summary>As PlanClimb, and gives the move it planned (default when there is none).</summary>
    public bool PlanClimb(ref LedgeTarget ledge, out ObstacleTarget move)
    {
        move = default;
        ledge.Climb = LedgeClimb.None;
        if (!WallDetector.CanUseSurface(ledge.Surface)) return false;
        Transform support = ledge.Surface.transform;
        Vector3 edge = support.TransformPoint(ledge.LocalEdge), feet = support.TransformPoint(ledge.LocalHang);
        Vector3 inward = Vector3.ProjectOnPlane(-support.TransformDirection(ledge.LocalNormal), Vector3.up).normalized;
        // The edge in front of where the body hangs: a shimmy may have left the stored point behind.
        Vector3 along = Vector3.Cross(Vector3.up, inward);
        edge += along * Vector3.Dot(feet - edge, along);
        if (!TryHeave(ledge.Surface, edge, inward, feet, out move)) return false;
        ledge.Climb = move.EndsCrouched ? LedgeClimb.Crouch : LedgeClimb.Stand;
        ledge.LocalStand = support.InverseTransformPoint(move.End);
        return true;
    }
    public bool TryShimmy(LedgeTarget current, Vector3 displacement, out LedgeTarget next)
    {
        next = current;
        if (!WallDetector.CanUseSurface(current.Surface)) return false;
        Transform support = current.Surface.transform;
        Vector3 normal = support.TransformDirection(current.LocalNormal);
        Vector3 feet = support.TransformPoint(current.LocalHang) + displacement;
        // The ledge's own search alone: how its top is climbed is planned as a step ends (LedgeHangState.Step), the plan
        // before going on meanwhile. Planned at every 5 cm probe, as until 0.9.404, it cost 15 to 40 physics queries a probe,
        // 150 to 400 in the frame a step began (the experts' review of 2026-11-24).
        if (!LedgeFinder.Find(Body, LedgeRules, feet, -normal, out LedgeTarget candidate, out _)) return false;
        candidate.Climb = current.Climb;
        candidate.LocalStand = current.LocalStand;
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
