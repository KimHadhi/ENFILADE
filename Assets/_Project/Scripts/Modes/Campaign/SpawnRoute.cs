using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The mission's route through a map, and the flow of any place on it: how far along the route that place lies (m).
/// The route is what the maps already hold: the chapter's steps in order (Tables/CampaignSteps), from the spot named
/// Start through each step's spot, laid over the walk mesh as a zombie would walk it. Where the walk mesh does not
/// join two spots (a zipline, a roof to jump from, a shut gate) the route goes as far as the mesh does and then
/// straight across. A step's stretch is its way: the route from the spot before it to its own.
/// <para>A spawn point's flow is that of the part of the route it walks to in the least distance (Place: every place
/// of the route within reach is weighed by the walk to it); with no walk to the route at all, of the nearest part in
/// a straight line, the same floor first. Where the route crosses the same ground twice the point belongs to the
/// later crossing. A player's flow is that of the route's nearest place in a straight line, followed from where they
/// were a moment ago so that on ground crossed twice they stay on the crossing they are on (Trace): no path is
/// searched for them, they are looked at twice a second.</para>
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
    // A spawn point weighs every Stride-th place of the route within reach, then the ones between round the best: the
    // walk to a place changes little from one place to the next, so the answer is the same as weighing every one, at a
    // third of the path searches.
    private const int Stride = 3;
    // A player is looked for within this many places of the route either way of where they were half a second ago.
    private const int Window = 8;

    public readonly List<Leg> Legs = new List<Leg>();
    /// <summary>The route's whole length (m), and how far apart its places are.</summary>
    public float Length { get; private set; }
    public readonly float Grain;
    /// <summary>How many places the route is measured at, and how many path searches placing things on it has cost.</summary>
    public int Count => flows.Count;
    public int Searches { get; private set; }

    private readonly List<Vector3> places = new List<Vector3>();
    private readonly List<float> flows = new List<float>();
    // Each place as the walk mesh has it, looked for when a walk to it is first asked for (WalkTo): most places of a
    // laying are never walked to, and a laying that comes out as the last one was is thrown away at once.
    private Vector3[] onMesh = new Vector3[0];
    private byte[] meshed = new byte[0];
    private const byte NotLooked = 0, OnMesh = 1, OffMesh = 2;
    private readonly NavMeshPath path = new NavMeshPath();
    private readonly Vector3[] corners = new Vector3[256];
    private float[] away = new float[0], walks = new float[0];
    private float storey = 2f;
    private readonly float reach;

    private SpawnRoute(float grain, float reach)
    {
        Grain = Mathf.Max(.5f, grain);
        this.reach = Mathf.Max(Grain, reach);
    }

    /// <summary>
    /// The route of these steps through the scene's spots as they stand now. <paramref name="spots"/>: the scene's
    /// spots by name (CampaignRun's, a re-placed one included). <paramref name="grain"/>: how far apart its places are
    /// (m); <paramref name="reach"/>: how far from itself, in a straight line, it looks for a spawn point's walk to it
    /// (m). Call where a NavMeshPath may be made (not from a field initialiser).
    /// </summary>
    public static SpawnRoute Lay(IReadOnlyList<CampaignStep> steps, IReadOnlyDictionary<string, Transform> spots, float grain, float reach)
    {
        var route = new SpawnRoute(grain, reach);
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
        walks = new float[flows.Count];
        onMesh = new Vector3[flows.Count];
        meshed = new byte[flows.Count];
    }

    private void Add(Vector3 place, float flow)
    {
        places.Add(place);
        flows.Add(flow);
    }

    /// <summary>
    /// This laying came out the same as another: every place where it was and every stretch joined as it was. Then
    /// whatever was placed on the other stands on this one as it stood, and nothing need be placed again (most steps
    /// open no gate and move no spot).
    /// </summary>
    public bool SameAs(SpawnRoute other)
    {
        if (other == null || other.places.Count != places.Count || other.Legs.Count != Legs.Count) return false;
        for (int i = 0; i < Legs.Count; i++)
            if (Legs[i].HasSpot != other.Legs[i].HasSpot || Legs[i].Joined != other.Legs[i].Joined) return false;
        for (int i = 0; i < places.Count; i++)
            if ((places[i] - other.places[i]).sqrMagnitude > .0001f) return false;
        return true;
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
    /// Where a spawn point is on the route, by walking: of the route's places within reach of it in a straight line,
    /// the one the walk to is shortest. Where the route comes as near a second time (a crossing whose shortest walk is
    /// within the route's own grain of the shortest of all), the two are the same distance away, and the later crossing
    /// is its place: the squad has not been through there for the last time until then. With no walk to any of them (or
    /// off the walk mesh), the nearest in a straight line, the same floor first. False on a route with no places.
    /// A few dozen path searches, once a laying.
    /// </summary>
    public bool Place(Vector3 position, out Placement place)
    {
        place = default;
        int count = flows.Count;
        if (count == 0) return false;
        Distances(position);
        place.OnMesh = NavMesh.SamplePosition(position, out NavMeshHit feet, PlaceSearch, NavMesh.AllAreas);
        int best = -1;
        if (place.OnMesh)
        {
            for (int i = 0; i < count; i++) walks[i] = float.NaN;
            for (int i = 0; i < count; i += Stride) Weigh(feet.position, i);
            // The last place is the route's end, where a finale's points gather: weighed whatever the stride.
            Weigh(feet.position, count - 1);
            best = Round(feet.position, Shortest(0, count - 1));
            if (best >= 0) best = Round(feet.position, Crossing(best));
        }
        place.ByWalk = best >= 0;
        if (best < 0) best = Straight(position);
        place.Flow = flows[best];
        place.Off = place.ByWalk ? walks[best] : away[best];
        place.Leg = LegOf(place.Flow);
        return true;
    }

    // The walk to one place of the route, into `walks` (searched once a Place): negative for a place out of reach, off
    // the mesh or not joined to here.
    private void Weigh(Vector3 from, int index)
    {
        if (!float.IsNaN(walks[index])) return;
        walks[index] = away[index] > reach ? -1f : WalkTo(from, index);
    }

    // Of the places weighed between two of the route's places, the one the walk to is shortest; -1 for none.
    private int Shortest(int from, int to)
    {
        int best = -1;
        for (int i = from; i <= to; i++)
            if (walks[i] >= 0f && (best < 0 || walks[i] < walks[best])) best = i;
        return best;
    }

    // The places between the weighed ones round a place are weighed too, and the shortest of them all is it.
    private int Round(Vector3 from, int index)
    {
        if (index < 0) return index;
        int low = Mathf.Max(0, index - Stride + 1), high = Mathf.Min(flows.Count - 1, index + Stride - 1);
        for (int i = low; i <= high; i++) Weigh(from, i);
        return Shortest(low, high);
    }

    // The route may come as near a second time. A crossing is a run of weighed places next to one another along the
    // route that are all as near as the nearest of all (within the grain); a weighed place that is farther, or cannot
    // be walked to, parts two crossings. Of the crossings the last; of it, the place the walk to is shortest.
    private int Crossing(int nearest)
    {
        float most = walks[nearest] + Grain;
        int count = flows.Count, chosen = -1, run = -1;
        for (int i = 0; i <= count; i++)
        {
            if (i < count && float.IsNaN(walks[i])) continue;
            if (i < count && walks[i] >= 0f && walks[i] <= most)
            {
                if (run < 0 || walks[i] < walks[run]) run = i;
                continue;
            }
            // A farther place, one that cannot be walked to, or the route's end closes the run before it.
            if (run >= 0) chosen = run;
            run = -1;
        }
        return chosen;
    }

    /// <summary>
    /// A player's flow now, <paramref name="last"/> being where they were when last looked at (or where the mission
    /// stands, for someone looked at for the first time): the route's nearest place in a straight line among those
    /// within a few places of there, the same floor first, since nobody moves far along the route between two looks. On
    /// ground the route crosses twice that keeps them on the crossing they are on. When the route as a whole has a
    /// place nearer by more than a storey than any of those, they are somewhere else altogether (a fall, a shortcut, a
    /// way back to the rest stop) and that place is theirs. No path is searched.
    /// </summary>
    public float Trace(Vector3 position, float last)
    {
        int count = flows.Count;
        if (count == 0) return 0f;
        Distances(position);
        float low = last - Window * Grain, high = last + Window * Grain;
        int level = -1, other = -1;
        for (int i = 0; i < count; i++)
        {
            if (flows[i] < low || flows[i] > high) continue;
            if (other < 0 || away[i] < away[other]) other = i;
            if (Mathf.Abs(places[i].y - position.y) < storey && (level < 0 || away[i] < away[level])) level = i;
        }
        int near = level >= 0 ? level : other, anywhere = Straight(position);
        return flows[near >= 0 && away[near] <= away[anywhere] + storey ? near : anywhere];
    }

    /// <summary>The stretch a place stands on, by the route's nearest part in a straight line, the same floor first (a zombie, many of them at a time: no path is searched).</summary>
    public int LegAt(Vector3 position)
    {
        if (flows.Count == 0) return 0;
        Distances(position);
        return LegOf(flows[Straight(position)]);
    }

    // Every place's straight distance from a position, into `away`.
    private void Distances(Vector3 position)
    {
        for (int i = 0; i < flows.Count; i++) away[i] = Vector3.Distance(position, places[i]);
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
        if (meshed[index] == NotLooked)
        {
            bool on = NavMesh.SamplePosition(places[index], out NavMeshHit hit, PlaceSearch, NavMesh.AllAreas);
            meshed[index] = on ? OnMesh : OffMesh;
            onMesh[index] = on ? hit.position : places[index];
        }
        if (meshed[index] != OnMesh) return -1f;
        Searches++;
        if (!NavMesh.CalculatePath(from, onMesh[index], NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
        int count = path.GetCornersNonAlloc(corners);
        float length = 0f;
        for (int k = 1; k < count; k++) length += Vector3.Distance(corners[k - 1], corners[k]);
        return length;
    }
}
