using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The mission's route through a map, and the flow of any place on it: how far along the route that place lies (m).
/// The route is what the maps already hold: the chapter's steps in order (Tables/CampaignSteps), from the spot named
/// Start through each step's spot, laid over the walk mesh as a zombie would walk it. Where the walk mesh does not
/// join two spots (a zipline, a roof to jump from, a shut gate) the route goes as far as the mesh does and then
/// straight across. A step's stretch is its way: the route from the spot before it to its own.
/// <para>A place's flow is that of the part of the route it walks to in the least distance (Place); with no walk to the
/// route at all, of the nearest part in a straight line, the same floor first. Where the route crosses the same
/// ground twice a spawn point belongs to the later crossing, and a player to the one nearest where they were.</para>
/// <para>Laid when a chapter starts, and again whenever the mission moves on a step (a gate may have opened) or a spot
/// has moved (the landing zone the team chose, the airliner the tug pushes): SpawnGround. It needs no map rebuild and
/// nothing authored beyond the steps' spots, so a mod's map has it too.</para>
/// </summary>
internal sealed class SpawnRoute
{
    /// <summary>One step's stretch of the route.</summary>
    public struct Leg
    {
        public string Step, Spot;
        public CampaignStepKind Kind;
        /// <summary>The flow where it begins and where it ends, at its spot (m). Equal: no ground of its own (the same spot as the step before).</summary>
        public float Start, End;
        /// <summary>How much of it lies over the walk mesh, and how much goes straight over what the mesh does not join (m).</summary>
        public float Walk, Gap;
        /// <summary>The scene has its spot; the walk mesh joins its two ends.</summary>
        public bool HasSpot, Joined;
        /// <summary>Where its spot was when the route was laid, and how near counts as there (the step's radius).</summary>
        public Vector3 At;
        public float Radius;
        public bool HasGround => HasSpot && End - Start > .01f;
    }

    /// <summary>A place's place on the route.</summary>
    public struct Placement
    {
        /// <summary>How far along the route (m), the stretch that is on, and how far from the route the place is (m).</summary>
        public float Flow, Off;
        public int Leg;
        /// <summary>Off is a walk over the mesh (else a straight line: the mesh joins it to no part of the route).</summary>
        public bool ByWalk;
        /// <summary>The place itself is on the walk mesh.</summary>
        public bool OnMesh;
    }

    // How far from a spot, and from any other place, the walk mesh is looked for (m).
    private const float SpotSearch = 3f, PlaceSearch = 2f;
    // A place looks for its part of the route among this many of the route's nearest approaches to it, and a spawn
    // point then follows the route this many places either way while the walk grows shorter.
    private const int Approaches = 4, Follow = 8;

    public readonly List<Leg> Legs = new List<Leg>();
    /// <summary>The route's whole length (m), and how far apart its places are.</summary>
    public float Length { get; private set; }
    public readonly float Grain;
    /// <summary>How many places the route is measured at, and how many path searches placing things on it has cost.</summary>
    public int Count => flows.Count;
    public int Searches { get; private set; }

    private readonly List<Vector3> places = new List<Vector3>(), onMesh = new List<Vector3>();
    private readonly List<float> flows = new List<float>();
    private readonly List<bool> meshed = new List<bool>();
    private readonly NavMeshPath path = new NavMeshPath();
    private readonly Vector3[] corners = new Vector3[256];
    private readonly int[] nearest = new int[Approaches];
    private float[] away = new float[0];
    private float storey = 2f;

    private SpawnRoute(float grain) => Grain = Mathf.Max(.5f, grain);

    /// <summary>
    /// The route of these steps through the scene's spots as they stand now. <paramref name="spots"/>: the scene's
    /// spots by name (CampaignRun's, a re-placed one included). Call where a NavMeshPath may be made (not from a field
    /// initialiser).
    /// </summary>
    public static SpawnRoute Lay(IReadOnlyList<CampaignStep> steps, IReadOnlyDictionary<string, Transform> spots, float grain)
    {
        var route = new SpawnRoute(grain);
        // Two places more than an agent's height apart in height are on different floors.
        route.storey = Mathf.Max(.5f, NavMesh.GetSettingsByID(0).agentHeight);
        var line = new List<Vector3>();
        var owner = new List<int>();
        bool begun = false;
        Vector3 at = default;
        if (spots != null && spots.TryGetValue(CampaignRun.StartSpot, out var start) && start != null) { at = start.position; begun = true; }
        if (begun) { line.Add(at); owner.Add(0); }
        for (int i = 0; steps != null && i < steps.Count; i++)
        {
            var step = steps[i];
            var leg = new Leg { Step = step.Id, Spot = step.Spot, Kind = step.Kind, Radius = step.Radius };
            Transform spot = null;
            if (spots != null && !string.IsNullOrEmpty(step.Spot)) spots.TryGetValue(step.Spot, out spot);
            if (spot == null) { route.Legs.Add(leg); continue; }
            Vector3 to = spot.position;
            leg.HasSpot = true;
            leg.At = to;
            if (!begun)
            {
                // No Start in the scene: the route begins at the first spot there is.
                begun = leg.Joined = true;
                line.Add(to);
                owner.Add(i);
            }
            else if ((to - at).sqrMagnitude < .0025f) leg.Joined = true;
            else route.Join(at, to, i, line, owner, ref leg);
            at = to;
            route.Legs.Add(leg);
        }
        route.Measure(line, owner);
        return route;
    }

