using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// dev-text: file (the record's lines are for whoever reads Logs/placement.txt; nothing here is shown in the game)

/// <summary>
/// Something a player set down that stays where it was put: a Support's planks, ammo crate or searchlight (Barricade),
/// its claymore (ClaymoreMine), and whatever a later class or a mod sets down. To the placement rule every one of them
/// is a structure: it takes room, and nothing else stands on it (it can come down under what stood there). And every
/// one of them is counted in one place (Deployables): how many its owner has standing, how many they may keep, and
/// which comes down when another goes up.
/// </summary>
public interface IPlaced
{
    /// <summary>Who set it down.</summary>
    PlayerMovement Owner { get; }
    /// <summary>Its kind: a row of Tables/Deployables (its name, who holds it, how many one player keeps).</summary>
    string KindId { get; }
    /// <summary>Still standing: not broken, not gone off, not already being taken back.</summary>
    bool Stands { get; }
    /// <summary>Taken back for a newer one of its owner's, past their most: it comes down and harms nobody.</summary>
    void Retire();
}

/// <summary>
/// The one rule of what stands in the way of something a player sets down, and of what it may stand on. Every
/// deployable asks here (a Support's structures through Barricade.Room and SupportKit.Spot, its claymore through
/// PlayerGrenades.Spot), and whatever comes later or from a mod asks the same two questions (Ray, Box), so no deployable
/// has a rule of its own to get wrong.
/// <para>
/// The user, 2026-11-18: "시체 콜라이더로 인해 서포트 구조물 … 설치가 막히는 판정 수정". Until then Barricade.Room tested
/// every layer and let through only what it named (a zombie, a player, a barricade), so everything it did not name was
/// a wall or a floor: a zombie's corpse lying on the spot, a severed arm, a dropped magazine, a stone, a physics crate,
/// even the planks still flying off the Support's own last barricade. The claymore had a second rule (a layer mask),
/// which let it be stuck on a barricade or a rolling crate and left hanging in the air when that went.
/// </para>
/// <para>
/// The rule says what a collider IS to something being set down, by what the thing is, not by where it was filed:
/// <list type="bullet">
/// <item>World: the built map, what stays where it is. The only thing a deployable stands on.</item>
/// <item>Body: a living player or zombie. It takes room (nothing goes up inside a friend) and is no ground.</item>
/// <item>Structure: another deployable (IPlaced). It takes room and is no ground.</item>
/// <item>Loose: what lies about and gives way. The dead (a doll, RagdollCorpse, whether a zombie's or a player's, and
/// what a dead one left standing), gore, what was thrown or dropped, anything physics carries (it is pushed aside by
/// what goes up, and would carry off what stood on it), anything walked through (a trigger: barbed wire, a zone), and
/// whatever else lies on the layer the players pass (WorldLayers.Passed). It never takes room and is never ground.</item>
/// </list>
/// The builder's own body is never in its own way.
/// </para>
/// <para>
/// One loose thing the rule cannot meet at all: a field pickup (FieldPickups: a medkit, a shield cell, ammo, money, a
/// gun on the ground). It has no body: its keeper holds it as a point on the floor and its look is made with every
/// collider taken off (PickupProps), so no ray or box here ever finds one and anything may be set down over it. The
/// user, 2026-11-18, of what their list had meant: "바닥에 떨어진 체력킷, 쉴드쉘이 서포트의 구조물 설치 판정을 막는다는
/// 뜻". Read in the code that day: nothing of a pickup can be met; what the rule did meet where one lay was the corpse
/// of the zombie that dropped it (it lies 30 s, the drop 90 s or more), which the old rule took for a wall. Nothing
/// pushes a pickup aside either, as physics pushes a corpse or a loose crate out from under what goes up: so the rule
/// that passes it also says where it then lies (Lie): out from inside a solid structure's body, on the free floor
/// right beside it. Its keeper asks when a thing is laid and when something is set down (Deployables.Stood).
/// </para>
/// Checked without playing: every placement let go, refused or not, leaves a line in Logs/placement.txt (PlayRecord)
/// with the collider that stood in the way and what the rule took it for, with what it passed there and which
/// pickups lay there; each pickup that made way (or had nowhere to go) leaves a line of its own. The reckoning of
/// where it goes is plain sums: Tools/Checks/pickup_aside_check.py runs the same ones on the game's own numbers.
/// </summary>
public static class Placement
{
    /// <summary>What a collider is to something being set down.</summary>
    [Flags]
    public enum Thing : byte
    {
        None = 0,
        World = 1,
        Body = 2,
        Structure = 4,
        Loose = 8,
    }

