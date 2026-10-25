using UnityEngine;
using UnityEngine.AI;

/// <summary>One-off combat testing enemies, independent from wave progress and cleanup.</summary>
public static class ConsoleEnemySpawner
{
    public static string Spawn(PlayerMovement player)
    {
        EnemyHealth prefab = null;
        foreach (var wave in Object.FindObjectsByType<WaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (wave.gameObject.scene != player.gameObject.scene || wave.EnemyPrefab == null) continue;
            if (prefab != null && prefab != wave.EnemyPrefab) return "적 종류가 여러 개입니다. 이 씬의 WaveManager에 같은 Enemy Prefab을 지정하세요.";
            prefab = wave.EnemyPrefab;
        }
        if (prefab == null) return "소환할 적이 없습니다. 이 씬의 WaveManager에 Enemy Prefab을 연결하세요.";
        return TrySpawn(player, prefab, out _, out string error) ? "적 한 마리를 소환했습니다. (웨이브 집계 제외)" : error;
    }

    public static bool TrySpawn(PlayerMovement player, EnemyHealth prefab, out EnemyHealth enemy, out string error)
    {
        enemy = null;
        error = "앞쪽에 적을 소환할 빈 이동 공간이 없습니다. 조금 이동하거나 다른 방향을 바라보고 다시 시도하세요.";
        if (player == null || prefab == null) return false;
        var agent = prefab.GetComponent<NavMeshAgent>();
        if (agent == null) { error = "적 프리팹에 NavMeshAgent가 필요합니다."; return false; }
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.SamplePosition(player.transform.position, out var start, 2f, filter)) return false;
        var camera = player.GetComponentInChildren<Camera>();
        Vector3 eye = camera != null ? camera.transform.position : player.transform.position + Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(camera != null ? camera.transform.forward : player.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = player.transform.forward;
        float radius = Mathf.Max(.2f, agent.radius * Mathf.Max(Mathf.Abs(prefab.transform.lossyScale.x), Mathf.Abs(prefab.transform.lossyScale.z)));
        float height = Mathf.Max(radius * 2f, agent.height * Mathf.Abs(prefab.transform.lossyScale.y));
        foreach (float distance in new[] { 3f, 4f, 5f })
        foreach (float angle in new[] { 0f, -30f, 30f, -60f, 60f })
        {
            Vector3 desired = start.position + Quaternion.AngleAxis(angle, Vector3.up) * forward * distance;
            if (!NavMesh.SamplePosition(desired, out var hit, .6f, filter)
                || Mathf.Abs(hit.position.y - start.position.y) > 1.5f
                || Vector3.Distance(hit.position, start.position) < 2f
                || NavMesh.Raycast(start.position, hit.position, out _, filter)) continue;
            Vector3 bottom = hit.position + Vector3.up * (radius + .06f);
            Vector3 top = hit.position + Vector3.up * Mathf.Max(radius + .06f, height - radius);
            if (Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore)) continue;
            Vector3 sight = hit.position + Vector3.up * (height * .5f) - eye;
            bool blocked = false;
            foreach (var obstruction in Physics.RaycastAll(eye, sight.normalized, sight.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!obstruction.transform.IsChildOf(player.transform)) { blocked = true; break; }
            if (blocked) continue;
            Transform container = null;
            foreach (var root in player.gameObject.scene.GetRootGameObjects())
                if (root.name == "Console Enemies") { container = root.transform; break; }
            if (container == null)
            {
                var root = new GameObject("Console Enemies");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
                container = root.transform;
            }
            Vector3 facing = Vector3.ProjectOnPlane(player.transform.position - hit.position, Vector3.up);
            enemy = Object.Instantiate(prefab, hit.position + Vector3.up * agent.baseOffset,
                Quaternion.LookRotation(facing.normalized), container);
            enemy.name = prefab.name + " (Console)";
            error = null;
            return true;
        }
        return false;
    }
}
