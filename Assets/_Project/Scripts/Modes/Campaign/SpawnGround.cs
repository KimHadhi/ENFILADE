using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The ground of a chapter as the squad's progress leaves it, on the host: where the route is (SpawnRoute), where
/// each spawn point and each living player is on it, how far the squad's front has come, and which spawn points'
/// ground is cleared. The one place that knows any of that; the horde asks it (CampaignHorde) and SpawnPick weighs
/// what it says.
/// <para>Cleared, as the user means it (2026-11-18: "소탕은 말 그대로 소탕입니다. 한 방을 클리어했다면 양자역학이 아닌 이상
/// 거기서 다시 좀비가 출몰해 나오는 게 이상하죠"; no step done, no ten metres past). The unit of ground is a spawn
/// point's own place, the room it stands in: a zombie made there comes out of that place, so that place is what can be
/// cleared. It is cleared the moment someone of the squad sees it from near (within clearWithin, a body standing on
/// it would be in their sight) while nothing alive stands near it (no living zombie within clearWithin of it, on its
/// floor): they are there, they look, and it is empty, whether they emptied it or found it so. From then on nothing
/// comes out of it, for the rest of the chapter: what the squad has seen empty does not fill up again behind its back.
/// A place it only passed by without a look inside (a flat whose door stayed shut) is not cleared; nor is one where
/// something still lives.</para>
/// <para>A spawn room (Tables/SpawnPoints) cannot be cleared: it is a way in from where the squad cannot go, and what
/// comes through it was never in the room to be killed. That is the difference between a room and open ground, and
/// why a map's holds want rooms.</para>
/// <para>A wipe puts the world after the last rest stop back as it was (every zombie gone, the calm ones set anew
/// along the way ahead), so the points past that rest stop on the route are no longer cleared; those before it stay
/// as they were. Nothing is set on cleared ground either (ClearedAt: the calm ones, a cage's crowd, a round's
/// walkers).</para>
/// <para>Only the host has it (the horde is the host's); it changes nothing a guest sees but where zombies appear.</para>
/// </summary>
internal sealed class SpawnGround
{
    /// <summary>One spawn point: what it is, where on the route it was found to be, and whether its ground is cleared.</summary>
    public struct PointRecord
    {
        public string Name;
        public SpawnRole Role;
        /// <summary>Its place on the route is known (Place); it was found on an earlier laying and waits to be found again on this one.</summary>
        public bool Placed, Stale;
        public SpawnRoute.Placement Place;
        /// <summary>The squad has cleared its ground; never so for a room.</summary>
        public bool Cleared;
    }

    /// <summary>
    /// What the spawn model does, a line at a time, for whoever keeps a record (SpawnReport: Logs/spawns.txt). With
    /// nobody listening no line is written at all: ask Heard before building one.
    /// </summary>
    public static event System.Action<string> Said;
    public static bool Heard => Said != null;
    public static void Say(string line) => Said?.Invoke(line);

    private readonly Transform[] points;
    private readonly PointRecord[] records;
    private readonly string chapter;
    private readonly Dictionary<PlayerMovement, float> flows = new Dictionary<PlayerMovement, float>(), lookAt = new Dictionary<PlayerMovement, float>();
    private readonly List<PlayerMovement> gone = new List<PlayerMovement>();
    private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
    // Where each step's spot stood at the last look (Moved).
    private Vector3[] seenAt = new Vector3[0];
    private SpawnRoute reportedFor;
    private int laidAtStep = -1, nextPoint;
    private float nextMoved, nextSweep, laidAt, storey;
    // What placing the points on the route as laid now has cost: in all and in its dearest frame (ms).
    private double placingMs, dearestMs;

    public SpawnGround(string chapter, Transform[] points)
    {
        this.chapter = chapter ?? "";
        this.points = points ?? new Transform[0];
        records = new PointRecord[this.points.Length];
        for (int i = 0; i < records.Length; i++)
        {
            string name = this.points[i] != null ? this.points[i].name : "";
            records[i] = new PointRecord { Name = name, Role = SpawnRules.RoleOf(this.chapter, name) };
        }
    }