    /// <summary>What takes room and can be met at all: everything but what gives way.</summary>
    public const Thing Solid = Thing.World | Thing.Body | Thing.Structure;
    /// <summary>What a view or a line of sight ends on: the built world and what players built in it. Bodies are looked past.</summary>
    public const Thing Built = Thing.World | Thing.Structure;

    public const string RecordName = "placement";
    private const string RecordHeading = "what each placement let go met (Placement): REFUSED/OK kind fit at spot | by = the collider in the way "
        + "[what the rule took it for: why, layer] | passed = what lay there and gave way | over = the pickups lying there (they have no "
        + "body: 'colliders on them' must read 0). ASIDE/STAYS = on the PC that keeps the pickups (the host): a pickup a structure's body "
        + "held, where it went, or that no floor beside the structure was free";

    /// <summary>How far above or below a spot its floor is looked for (m): a step up or down, never the storey below.</summary>
    internal const float FloorSpan = .6f;
    // A hair above the floor point a lying thing is kept at (m): where a body is asked whether it holds the thing (one
    // resting on top of a crate is not in it), and how far off its floor the room it takes is measured.
    private const float FloorSkin = .05f;

    private static readonly RaycastHit[] rays = new RaycastHit[64];
    private static readonly Collider[] found = new Collider[64];
    private static readonly List<Collider> bodies = new List<Collider>();
    private static readonly Vector3[] sides = new Vector3[4];

    /// <summary>What this collider is to something being set down.</summary>
    public static Thing Of(Collider collider) => Of(collider, out _);

    /// <summary>What this collider is to something being set down, and in a word why (the record's).</summary>
    public static Thing Of(Collider collider, out string why)
    {
        why = "";
        if (collider == null) return Thing.None;
        if (collider.isTrigger) { why = "trigger"; return Thing.Loose; }
        // What it is comes first: a knocked-down zombie is carried by physics too, and it is alive.
        var enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
        {
            why = enemy.IsAlive ? "zombie" : "dead zombie";
            return enemy.IsAlive ? Thing.Body : Thing.Loose;
        }
        var player = collider.GetComponentInParent<PlayerMovement>();
        if (player != null)
        {
            why = player.IsDead ? "dead player" : "player";
            return player.IsDead ? Thing.Loose : Thing.Body;
        }
        if (collider.GetComponentInParent<IPlaced>() != null) { why = "placed"; return Thing.Structure; }
        // The dead lie in their dolls, held still or not.
        if (collider.GetComponentInParent<RagdollCorpse>() != null) { why = "corpse"; return Thing.Loose; }
        if (GoreGib.IsPiece(collider)) { why = "gore"; return Thing.Loose; }
        if (DroppedMagazine.IsMagazine(collider)) { why = "magazine"; return Thing.Loose; }
        if (ThrownStone.IsStone(collider)) { why = "thrown"; return Thing.Loose; }
        var carried = collider.attachedRigidbody;
        if (carried != null && !carried.isKinematic) { why = "physics"; return Thing.Loose; }
        if (collider.gameObject.layer == WorldLayers.Passed) { why = "passed layer"; return Thing.Loose; }
        why = "world";
        return Thing.World;
    }

