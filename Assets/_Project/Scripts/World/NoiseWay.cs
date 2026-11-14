using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// How a sound gets from where it is made to an ear, and how much of its reach is left when it does (ZombieMind hears by
/// it). Two ways, and whichever leaves the sound louder counts:
/// - Straight through what stands between. Each wall on the line leaves Cuts.Wall of the reach and each floor or ceiling
///   Cuts.Floor, or Cuts.LoudFloor for a loud sound (Cuts.LoudFrom metres or more, and no lure): gunfire and blasts carry
///   from storey to storey, steps and voices don't. An open doorway or a hole in a floor on the line is no wall.
/// - Round by the open way, as a zombie would walk it: up the stairs, through open doors, down a hole it can drop through
///   (the walk mesh; a shut door carves it and is no way). Measured along that way, each right angle it turns leaving
///   Cuts.Bend of the reach, a gentler turn less.
/// It was a round reach that anything at all between halved once, so a concrete floor cut no more than a partition, and
/// a step is made right on the floor's top, where that one ray often started inside the slab and found nothing to cut
/// it: the zombie on the storey below, its ears 1.4 m under the step, heard a walk and woke to a run (user, 2026-11-14,
/// chapter 1's flats: "아니 아파트에선 걸을 수가 없네. ㅅㅂ 위아래도 애들이 존나 잘 들으면 뭐 맨날 걸어다녀야 되잖아").
/// </summary>
public static class NoiseWay
{
    // A sound is heard from this far above the point it was made at (m): a step's point lies right on the floor's top.
    private const float Lift = .1f;
    // Past each face a ray met, how far on the next ray starts (m).
    private const float Skip = .02f;
    // Faces counted each way at most: past this many the sound is lost anyway.
    private const int MostFaces = 5;
    // A face whose normal is this upright or more is a floor or a ceiling; less, a wall.
    private const float Upright = .7f;
    // How far off the walk mesh a sound or a listener may be and still be put on it (m): a shot at head height, a stone on a table.
    private const float MeshReach = 2f;
    // A way round once found serves the sounds made within SameSpot of where it was found from (m) while the listener stays
    // within Stayed of where it stood (m), for Keep seconds: a walk's steps, a burst of fire.
    private const float SameSpot = 1f, Stayed = .4f, Keep = 2f;
    // The most corners a way round may have (longer ways carry nothing that far anyway).
    private const int MostCorners = 48;

    /// <summary>What cuts a sound on its way to a listener: its numbers (Tables/ZombieSenses).</summary>
    public readonly struct Cuts
    {
        /// <summary>Share of the reach each wall on the straight way leaves.</summary>
        public readonly float Wall;
        /// <summary>Share each floor or ceiling on the straight way leaves a sound that isn't loud.</summary>
        public readonly float Floor;
        /// <summary>Share each floor or ceiling on the straight way leaves a loud sound.</summary>
        public readonly float LoudFloor;
        /// <summary>A sound carrying this far or farther (m) is loud, unless it is a lure.</summary>
        public readonly float LoudFrom;
        /// <summary>Share of the reach each right angle on the way round leaves.</summary>
        public readonly float Bend;
        /// <summary>Searches of the walk mesh for a way round allowed a frame, all listeners together.</summary>
        public readonly int Searches;

        public Cuts(float wall, float floor, float loudFloor, float loudFrom, float bend, int searches)
        {
            Wall = wall;
            Floor = floor;
            LoudFloor = loudFloor;
            LoudFrom = loudFrom;
            Bend = bend;
            Searches = searches;
        }
    }

    /// <summary>One listener's last way round, kept a moment for the next sound made about there (each listener keeps its own).</summary>
    public struct Memory
    {
        internal bool Known, Found, Bends;
        // Where the sound was on the walk mesh, where the listener stood, and until when it serves.
        internal Vector3 From, Feet;
        internal float Until;
        // The way's first and last turn, its length between them (m) and how far it turns in all (degrees).
        internal Vector3 First, Last;
        internal float Middle, Turned;
    }