    /// <summary>The route as it is laid now; null before the first Follow.</summary>
    public SpawnRoute Route { get; private set; }
    /// <summary>Where the squad is on the flow (SpawnPick.Squad): unknown on a map without a route or before anyone is placed.</summary>
    public SpawnPick.Squad Squad;
    /// <summary>The flow of the living player least far along (m), for the record; the rules ask only for the front.</summary>
    public float Rear { get; private set; }
    public int PointCount => records.Length;
    public PointRecord Record(int index) => records[index];
    /// <summary>Every spawn point has its place on the route as laid now.</summary>
    public bool AllPlaced => Route != null && nextPoint >= records.Length;
    public string Chapter => chapter;

    /// <summary>
    /// This place lies on cleared ground: within clearWithin of a spawn point the squad has cleared, on its floor. For
    /// whoever sets zombies somewhere by a rule of their own (the calm ones along the way, a cage's crowd, a round's
    /// walkers): nothing is set where the squad has looked and found nothing.
    /// </summary>
    public bool ClearedAt(Vector3 position)
    {
        float within = SpawnRules.ClearWithin, floor = Storey;
        for (int i = 0; i < records.Length; i++)
        {
            if (!records[i].Cleared || points[i] == null) continue;
            Vector3 away = points[i].position - position;
            if (Mathf.Abs(away.y) < floor && away.sqrMagnitude <= within * within) return true;
        }
        return false;
    }

    /// <summary>Where this player is on the route (m); false when they have no place yet.</summary>
    public bool TryFlow(PlayerMovement player, out float flow) => flows.TryGetValue(player, out flow);

    // Two places more than an agent's height apart in height are on different floors.
    private float Storey => storey > 0f ? storey : storey = Mathf.Max(.5f, NavMesh.GetSettingsByID(0).agentHeight);

    /// <summary>
    /// The host, every frame of a chapter. The route is laid when there is none, when the mission has moved on a step,
    /// when a step's spot has come to rest farther from where the route has it than that step's own radius (a spot a
    /// gimmick re-placed, one that rides a moving thing), and every relayEvery seconds besides: a door broken or a gate
    /// raised joins the walk mesh and nothing tells of it (the play of 2026-11-18: the laying of the chapter's first
    /// frame, before the map's blockers had carved the mesh, was not the one of twenty seconds on). Then a spawn point
    /// finds its place on it, each living player's place is looked at again when due, and so is what they have cleared.
    /// </summary>
    public void Follow(IReadOnlyList<CampaignStep> steps, int step, IReadOnlyDictionary<string, Transform> spots, List<PlayerMovement> players)
    {
        string why = Route == null ? "the chapter began" : step != laidAtStep ? "the mission moved on" : null;
        if (why == null && Time.time >= nextMoved)
        {
            nextMoved = Time.time + SpawnRules.LookEvery;
            if (Moved(spots)) why = "a spot moved";
            else if (Time.time - laidAt >= SpawnRules.RelayEvery) why = "the walk mesh may have changed";
        }
        if (why != null) Lay(steps, step, spots, why);
        PlacePoints(SpawnRules.PerFrame);
        Track(players, step);
        Sweep(players);
    }

    /// <summary>A wipe sent the run back to the step after this rest stop (-1: the chapter's start): the points past
    /// that rest stop on the route are no longer cleared.</summary>
    public void Wipe(int checkpointStep)
    {
        float from = Route != null && checkpointStep >= 0 && checkpointStep < Route.Legs.Count ? Route.Legs[checkpointStep].End : float.NegativeInfinity;
        int undone = 0;
        for (int i = 0; i < records.Length; i++)
        {
            // A point whose place is not known yet is taken as past it: uncleared is the state that makes no promise.
            if (!records[i].Cleared || records[i].Placed && records[i].Place.Flow <= from) continue;
            records[i].Cleared = false;
            undone++;
        }
        // Everyone comes back at the rest stop: their places are found again from there.
        flows.Clear();
        lookAt.Clear();
        Squad = default;
        Rear = 0f;
        if (Heard) Say($"WIPE back to after step {checkpointStep + 1}: {undone} points past it are no longer cleared · cleared now: {ClearedList()}");
    }

    // A step's spot stands farther from where the route has it than that step's radius, and no spot has moved since the
    // last look: a spot still on the move (the airliner the tug pushes, forty metres over a hold) is laid again once,
    // where it comes to rest, not every few metres of its way.
    private bool Moved(IReadOnlyDictionary<string, Transform> spots)
    {
        if (spots == null) return false;
        bool off = false, still = true;
        for (int i = 0; i < Route.Legs.Count; i++)
        {
            var leg = Route.Legs[i];
            if (!leg.HasSpot || string.IsNullOrEmpty(leg.Spot) || !spots.TryGetValue(leg.Spot, out var spot) || spot == null) continue;
            Vector3 now = spot.position;
            if (i < seenAt.Length)
            {
                if ((now - seenAt[i]).sqrMagnitude > .0001f) still = false;
                seenAt[i] = now;
            }
            float reach = Mathf.Max(.5f, leg.Radius);
            if ((now - leg.At).sqrMagnitude > reach * reach) off = true;
        }
        return off && still;
    }

