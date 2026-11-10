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
/// </summary>
internal sealed class CampaignHorde
{
    // Spawns saved up while none could come (nobody in reach), so a step's first moments don't burst.
    private const float MostSaved = 3f, PointSearch = 2f, PlayerSearch = 3f, EyeHeight = 1.6f, HeadHeight = 1.5f;
    // What blocks a player's sight: everything but players (layer 31) and enemies. Found on first use.
    private static int sightMask;
    private static int SightMask => sightMask != 0 ? sightMask : sightMask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");

    private readonly EnemyHealth prefab;
    private readonly Transform[] spawns;
    private readonly Scene scene;
    private readonly HashSet<EnemyHealth> alive = new HashSet<EnemyHealth>(), brutes = new HashSet<EnemyHealth>();
    private readonly List<Vector3> choices = new List<Vector3>(), inView = new List<Vector3>();
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

    /// <summary>Every enemy it made goes at once (a wipe starting over, a chapter done).</summary>
    public void Clear()
    {
        foreach (var enemy in alive) Remove(enemy);
        foreach (var enemy in brutes) Remove(enemy);
        alive.Clear();
        brutes.Clear();
        saved = 0f;
    }

    private void Remove(EnemyHealth enemy)
    {
        if (enemy == null) return;
        enemy.Died -= OnDied;
        enemy.gameObject.SetActive(false);
        Object.Destroy(enemy.gameObject);
    }

    // Set up runs before the enemy goes out to the room, so its copies are born what it is.
    private EnemyHealth SpawnOne(List<PlayerMovement> players, float near, float far, float seenFar, System.Action<EnemyHealth> setUp)
    {
        choices.Clear();
        inView.Clear();
        foreach (var point in spawns)
        {
            if (point == null || !NavMesh.SamplePosition(point.position, out NavMeshHit ground, PointSearch, NavMesh.AllAreas)) continue;
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
