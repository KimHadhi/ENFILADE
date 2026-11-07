using UnityEngine;

/// <summary>
/// Finds which vault or climb the obstacle ahead allows and where it ends. Nothing marks an obstacle as a vault or a climb:
/// the moves look past its top, and open space beyond it means over, otherwise onto it (a ParkourObstacle can overrule
/// that for one object). In the air it tries over, a kong vault, then onto; facing a wall, the thin-wall vault, then the
/// high climb. Ported check for check from Beatrun (vaulting.lua and the direct moves of climb.lua): distances stay in its
/// units, 72 to the standing height, and PlayerMovement.HeightUnit fits them to this player at any capsule size.
/// </summary>
public sealed class VaultFinder
{
    private const float Chest = 32f, Thorax = 48f, Neck = 54f, Eye = 64f, Hair = 80f, AirCheck = 600f;
    private const float MantleLift = 16f, HighLift = 24f, Hull = 72f, HullHalfWidth = 16f, ReachOver = 35f, EdgeSearch = 150f;
    private static readonly float[] EdgeHeights = { Chest, 44f, 56f, 20f };
    private const int RiseSamples = 4;
    private const float OntoReach = 70f, OntoReachLeast = 25f, OntoReachStep = 5f;
    // Beatrun's jump (player_beatrun.lua's SetJumpPower 230 under GMod's sv_gravity 600) and its server tick (s).
    private const float JumpSpeed = 230f, JumpGravity = 600f;
    internal const float Tick = .015f;

    private readonly PlayerMovement ctx;
    private readonly ParkourDetector detector;
    private readonly Collider[] overlaps = new Collider[16];
    private float u;
    internal VaultFinder(PlayerMovement context, ParkourDetector parkour) { ctx = context; detector = parkour; }

    /// <summary>
    /// Airborne with W held: over the obstacle if the far side is open, otherwise onto it. Beatrun looks every tick, and at
    /// its first airborne one its jump has the feet 3 units up; the over moves find a top only above the chest box, 32
    /// units over the feet, so a waist-high rail is caught there. This game's jump rises faster, and at 60 frames a second
    /// its first airborne frame is already past that band, so a running jump sailed over the rail. While a jump off the
    /// ground still rises, the moves look from where Beatrun's jump would have the feet by now: <paramref name="sinceJump"/>
    /// seconds of it above <paramref name="groundY"/> (infinite when not in one). The move itself starts where the capsule is.
    /// </summary>
    internal bool TryAir(Vector3 forward, float sinceJump, float groundY, out ObstacleTarget target)
    {
        u = ctx.HeightUnit;
        Vector3 feet = ctx.transform.position;
        if (!float.IsPositiveInfinity(sinceJump))
            feet.y = Mathf.Min(feet.y, groundY + Mathf.Max(0f, (JumpSpeed - .5f * JumpGravity * sinceJump) * sinceJump) * u);
        return Over(feet, forward, out target) || Kong(feet, forward, out target) || Onto(feet, forward, out target);
    }

    /// <summary>
    /// Whether a jump off the ground at <paramref name="feet"/> would be vaulted over (Vault2 or Vault3) on its first
    /// airborne tick, where TryAir first looks, and the move it would be. ParkourTraversal.SurgesToVault runs an early
    /// jump on until this is so, and starts that move there and then.
    /// </summary>
    internal bool CatchesLow(Vector3 feet, Vector3 forward, out ObstacleTarget target)
    {
        u = ctx.HeightUnit;
        feet += Vector3.up * ((JumpSpeed - .5f * JumpGravity * Tick) * Tick * u);
        return Over(feet, forward, out target) || Kong(feet, forward, out target);
    }

