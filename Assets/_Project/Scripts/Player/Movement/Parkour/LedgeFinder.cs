using System.Collections.Generic;
using UnityEngine;

/// <summary>How a body gets from hanging on a ledge onto its top: not at all (a ledge to hang from only), crouched, or standing.</summary>
public enum LedgeClimb : byte { None, Crouch, Stand }

/// <summary>
/// What a ledge is, and the one place it is found. A ledge is an edge a body can take hold of and hang from: an upright
/// face with a top a palm can rest on, free space over that top for the hand to come over the edge, and room below for
/// the body to hang. Whether the body can also get on top of it, and in what posture, is part of the ledge (LedgeClimb):
/// one that only holds is still a ledge, and says so.
/// The movement (ParkourDetector.TryLedge, the shimmy, the climb up) and the hands (PlayerBodyHands.Ledge) both ask here,
/// so they cannot disagree about where an edge is. Until 2026-11-18 each had a rule of its own. The movement's took the
/// first thing under a ray cast down from above the hands, if it belonged to the same collider as the wall: a ray that
/// starts inside a ceiling slab passes out of it unseen and lands on the top of the wall the slab rests on, so the seam
/// between wall and slab was a ledge, the hands went into it, and the climb up, which needs the room the slab fills,
/// did nothing: the player hung there until they jumped away or let go (the user, 2026-11-18: "천장 지지 벽 상단에 손이
/// 걸려 벽과 천장 사이로 클리핑되며 끼이는 현상"). And the slab's own top, the edge that was meant, was refused for
/// being another collider than the wall. Here a top counts by what is there, whichever collider it is: free space over
/// it for a hand (HandRoom), which a seam does not have, is what makes it a ledge. This is what Beatrun's ClimbingCheck
/// asks with its traces that must start outside the solid and its clear line back over the top (climb.lua).
/// Everything is asked of the physics scene with plain numbers (Body, Rules), so an editor sweep and a console probe
/// ask exactly what the game asks (LedgeSweep, the console's /ledge). The numbers are Player.csv's Ability.ledge* rows.
/// </summary>
public static class LedgeFinder
{
    /// <summary>The body that would hang, in metres, what is world to it, and what is itself.</summary>
    public struct Body
    {
        public float Radius, Height, CrouchHeight, SlopeLimit;
        public int Layers;
        public Transform Self;
    }

    /// <summary>The design numbers of a ledge (Player.csv Ability.ledge*), all in metres.</summary>
    public struct Rules
    {
        /// <summary>How far ahead a face is looked for.</summary>
        public float Reach;
        /// <summary>Hanging, the edge is this far under the top of the head: the feet hang the body's height less this below it.</summary>
        public float EdgeBelowHead;
        /// <summary>The face is looked for this far under the hands' height.</summary>
        public float WallBelowHands;
        /// <summary>A top from this far under the hands' height to this far over it can be caught: the reach of the arms.</summary>
        public float BelowHands, AboveHands;
        /// <summary>How far in from the face the palm rests, where the top is looked for.</summary>
        public float Inset;
        /// <summary>The free height a hand needs over the top to come over the edge.</summary>
        public float HandRoom;

        /// <summary>How far above the feet the hands hold when the body hangs.</summary>
        public float Hands(float bodyHeight) => bodyHeight - EdgeBelowHead;
    }

    public enum Why : byte { Found, NoFace, NoTop, TooSteep, NoHandRoom, HangBlocked }

    /// <summary>What a search came to and where, for the records: the top it took, or the last it refused and why.</summary>
    public struct Probe
    {
        public Why Result;
        public Vector3 Point;
        public Collider On;
    }

    // The gap kept between a hanging capsule and the face; how far under a top its face is looked for, and from how far
    // out; how thick the cast for the face is; how far out past the edge and how wide a hand's room is.
    private const float HangGap = .06f, FaceUnder = .03f, FaceReach = .3f, CastRadius = .1f, HandOut = .04f, HandHalfWidth = .04f;
    private static readonly RaycastHit[] hits = new RaycastHit[16];
    private static readonly Collider[] touching = new Collider[16];
    private static readonly System.Comparison<RaycastHit> Nearest = (a, b) => a.distance.CompareTo(b.distance);
    private static readonly List<RaycastHit> ordered = new List<RaycastHit>(16);

