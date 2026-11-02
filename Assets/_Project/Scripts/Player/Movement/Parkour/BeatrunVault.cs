using UnityEngine;

/// <summary>
/// Beatrun's vaulting.lua and the direct moves of climb.lua, check for check. Distances stay in its units, 72 to the hull,
/// and PlayerMovement.HullUnit fits them to this player, so they keep their share of the body at any capsule size. Nothing
/// marks an obstacle as a vault or a climb: the moves look past its top. Open space beyond it means over, otherwise onto it.
/// A ParkourObstacle can overrule that for one object. The airborne order is Vault2, Vault3, Vault1; facing a wall it is
/// Vault4, Vault5.
/// </summary>
public sealed class BeatrunVault
{
    private const float Chest = 32f, Thorax = 48f, Neck = 54f, Eye = 64f, Hair = 80f, AirCheck = 600f;
    private const float MantleLift = 16f, HighLift = 24f, Hull = 72f, HullHalfWidth = 16f, ReachOver = 35f, EdgeSearch = 150f;
    private static readonly float[] EdgeHeights = { Chest, 44f, 56f, 20f };
    private const int RiseSamples = 4;
    private const float OntoReach = 70f, OntoReachLeast = 25f, OntoReachStep = 5f;

    private readonly PlayerMovement ctx;
    private readonly ParkourDetector detector;
    private readonly Collider[] overlaps = new Collider[16];
    private float u;
    internal BeatrunVault(PlayerMovement context, ParkourDetector parkour) { ctx = context; detector = parkour; }

    /// <summary>Airborne with W held: over the obstacle if the far side is open, otherwise onto it.</summary>
    internal bool TryAir(Vector3 forward, out ObstacleTarget target)
    {
        u = ctx.HullUnit;
        Vector3 feet = ctx.transform.position;
        return Over(feet, forward, out target) || Kong(feet, forward, out target) || Onto(feet, forward, out target);
    }

    /// <summary>
    /// Whether this jump's remaining rise will bring a move within reach: a top too high from here can still be one.
    /// Not in Beatrun, where the vertical wall run gets there first; here a wall climb waits for it.
    /// </summary>
    internal bool TryLaterInJump(Vector3 forward, float rise, out ObstacleTarget target)
    {
        target = default;
        u = ctx.HullUnit;
        for (int step = 1; step <= RiseSamples; step++)
        {
            Vector3 feet = ctx.transform.position + Vector3.up * (rise * step / RiseSamples);
            if (Over(feet, forward, out target) || Kong(feet, forward, out target)
                || High(feet, forward, ctx.VerticalVelocity, out target)) return true;
        }
        return false;
    }

    /// <summary>
    /// The start of ClimbingCheck: a wall in front with its top within reach. Holding W, it stands the player at the wall
    /// with the top 60 units above the feet, then tries the thin-wall vault and the high climb before any hang.
    /// </summary>
    internal bool TryHigh(Vector3 forward, float verticalSpeed, out ObstacleTarget target)
    {
        u = ctx.HullUnit;
        return High(ctx.transform.position, forward, verticalSpeed, out target);
    }