    /// <summary>
    /// Whether this jump's remaining rise will bring a move within reach: a top too high from here can still be one.
    /// Not in Beatrun, where the vertical wall run gets there first; here a wall climb waits for it.
    /// </summary>
    internal bool TryLaterInJump(Vector3 forward, float rise, out ObstacleTarget target)
    {
        target = default;
        u = ctx.HeightUnit;
        for (int step = 1; step <= RiseSamples; step++)
        {
            Vector3 feet = ctx.transform.position + Vector3.up * (rise * step / RiseSamples);
            if (Over(feet, forward, out target) || Kong(feet, forward, out target)
                || High(feet, forward, ctx.VerticalVelocity, true, false, out target)) return true;
        }
        return false;
    }

    /// <summary>
    /// The start of ClimbingCheck: a wall in front with its top within reach. Holding W, it stands the player at the wall
    /// with the top 60 units above the feet, then tries the thin-wall vault and the high climb before any hang. During a
    /// wall climb (<paramref name="wallClimb"/>) the top is looked for higher, as Beatrun does on its vertical wall run.
    /// </summary>
    internal bool TryHigh(Vector3 forward, float verticalSpeed, bool wallClimb, out ObstacleTarget target)
    {
        u = ctx.HeightUnit;
        return High(ctx.transform.position, forward, verticalSpeed, false, wallClimb, out target);
    }

    /// <summary>
    /// A ledge caught higher than TryHigh reaches from here: ClimbingCheck stands the player at the wall with this top 60
    /// units above the feet and, holding W, tries the same moves before it settles for the hang.
    /// </summary>
    internal bool TryHighBelow(Vector3 edge, Vector3 forward, float verticalSpeed, bool wallClimb, out ObstacleTarget target)
    {
        u = ctx.HeightUnit;
        Vector3 feet = ctx.transform.position;
        feet.y = edge.y - 60f * u;
        return High(feet, forward, verticalSpeed, true, wallClimb, out target);
    }

    // Beatrun probes for the top from where the player is, every frame on the way in, and its 16-unit-wide hull stops 16
    // units from the wall, where the nearer probe lands just past the face. Asked ahead of time (a jump still rising, a
    // ledge caught from farther out), the probes stand there, at the wall (fromWall), as they will be by the time the
    // move starts: probed from out here, a top thinner than the gap between the probes is missed, and a thin wall the
    // jump would vault got climbed and hung from instead.
    private bool High(Vector3 feet, Vector3 forward, float verticalSpeed, bool fromWall, bool wallClimb, out ObstacleTarget target)
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

        Vector3 probe = feet;
        if (fromWall) probe += inward * (Vector3.Dot(wall.point - feet, inward) - HullHalfWidth * u);
        // On its vertical wall run ClimbingCheck looks for the top from 90 units up instead of 65 (upvalue).
        float lookFrom = wallClimb ? 90f : 65f;
        float detection = 60f;
        if (!TopWithin(probe + inward * (45f * u), out RaycastHit top))
        {
            if (!TopWithin(probe + inward * (20f * u), out top)) return false;
            detection = 25f;
        }
        Vector3 start = wall.point - inward * (16.533f * u);
        start.y = top.point.y - 77f * u;
        Vector3 skim = Vector3.up * .01f;
        if (Line(top.point + skim, top.point + skim - inward * (detection * u))) return false;
        Vector3 topAtWall = start + Up(77f) + skim;
        if (Line(topAtWall, topAtWall + inward * (detection * .5f * u)) || Line(feet, feet + Up(75f))) return false;

        // Holding W, ClimbingCheck lifts the hang spot 17 units before trying the moves; on a wall run the thin-wall vault
        // starts from the hang's own height.
        Vector3 lifted = start + Up(17f);
        Vector3 edge = wall.point;
        edge.y = top.point.y;
        Vector3 normal = -inward;
        return OverHigh(wallClimb ? start : lifted, forward, wall.collider, edge, normal, out target)
            || OntoHigh(lifted, forward, verticalSpeed, wall.collider, edge, normal, out target);