    /// <summary>
    /// The ledge ahead of a body whose feet are at <paramref name="feet"/>, the way <paramref name="facing"/> looks: the
    /// highest top in the arms' reach that a hand can come over and a body can hang under. Its Climb is not set here
    /// (ParkourDetector plans the climb with the moves it has and writes what it found).
    /// </summary>
    public static bool Find(in Body body, in Rules rules, Vector3 feet, Vector3 facing, out LedgeTarget ledge, out Probe probe)
    {
        ledge = default;
        probe = new Probe { Result = Why.NoFace, Point = feet };
        facing = Vector3.ProjectOnPlane(facing, Vector3.up);
        if (facing.sqrMagnitude < 1e-6f) return false;
        facing.Normalize();
        float hands = rules.Hands(body.Height), low = hands - rules.BelowHands, high = hands + rules.AboveHands;
        if (!Face(body, rules, feet, facing, hands, high, out RaycastHit wall)) return false;
        Vector3 normal = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;

        // Every top on the line a palm's depth inside the face, from the highest the arms reach down to the lowest. A
        // ray sees nothing of a solid it starts in, so one that starts in a slab comes out under it onto whatever the
        // slab rests on; such a top is still looked at, and falls to the room a hand needs over it.
        Vector3 inside = wall.point - normal * rules.Inset;
        var from = new Vector3(inside.x, feet.y + high, inside.z);
        int count = Physics.RaycastNonAlloc(from, Vector3.down, hits, high - low, body.Layers, QueryTriggerInteraction.Ignore);
        ordered.Clear();
        for (int i = 0; i < count; i++) ordered.Add(hits[i]);
        ordered.Sort(Nearest);
        probe.Result = Why.NoTop;
        probe.Point = wall.point;
        probe.On = wall.collider;
        float flat = Mathf.Cos(body.SlopeLimit * Mathf.Deg2Rad);
        foreach (var top in ordered)
        {
            if (IsSelf(body, top.collider) || !WallDetector.CanUseSurface(top.collider)) continue;
            if (top.normal.y < flat) { Note(ref probe, Why.TooSteep, top); continue; }
            Edge(body, rules, wall, normal, top, out Vector3 edge, out Vector3 outward);
            if (!HandRoom(body, rules, edge, outward, top.normal)) { Note(ref probe, Why.NoHandRoom, top); continue; }
            Vector3 hang = edge + outward * (body.Radius + HangGap) - Vector3.up * hands;
            if (!ClearPath(body, feet, hang, body.Height)) { Note(ref probe, Why.HangBlocked, top); continue; }
            Transform support = top.collider.transform;
            ledge = new LedgeTarget
            {
                Surface = top.collider, LocalHang = support.InverseTransformPoint(hang), LocalEdge = support.InverseTransformPoint(edge),
                LocalNormal = support.InverseTransformDirection(outward)
            };
            probe = new Probe { Result = Why.Found, Point = edge, On = top.collider };
            return true;
        }
        return false;
    }

    // An upright face ahead that the body faces: at the height the hands' forearms meet a wall, or, when nothing is
    // there (the edge of a platform with air under it), anywhere up to the highest top in reach.
    private static bool Face(in Body body, in Rules rules, Vector3 feet, Vector3 facing, float hands, float high, out RaycastHit wall)
    {
        Vector3 chest = feet + Vector3.up * (hands - rules.WallBelowHands);
        bool found = Physics.SphereCast(chest, CastRadius, facing, out wall, rules.Reach, body.Layers, QueryTriggerInteraction.Ignore);
        if (!found)
        {
            Vector3 top = feet + Vector3.up * Mathf.Max(hands - rules.WallBelowHands, high - CastRadius);
            if (!Physics.CapsuleCast(chest, top, CastRadius, facing, out RaycastHit met, rules.Reach, body.Layers, QueryTriggerInteraction.Ignore)) return false;
            // The capsule's round end may have met the face's lower corner, whose normal leans: the face itself is asked,
            // straight on at the height it was met.
            var level = new Vector3(feet.x, met.point.y, feet.z);
            if (!Physics.Raycast(level, facing, out wall, rules.Reach + CastRadius + .05f, body.Layers, QueryTriggerInteraction.Ignore)) return false;
        }
        return WallDetector.CanUseSurface(wall.collider) && !IsSelf(body, wall.collider)
            && Mathf.Abs(wall.normal.y) <= .25f && Vector3.Dot(facing, -wall.normal) >= .4f;
    }

    // Where the fingers go over: the face just under this top, which may stand a little out from, or back from, the face
    // met lower down (a slab proud of its wall). Without one in reach, the face met lower down, at the top's height.
    private static void Edge(in Body body, in Rules rules, RaycastHit wall, Vector3 normal, RaycastHit top, out Vector3 edge, out Vector3 outward)
    {
        edge = new Vector3(wall.point.x, top.point.y, wall.point.z);
        outward = normal;
        Vector3 from = edge + normal * FaceReach - Vector3.up * FaceUnder;
        if (!First(body, from, -normal, FaceReach + rules.Inset, out RaycastHit face) || Mathf.Abs(face.normal.y) > .35f) return;
        Vector3 level = Vector3.ProjectOnPlane(face.normal, Vector3.up);
        if (level.sqrMagnitude < .25f || Vector3.Dot(level.normalized, normal) < .7f) return;
        outward = level.normalized;
        edge = new Vector3(face.point.x, top.point.y, face.point.z);
    }

