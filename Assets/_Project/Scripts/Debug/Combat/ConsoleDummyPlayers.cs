using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stand-in players for trying the multiplayer structure without a network. They are made from the saved player prefab,
/// the way a friend's character will be, and stand still: enemies chase them, hit them and kill them like anyone else.
/// </summary>
public static class ConsoleDummyPlayers
{
    private static readonly List<GameObject> dummies = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => dummies.Clear();

    public static string Spawn(PlayerMovement player, int count)
    {
        var prefab = Resources.Load<GameObject>(PlayerSpawnPoint.PrefabPath);
        if (prefab == null) return "플레이어 프리팹이 없습니다. Resources/Characters/Player.prefab을 확인하세요.";
        for (int spawned = 0; spawned < count; spawned++)
        {
            // Newly created colliders must occupy their positions before choosing the next spot.
            Physics.SyncTransforms();
            if (!TryFindSpot(player, out Vector3 spot))
                return spawned == 0 ? "앞쪽에 더미를 놓을 빈 공간이 없습니다." : $"더미 {spawned}/{count}명을 소환했습니다. 앞쪽 공간이 부족합니다.";
            SpawnAt(player, prefab, spot);
        }
        return $"더미 플레이어 {count}명을 소환했습니다. 제거: /dummy clear";
    }

    public static string Clear()
    {
        int removed = 0;
        foreach (var dummy in dummies)
            if (dummy != null) { Object.Destroy(dummy); removed++; }
        dummies.Clear();
        return removed > 0 ? $"더미 {removed}명을 제거했습니다." : "제거할 더미가 없습니다.";
    }

    private static void SpawnAt(PlayerMovement player, GameObject prefab, Vector3 spot)
    {
        // The copy must learn it is remote before it activates: it may not add screen effects or claim the mouse.
        var holder = new GameObject("Dummy Holder");
        holder.SetActive(false);
        Vector3 facing = Vector3.ProjectOnPlane(player.transform.position - spot, Vector3.up);
        var dummy = Object.Instantiate(prefab, spot, Quaternion.LookRotation(facing.normalized), holder.transform);
        dummy.name = "Dummy Player " + (dummies.Count + 1);
        dummy.GetComponent<PlayerMovement>().MakeRemote();
        dummy.transform.SetParent(RuntimeSceneHierarchy.GetGroup(player.gameObject.scene, "PLAYERS", "Dummies"), true);
        Object.Destroy(holder);
        dummies.Add(dummy);
    }

    private static bool TryFindSpot(PlayerMovement player, out Vector3 spot)
    {
        var capsule = player.GetComponent<CharacterController>();
        float reach = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
        Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        foreach (float distance in new[] { 3f, 4f, 5f, 7f })
        foreach (float angle in new[] { 0f, -30f, 30f, -60f, 60f })
        {
            spot = player.transform.position + Quaternion.AngleAxis(angle, Vector3.up) * forward * distance;
            Vector3 centre = spot + capsule.center + Vector3.up * .1f;
            if (!Physics.CheckCapsule(centre + Vector3.down * reach, centre + Vector3.up * reach, capsule.radius, ~0, QueryTriggerInteraction.Ignore))
                return true;
        }
        spot = default;
        return false;
    }
}
