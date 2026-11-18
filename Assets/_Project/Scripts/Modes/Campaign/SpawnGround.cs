using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ground of a chapter as the squad's progress leaves it, on the host: where the route is (SpawnRoute), where
/// each spawn point and each living player is on it, how far the squad's front and rear have come, and which
/// stretches are cleared. The one place that knows any of that; the horde asks it (CampaignHorde) and SpawnPick
/// weighs what it says.
/// <para>Cleared, in the world's terms (user, 2026-11-18: "이미 소탕(클리어)한 구역에서 미션 진행 후 적이 다시 리스폰되지
/// 않도록"). A stretch is a step's way: the route from the spot before it to its own. It is cleared from the moment
/// these three hold together, and stays so: its step is done (the mission has moved on); the squad has been through
/// it and left it, every living player farther along the route than its end by leftBy; and it is empty, no living
/// zombie standing on it. Zombies the squad slipped past keep their stretch uncleared until they die or walk off it,
/// so ground only sneaked through is not ground cleared. Nothing is set on cleared ground again and, under a rule that
/// keeps cleared ground shut, nothing comes out of it.</para>
/// <para>A wipe puts the world after the last rest stop back as it was (every zombie gone, the calm ones set anew
/// along the way ahead), so the stretches after that rest stop are no longer cleared; those up to it stay as they
/// were, and the ones the wipe emptied become cleared by the same rule as the squad leaves the rest stop.</para>
/// <para>Only the host has it (the horde is the host's); it changes nothing a guest sees but where zombies appear.</para>
/// </summary>
internal sealed class SpawnGround
{
    /// <summary>One spawn point: what it is and where on the route it was found to be.</summary>
    public struct PointRecord
    {
        public string Name;
        public SpawnRole Role;
        /// <summary>Its place on the route is known (Place); it was found on an earlier laying and waits to be found again on this one.</summary>
        public bool Placed, Stale;
        public SpawnRoute.Placement Place;
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
    private bool[] cleared = new bool[0], occupied = new bool[0];
    private int laidAtStep = -1, nextPoint;
    private float nextMoved, nextClear;

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
    public int PointCount => records.Length;
    public PointRecord Record(int index) => records[index];
    /// <summary>Every spawn point has its place on the route as laid now.</summary>
    public bool AllPlaced => Route != null && nextPoint >= records.Length;
    public string Chapter => chapter;

    /// <summary>This stretch (a step's index) is cleared.</summary>
    public bool IsCleared(int leg) => leg >= 0 && leg < cleared.Length && cleared[leg];

    /// <summary>This place lies on cleared ground (by the route's nearest part in a straight line, the same floor first).</summary>
    public bool ClearedAt(Vector3 position) => Route != null && Route.Length > 0f && IsCleared(Route.LegAt(position));

    /// <summary>Where this player is on the route (m); false when they have no place yet.</summary>
    public bool TryFlow(PlayerMovement player, out float flow) => flows.TryGetValue(player, out flow);

    /// <summary>
    /// The host, every frame of a chapter: lays the route when there is none, when the mission has moved on a step (a
    /// gate may have opened with it), and when a step's spot is no longer where the route has it, farther off than
    /// that step's own radius (a spot a gimmick re-placed, one that rides a moving thing). Then a few spawn points find
    /// their place on it, each living player's place is looked at again when due, and what is cleared is looked at.
    /// </summary>
    public void Follow(IReadOnlyList<CampaignStep> steps, int step, IReadOnlyDictionary<string, Transform> spots, List<PlayerMovement> players)
    {
        bool moved = false;
        if (Route != null && step == laidAtStep && Time.time >= nextMoved)
        {
            nextMoved = Time.time + SpawnRules.LookEvery;
            moved = Moved(spots);
        }
        if (Route == null || step != laidAtStep || moved) Lay(steps, step, spots, moved ? "a spot moved" : Route == null ? "the chapter began" : "the mission moved on");
        PlacePoints(SpawnRules.PerFrame);
        Track(players, step);
        Clear(step);
    }

    /// <summary>A wipe sent the run back to the step after this rest stop (-1: the chapter's start).</summary>
    public void Wipe(int checkpointStep)
    {
        for (int i = Mathf.Max(0, checkpointStep + 1); i < cleared.Length; i++) cleared[i] = false;
        // Everyone comes back at the rest stop: their places are found again from there.
        flows.Clear();
        lookAt.Clear();
        Squad = default;
        if (Heard) Say($"WIPE back to after step {checkpointStep + 1}: the stretches after it are no longer cleared ({ClearedList()})");
    }

    private bool Moved(IReadOnlyDictionary<string, Transform> spots)
    {
        if (spots == null) return false;
        foreach (var leg in Route.Legs)
        {
            if (!leg.HasSpot || string.IsNullOrEmpty(leg.Spot) || !spots.TryGetValue(leg.Spot, out var spot) || spot == null) continue;
            float reach = Mathf.Max(.5f, leg.Radius);
            if ((spot.position - leg.At).sqrMagnitude > reach * reach) return true;
        }
        return false;
    }