        bool TopWithin(Vector3 from, out RaycastHit hit)
        {
            Vector3 origin = from + Up(lookFrom);
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
        if (Inside(origin)) return false;
        if (!Down(origin, Neck, out RaycastHit top))
        {
            // Temporary (ParkourProbeLog): the top just below the probe's reach, where a jump only bumps into it.
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit below, (Neck + 40f) * u, ctx.groundLayers, QueryTriggerInteraction.Ignore)
                && below.point.y > feet.y - .05f)
                return OntoProbe($"top {below.point.y - feet.y:0.00} m over the feet is past the probe's {Neck * u:0.00} m");
            return false;
        }
        if (!WallDetector.CanUseSurface(top.collider) || !ParkourObstacle.AllowsClimb(top.collider)) return OntoProbe("surface not climbable");
        float fraction = top.distance / (Neck * u);
        if (fraction <= .3f) return OntoProbe($"top too high (fraction {fraction:0.00})");
        // Not in Beatrun: a top shallower than the body (a rail, a thin wall) is gone over, never stepped or climbed onto.
        // Caught a moment late in a running jump, the rail was climbed and stood on instead of vaulted, which the user found
        // awkward (2026-11-06). A ParkourObstacle set to Climb Onto still can be.
        if (ParkourObstacle.AllowsVault(top.collider) && Depth(top, forward) < 2f * ctx.ControllerRadius) return OntoProbe("top too shallow");
        // Beatrun steps onto a top in the lower 35% of the probe's reach (fraction > .65) and climbs onto a higher one. Here
        // that is judged from the floor in front of the obstacle, so the obstacle's own height decides, not how far a jump
        // has risen: caught on the way up, a 0.9 m rail read as a step from the feet, and the step was over in a tenth of a
        // second, where from closer the same rail was climbed onto (user, 2026-11-07: "0.1초 동안 mantle이라 부자연스러움").
        bool stepUp = fraction > .65f;
        if (FloorUnder(feet, out float floor)) stepUp = top.point.y - floor < (Eye - .65f * Neck) * u;
        if (Line(origin - forward * (50f * u), origin)) return OntoProbe("something at eye height");
        if (!detector.ClearStraightCapsule(top.point, top.collider)) return OntoProbe("the capsule doesn't fit on the top");
        Vector3 here = top.point + Up(MantleLift), onward = here + forward * (50f * u);
        Vector3 end = stepUp ? here : onward;
        if (!detector.ClearStraightExit(end))
        {
            end = stepUp ? onward : here;
            stepUp = !stepUp;
            if (!detector.ClearStraightExit(end)) return OntoProbe("no clear place to end");
        }
        if (Line(feet + Up(Eye), end + Up(Eye))) return OntoProbe("the view to the end is blocked");
        return Target(stepUp ? VaultKind.StepUp : VaultKind.Onto, top.collider, feet, forward, end, out target, top.point.y);
    }

    // The floor under the feet (m), within reach of an obstacle's height; false over a drop.
    private bool FloorUnder(Vector3 feet, out float floor)
    {
        floor = 0f;
        if (!Physics.Raycast(feet + Vector3.up * .05f, Vector3.down, out RaycastHit hit, (Hull + 5f) * u, ctx.groundLayers,
                QueryTriggerInteraction.Ignore)) return false;
        floor = hit.point.y;
        return true;
    }

    // Temporary (ParkourProbeLog, 2026-11-07): why a top ahead wasn't climbed onto, at most every OntoProbeStep seconds.
    private float ontoProbeNext;
    private const float OntoProbeStep = .1f;
    private bool OntoProbe(string why)
    {
        if (ctx.IsLocal && Time.time >= ontoProbeNext)
        {
            ontoProbeNext = Time.time + OntoProbeStep;
            ParkourProbeLog.Line($"onto refused: {why}");
        }
        return false;
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
        if (!detector.StraightTarget(surface, ctx.transform.position, end, true, out target)) return false;
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
                || !detector.StraightTarget(top.collider, ctx.transform.position, top.point + Up(MantleLift), false, out target)) continue;
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
            if (!detector.ClearStraightPath(feet, spot, target.Surface) || !detector.ClearStraightPath(spot, target.End, target.Surface)) continue;
            target.HasApproach = true;
            target.Approach = spot;
            return;
        }
    }

    // topY: the top's height when the move already found it (climbing onto it), for where the hands go.
    private bool Target(VaultKind kind, Collider surface, Vector3 feet, Vector3 forward, Vector3 end, out ObstacleTarget target,
        float topY = float.NaN)
    {
        bool over = kind == VaultKind.Over || kind == VaultKind.Kong;
        if (!detector.StraightTarget(surface, feet, end, over, out target)) return false;
        FindEdge(surface, feet, forward, topY, out Vector3 edge, out Vector3 normal);
        if (over && normal != Vector3.zero && !CrossedBy(surface, edge, forward, end))
        {
            OntoProbe($"{kind} wouldn't get past the obstacle");
            target = default;
            return false;
        }
        Finish(ref target, kind, edge, normal);
        return true;
    }

    /// <summary>
    /// Not in Beatrun: a vault over has to get past the obstacle, its far side nearer along the way than where the move
    /// ends. Beatrun's far-side test is one line straight ahead, and beside a narrow rail met end-on and a little aslant it
    /// found open air: the kong ran 2 m along a 4 m rail and dropped beside it (2026-11-07). Such a top is climbed onto.
    /// </summary>
    private bool CrossedBy(Collider surface, Vector3 edge, Vector3 forward, Vector3 end)
    {
        float along = Vector3.Dot(end - edge, forward);
        if (along <= 0f) return false;
        // Just under the top, from where the move ends back toward the near face, it must meet the far face: inside the
        // obstacle the ray finds nothing, and beside a rail it passes it or meets its side.
        Vector3 from = edge + forward * along - Vector3.up * .03f;
        return surface.Raycast(new Ray(from, -forward), out RaycastHit far, along) && Vector3.Dot(far.normal, forward) > .5f;
    }

    private static void Finish(ref ObstacleTarget target, VaultKind kind, Vector3 edge, Vector3 normal)
    {
        target.Kind = kind;
        target.Edge = edge;
        target.EdgeNormal = normal;
    }

    // Not part of the port: where hands can rest on this obstacle, for characters whose arms don't meet it by themselves.
    // A top already found (topY) is looked for just under it first, wherever a jump has the feet: from up in a jump the
    // heights from the feet all passed over a 0.9 m rail, and the hands found no edge to go to (2026-11-07).
    private void FindEdge(Collider surface, Vector3 feet, Vector3 forward, float topY, out Vector3 edge, out Vector3 normal)
    {
        edge = normal = Vector3.zero;
        if (!float.IsNaN(topY) && At(topY - .04f, out edge, out normal)) return;
        foreach (float height in EdgeHeights)
            if (At(feet.y + height * u, out edge, out normal)) return;

        bool At(float y, out Vector3 found, out Vector3 facing)
        {
            found = facing = Vector3.zero;
            Vector3 from = feet;
            from.y = y;
            if (!surface.Raycast(new Ray(from, forward), out RaycastHit front, EdgeSearch * u)) return false;
            Vector3 flat = Vector3.ProjectOnPlane(front.normal, Vector3.up);
            if (flat.sqrMagnitude < .25f) return false;
            Vector3 above = front.point + forward * (3f * u) + Up(Hair);
            if (!surface.Raycast(new Ray(above, Vector3.down), out RaycastHit top, Mathf.Max(above.y - feet.y, above.y - y + .05f))) return false;
            found = front.point;
            found.y = top.point.y;
            facing = flat.normalized;
            return true;
        }
    }

    // How deep a top is along the way the player goes (m): between its near and far faces, just under the top. Unmeasured
    // (no faces within reach, as on a long platform), it counts as deep.
    private static float Depth(RaycastHit top, Vector3 forward)
    {
        const float Reach = 3f;
        Vector3 under = top.point - Vector3.up * .03f;
        if (!top.collider.Raycast(new Ray(under - forward * Reach, forward), out RaycastHit near, Reach)
            || !top.collider.Raycast(new Ray(under + forward * Reach, -forward), out RaycastHit far, Reach)) return float.PositiveInfinity;
        return Vector3.Distance(near.point, far.point);
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