    private static NavMeshPath path;
    private static readonly Vector3[] corners = new Vector3[MostCorners];
    private static int searchFrame = -1, searched;
    // The last sound put on the walk mesh (every listener of one sound asks of the same point): for which kind of walker,
    // in which frame, and where it went.
    private static Vector3 placedFor, placedAt;
    private static int placedType, placedFrame = -1;
    private static bool placed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        searchFrame = placedFrame = -1;
        searched = 0;
        path = null;
    }

    /// <summary>
    /// Whether <paramref name="noise"/> gets to <paramref name="ear"/>, which hears it to <paramref name="reach"/> metres in
    /// the open (the listener's share of the sound's reach already taken), by the way that leaves it loudest: how far the
    /// sound went that way (<paramref name="distance"/>, m) and how far it carries by it (<paramref name="left"/>, m); it is
    /// heard at 1 - distance / left. <paramref name="feet"/> and <paramref name="walker"/> put the listener on the walk mesh
    /// (no walker: the default kind). Only what <paramref name="mask"/> takes in muffles it.
    /// </summary>
    public static bool Carries(in NoiseEvent noise, Vector3 ear, Vector3 feet, NavMeshAgent walker, float reach, in Cuts cuts,
        int mask, ref Memory memory, out float distance, out float left)
    {
        Vector3 from = noise.Point + Vector3.up * Lift;
        distance = Vector3.Distance(from, ear);
        left = reach;
        // No way round is shorter than the straight one.
        if (reach <= 0f || distance > reach) return false;
        if (distance < Skip) return true;
        float floor = !noise.Lure && noise.Radius >= cuts.LoudFrom ? cuts.LoudFloor : cuts.Floor;
        float through = reach * Through(from, ear, mask, cuts.Wall, floor);
        // Nothing between: nothing round is louder.
        if (through >= reach) return true;
        bool straight = distance <= through;
        if (Around(from, ear, feet, walker, cuts, ref memory, out float way, out float share))
        {
            float round = reach * share;
            if (way <= round && (!straight || way / round < distance / through))
            {
                distance = way;
                left = round;
                return true;
            }
        }
        left = through;
        return straight;
    }

    // One face a straight way met: what it belongs to, and whether it lies flat (a floor or a ceiling).
    private struct Face
    {
        public Collider Shape;
        public bool Flat;
    }

    // The faces met going out from the sound and coming back from the ear (reused: hearing runs on the main thread only).
    private static readonly Face[] outward = new Face[MostFaces], backward = new Face[MostFaces];

    // The share of a sound's reach left going straight from one point to the other: each floor or ceiling between leaves
    // floorShare of it, each wall wallShare. Counted from both ends, the larger count of each kind standing: a ray meets
    // only the faces turned to it (a one-sided floor from above, the ceiling under it from below), and never the solid it
    // starts in.
    private static float Through(Vector3 from, Vector3 to, int mask, float wallShare, float floorShare)
    {
        int outs = Faces(from, to, mask, outward), backs = Faces(to, from, mask, backward);
        Tally(outward, outs, backward, backs, to, out int floorsOut, out int wallsOut);
        Tally(backward, backs, outward, outs, from, out int floorsBack, out int wallsBack);
        return Mathf.Pow(Mathf.Clamp01(floorShare), Mathf.Max(floorsOut, floorsBack))
            * Mathf.Pow(Mathf.Clamp01(wallShare), Mathf.Max(wallsOut, wallsBack));
    }

    // The faces met going straight from one point to the other, nearest first, at most MostFaces: how many it found.
    private static int Faces(Vector3 from, Vector3 to, int mask, Face[] met)
    {
        Vector3 line = to - from;
        float left = line.magnitude;
        if (left < Skip) return 0;
        Vector3 way = line / left;
        Vector3 at = from;
        int count = 0;
        while (count < met.Length && Physics.Raycast(at, way, out RaycastHit hit, left, mask, QueryTriggerInteraction.Ignore))
        {
            met[count++] = new Face { Shape = hit.collider, Flat = Mathf.Abs(hit.normal.y) >= Upright };
            float on = hit.distance + Skip;
            left -= on;
            if (left <= 0f) break;
            at += way * on;
        }
        return count;
    }

    // The floors or ceilings and the walls among one way's faces, leaving out the solid its far end lies in: a sound isn't
    // muffled by the thing it is made in (a door it bursts, a car it wails from, a speaker on a wall), nor an ear by a wall
    // a head pokes into.
    private static void Tally(Face[] met, int count, Face[] other, int others, Vector3 end, out int floors, out int walls)
    {
        floors = walls = 0;
        for (int i = 0; i < count; i++)
        {
            if (Shell(met[i].Shape, end, other, others)) continue;
            if (met[i].Flat) floors++;
            else walls++;
        }
    }

    // Whether this point lies inside that solid. A box, a ball, a capsule or a convex mesh can say. A hollow mesh can't: it
    // counts as holding the point when its bounds do and the way out from the point never met it (a ray starting inside a
    // mesh meets none of its faces), as a car's body round its alarm.
    private static bool Shell(Collider shape, Vector3 point, Face[] other, int others)
    {
        if (shape == null || !shape.bounds.Contains(point)) return false;
        if (shape is MeshCollider mesh && !mesh.convex)
        {
            for (int i = 0; i < others; i++) if (other[i].Shape == shape) return false;
            return true;
        }
        bool solid = shape is MeshCollider || shape is BoxCollider || shape is SphereCollider || shape is CapsuleCollider;
        return solid && (shape.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
    }

    // The way round by the walk mesh from the sound to the listener: how long it is from the sound to the ear (m), and the
    // share of the reach its turns leave. False when there is none, or none could be looked for this frame.
    private static bool Around(Vector3 from, Vector3 ear, Vector3 feet, NavMeshAgent walker, in Cuts cuts, ref Memory memory,
        out float length, out float share)
    {
        length = share = 0f;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = walker != null ? walker.agentTypeID : 0,
            areaMask = walker != null ? walker.areaMask : NavMesh.AllAreas
        };
        if (!Place(from, filter, out Vector3 start)) return false;
        bool kept = memory.Known && Time.time < memory.Until && (memory.From - start).sqrMagnitude <= SameSpot * SameSpot
            && (memory.Feet - feet).sqrMagnitude <= Stayed * Stayed;
        if (!kept && !Search(start, feet, filter, cuts.Searches, ref memory)) return false;
        if (!memory.Found) return false;
        length = memory.Bends ? Vector3.Distance(from, memory.First) + memory.Middle + Vector3.Distance(memory.Last, ear)
            : Vector3.Distance(from, ear);
        share = Mathf.Pow(Mathf.Clamp01(cuts.Bend), memory.Turned / 90f);
        return true;
    }

    // Where a sound is on the walk mesh: looked up once a sound for each kind of walker, as every listener asks of it.
    private static bool Place(Vector3 point, NavMeshQueryFilter filter, out Vector3 onMesh)
    {
        if (placedFrame != Time.frameCount || placedType != filter.agentTypeID || (placedFor - point).sqrMagnitude > 1e-6f)
        {
            placedFrame = Time.frameCount;
            placedType = filter.agentTypeID;
            placedFor = point;
            placed = NavMesh.SamplePosition(point, out NavMeshHit hit, MeshReach, filter);
            placedAt = placed ? hit.position : point;
        }
        onMesh = placedAt;
        return placed;
    }

    // Looks for the way round on the walk mesh and keeps it in the listener's memory, found or not. False when this frame's
    // searches are used up: nothing is kept, and the next sound asks again.
    private static bool Search(Vector3 start, Vector3 feet, NavMeshQueryFilter filter, int most, ref Memory memory)
    {
        if (searchFrame != Time.frameCount) { searchFrame = Time.frameCount; searched = 0; }
        if (searched >= most) return false;
        searched++;
        memory.Known = true;
        memory.From = start;
        memory.Feet = feet;
        memory.Until = Time.time + Keep;
        memory.Found = memory.Bends = false;
        memory.Middle = memory.Turned = 0f;
        if (!NavMesh.SamplePosition(feet, out NavMeshHit stand, MeshReach, filter)) return true;
        path ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(start, stand.position, filter, path) || path.status != NavMeshPathStatus.PathComplete) return true;
        int count = path.GetCornersNonAlloc(corners);
        if (count < 2 || count >= corners.Length) return true;
        memory.Found = true;
        if (count == 2) return true;
        memory.Bends = true;
        memory.First = corners[1];
        memory.Last = corners[count - 2];
        for (int i = 1; i < count - 2; i++) memory.Middle += Vector3.Distance(corners[i], corners[i + 1]);
        for (int i = 1; i < count - 1; i++) memory.Turned += Vector3.Angle(corners[i] - corners[i - 1], corners[i + 1] - corners[i]);
        return true;
    }
}