    /// <summary>
    /// Whether a hand can come over this edge and rest on the top behind it: nothing of the world in the space from just
    /// outside the edge to a palm's depth inside it, a hand's room high (Rules.HandRoom) over the top. The seam under a
    /// slab, a top under a low overhang and a top with a wall standing on its edge have none. Zombies and the body
    /// itself are not the world.
    /// </summary>
    public static bool HandRoom(in Body body, in Rules rules, Vector3 edge, Vector3 outward, Vector3 up)
    {
        // Laid along the top itself, so a top that slopes is not taken for something in the hand's way.
        Vector3 along = Vector3.ProjectOnPlane(outward, up);
        if (along.sqrMagnitude < 1e-6f) return false;
        along.Normalize();
        float depth = (rules.Inset + HandOut) * .5f, height = Mathf.Max(.01f, rules.HandRoom) * .5f;
        Vector3 centre = edge + along * (HandOut - depth) + up * (height + .012f);
        int count = Physics.OverlapBoxNonAlloc(centre, new Vector3(HandHalfWidth, height, depth), touching,
            Quaternion.LookRotation(along, up), body.Layers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!IsSelf(body, touching[i]) && !DodgeEnemyPassThrough.IsEnemy(touching[i])) return false;
        return true;
    }

    /// <summary>
    /// The top one palm can rest on where the edge passes <paramref name="onEdge"/> (the hands, each at its own place
    /// along the ledge): the first thing under a point <paramref name="inset"/> inside the edge, within
    /// <paramref name="tolerance"/> of <paramref name="height"/>, facing up at least <paramref name="leastUp"/>, with a
    /// hand's room over it by the same rule the ledge itself was found by.
    /// </summary>
    public static bool HandTop(in Body body, in Rules rules, Vector3 onEdge, Vector3 outward, float inset, float height, float tolerance,
        float probeAbove, float leastUp, out RaycastHit top)
    {
        Vector3 probe = onEdge - outward * inset;
        probe.y = height + probeAbove;
        if (!First(body, probe, Vector3.down, probeAbove + tolerance, out top)) return false;
        if (top.normal.y < leastUp || Mathf.Abs(top.point.y - height) > tolerance) return false;
        return HandRoom(body, rules, new Vector3(onEdge.x, top.point.y, onEdge.z), outward, top.normal);
    }

    /// <summary>
    /// A place to stand on a ledge's top, under <paramref name="above"/>: walkable, on anything a body can use, and looked
    /// for from free space (a look that starts inside a slab would find the seam under it).
    /// </summary>
    public static bool Tread(in Body body, Vector3 above, float reach, out RaycastHit tread)
    {
        tread = default;
        if (Physics.CheckSphere(above, .005f, body.Layers, QueryTriggerInteraction.Ignore)) return false;
        return First(body, above, Vector3.down, reach, out tread) && WallDetector.CanUseSurface(tread.collider)
            && tread.normal.y >= Mathf.Cos(body.SlopeLimit * Mathf.Deg2Rad);
    }

    // ---- The body's room (ParkourDetector's capsule tests are these) ----

    /// <summary>The radius the body's room is asked with: a little inside the capsule, so standing against a wall is not being in it.</summary>
    public static float QueryRadius(in Body body, float height) => Mathf.Max(.05f, Mathf.Min(body.Radius, height * .5f) - .025f);

    public static void Capsule(in Body body, Vector3 feet, float height, out Vector3 bottom, out Vector3 top, out float radius)
    {
        radius = QueryRadius(body, height);
        bottom = feet + Vector3.up * (radius + .04f);
        top = feet + Vector3.up * Mathf.Max(radius + .04f, height - radius);
    }

    public static bool ClearCapsule(in Body body, Vector3 feet, float height, int layers)
    {
        Capsule(body, feet, height, out Vector3 a, out Vector3 b, out float r);
        return !Physics.CheckCapsule(a, b, r, layers, QueryTriggerInteraction.Ignore);
    }

    public static bool ClearPath(in Body body, Vector3 start, Vector3 end, float height) => ClearPath(body, start, end, height, body.Layers);

    public static bool ClearPath(in Body body, Vector3 start, Vector3 end, float height, int layers)
    {
        if (!ClearCapsule(body, end, height, layers)) return false;
        Vector3 delta = end - start;
        if (delta.sqrMagnitude < .0001f) return true;
        Capsule(body, start, height, out Vector3 a, out Vector3 b, out float r);
        return !Physics.CapsuleCast(a, b, r, delta.normalized, delta.magnitude, layers, QueryTriggerInteraction.Ignore);
    }

    // The first thing a ray meets that is not the body itself.
    private static bool First(in Body body, Vector3 from, Vector3 way, float reach, out RaycastHit hit)
    {
        hit = default;
        int count = Physics.RaycastNonAlloc(from, way, hits, reach, body.Layers, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < count; i++)
            if (!IsSelf(body, hits[i].collider) && (nearest < 0 || hits[i].distance < hits[nearest].distance)) nearest = i;
        if (nearest < 0) return false;
        hit = hits[nearest];
        return true;
    }

    private static bool IsSelf(in Body body, Collider collider)
        => body.Self != null && collider != null && collider.transform.IsChildOf(body.Self);

    // The first refusal that says most is kept: a top with no room for a hand (a seam) before one only too steep or
    // blocked below, since that is the one a map's builder wants to hear of.
    private static void Note(ref Probe probe, Why why, RaycastHit top)
    {
        if (probe.Result == Why.NoHandRoom && why != Why.NoHandRoom) return;
        probe.Result = why;
        probe.Point = top.point;
        probe.On = top.collider;
    }
}