    /// <summary>
    /// The nearest thing along a ray among those in <paramref name="meets"/>, the <paramref name="builder"/>'s own body
    /// aside: what it is (None when there is nothing within <paramref name="reach"/>) and where it was met.
    /// </summary>
    public static Thing Ray(Vector3 from, Vector3 way, float reach, Transform builder, Thing meets, out RaycastHit hit)
    {
        hit = default;
        if (reach <= 0f || way.sqrMagnitude < 1e-8f) return Thing.None;
        way.Normalize();
        int count = Physics.RaycastNonAlloc(from, way, rays, reach, ~0, QueryTriggerInteraction.Ignore);
        var hits = rays;
        // A full buffer may have left the nearest one out (a pile of corpses is a hundred shapes): ask for them all.
        if (count == rays.Length)
        {
            hits = Physics.RaycastAll(from, way, reach, ~0, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        var what = Thing.None;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var candidate = hits[i];
            if (candidate.collider == null || candidate.distance >= nearest) continue;
            if (builder != null && candidate.collider.transform.IsChildOf(builder)) continue;
            var kind = Of(candidate.collider);
            if ((kind & meets) == 0) continue;
            nearest = candidate.distance;
            hit = candidate;
            what = kind;
        }
        return what;
    }

    /// <summary>
    /// Whether a box holds any of the things in <paramref name="meets"/>, the <paramref name="builder"/>'s own body
    /// aside: what the first one found is (None when the box is free of them), and which collider.
    /// </summary>
    public static Thing Box(Vector3 middle, Vector3 half, Quaternion turn, Transform builder, Thing meets, out Collider by)
    {
        by = null;
        int count = Physics.OverlapBoxNonAlloc(middle, half, found, turn, ~0, QueryTriggerInteraction.Ignore);
        var inside = found;
        if (count == found.Length)
        {
            inside = Physics.OverlapBox(middle, half, turn, ~0, QueryTriggerInteraction.Ignore);
            count = inside.Length;
        }
        for (int i = 0; i < count; i++)
        {
            var candidate = inside[i];
            if (candidate == null || (builder != null && candidate.transform.IsChildOf(builder))) continue;
            var kind = Of(candidate);
            if ((kind & meets) == 0) continue;
            by = candidate;
            return kind;
        }
        return Thing.None;
    }

    // ---- What lies loose and has no body (a field pickup) ----

    /// <summary>
    /// Where a thing that lies loose and has no body comes to lie: <paramref name="at"/> (its floor point) itself,
    /// unless a standing structure's solid body holds that point. Then the free floor right beside the body, the
    /// thing's middle <paramref name="clear"/> m off the body's face, on the side nearest <paramref name="toward"/>,
    /// else on another side by the shortest way out. <paramref name="toward"/> is whoever set the structure down (it
    /// comes to lie on their side of what they built, where they can take it), or the thing's own place when it was
    /// laid into a structure already standing (only the shortest way out is left of the rule: a gun put down at an
    /// ammo crate). With no free floor on any side it stays where it is: a pickup is taken by
    /// reach and look, not by a ray, so it can still be taken from under what stands on it. Barbed wire is walked
    /// through (a trigger): nothing makes way for it.
    /// <paramref name="body"/>: the body that held it (null when nothing did, and <paramref name="at"/> comes back).
    /// <paramref name="placed"/>: a structure set down this very frame, asked by its own shape first; the physics
    /// scene is not relied on for a collider that new.
    /// </summary>
    public static Vector3 Lie(Vector3 at, float clear, Vector3 toward, out Collider body, Component placed = null)
    {
        body = placed != null ? Holder(placed, at) : null;
        if (body == null) body = Holder(at);
        if (body == null) return at;
        Footprint(body, out Vector3 middle, out Vector3 along, out Vector3 through, out float halfAlong, out float halfThrough);
        Sides(at, middle, along, through, halfAlong + clear, halfThrough + clear, toward, sides);
        for (int i = 0; i < sides.Length; i++)
            if (Free(at, sides[i], clear, out Vector3 floor)) return floor;
        return at;
    }

    /// <summary>
    /// Whether a body holds a point, reckoned from the body's own shape where that is a box (every structure's so
    /// far), else from its bounds.
    /// </summary>
    internal static bool Holds(Collider body, Vector3 point)
    {
        if (!(body is BoxCollider box)) return body.bounds.Contains(point);
        Vector3 local = box.transform.InverseTransformPoint(point) - box.center, half = box.size * .5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    // The solid body of this structure that holds a lying thing's floor point, by the body's own shape.
    private static Collider Holder(Component placed, Vector3 at)
    {
        placed.GetComponentsInChildren(false, bodies);
        Vector3 point = at + Vector3.up * FloorSkin;
        Collider holder = null;
        foreach (var body in bodies)
            if (body != null && body.enabled && !body.isTrigger && Holds(body, point)) { holder = body; break; }
        bodies.Clear();
        return holder;
    }

    // The solid body of any standing structure that holds a lying thing's floor point, as the physics scene has it.
    private static Collider Holder(Vector3 at)
    {
        Vector3 point = at + Vector3.up * FloorSkin;
        float small = FloorSkin * .5f;
        int count = Physics.OverlapSphereNonAlloc(point, small, found, ~0, QueryTriggerInteraction.Ignore);
        var inside = found;
        if (count == found.Length)
        {
            inside = Physics.OverlapSphere(point, small, ~0, QueryTriggerInteraction.Ignore);
            count = inside.Length;
        }
        for (int i = 0; i < count; i++)
            if (inside[i] != null && Of(inside[i]) == Thing.Structure) return inside[i];
        return null;
    }

    // A body's footprint on the floor: its middle, its two flat axes and its half sizes along them. A box standing
    // upright (every structure so far) by its own shape; anything else by the box its bounds make.
    private static void Footprint(Collider body, out Vector3 middle, out Vector3 along, out Vector3 through, out float halfAlong, out float halfThrough)
    {
        if (body is BoxCollider box && box.transform.up.y > .99f)
        {
            var frame = box.transform;
            Vector3 scale = frame.lossyScale;
            middle = frame.TransformPoint(box.center);
            along = Vector3.ProjectOnPlane(frame.right, Vector3.up).normalized;
            through = Vector3.ProjectOnPlane(frame.forward, Vector3.up).normalized;
            halfAlong = Mathf.Abs(box.size.x * scale.x) * .5f;
            halfThrough = Mathf.Abs(box.size.z * scale.z) * .5f;
            return;
        }
        var bounds = body.bounds;
        middle = bounds.center;
        along = Vector3.right;
        through = Vector3.forward;
        halfAlong = bounds.extents.x;
        halfThrough = bounds.extents.z;
    }

    /// <summary>
    /// The four spots beside a footprint that a thing lying at <paramref name="at"/> can make way to: straight out
    /// through each side of the rectangle (the body's footprint grown by how far off its face the thing must lie:
    /// <paramref name="halfAlong"/>, <paramref name="halfThrough"/>), keeping its place along that side, at the height
    /// it lay. First the one nearest <paramref name="toward"/> (flat distance: the side its builder stands on), then
    /// the others by how short the way there is for the thing itself. Plain sums, no physics: the check tool runs the
    /// same ones (Tools/Checks/pickup_aside_check.py), so change them together.
    /// </summary>
    internal static void Sides(Vector3 at, Vector3 middle, Vector3 along, Vector3 through, float halfAlong, float halfThrough, Vector3 toward, Vector3[] into)
    {
        Vector3 off = at - middle, foot = new Vector3(middle.x, at.y, middle.z);
        float x = Vector3.Dot(off, along), z = Vector3.Dot(off, through);
        into[0] = foot + along * x - through * halfThrough;
        into[1] = foot + along * x + through * halfThrough;
        into[2] = foot - along * halfAlong + through * z;
        into[3] = foot + along * halfAlong + through * z;
        // The shortest way out first ...
        for (int i = 1; i < 4; i++)
            for (int j = i; j > 0 && FlatSquare(into[j] - at) < FlatSquare(into[j - 1] - at); j--)
                (into[j], into[j - 1]) = (into[j - 1], into[j]);
        // ... and before them all the side nearest whoever it should come to lie toward (the thing itself: no change).
        int first = 0;
        for (int i = 1; i < 4; i++)
            if (FlatSquare(into[i] - toward) < FlatSquare(into[first] - toward)) first = i;
        for (int j = first; j > 0; j--)
            (into[j], into[j - 1]) = (into[j - 1], into[j]);
    }

    private static float FlatSquare(Vector3 way) => way.x * way.x + way.z * way.z;

    // Whether a lying thing can make way from one floor point to another: the built world's floor under the spot within
    // a step of where it lay (no ledge's drop, no other structure's top), nothing built standing in the room it takes
    // there, and nothing of the map between the two (a thin wall the structure stands against is not gone through).
    private static bool Free(Vector3 from, Vector3 spot, float clear, out Vector3 floor)
    {
        floor = spot;
        if (Ray(spot + Vector3.up * FloorSpan, Vector3.down, FloorSpan * 2f, null, Built, out RaycastHit ground) != Thing.World) return false;
        if (ground.normal.y < .5f) return false;
        floor = ground.point;
        // The room it takes, standing on the floor as the floor leans.
        float half = clear * .5f;
        var lean = Quaternion.FromToRotation(Vector3.up, ground.normal);
        if (Box(floor + ground.normal * (half + FloorSkin), new Vector3(half, half, half), lean, null, Built, out _) != Thing.None) return false;
        Vector3 lift = Vector3.up * (half + FloorSkin), way = floor - from;
        return Ray(from + lift, way, way.magnitude, null, Thing.World, out _) == Thing.None;
    }

    // ---- The record (Logs/placement.txt) ----

    /// <summary>A collider as the record names it: where it hangs in the scene, what the rule takes it for and why.</summary>
    public static string Describe(Collider collider)
    {
        if (collider == null) return "nothing";
        var what = Of(collider, out string why);
        return $"\"{PlayRecord.PathOf(collider.transform)}\" [{what}: {why}, layer {collider.gameObject.layer}]";
    }

    /// <summary>
    /// What lies in a box and gives way to something set down there, counted by what it is ("corpse ×14, gore ×2"):
    /// the record's proof that the dead were there and were passed. Asked only when a line is written.
    /// </summary>
    public static string Passed(Vector3 middle, Vector3 half, Quaternion turn, Transform builder)
    {
        var inside = Physics.OverlapBox(middle, half, turn, ~0, QueryTriggerInteraction.Collide);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in inside)
        {
            if (candidate == null || (builder != null && candidate.transform.IsChildOf(builder))) continue;
            if (Of(candidate, out string why) != Thing.Loose) continue;
            counts[why] = counts.TryGetValue(why, out int n) ? n + 1 : 1;
        }
        if (counts.Count == 0) return "nothing";
        var text = new StringBuilder();
        foreach (var pair in counts)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(pair.Key).Append(" ×").Append(pair.Value);
        }
        return text.ToString();
    }

