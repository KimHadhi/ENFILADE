using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// The campaign's enemies: how many may be about and how many come a second, as the fight's phase paces them
/// (CampaignPhases), and where each comes from. The host makes them; in a room they reach the others as networked
/// enemies, as the waves' do.
/// Where they come from is the mission's flow and what the squad has cleared (user, 2026-11-18: "적 스폰 위치 및 조건
/// 전면 개편"). SpawnGround keeps the route, everyone's place on it and the cleared stretches; this measures each of
/// the scene's spawn points against the squad as it stands (Measure), and SpawnPick, a pure function, chooses by the
/// step's rule (Tables/SpawnRules) and names why each other point was refused. To be measured as fit a point must have
/// walk mesh, room for a body and no searchlight over it, be hidden from every player's eyes as a whole body is (its
/// height and its width, not one ray to its head) and out of their near view, and its zombie must be able to walk to
/// the player it heads for: a rooftop cut off from the street sends nothing at players on the ground.
/// A quiet stretch (CampaignRun, stage C-15) has no horde: instead zombies stand calm along the way ahead, set by
/// PlaceCalm, and have to notice the players. They, a cage's crowd and a round's walkers are set by their own rules,
/// but never on cleared ground (Placeable).
/// </summary>
internal sealed class CampaignHorde
{
    // Spawns saved up while none could come (nobody in reach), so a step's first moments don't burst.
    private const float MostSaved = 3f, PointSearch = 2f, PlayerSearch = 3f, EyeHeight = 1.6f;
    // A zombie's body as a capsule standing on a point: its lower and upper centres over the feet and its radius (m). It
    // must fit there (Roomy) and be hidden as a whole (AnyoneSees).
    private const float BodyLow = .35f, BodyHigh = 1.55f, BodyRadius = .28f;
    // A pick that found no place is put in the record no more often than this (s).
    private const float NoneEvery = 2f;
    // Calm zombies: one about this far along the walk (m), up to this far off it to one side, a ring of places this far
    // round where the walk ends, and no two nearer each other than this.
    private const float CalmEvery = 9f, CalmAside = 3f, CalmRound = 6f, CalmApart = 2.5f;
    // A share of them stand two or three round one spot, this near one another.
    private const float HuddleShare = .45f, HuddleApart = 1.4f;
    // What blocks a player's sight: everything but players (layer 31) and enemies. Found on first use.
    private static int sightMask;
    private static int SightMask => sightMask != 0 ? sightMask : sightMask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");

    private readonly EnemyHealth prefab;
    private readonly Transform[] spawns;
    private readonly Scene scene;
    private readonly HashSet<EnemyHealth> alive = new HashSet<EnemyHealth>(), brutes = new HashSet<EnemyHealth>();
    private readonly List<Vector3> calmPlaces = new List<Vector3>(), calmTaken = new List<Vector3>();
    // The way the walk goes at each of calmPlaces (degrees about up).
    private readonly List<float> calmWays = new List<float>();
    private readonly NavMeshPath path = new NavMeshPath();
    private readonly Vector3[] corners = new Vector3[256];
    // The ground as the squad's progress leaves it, and the rule the step under way sends by.
    private readonly SpawnGround spawnGround;
    private SpawnRule rule;
    // What was measured of each spawn point for this frame's picks (Measure), where a body would stand at each, and what
    // the last pick made of each.
    private readonly List<SpawnPick.Point> facts = new List<SpawnPick.Point>();
    private readonly List<Vector3> stands = new List<Vector3>();
    private readonly SpawnPick.Verdict[] verdicts;
    private float saved, nextNone;

    public CampaignHorde(EnemyHealth prefab, Transform[] spawns, Scene scene)
    {
        this.prefab = prefab;
        this.spawns = spawns ?? new Transform[0];
        this.scene = scene;
        spawnGround = new SpawnGround(scene.name, this.spawns);
        verdicts = new SpawnPick.Verdict[this.spawns.Length];
    }

    public int Alive => alive.Count;

    /// <summary>The ground as the squad's progress leaves it (the route, the flow, what is cleared), for the record.</summary>
    internal SpawnGround Ground => spawnGround;

    /// <summary>The rule the step under way sends by (Tables/SpawnRules); null before the first Follow.</summary>
    internal SpawnRule Rule => rule;