    // One stretch: the walk from one spot to the next as far as the mesh goes, then straight to the spot.
    private void Join(Vector3 from, Vector3 to, int index, List<Vector3> line, List<int> owner, ref Leg leg)
    {
        Vector3 last = from;
        if (NavMesh.SamplePosition(from, out NavMeshHit a, SpotSearch, NavMesh.AllAreas) && NavMesh.SamplePosition(to, out NavMeshHit b, SpotSearch, NavMesh.AllAreas)
            && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
        {
            int count = path.GetCornersNonAlloc(corners);
            for (int k = 0; k < count; k++)
            {
                if ((corners[k] - last).sqrMagnitude < .0025f) continue;
                if (k > 0) leg.Walk += Vector3.Distance(corners[k - 1], corners[k]);
                line.Add(corners[k]);
                owner.Add(index);
                last = corners[k];
            }
            leg.Joined = path.status == NavMeshPathStatus.PathComplete;
        }
        if ((to - last).sqrMagnitude < .0025f) return;
        // What the walk mesh does not join (or the spot's own height off the mesh, a step up to a platform).
        if (!leg.Joined) leg.Gap = Vector3.Distance(last, to);
        line.Add(to);
        owner.Add(index);
    }

    // The line's lengths: where each stretch ends, and a place every Grain along it.
    private void Measure(List<Vector3> line, List<int> owner)
    {
        float flow = 0f;
        var ends = new float[Legs.Count];
        for (int k = 1; k < line.Count; k++)
        {
            float length = Vector3.Distance(line[k - 1], line[k]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / Grain));
            for (int j = 0; j < steps; j++) Add(Vector3.Lerp(line[k - 1], line[k], j / (float)steps), flow + length * j / steps);
            flow += length;
            ends[owner[k]] = flow;
        }
        if (line.Count > 0) Add(line[line.Count - 1], flow);
        Length = flow;
        float before = 0f;
        for (int i = 0; i < Legs.Count; i++)
        {
            var leg = Legs[i];
            leg.Start = before;
            // A stretch with no line of its own (no spot, or the spot before it again) ends where it begins.
            leg.End = Mathf.Max(before, ends[i]);
            before = leg.End;
            Legs[i] = leg;
        }
        away = new float[flows.Count];
    }

    private void Add(Vector3 place, float flow)
    {
        places.Add(place);
        flows.Add(flow);
        bool on = NavMesh.SamplePosition(place, out NavMeshHit hit, PlaceSearch, NavMesh.AllAreas);
        meshed.Add(on);
        onMesh.Add(on ? hit.position : place);
    }

    /// <summary>The stretch a flow lies on: the first with ground of its own that reaches it (a place at a spot belongs to the stretch that ends there).</summary>
    public int LegOf(float flow)
    {
        int last = -1;
        for (int i = 0; i < Legs.Count; i++)
        {
            if (!Legs[i].HasGround) continue;
            last = i;
            if (flow <= Legs[i].End + .001f) return i;
        }
        return Mathf.Max(0, last);
    }

    /// <summary>A flow of another laying of the same steps, as this one has it: the same share of the same stretch.</summary>
    public float FromOther(SpawnRoute other, float flow)
    {
        if (other == null || other.Legs.Count != Legs.Count || Legs.Count == 0) return Mathf.Clamp(flow, 0f, Length);
        int index = other.LegOf(flow);
        var theirs = other.Legs[index];
        var mine = Legs[index];
        float share = theirs.End - theirs.Start > .01f ? Mathf.Clamp01((flow - theirs.Start) / (theirs.End - theirs.Start)) : 1f;
        return Mathf.Lerp(mine.Start, mine.End, share);
    }

