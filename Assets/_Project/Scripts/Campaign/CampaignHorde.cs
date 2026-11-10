using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// The campaign's enemies: how many may be about and how many come a second, as the director paces them
/// (CampaignDirector). They come from the scene's spawn points no nearer than the near distance to anyone, no farther
/// than the far one from someone, and only where they can walk to that player: a rooftop cut off from the street sends
/// nothing at players on the ground. Points no player can see come first (a sight line from every player's eyes to the
/// point is blocked); one in plain view is used only past the seen distance, and only when no hidden one will do. The
/// host makes them; in a room they reach the others as networked enemies, as the waves' do.
/// A quiet stretch (CampaignRun, stage C-15) has no horde: instead zombies stand calm along the way, set by PlaceCalm,
/// and have to notice the players.
/// </summary>
internal sealed class CampaignHorde
{
    // Spawns saved up while none could come (nobody in reach), so a step's first moments don't burst.
    private const float MostSaved = 3f, PointSearch = 2f, PlayerSearch = 3f, EyeHeight = 1.6f, HeadHeight = 1.5f;
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
    private readonly List<Vector3> choices = new List<Vector3>(), inView = new List<Vector3>(), calmPlaces = new List<Vector3>(),
        calmTaken = new List<Vector3>();
    // The way the walk goes at each of calmPlaces (degrees about up).
    private readonly List<float> calmWays = new List<float>();
    private readonly NavMeshPath path = new NavMeshPath();
    private float saved;

    public CampaignHorde(EnemyHealth prefab, Transform[] spawns, Scene scene)
    {
        this.prefab = prefab;
        this.spawns = spawns ?? new Transform[0];
        this.scene = scene;
    }

    public int Alive => alive.Count;

    /// <summary>Makes what may come this frame; returns how many it made.</summary>
    public int Tick(float dt, int most, float rate, List<PlayerMovement> players, float near, float far, float seenFar)
    {
        alive.RemoveWhere(enemy => enemy == null);
        if (prefab == null || most <= 0 || rate <= 0f || players.Count == 0) { saved = 0f; return 0; }
        saved = Mathf.Min(MostSaved, saved + rate * dt);
        int made = 0;
        while (saved >= 1f && alive.Count < most)
        {
            saved -= 1f;
            var enemy = SpawnOne(players, near, far, seenFar, null);
            if (enemy == null) { saved = 0f; break; }
            alive.Add(enemy);
            made++;
        }
        return made;
    }

    /// <summary>
    /// Friend feedback C2: brutes (ZombieKind.Brute), a song's highlight's boss, from the same hidden spawn points but
    /// outside the step's numbers. Elite, so their kill burns in the feed. Returns how many came (fewer when no spot
    /// would do).
    /// </summary>
    public int SpawnBrutes(int count, List<PlayerMovement> players, float near, float far, float seenFar)
    {
        brutes.RemoveWhere(enemy => enemy == null);
        if (prefab == null || players.Count == 0) return 0;
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            var brute = SpawnOne(players, near, far, seenFar, enemy =>
            {
                enemy.SetRank(EnemyRank.Elite);
                if (enemy.TryGetComponent(out ZombieType type)) type.Set(ZombieKind.Brute);
            });
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
        var container = RuntimeSceneHierarchy.GetGroup(scene, "ENEMIES", "Campaign");
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
        return Free(ground, players, near, apart) && Roomy(ground);
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
        => !Physics.CheckCapsule(feet + Vector3.up * .35f, feet + Vector3.up * 1.55f, .28f, SightMask, QueryTriggerInteraction.Ignore);

    // Set up runs before the enemy goes out to the room, so its copies are born what it is.
    private EnemyHealth SpawnOne(List<PlayerMovement> players, float near, float far, float seenFar, System.Action<EnemyHealth> setUp)
    {
        choices.Clear();
        inView.Clear();
        foreach (var point in spawns)
        {
            if (point == null || !NavMesh.SamplePosition(point.position, out NavMeshHit ground, PointSearch, NavMesh.AllAreas)
                || !Roomy(ground.position)) continue;
            var target = Nearest(players, ground.position, out float distance);
            if (target == null || distance < near || distance > far) continue;
            if (!NavMesh.SamplePosition(target.transform.position, out NavMeshHit them, PlayerSearch, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(ground.position, them.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                continue;
            if (!AnyoneSees(ground.position, players)) choices.Add(ground.position);
            else if (distance >= seenFar) inView.Add(ground.position);
        }
        var from = choices.Count > 0 ? choices : inView;
        if (from.Count == 0) return null;
        var container = RuntimeSceneHierarchy.GetGroup(scene, "ENEMIES", "Campaign");
        var enemy = Object.Instantiate(prefab, from[Random.Range(0, from.Count)], Quaternion.identity, container);
        setUp?.Invoke(enemy);
        EnemyNetwork.SpawnInRoom(enemy);
        enemy.Died += OnDied;
        return enemy;
    }

    private void OnDied(EnemyHealth enemy)
    {
        enemy.Died -= OnDied;
        alive.Remove(enemy);
        brutes.Remove(enemy);
    }

    // Someone has a clear line from their eyes to where its head would be (players' own capsules and enemies aside).
    private static bool AnyoneSees(Vector3 feet, List<PlayerMovement> players)
    {
        Vector3 head = feet + Vector3.up * HeadHeight;
        foreach (var player in players)
            if (!Physics.Linecast(player.transform.position + Vector3.up * EyeHeight, head, SightMask, QueryTriggerInteraction.Ignore))
                return true;
        return false;
    }

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