    /// <summary>
    /// The host, every frame the chapter runs and before Tick: where the mission stands. The ground follows it (the
    /// route laid, everyone's place on it, what is cleared) and the step's rule is read.
    /// </summary>
    public void Follow(IReadOnlyList<CampaignStep> steps, int step, CampaignStep now, IReadOnlyDictionary<string, Transform> spots,
        List<PlayerMovement> players)
    {
        spawnGround.Follow(steps, step, spots, players);
        rule = SpawnRules.For(spawnGround.Chapter, now);
    }

    /// <summary>A wipe sent the run back to the step after this rest stop (-1: the chapter's start): the stretches after
    /// it are no longer cleared (SpawnGround.Wipe).</summary>
    public void Wipe(int checkpointStep) => spawnGround.Wipe(checkpointStep);

    /// <summary>
    /// Makes what may come this frame; returns how many it made. <paramref name="special"/>: the share of the shamblers
    /// among them made other kinds instead (an assault, CampaignPhases). The points are measured once for the frame's
    /// picks, and a point that sent one sends no other in the same frame.
    /// </summary>
    public int Tick(float dt, int most, float rate, List<PlayerMovement> players, float special = 0f)
    {
        alive.RemoveWhere(enemy => enemy == null);
        if (prefab == null || rule == null || most <= 0 || rate <= 0f || players.Count == 0) { saved = 0f; return 0; }
        saved = Mathf.Min(MostSaved, saved + rate * dt);
        if (saved < 1f || alive.Count >= most) return 0;
        Measure(players);
        int made = 0;
        while (saved >= 1f && alive.Count < most)
        {
            saved -= 1f;
            var enemy = SpawnOne(special > 0f ? Harder(special) : null, "horde");
            if (enemy == null) { saved = 0f; break; }
            alive.Add(enemy);
            made++;
        }
        return made;
    }

    // An assault's harder horde: this share of the shamblers come as another kind (ZombieType.MakeSpecial).
    private static System.Action<EnemyHealth> Harder(float special) => enemy =>
    {
        if (Random.value < special && enemy.TryGetComponent(out ZombieType type) && type.Kind == ZombieKind.Shambler) type.MakeSpecial();
    };