    private void Lay(IReadOnlyList<CampaignStep> steps, int step, IReadOnlyDictionary<string, Transform> spots, string why)
    {
        var before = Route;
        Route = SpawnRoute.Lay(steps, spots, SpawnRules.Grain, SpawnRules.Reach);
        laidAtStep = step;
        laidAt = Time.time;
        nextMoved = Time.time + SpawnRules.LookEvery;
        int count = Route.Legs.Count;
        if (seenAt.Length != count) seenAt = new Vector3[count];
        for (int i = 0; i < count; i++) seenAt[i] = Route.Legs[i].At;
        // Most layings open no gate and move no spot: the route comes out as it was, and every point and player stands
        // on it where they stood. Placing the points again is what a laying costs (dozens of path searches each).
        if (Route.SameAs(before))
        {
            if (reportedFor == before) reportedFor = Route;
            return;
        }
        // The points keep the place they had until each is found again on the new laying (one a frame).
        for (int i = 0; i < records.Length; i++)
            if (records[i].Placed)
            {
                records[i].Stale = true;
                records[i].Place.Flow = Route.FromOther(before, records[i].Place.Flow);
            }
        nextPoint = 0;
        placingMs = dearestMs = 0d;
        // Each player's flow as the new laying has it, to be looked at again at once.
        if (before != null && flows.Count > 0)
        {
            gone.Clear();
            gone.AddRange(flows.Keys);
            foreach (var player in gone) flows[player] = Route.FromOther(before, flows[player]);
            gone.Clear();
        }
        lookAt.Clear();
        if (!Heard) return;
        Say($"ROUTE laid ({why}) at step {step + 1}: {Route.Length:0} m over {count} steps, {Route.Count} places {Route.Grain:0.#} m apart");
        for (int i = 0; i < count; i++)
        {
            var leg = Route.Legs[i];
            Say(leg.HasSpot
                ? $"  {leg.Step,-9} {leg.Kind,-10} {leg.Spot,-16} flow {leg.Start,6:0.0} -> {leg.End,6:0.0}  walk {leg.Walk,6:0.0}  gap {leg.Gap,5:0.0}"
                    + (leg.Joined ? "" : "  NOT JOINED by the walk mesh")
                : $"  {leg.Step,-9} {leg.Kind,-10} {leg.Spot,-16} NO SPOT of that name in the scene");
        }
    }

    private void PlacePoints(int most)
    {
        if (Route == null || nextPoint >= records.Length && (!Heard || reportedFor == Route)) return;
        clock.Restart();
        for (; most > 0 && nextPoint < records.Length; nextPoint++, most--)
        {
            var point = points[nextPoint];
            if (point == null) continue;
            records[nextPoint].Placed = Route.Place(point.position, out records[nextPoint].Place);
            records[nextPoint].Stale = false;
        }
        double took = clock.Elapsed.TotalMilliseconds;
        placingMs += took;
        if (took > dearestMs) dearestMs = took;
        if (nextPoint < records.Length || !Heard || reportedFor == Route) return;
        reportedFor = Route;
        SayPoints();
    }