    /// <summary>
    /// A placement let go: what was set down (or not), where, and what the rule met there. <paramref name="fit"/> is the
    /// deployable's own word for the outcome ("Ok", "Blocked", "Steep"); <paramref name="by"/> the collider in the way;
    /// <paramref name="over"/> what lay there with no body for the rule to meet, in its keeper's words (FieldPickups.Under).
    /// </summary>
    public static void Record(string kind, string fit, bool placed, Vector3 at, float yaw, Vector3 size, Collider by, Transform builder, string over = null)
    {
        if (!PlayRecord.On) return;
        Vector3 middle = at + Vector3.up * (size.y * .5f + .1f);
        var half = new Vector3(size.x * .5f, Mathf.Max(.05f, size.y * .5f - .05f), size.z * .5f);
        string passed = Passed(middle, half, Quaternion.Euler(0f, yaw, 0f), builder);
        PlayRecord.Line(RecordName, RecordHeading, $"{(placed ? "OK     " : "REFUSED")} {kind} {fit} at {PlayRecord.At(at)} yaw {yaw:0}"
            + $" | by {(by != null ? Describe(by) : "nothing")} | passed {passed} | over {(string.IsNullOrEmpty(over) ? "nothing" : over)}");
    }

    /// <summary>
    /// A line of the same record from whoever keeps what lies loose there: a pickup that made way for a structure, or
    /// had nowhere to go (FieldPickups).
    /// </summary>
    public static void Note(string line)
    {
        if (PlayRecord.On) PlayRecord.Line(RecordName, RecordHeading, line);
    }
}