    /// <summary>
    /// Where a place is on the route. <paramref name="hint"/>: the flow it was at before (a player), so that on ground
    /// the route crosses twice it stays on the crossing it was on; negative for none (a spawn point: the later
    /// crossing). <paramref name="thorough"/>: follow the route from the best approach while the walk grows shorter (a
    /// spawn point, placed once; a player is looked at again in a moment). False on a route with no length.
    /// </summary>
    public bool Place(Vector3 position, float hint, bool thorough, out Placement place)
    {
        place = default;
        if (flows.Count == 0) return false;
        int found = Approach(position);
        place.OnMesh = NavMesh.SamplePosition(position, out NavMeshHit feet, PlaceSearch, NavMesh.AllAreas);
        int best = -1;
        float least = float.PositiveInfinity, chosen = 0f;
        if (place.OnMesh)
        {
            for (int k = 0; k < found; k++)
            {
                int candidate = nearest[k];
                float walk = WalkTo(feet.position, candidate);
                if (walk < 0f) continue;
                if (best < 0 || walk < least - Grain) { best = candidate; least = chosen = walk; continue; }
                if (walk > least + Grain) continue;
                // As near as the route's own grain, the two are the same distance away: the one nearest where it was,
                // or for a place with no past the later crossing.
                if (Prefer(candidate, best, hint)) { best = candidate; chosen = walk; }
                least = Mathf.Min(least, walk);
            }
            if (best >= 0 && thorough)
            {
                // The route's nearest approach in a straight line is not always where the walk meets it (a room beside
                // a passage, its door farther back): along the route while the walk grows shorter.
                int from = best;
                float there = chosen;
                for (int way = -1; way <= 1; way += 2)
                {
                    float shortest = there;
                    for (int step = 1, at = from + way; step <= Follow && at >= 0 && at < flows.Count; step++, at += way)
                    {
                        float walk = WalkTo(feet.position, at);
                        if (walk < 0f || walk >= shortest - .01f) break;
                        shortest = walk;
                        if (walk < chosen) { chosen = walk; best = at; }
                    }
                }
            }
        }
        place.ByWalk = best >= 0;
        if (best < 0)
        {
            best = Straight(position);
            chosen = away[best];
        }
        place.Flow = flows[best];
        place.Off = chosen;
        place.Leg = LegOf(place.Flow);
        return true;
    }

    /// <summary>The stretch a place stands on, by the route's nearest part in a straight line, the same floor first (a zombie, many times a second: no path is searched).</summary>
    public int LegAt(Vector3 position)
    {
        if (flows.Count == 0) return 0;
        for (int i = 0; i < flows.Count; i++) away[i] = Vector3.Distance(position, places[i]);
        return LegOf(flows[Straight(position)]);
    }

    private bool Prefer(int candidate, int kept, float hint)
        => hint >= 0f ? Mathf.Abs(flows[candidate] - hint) < Mathf.Abs(flows[kept] - hint) : flows[candidate] > flows[kept];

    // The route's nearest approaches to a place: the places of the route nearer it than their neighbours, the nearest
    // few of them in `nearest`, the nearest first. Returns how many. `away` is left holding every place's straight
    // distance.
    private int Approach(Vector3 position)
    {
        int count = flows.Count, found = 0;
        for (int i = 0; i < count; i++) away[i] = Vector3.Distance(position, places[i]);
        for (int i = 0; i < count; i++)
        {
            // Nearer than both its neighbours (of a run of equals, the first).
            if (i > 0 && away[i - 1] <= away[i] || i + 1 < count && away[i + 1] < away[i]) continue;
            int at;
            if (found < Approaches) at = found++;
            else if (away[i] < away[nearest[Approaches - 1]]) at = Approaches - 1;
            else continue;
            nearest[at] = i;
            for (int k = at; k > 0 && away[nearest[k]] < away[nearest[k - 1]]; k--) (nearest[k], nearest[k - 1]) = (nearest[k - 1], nearest[k]);
        }
        return found;
    }

    // The nearest place of the route in a straight line, one on the same floor before any other. Reads `away`.
    private int Straight(Vector3 position)
    {
        int level = -1, any = 0;
        for (int i = 0; i < flows.Count; i++)
        {
            if (away[i] < away[any]) any = i;
            if (Mathf.Abs(places[i].y - position.y) < storey && (level < 0 || away[i] < away[level])) level = i;
        }
        return level >= 0 ? level : any;
    }

    // The walk from a place on the mesh to one of the route's places (m); negative when the mesh does not join them.
    private float WalkTo(Vector3 from, int index)
    {
        if (!meshed[index]) return -1f;
        Searches++;
        if (!NavMesh.CalculatePath(from, onMesh[index], NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
        int count = path.GetCornersNonAlloc(corners);
        float length = 0f;
        for (int k = 1; k < count; k++) length += Vector3.Distance(corners[k - 1], corners[k]);
        return length;
    }
}