    // Every point's place, and what is worth a look in it (FINDING lines).
    private void SayPoints()
    {
        Say($"POINTS placed: {records.Length} ({Route.Searches} path searches, {placingMs:0.0} ms in all, {dearestMs:0.00} ms in the dearest frame)");
        for (int i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (!record.Placed) { Say($"  {record.Name,-18} NO PLACE on the route" + (points[i] == null ? " (the scene lost the point)" : "")); continue; }
            var leg = Route.Legs[Mathf.Clamp(record.Place.Leg, 0, Route.Legs.Count - 1)];
            Say($"  {record.Name,-18} flow {record.Place.Flow,6:0.0}  stretch {leg.Step,-9} {leg.Kind,-10} {record.Role,-5} {record.Place.Off,5:0.0} m from the route "
                + (record.Place.ByWalk ? "by walk" : record.Place.OnMesh ? "in a STRAIGHT line (the walk mesh joins it to no part of the route)" : "in a STRAIGHT line (OFF the walk mesh)")
                + (record.Cleared ? "  cleared" : ""));
        }
        for (int i = 0; i < Route.Legs.Count; i++)
        {
            var leg = Route.Legs[i];
            if (!leg.HasSpot) { Say($"FINDING {leg.Step}: no spot '{leg.Spot}' in the scene, so the route skips it"); continue; }
            if (!leg.Joined) Say($"FINDING {leg.Step}: the walk mesh does not join it to the spot before it ({leg.Gap:0.0} m straight across) as it stands now: no zombie walks that way");
            int ahead = 0;
            foreach (var record in records) if (record.Placed && record.Place.Flow > leg.End) ahead++;
            if (ahead == 0 && i + 1 < Route.Legs.Count) Say($"FINDING {leg.Step}: no spawn point lies ahead of its spot");
        }
    }

    // Each living player's place on the route, looked at again every lookEvery (SpawnRoute.Trace: a straight line, no
    // path searched); the squad's front, and its rear for the record.
    private void Track(List<PlayerMovement> players, int step)
    {
        Squad = default;
        Rear = 0f;
        if (Route == null || Route.Length <= 0f || players == null) return;
        gone.Clear();
        foreach (var someone in flows.Keys) if (someone == null || !players.Contains(someone)) gone.Add(someone);
        foreach (var left in gone) { flows.Remove(left); lookAt.Remove(left); }
        gone.Clear();
        float now = Time.time, every = SpawnRules.LookEvery;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null) continue;
            bool known = flows.TryGetValue(player, out float flow);
            if (!known || !lookAt.TryGetValue(player, out float due) || now >= due)
            {
                lookAt[player] = now + every;
                // Someone with no place yet is looked for from where the mission stands.
                float from = known ? flow : Route.Legs.Count > 0 ? Route.Legs[Mathf.Clamp(step, 0, Route.Legs.Count - 1)].Start : 0f;
                flow = Route.Trace(player.transform.position, from);
                flows[player] = flow;
            }
            if (!Squad.Known) { Squad.Known = true; Squad.Front = Rear = flow; }
            else
            {
                Squad.Front = Mathf.Max(Squad.Front, flow);
                Rear = Mathf.Min(Rear, flow);
            }
        }
    }

    // What the squad has cleared, looked at every lookEvery: a spawn point (not a room) that someone living sees from
    // within clearWithin while no living zombie stands within clearWithin of it on its floor. It needs no route: a
    // place is seen and found empty wherever the mission stands.
    private void Sweep(List<PlayerMovement> players)
    {
        if (players == null || players.Count == 0 || Time.time < nextSweep) return;
        nextSweep = Time.time + SpawnRules.LookEvery;
        float within = SpawnRules.ClearWithin, most = within * within, floor = Storey;
        if (within <= 0f) return;
        var living = EnemyHealth.Living;
        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].Cleared || records[i].Role == SpawnRole.Room || points[i] == null) continue;
            Vector3 place = points[i].position;
            PlayerMovement seer = null;
            foreach (var player in players)
            {
                if (player == null || (player.transform.position - place).sqrMagnitude > most) continue;
                if (SpawnSight.Sees(player, place)) { seer = player; break; }
            }
            if (seer == null) continue;
            bool empty = true;
            for (int k = 0; k < living.Count && empty; k++)
            {
                var enemy = living[k];
                if (enemy == null || !enemy.IsAlive) continue;
                Vector3 away = enemy.transform.position - place;
                empty = Mathf.Abs(away.y) >= floor || away.sqrMagnitude > most;
            }
            if (!empty) continue;
            records[i].Cleared = true;
            if (Heard) Say($"CLEARED {records[i].Name}: {seer.name} sees it from {Vector3.Distance(seer.transform.position, place):0} m and nothing alive stands within {within:0.#} m of it");
        }
    }

    /// <summary>The cleared points' names in a line, for the record.</summary>
    public string ClearedList()
    {
        var text = new System.Text.StringBuilder();
        int count = 0;
        for (int i = 0; i < records.Length; i++)
        {
            if (!records[i].Cleared) continue;
            text.Append(count++ > 0 ? ", " : "").Append(records[i].Name);
        }
        return count > 0 ? count + " of " + records.Length + " (" + text + ")" : "none";
    }
}