    /// <summary>
    /// Friend feedback C2: brutes (ZombieKind.Brute), a song's highlight's boss, from the same places by the same rule as
    /// the horde but outside the step's numbers. Elite, so their kill burns in the feed. Returns how many came (fewer
    /// when no place would do).
    /// </summary>
    public int SpawnBrutes(int count, List<PlayerMovement> players)
    {
        brutes.RemoveWhere(enemy => enemy == null);
        if (prefab == null || rule == null || players.Count == 0 || count <= 0) return 0;
        Measure(players);
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            var brute = SpawnOne(enemy =>
            {
                enemy.SetRank(EnemyRank.Elite);
                if (enemy.TryGetComponent(out ZombieType type)) type.Set(ZombieKind.Brute);
            }, "brute");
            if (brute == null) break;
            brutes.Add(brute);
            made++;
        }
        return made;
    }

    /// <summary>
    /// Stage C-15: zombies standing calm on the way from one place to another, who have to notice the players
    /// (ZombieMind.Calm). Places are taken along the walk between the two, as far as the walk mesh goes (a gap the
    /// players jump leaves it short), and round where it ends; none nearer than <paramref name="near"/> to a player. They
    /// count with the horde's own. Called while the players can't see the way ahead (a chapter's opening card, a rest
    /// stop's shut door). Returns how many were set. The 2026-11-11 stealth round: some stand two or three round one spot
    /// facing in, as if over something, which leaves gaps elsewhere; a lone one looks along the way (its back to whoever
    /// comes), across it, or now and then back down it, and every one looks round from there (ZombieMind). They each
    /// faced a random way, so a scattered few watched every way past (user: "얘들이 각자 흩어져 있으니까 사방을 보고 있고").
    /// </summary>
    public int PlaceCalm(int count, Vector3 from, Vector3 to, List<PlayerMovement> players, float near)
    {
        if (prefab == null || count <= 0) return 0;
        calmPlaces.Clear();
        calmWays.Clear();
        Vector3 travel = to - from;
        float wholeWay = new Vector2(travel.x, travel.z).sqrMagnitude > .01f ? Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);
        if (NavMesh.SamplePosition(from, out NavMeshHit start, PlayerSearch, NavMesh.AllAreas)
            && NavMesh.SamplePosition(to, out NavMeshHit end, PlayerSearch, NavMesh.AllAreas)
            && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path))
        {
            var corners = path.corners;
            float along = CalmEvery * .5f;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 leg = corners[i] - corners[i - 1];
                float length = leg.magnitude;
                float way = new Vector2(leg.x, leg.z).sqrMagnitude > .01f ? Mathf.Atan2(leg.x, leg.z) * Mathf.Rad2Deg : wholeWay;
                for (; along < length; along += CalmEvery)
                {
                    calmPlaces.Add(Vector3.Lerp(corners[i - 1], corners[i], along / length));
                    calmWays.Add(way);
                }
                along -= length;
            }
        }
        for (int i = 0; i < 6; i++)
        {
            float turn = (i + Random.value) / 6f * Mathf.PI * 2f;
            calmPlaces.Add(to + new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn)) * (CalmRound * Random.Range(.6f, 1f)));
            calmWays.Add(wholeWay);
        }
        int made = 0;
        var container = RuntimeSceneHierarchy.GetGroup(scene, RuntimeSceneHierarchy.Enemies, "Campaign");
        while (made < count && calmPlaces.Count > 0)
        {
            int pick = Random.Range(0, calmPlaces.Count);
            Vector2 aside = Random.insideUnitCircle * CalmAside;
            Vector3 wanted = calmPlaces[pick] + new Vector3(aside.x, 0f, aside.y);
            float way = calmWays[pick];
            calmPlaces.RemoveAt(pick);
            calmWays.RemoveAt(pick);
            if (!Placeable(wanted, players, near, CalmApart, out Vector3 ground)) continue;
            int group = count - made >= 2 && Random.value < HuddleShare ? Random.Range(2, 4) : 1;
            if (group == 1)
            {
                if (CalmOne(ground, LoneWay(way), container)) made++;
                continue;
            }
            // Round the spot, facing in.
            float first = Random.Range(0f, 360f);
            for (int k = 0; k < group && made < count; k++)
            {
                float round = (first + 360f * k / group + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
                Vector3 spot = ground + new Vector3(Mathf.Sin(round), 0f, Mathf.Cos(round)) * Random.Range(1.1f, 1.6f);
                if (!Placeable(spot, players, near, HuddleApart, out Vector3 at)) continue;
                Vector3 inward = ground - at;
                float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg + Random.Range(-25f, 25f);
                if (CalmOne(at, yaw, container)) made++;
            }
        }
        return made;
    }

    // A lone one looks along the way (its back to whoever comes that way), across it, or now and then back down it.
    private static float LoneWay(float way)
    {
        float roll = Random.value;
        if (roll < .4f) return way + Random.Range(-50f, 50f);
        if (roll < .8f) return way + (Random.value < .5f ? -90f : 90f) + Random.Range(-30f, 30f);
        return way + 180f + Random.Range(-40f, 40f);
    }

    // On the walk mesh near there, far enough from the players and the calm ones set before it, with room to stand.
    private bool Placeable(Vector3 wanted, List<PlayerMovement> players, float near, float apart, out Vector3 ground)
    {
        ground = wanted;
        if (!NavMesh.SamplePosition(wanted, out NavMeshHit hit, PointSearch, NavMesh.AllAreas)) return false;
        ground = hit.position;
        // A Support's searchlight keeps its ground safe: nothing comes up within its reach (Barricade.Shelters). And
        // nothing is set on ground the squad has cleared: the calm ones, a cage's crowd and a round's walkers go by
        // their own rules, but cleared stays cleared for them too (SpawnGround).
        return Free(ground, players, near, apart) && Roomy(ground) && !Barricade.Shelters(ground) && !spawnGround.ClearedAt(ground);
    }

    /// <summary>
    /// A cage's crowd (the map gimmick card "철창에 갇힌 좀비 창고"): <paramref name="count"/> zombies stood behind its bars
    /// round <paramref name="middle"/>, within <paramref name="radius"/>, each its own way, none nearer than
    /// <paramref name="near"/> to a player. Shut in (ZombieMind.Cage) they rage at their bars on their feet, in no calm
    /// pose, and raise no alarm; they count with the calm ones. Returns how many were set.
    /// The same places once laid a map's crowds face down on the floor as well (asleep until 2026-11-15, lying after):
    /// those are gone from the game (user, 2026-11-18: "제거 지시된 자는 좀비 오브젝트 완전 삭제", and asked how far,
    /// "엎드린 무리까지"), so the only crowd a map sets in one place is a cage's.
    /// </summary>
    public int PlaceCaged(Vector3 middle, int count, float radius, List<PlayerMovement> players, float near)
    {
        if (prefab == null) return 0;
        var container = RuntimeSceneHierarchy.GetGroup(scene, RuntimeSceneHierarchy.Enemies, "Campaign");
        int made = 0;
        for (int tries = 0; made < count && tries < count * 8; tries++)
        {
            Vector2 aside = Random.insideUnitCircle * radius;
            if (!Placeable(middle + new Vector3(aside.x, 0f, aside.y), players, near, CagedApart, out Vector3 ground)) continue;
            // On the cage's own floor: the walk mesh of the storey above or below is not this one.
            if (Mathf.Abs(ground.y - middle.y) > 1.2f) continue;
            var enemy = Object.Instantiate(prefab, ground, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), container);
            if (enemy.TryGetComponent(out ZombieMind mind)) mind.Cage();
            if (enemy.TryGetComponent(out ZombieBody body)) body.ForcedPose = ZombieBody.NoPose;
            EnemyNetwork.SpawnInRoom(enemy);
            enemy.Died += OnDied;
            alive.Add(enemy);
            calmTaken.Add(ground);
            made++;
        }
        return made;
    }

    // Two of a cage's crowd stand no nearer each other than this (m): bodies side by side, not inside each other.
    private const float CagedApart = 1.1f;

    /// <summary>
    /// A map's round (PatrolRoute): its count of walkers, each set calm at a stop of its own spread round the stops (none
    /// nearer than <paramref name="near"/> to a player) and walking on from there (ZombieMind.Patrol). They count with the
    /// calm ones. Returns how many were set.
    /// </summary>
    public int PlaceWalkers(PatrolRoute route, List<PlayerMovement> players, float near)
    {
        if (prefab == null || route == null) return 0;
        var stops = route.Stops;
        if (stops.Length < 2) return 0;
        var container = RuntimeSceneHierarchy.GetGroup(scene, RuntimeSceneHierarchy.Enemies, "Campaign");
        int made = 0;
        for (int i = 0; i < route.Count; i++)
        {
            int start = (i * stops.Length / Mathf.Max(1, route.Count) + Random.Range(0, stops.Length)) % stops.Length;
            if (!Placeable(stops[start], players, near, CalmApart, out Vector3 ground)) continue;
            Vector3 ahead = stops[(start + 1) % stops.Length] - ground;
            float yaw = new Vector2(ahead.x, ahead.z).sqrMagnitude > .01f ? Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg : 0f;
            var enemy = Object.Instantiate(prefab, ground, Quaternion.Euler(0f, yaw, 0f), container);
            if (enemy.TryGetComponent(out ZombieMind mind)) mind.Patrol(stops, start);
            // Walking, it never sits down in a pose: the round would leave it sliding along seated.
            if (enemy.TryGetComponent(out ZombieBody body)) body.ForcedPose = ZombieBody.NoPose;
            EnemyNetwork.SpawnInRoom(enemy);
            enemy.Died += OnDied;
            alive.Add(enemy);
            calmTaken.Add(ground);
            made++;
        }
        return made;
    }

    private bool CalmOne(Vector3 at, float yaw, Transform container)
    {
        var enemy = Object.Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), container);
        if (enemy.TryGetComponent(out ZombieMind mind)) { mind.Calm(); mind.FaceWhenCalm(yaw); }
        EnemyNetwork.SpawnInRoom(enemy);
        enemy.Died += OnDied;
        alive.Add(enemy);
        calmTaken.Add(at);
        return true;
    }

    // Far enough from every player and from every calm zombie set before it.
    private bool Free(Vector3 place, List<PlayerMovement> players, float near, float apart)
    {
        foreach (var player in players) if ((player.transform.position - place).sqrMagnitude < near * near) return false;
        foreach (var taken in calmTaken) if ((taken - place).sqrMagnitude < apart * apart) return false;
        return true;
    }

    /// <summary>Every enemy it made goes at once (a wipe starting over, a chapter done).</summary>
    public void Clear()
    {
        foreach (var enemy in alive) Remove(enemy);
        foreach (var enemy in brutes) Remove(enemy);
        alive.Clear();
        brutes.Clear();
        calmTaken.Clear();
        saved = 0f;
    }

    private void Remove(EnemyHealth enemy)
    {
        if (enemy == null) return;
        enemy.Died -= OnDied;
        enemy.gameObject.SetActive(false);
        Object.Destroy(enemy.gameObject);
    }

    // A body fits there: nothing of the world inside a zombie standing on the spot (user, 2026-11-11 playtest: a runner
    // came out of a wall during the alley hold).
    private static bool Roomy(Vector3 feet)
        => !Physics.CheckCapsule(feet + Vector3.up * BodyLow, feet + Vector3.up * BodyHigh, BodyRadius, SightMask, QueryTriggerInteraction.Ignore);

    /// <summary>
    /// What is so of every spawn point this frame, for SpawnPick to weigh: the ground under it, its place on the route
    /// and whether that is cleared (SpawnGround), how near the nearest player is and whether it lies in anyone's near
    /// view. Only a point those leave standing (SpawnPick.Early) is looked at from every player's eyes and has its walk
    /// to the player it would head for searched: the rays and the path search are what measuring costs.
    /// </summary>
    private void Measure(List<PlayerMovement> players)
    {
        facts.Clear();
        stands.Clear();
        var squad = spawnGround.Squad;
        for (int i = 0; i < spawns.Length; i++)
        {
            var fact = new SpawnPick.Point { Near = float.PositiveInfinity, Walk = -1f };
            Vector3 feet = default;
            var point = spawns[i];
            if (point != null && NavMesh.SamplePosition(point.position, out NavMeshHit ground, PointSearch, NavMesh.AllAreas))
            {
                feet = ground.position;
                fact.Ground = Roomy(feet) && !Barricade.Shelters(feet);
            }
            var record = spawnGround.Record(i);
            fact.Placed = record.Placed;
            fact.Flow = record.Place.Flow;
            fact.Room = record.Role == SpawnRole.Room;
            fact.OnCleared = record.Placed && spawnGround.IsCleared(record.Place.Leg);
            if (fact.Ground)
            {
                var target = Nearest(players, feet, out fact.Near);
                fact.InView = InView(feet, players, rule.ViewNear, rule.ViewAngle * .5f);
                if (target != null && !SpawnPick.Early(fact, squad, rule, out _))
                {
                    fact.Seen = AnyoneSees(feet, players);
                    fact.Walk = WalkTo(feet, target);
                }
            }
            facts.Add(fact);
            stands.Add(feet);
        }
    }

    // The next one, from the place SpawnPick takes of what Measure found; null when none will do now. Set up runs
    // before the enemy goes out to the room, so its copies are born what it is. The place it took sends no other this
    // frame: two made on one spot stand inside each other.
    private EnemyHealth SpawnOne(System.Action<EnemyHealth> setUp, string what)
    {
        int picked = SpawnPick.Pick(facts, spawnGround.Squad, rule, Random.value, verdicts);
        if (SpawnGround.Heard) SayPick(picked, what);
        if (picked < 0) return null;
        var container = RuntimeSceneHierarchy.GetGroup(scene, RuntimeSceneHierarchy.Enemies, "Campaign");
        var enemy = Object.Instantiate(prefab, stands[picked], Quaternion.identity, container);
        setUp?.Invoke(enemy);
        EnemyNetwork.SpawnInRoom(enemy);
        enemy.Died += OnDied;
        var taken = facts[picked];
        taken.Ground = false;
        facts[picked] = taken;
        return enemy;
    }

    // A pick for the record: where it came from and what refused the rest; a pick that found nothing only now and then.
    private void SayPick(int picked, string what)
    {
        var squad = spawnGround.Squad;
        string where = squad.Known ? $"front {squad.Front:0} rear {squad.Rear:0}" : "no place on a route";
        if (picked < 0)
        {
            if (Time.time < nextNone) return;
            nextNone = Time.time + NoneEvery;
            SpawnGround.Say($"NONE for the {what} · squad {where} · rule {rule.Rows} ({rule.From}, cleared {rule.Cleared}, otherwise {rule.Otherwise}) · {Tally()}");
            return;
        }
        var fact = facts[picked];
        SpawnGround.Say($"SPAWN {what} at {spawnGround.Record(picked).Name} (flow {fact.Flow:0}, {(fact.Room ? "room" : "open")}"
            + $"{(fact.Seen ? ", SEEN" : "")}, {fact.Near:0} m off, {fact.Walk:0} m to walk{(fact.Walk > rule.Farthest ? ", FARTHER than the rule's farthest" : "")})"
            + $" · squad {where} · rule {rule.Rows} · {Tally()}");
    }

    /// <summary>How many points the last pick gave each verdict, in a line.</summary>
    internal string Tally()
    {
        var text = new System.Text.StringBuilder();
        var names = System.Enum.GetValues(typeof(SpawnPick.Verdict));
        foreach (SpawnPick.Verdict verdict in names)
        {
            int count = 0;
            for (int i = 0; i < facts.Count && i < verdicts.Length; i++) if (verdicts[i] == verdict) count++;
            if (count > 0) text.Append(text.Length > 0 ? ", " : "").Append(verdict).Append(' ').Append(count);
        }
        return text.Length > 0 ? text.ToString() : "no points";
    }

    /// <summary>
    /// /spawns: the spawn model as it stands this moment, for the record (SpawnReport): the squad, the rule, every
    /// stretch and what is cleared, and every point with what a pick would make of it now. The points are measured for
    /// real, as a pick measures them, and judged; nothing is made.
    /// </summary>
    internal string Describe(List<PlayerMovement> players, int step, CampaignStep now)
    {
        var text = new System.Text.StringBuilder();
        var route = spawnGround.Route;
        var squad = spawnGround.Squad;
        text.Append(spawnGround.Chapter).Append(" step ").Append(now != null ? now.Id + " " + now.Kind : "none").Append(": squad ")
            .Append(squad.Known ? $"front {squad.Front:0} m, rear {squad.Rear:0} m" : "has no place on a route").Append(" · rule ")
            .Append(rule != null ? $"{rule.Rows} ({rule.From}, cleared {rule.Cleared}, otherwise {rule.Otherwise})" : "none yet")
            .Append(" · cleared: ").Append(spawnGround.ClearedList()).Append('\n');
        if (route == null || rule == null)
        {
            text.Append("the horde has not followed the mission yet: a frame of the chapter must run first\n");
            return text.ToString();
        }
        text.Append($"rule: nearest {rule.Nearest:0.#} m, farthest {rule.Farthest:0.#} m by walking, behind {rule.Behind:0.#} m, seenFrom {rule.SeenFrom:0.#} m, "
            + $"view {rule.ViewAngle:0.#} deg within {rule.ViewNear:0.#} m\n");
        text.Append($"route: {route.Length:0} m, {route.Count} places {route.Grain:0.#} m apart, {route.Searches} path searches so far; points placed: "
            + (spawnGround.AllPlaced ? "all" : "not all yet") + "\n");
        foreach (var player in players)
            text.Append("  ").Append(player.name).Append(spawnGround.TryFlow(player, out float flow) ? $" at flow {flow:0.0}" : " has no place yet").Append('\n');
        for (int i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            text.Append(i == step ? "> " : "  ").Append($"{leg.Step,-9} {leg.Kind,-10} {leg.Spot,-16} ");
            if (!leg.HasSpot) { text.Append("NO SPOT of that name in the scene\n"); continue; }
            text.Append($"flow {leg.Start,6:0.0} -> {leg.End,6:0.0}  walk {leg.Walk,6:0.0}  gap {leg.Gap,5:0.0}").Append(leg.Joined ? "" : "  NOT JOINED by the walk mesh")
                .Append(spawnGround.IsCleared(i) ? "  cleared" : "").Append('\n');
        }
        if (players.Count == 0)
        {
            text.Append("nobody alive: the points are not measured\n");
            return text.ToString();
        }
        Measure(players);
        int picked = SpawnPick.Pick(facts, squad, rule, 0f, verdicts);
        text.Append("a pick now would take: ").Append(picked >= 0 ? spawnGround.Record(picked).Name : "nothing").Append(" · ").Append(Tally()).Append('\n');
        for (int i = 0; i < facts.Count; i++)
        {
            var fact = facts[i];
            var record = spawnGround.Record(i);
            // What a refusal that costs nothing left unmeasured is shown as a dash.
            bool looked = fact.Ground && !SpawnPick.Early(fact, squad, rule, out _);
            string stretch = record.Placed ? route.Legs[Mathf.Clamp(record.Place.Leg, 0, route.Legs.Count - 1)].Step : "?";
            text.Append($"  {record.Name,-18} flow {(record.Placed ? fact.Flow.ToString("0.0") : "?"),6} {stretch,-9} {record.Role,-5} ")
                .Append(fact.OnCleared ? "cleared " : "        ")
                .Append($"near {(float.IsInfinity(fact.Near) ? "-" : fact.Near.ToString("0")),4} walk {(looked ? fact.Walk < 0f ? "none" : fact.Walk.ToString("0") : "-"),4} ")
                .Append(looked ? fact.Seen ? "seen   " : "hidden " : "-      ").Append(fact.InView ? "in view " : "        ").Append("-> ").Append(verdicts[i]).Append('\n');
        }
        return text.ToString();
    }

    // Inside someone's view from near: nearer to them than `near` and within `half` degrees of the way their body faces
    // (flat on the ground, as the flashlight's beam is judged: ZombieMind.Lit). Walls are not asked: near and looked
    // at, a place is not used even behind cover, so nothing steps out from behind a van someone is watching.
    private static bool InView(Vector3 feet, List<PlayerMovement> players, float near, float half)
    {
        if (near <= 0f || half <= 0f) return false;
        foreach (var player in players)
        {
            Vector3 toward = feet - player.transform.position;
            if (toward.sqrMagnitude > near * near) continue;
            Vector3 flat = Vector3.ProjectOnPlane(toward, Vector3.up), facing = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f || facing.sqrMagnitude < 1e-4f || Vector3.Angle(facing, flat) <= half) return true;
        }
        return false;
    }

    // The walk from a place to a player over the mesh (m); negative when the mesh does not join them.
    private float WalkTo(Vector3 feet, PlayerMovement target)
    {
        if (!NavMesh.SamplePosition(target.transform.position, out NavMeshHit them, PlayerSearch, NavMesh.AllAreas)) return -1f;
        if (!NavMesh.CalculatePath(feet, them.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
        int count = path.GetCornersNonAlloc(corners);
        float length = 0f;
        for (int k = 1; k < count; k++) length += Vector3.Distance(corners[k - 1], corners[k]);
        return length;
    }

    private void OnDied(EnemyHealth enemy)
    {
        enemy.Died -= OnDied;
        alive.Remove(enemy);
        brutes.Remove(enemy);
    }

    // Someone has a clear line from their eyes to a body standing there (players' own capsules and enemies aside): to
    // its crown, its middle, its shins or either shoulder, the body's capsule seen from where they stand. It was one ray
    // to the head, so a body whose head a lamp post or a door frame covered counted as hidden while the rest of it
    // stood in plain view.
    private static bool AnyoneSees(Vector3 feet, List<PlayerMovement> players)
    {
        Vector3 crown = feet + Vector3.up * (BodyHigh + BodyRadius), middle = feet + Vector3.up * ((BodyLow + BodyHigh) * .5f),
            shins = feet + Vector3.up * BodyLow, shoulders = feet + Vector3.up * BodyHigh;
        foreach (var player in players)
        {
            Vector3 eye = player.transform.position + Vector3.up * EyeHeight;
            Vector3 across = Vector3.Cross(Vector3.up, middle - eye);
            across = across.sqrMagnitude > 1e-6f ? across.normalized * BodyRadius : Vector3.zero;
            if (Open(eye, crown) || Open(eye, middle) || Open(eye, shins) || Open(eye, shoulders + across) || Open(eye, shoulders - across)) return true;
        }
        return false;
    }

    private static bool Open(Vector3 eye, Vector3 at) => !Physics.Linecast(eye, at, SightMask, QueryTriggerInteraction.Ignore);

    private static PlayerMovement Nearest(List<PlayerMovement> players, Vector3 from, out float distance)
    {
        PlayerMovement nearest = null;
        distance = float.PositiveInfinity;
        foreach (var player in players)
        {
            float d = Vector3.Distance(player.transform.position, from);
            if (d < distance) { distance = d; nearest = player; }
        }
        return nearest;
    }
}