    private void Lay(IReadOnlyList<CampaignStep> steps, int step, IReadOnlyDictionary<string, Transform> spots, string why)
    {
        var before = Route;
        Route = SpawnRoute.Lay(steps, spots, SpawnRules.Grain);
        laidAtStep = step;
        nextMoved = Time.time + SpawnRules.LookEvery;
        int count = Route.Legs.Count;
        if (cleared.Length != count) { System.Array.Resize(ref cleared, count); occupied = new bool[count]; }
        // The points keep the place they had until each is found again on the new laying (a few a frame).
        for (int i = 0; i < records.Length; i++)
            if (records[i].Placed)
            {
                records[i].Stale = true;
                records[i].Place.Flow = Route.FromOther(before, records[i].Place.Flow);
            }
        nextPoint = 0;
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
                    + (leg.Joined ? "" : "  NOT JOINED by the walk mesh") + (IsCleared(i) ? "  cleared" : "")
                : $"  {leg.Step,-9} {leg.Kind,-10} {leg.Spot,-16} NO SPOT of that name in the scene");
        }
    }

    private void PlacePoints(int most)
    {
        if (Route == null) return;
        for (; most > 0 && nextPoint < records.Length; nextPoint++, most--)
        {
            var point = points[nextPoint];
            if (point == null) continue;
            records[nextPoint].Placed = Route.Place(point.position, -1f, true, out records[nextPoint].Place);
            records[nextPoint].Stale = false;
        }
        if (nextPoint < records.Length || !Heard || reportedFor == Route) return;
        reportedFor = Route;
        SayPoints();
    }

    private SpawnRoute reportedFor;

    // Every point's place, and what is worth a look in it (FINDING lines).
    private void SayPoints()
    {
        Say($"POINTS placed: {records.Length} ({Route.Searches} path searches so far)");
        for (int i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (!record.Placed) { Say($"  {record.Name,-18} NO PLACE on the route" + (points[i] == null ? " (the scene lost the point)" : "")); continue; }
            var leg = Route.Legs[Mathf.Clamp(record.Place.Leg, 0, Route.Legs.Count - 1)];
            Say($"  {record.Name,-18} flow {record.Place.Flow,6:0.0}  stretch {leg.Step,-9} {leg.Kind,-10} {record.Role,-5} {record.Place.Off,5:0.0} m from the route "
                + (record.Place.ByWalk ? "by walk" : record.Place.OnMesh ? "in a STRAIGHT line (the walk mesh joins it to no part of the route)" : "in a STRAIGHT line (OFF the walk mesh)"));
        }
        for (int i = 0; i < Route.Legs.Count; i++)
        {
            var leg = Route.Legs[i];
            if (!leg.HasSpot) { Say($"FINDING {leg.Step}: no spot '{leg.Spot}' in the scene, so the route skips it"); continue; }
            if (!leg.Joined) Say($"FINDING {leg.Step}: the walk mesh does not join it to the spot before it ({leg.Gap:0.0} m straight across): no zombie walks that way");
            int ahead = 0;
            foreach (var record in records) if (record.Placed && record.Place.Flow > leg.End) ahead++;
            if (ahead == 0 && i + 1 < Route.Legs.Count) Say($"FINDING {leg.Step}: no spawn point lies ahead of its spot");
        }
    }

    // Each living player's place on the route, looked at again every lookEvery; the squad's front and rear.
    private void Track(List<PlayerMovement> players, int step)
    {
        Squad = default;
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
                // One player a frame comes due, so their path searches do not fall together.
                lookAt[player] = now + every + i * Time.deltaTime;
                // Someone with no place yet is looked for from where the mission stands.
                float hint = known ? flow : Route.Legs.Count > 0 ? Route.Legs[Mathf.Clamp(step, 0, Route.Legs.Count - 1)].Start : 0f;
                // Off the walk mesh (in the air, on a ladder or a cable) they keep the place they had.
                if (Route.Place(player.transform.position, hint, false, out var place) && (place.OnMesh || !known))
                {
                    flow = place.Flow;
                    flows[player] = flow;
                    known = true;
                }
            }
            if (!known) continue;
            if (!Squad.Known) { Squad.Known = true; Squad.Front = Squad.Rear = flow; }
            else { Squad.Front = Mathf.Max(Squad.Front, flow); Squad.Rear = Mathf.Min(Squad.Rear, flow); }
        }
    }

    // What is cleared: a stretch whose step is done, that the rear has left by leftBy, with no living zombie on it.
    private void Clear(int step)
    {
        if (Route == null || !Squad.Known || Time.time < nextClear) return;
        nextClear = Time.time + SpawnRules.LookEvery;
        float leftBy = SpawnRules.LeftBy;
        int until = Mathf.Min(step, Route.Legs.Count);
        bool any = false;
        for (int i = 0; i < until && !any; i++) any = !cleared[i] && Route.Legs[i].HasGround && Squad.Rear > Route.Legs[i].End + leftBy;
        if (!any) return;
        System.Array.Clear(occupied, 0, occupied.Length);
        var living = EnemyHealth.Living;
        for (int i = 0; i < living.Count; i++)
        {
            var enemy = living[i];
            if (enemy == null || !enemy.IsAlive) continue;
            int leg = Route.LegAt(enemy.transform.position);
            if (leg >= 0 && leg < occupied.Length) occupied[leg] = true;
        }
        for (int i = 0; i < until; i++)
        {
            var leg = Route.Legs[i];
            if (cleared[i] || !leg.HasGround || Squad.Rear <= leg.End + leftBy || occupied[i]) continue;
            cleared[i] = true;
            if (Heard) Say($"CLEARED {leg.Step} (flow {leg.Start:0}-{leg.End:0}): its step is done, the rear is at {Squad.Rear:0} and nothing alive stands on it");
        }
    }

    /// <summary>The cleared stretches' steps in a line, for the record.</summary>
    public string ClearedList()
    {
        if (Route == null) return "none";
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < Route.Legs.Count; i++)
            if (IsCleared(i)) text.Append(text.Length > 0 ? " " : "").Append(Route.Legs[i].Step);
        return text.Length > 0 ? text.ToString() : "none";
    }
}