    private bool High(Vector3 feet, Vector3 forward, float verticalSpeed, out ObstacleTarget target)
    {
        target = default;
        // A box wider than the capsule would start inside a wall the player touches, and a cast can't see what it starts in.
        float half = ctx.ControllerRadius - .02f, reach = (50f + HullHalfWidth) * u - half;
        var box = new Vector3(half, (Hull - 45f) * u * .5f, half);
        if (!Physics.BoxCast(feet + Up((Hull + 45f) * .5f), box, forward, out RaycastHit wall, Quaternion.identity, reach,
                ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || !WallDetector.CanUseSurface(wall.collider) || Mathf.Abs(wall.normal.y) > .05f) return false;
        Vector3 inward = Vector3.ProjectOnPlane(-wall.normal, Vector3.up).normalized;
        if (Vector3.Angle(inward, forward) > 50f) return false;

        float detection = 60f;
        if (!TopWithin(feet + inward * (45f * u), out RaycastHit top))
        {
            if (!TopWithin(feet + inward * (20f * u), out top)) return false;
            detection = 25f;
        }
        Vector3 start = wall.point - inward * (16.533f * u);
        start.y = top.point.y - 77f * u;
        Vector3 skim = Vector3.up * .01f;
        if (Line(top.point + skim, top.point + skim - inward * (detection * u))) return false;
        Vector3 topAtWall = start + Up(77f) + skim;
        if (Line(topAtWall, topAtWall + inward * (detection * .5f * u)) || Line(feet, feet + Up(75f))) return false;

        start += Up(17f);
        Vector3 edge = wall.point;
        edge.y = top.point.y;
        Vector3 normal = -inward;
        return OverHigh(start, forward, wall.collider, edge, normal, out target)
            || OntoHigh(start, forward, verticalSpeed, wall.collider, edge, normal, out target);

        bool TopWithin(Vector3 from, out RaycastHit hit)
        {
            Vector3 origin = from + Up(65f);
            hit = default;
            return !Inside(origin) && Down(origin, 45f, out hit) && hit.distance > .001f;
        }
    }

    // Vault2: something at chest height, nothing at eye height for 100 units, the far side open down to 16 units.
    private bool Over(Vector3 feet, Vector3 forward, out ObstacleTarget target)
    {
        target = default;
        Vector3 start = feet + Up(Chest) + forward * (ReachOver * u);
        if (!HalfHull(start, out Collider surface) || !ParkourObstacle.AllowsVault(surface)) return false;
        Vector3 eye = feet + Up(Eye), past = eye + forward * (100f * u);
        if (Line(eye, past) || Line(past, past - Up(Thorax)) || HalfHull(start + Up(Chest), out _)) return false;
        return Target(VaultKind.Over, surface, feet, forward, start + forward * (ReachOver * u), out target);
    }

    // Vault3: the long vault. Farther clear, with ground somewhere below where it comes down.
    private bool Kong(Vector3 feet, Vector3 forward, out ObstacleTarget target)
    {
        target = default;
        Vector3 start = feet + Up(Chest) + forward * (ReachOver * u);
        if (!HalfHull(start, out Collider surface) || !ParkourObstacle.AllowsVault(surface)) return false;
        Vector3 eye = feet + Up(Eye), past = eye + forward * (150f * u);
        if (Line(eye, past) || Line(past, past - Up(Thorax)) || !Line(past, past - Up(AirCheck))
            || HalfHull(start + Up(Chest), out _)) return false;
        return Target(VaultKind.Kong, surface, feet, forward, start + forward * (60f * u), out target);
    }

    // Vault1: straight down in front of the eyes onto a top no higher than the waist; a low one is stepped onto.
    private bool Onto(Vector3 feet, Vector3 forward, out ObstacleTarget target)
    {
        target = default;
        Vector3 origin = feet + Up(Eye) + forward * (25f * u);
        if (Inside(origin) || !Down(origin, Neck, out RaycastHit top) || !WallDetector.CanUseSurface(top.collider)
            || !ParkourObstacle.AllowsClimb(top.collider)) return false;
        float fraction = top.distance / (Neck * u);
        if (fraction <= .3f) return false;
        bool stepUp = fraction > .65f;
        if (Line(origin - forward * (50f * u), origin) || !detector.ClearReferenceCapsule(top.point, top.collider)) return false;
        Vector3 here = top.point + Up(MantleLift), onward = here + forward * (50f * u);
        Vector3 end = stepUp ? here : onward;
        if (!detector.ClearReferenceExit(end))
        {
            end = stepUp ? onward : here;
            stepUp = !stepUp;
            if (!detector.ClearReferenceExit(end)) return false;
        }
        if (Line(feet + Up(Eye), end + Up(Eye))) return false;
        return Target(stepUp ? VaultKind.StepUp : VaultKind.Onto, top.collider, feet, forward, end, out target);
    }

    // Vault4: a wall too high for Vault2 but thin enough to land behind.
    private bool OverHigh(Vector3 feet, Vector3 forward, Collider surface, Vector3 edge, Vector3 normal, out ObstacleTarget target)
    {
        target = default;
        if (!ParkourObstacle.AllowsVault(surface)) return false;
        Vector3 hair = feet + Up(Hair), high = feet + Up(AirCheck) + forward * (40f * u), chest = feet + Up(Chest);
        if (Line(hair, hair + forward * (75f * u)) || Line(high, high - Up(Hair))
            || !Line(chest, chest + forward * (150f * u))) return false;
        Vector3 end = feet + forward * (65f * u) + Up(HighLift);
        if (!detector.ReferenceTarget(surface, ctx.transform.position, end, true, out target)) return false;
        Finish(ref target, VaultKind.OverHigh, edge, normal);
        Approach(ref target, feet);
        return true;
    }

    // Vault5: up onto a top too high for Vault1, unless already falling fast. Its probe lands 70 units ahead, past a shallower
    // top, so here it comes back until the body fits on what is there. Beatrun would leave such a top to the ledge hang.
    private bool OntoHigh(Vector3 feet, Vector3 forward, float verticalSpeed, Collider surface, Vector3 edge, Vector3 normal, out ObstacleTarget target)
    {
        target = default;
        if (verticalSpeed < -100f * u || !ParkourObstacle.AllowsClimb(surface)) return false;
        Vector3 hair = feet + Up(Hair);
        if (Line(hair, hair + forward * (60f * u))) return false;
        for (float reach = OntoReach; reach >= OntoReachLeast; reach -= OntoReachStep)
        {
            Vector3 origin = feet + Up(Eye) + forward * (reach * u);
            if (Inside(origin) || !Down(origin, Neck, out RaycastHit top)
                || (reach < OntoReach && !Down(origin + forward * ctx.ControllerRadius, Neck, out _))
                || Line(origin - forward * (reach * u), origin)
                || !detector.ReferenceTarget(top.collider, ctx.transform.position, top.point + Up(MantleLift), false, out target)) continue;
            Finish(ref target, VaultKind.OntoHigh, edge, normal);
            Approach(ref target, feet);
            return true;
        }
        return false;
    }

    // Beatrun puts the player at the wall at once and eases only the model there. Here the capsule goes too, so it never
    // waits in mid-air away from the wall. Where the floor is in the way it keeps its height and only closes the distance.
    private void Approach(ref ObstacleTarget target, Vector3 atWall)
    {
        Vector3 feet = ctx.transform.position, level = atWall;
        level.y = feet.y;
        foreach (Vector3 spot in new[] { atWall, level })
        {
            if (!detector.ClearReferencePath(feet, spot, target.Surface) || !detector.ClearReferencePath(spot, target.End, target.Surface)) continue;
            target.HasApproach = true;
            target.Approach = spot;
            return;
        }
    }

    private bool Target(VaultKind kind, Collider surface, Vector3 feet, Vector3 forward, Vector3 end, out ObstacleTarget target)
    {
        bool over = kind == VaultKind.Over || kind == VaultKind.Kong;
        if (!detector.ReferenceTarget(surface, feet, end, over, out target)) return false;
        FindEdge(surface, feet, forward, out Vector3 edge, out Vector3 normal);
        Finish(ref target, kind, edge, normal);
        return true;
    }

    private static void Finish(ref ObstacleTarget target, VaultKind kind, Vector3 edge, Vector3 normal)
    {
        target.Kind = kind;
        target.Edge = edge;
        target.EdgeNormal = normal;
    }

    // Not part of the port: where hands can rest on this obstacle, for characters whose arms don't meet it by themselves.
    private void FindEdge(Collider surface, Vector3 feet, Vector3 forward, out Vector3 edge, out Vector3 normal)
    {
        edge = normal = Vector3.zero;
        foreach (float height in EdgeHeights)
        {
            if (!surface.Raycast(new Ray(feet + Up(height), forward), out RaycastHit front, EdgeSearch * u)) continue;
            Vector3 flat = Vector3.ProjectOnPlane(front.normal, Vector3.up);
            if (flat.sqrMagnitude < .25f) continue;
            Vector3 above = front.point + forward * (3f * u) + Up(Hair);
            if (!surface.Raycast(new Ray(above, Vector3.down), out RaycastHit top, (Hair + height) * u)) continue;
            edge = front.point;
            edge.y = top.point.y;
            normal = flat.normalized;
            return;
        }
    }

    private Vector3 Up(float units) => Vector3.up * (units * u);
    private bool Line(Vector3 from, Vector3 to) => Physics.Linecast(from, to, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    private bool Down(Vector3 from, float units, out RaycastHit hit)
        => Physics.Raycast(from, Vector3.down, out hit, units * u, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    // A Source trace that starts solid reports fraction 0; a Unity ray simply passes through, so ask first.
    private bool Inside(Vector3 point) => Physics.CheckSphere(point, .005f, ctx.groundLayers, QueryTriggerInteraction.Ignore);

    // Half the player's hull, standing on this point: the shape Vault2 and Vault3 test with.
    private bool HalfHull(Vector3 feet, out Collider hit)
    {
        hit = null;
        var half = new Vector3(HullHalfWidth, Hull * .25f, HullHalfWidth) * u;
        int count = Physics.OverlapBoxNonAlloc(feet + Vector3.up * half.y, half, overlaps, Quaternion.identity,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            // Unlike Beatrun's NPCs, enemies can be vaulted here; only bars and things that would roll away are skipped.
            var body = overlaps[i].attachedRigidbody;
            if (overlaps[i].GetComponentInParent<SwingBar>() != null || (body != null && !body.isKinematic)) continue;
            hit = overlaps[i];
            return true;
        }
        return false;
    }
}
