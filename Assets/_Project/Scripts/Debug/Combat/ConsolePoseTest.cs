using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// /poses (PlayerConsoleCommands), for the frozen-pose bug (user, 2026-11-12: "calm한 좀비한테 들켰을 때 좀비가 여전히
/// 원래 포즈 그대로 와. 누워서 오거나 앉은 채로 부동 자세로 나한테 와"): one calm zombie in each calm pose
/// (ZombieLooks.calmPoses: sitting, eating, crouched, lying), in an arc before the player, woken one way once their poses
/// have settled. Each must get up where it is and only then come (ZombieBody.Moods, ZombieMind.GettingUp).
/// - Sight: they face the player, near enough for a dozer's dull eyes; the player stands in view and they notice.
/// - Noise: two loud sounds at the player's feet, NoiseGap apart (the first may only alert a dozer; the second wakes it).
/// - Shot: a 1-damage bullet from the player. The first one shot calls the rest awake (the call path), so "shot N" shoots
///   only that one to see the shot path on its pose.
/// - Stone: a stone lands beyond them; they turn to it and the nearest walks over to search (the search walk).
/// - Stay: nothing; the player sneaks or gets seen on their own.
/// The poses are forced on the host's PC only (ZombieBody.ForcedPose): other PCs show their own seeded pick.
/// </summary>
public sealed class ConsolePoseTest : MonoBehaviour
{
    public enum Wake { Stay, Sight, Noise, Shot, Stone }

    // How long after spawning the wake comes: each takes its pose on its first calm frame and an eater's meal is laid (s).
    private const float Settle = 2.5f;
    // The arc round the player: its radius facing them (a dozer sees 4.2 m: Tables/ZombieSenses dozeRangeShare), its
    // radius with their backs turned, the angle between two (degrees), and how much further out a taken spot tries (m).
    private const float NearRadius = 3.4f, FarRadius = 5.5f, Step = 30f, Further = 1.5f;
    // The noises: how far they carry (m) and how far apart they come (s); the stone's, and how far beyond the arc it lands.
    private const float NoiseRadius = 45f, NoiseGap = 1.2f, StoneRadius = 20f, StoneBeyond = 3f;

    private readonly List<EnemyHealth> made = new List<EnemyHealth>();
    private PlayerMovement player;
    private Wake wake;
    private int only;
    private Vector3 stone;

    /// <summary>Spawns the arc and starts the wake. Only: with Shot, the index of the one to shoot (-1: all).</summary>
    public static string Spawn(PlayerMovement player, Wake wake, int only = -1, ZombieKind? kind = null)
    {
        if (player == null) return "대상 플레이어를 찾지 못했습니다."; // dev-text
        if (GameSession.IsGuest) return "적은 호스트만 소환할 수 있습니다."; // dev-text
        var looks = ZombieLooks.Instance;
        if (looks == null || looks.calmPoses == null) return "ZombieLooks에 조용한 자세(calmPoses)가 없습니다."; // dev-text
        var poses = new List<int>();
        for (int i = 0; i < looks.calmPoses.Length; i++) if (looks.calmPoses[i].loop != null) poses.Add(i);
        if (poses.Count == 0) return "ZombieLooks에 조용한 자세(calmPoses)가 없습니다."; // dev-text
        var prefab = Prefab(player, out string error);
        if (prefab == null) return error;
        var agent = prefab.GetComponent<NavMeshAgent>();
        if (agent == null) return "적 프리팹에 NavMeshAgent가 필요합니다."; // dev-text
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.SamplePosition(player.transform.position, out var start, 2f, filter))
            return "플레이어 발밑에 이동 공간이 없습니다."; // dev-text

        var camera = player.GetComponentInChildren<Camera>();
        Vector3 forward = Vector3.ProjectOnPlane(camera != null ? camera.transform.forward : player.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .01f) forward = player.transform.forward;
        forward.Normalize();
        bool facing = wake == Wake.Sight;
        float radius = facing ? NearRadius : FarRadius;
        var run = new GameObject("Pose Test (Console)").AddComponent<ConsolePoseTest>();
        run.transform.SetParent(RuntimeSceneHierarchy.GetGroup(player.gameObject.scene, "ENEMIES", "Console"), false);
        run.player = player;
        run.wake = wake;
        run.only = only;
        run.stone = start.position + forward * (radius + StoneBeyond);

        var said = new StringBuilder();
        int skipped = 0;
        for (int n = 0; n < poses.Count; n++)
        {
            float angle = (n - (poses.Count - 1) * .5f) * Step;
            Vector3 way = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            if (!Place(prefab, agent, filter, start.position, way, radius, out Vector3 at)) { skipped++; continue; }
            // Turned to the player to see them, else its back to them; FaceWhenCalm makes that the way it looks round from.
            Vector3 look = facing ? -way : way;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            var enemy = Instantiate(prefab, at + Vector3.up * agent.baseOffset, Quaternion.Euler(0f, yaw, 0f),
                RuntimeSceneHierarchy.GetGroup(player.gameObject.scene, "ENEMIES", "Console"));
            enemy.name = prefab.name + " (Pose " + poses[n] + ")";
            // Before it dresses (its Start, next frame): the pose it takes, as the campaign's calm ones are calmed then.
            if (enemy.TryGetComponent(out ZombieBody body)) body.ForcedPose = poses[n];
            if (kind.HasValue && enemy.TryGetComponent(out ZombieType type)) type.Set(kind.Value);
            if (enemy.TryGetComponent(out ZombieMind mind)) { mind.Calm(); mind.FaceWhenCalm(yaw); }
            EnemyNetwork.SpawnInRoom(enemy);
            run.made.Add(enemy);
            var chosen = looks.calmPoses[poses[n]];
            said.Append(said.Length > 0 ? " · " : "").Append(run.made.Count - 1).Append(' ').Append(KindName(chosen.kind))
                .Append('(').Append(chosen.loop.name).Append(chosen.getUp == null ? ", 일어서기 없음" : "").Append(')'); // dev-text
        }
        if (run.made.Count == 0)
        {
            Destroy(run.gameObject);
            return "앞쪽에 자세 좀비를 둘 빈 이동 공간이 없습니다. 트인 곳에서 다시 해 보세요."; // dev-text
        }
        run.StartCoroutine(run.Waking());
        string how = wake switch
        {
            Wake.Sight => "보고 있다가 눈으로 알아챕니다. 가만히 보이는 곳에 서 계세요", // dev-text
            Wake.Noise => $"{Settle:0.#}초 뒤 큰 소리 두 번으로 깨웁니다", // dev-text
            Wake.Shot => only >= 0 ? $"{Settle:0.#}초 뒤 {only}번만 1 피해 총알로 깨웁니다(나머지는 그 부름에 깸)" // dev-text
                : $"{Settle:0.#}초 뒤 모두 1 피해 총알로 깨웁니다(처음 하나가 나머지를 부름)", // dev-text
            Wake.Stone => $"{Settle:0.#}초 뒤 뒤쪽에 돌을 떨어뜨려 가장 가까운 하나가 수색하러 걸어갑니다", // dev-text
            _ => "깨우지 않습니다. 몰래 다가가거나 일부러 들켜 보세요" // dev-text
        };
        return $"자세 좀비 {run.made.Count}마리: {said}. {how}. 모두 제자리에서 일어선 다음에 움직여야 합니다." // dev-text
            + (skipped > 0 ? $" (자리가 없어 {skipped}마리는 못 둠)" : "") + " (자세는 호스트 화면만 정해짐)"; // dev-text
    }

    // The scene's horde zombie, as /spawn takes it (every WaveManager here must agree).
    private static EnemyHealth Prefab(PlayerMovement player, out string error)
    {
        error = "소환할 적이 없습니다. 이 씬의 WaveManager에 Enemy Prefab을 연결하세요."; // dev-text
        EnemyHealth prefab = null;
        foreach (var wave in FindObjectsByType<WaveManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (wave.gameObject.scene != player.gameObject.scene || wave.EnemyPrefab == null) continue;
            if (prefab != null && prefab != wave.EnemyPrefab)
            {
                error = "적 종류가 여러 개입니다. 이 씬의 WaveManager에 같은 Enemy Prefab을 지정하세요."; // dev-text
                return null;
            }
            prefab = wave.EnemyPrefab;
        }
        return prefab;
    }

    // A free spot on the walk mesh that way from the player, at the radius or a little further, in a straight walk.
    private static bool Place(EnemyHealth prefab, NavMeshAgent agent, NavMeshQueryFilter filter, Vector3 start, Vector3 way,
        float radius, out Vector3 at)
    {
        at = start;
        float body = Mathf.Max(.2f, agent.radius * Mathf.Max(Mathf.Abs(prefab.transform.lossyScale.x), Mathf.Abs(prefab.transform.lossyScale.z)));
        float height = Mathf.Max(body * 2f, agent.height * Mathf.Abs(prefab.transform.lossyScale.y));
        for (int tries = 0; tries < 3; tries++)
        {
            Vector3 wanted = start + way * (radius + tries * Further);
            if (!NavMesh.SamplePosition(wanted, out var hit, .8f, filter) || Mathf.Abs(hit.position.y - start.y) > 1.5f
                || NavMesh.Raycast(start, hit.position, out _, filter)) continue;
            Vector3 bottom = hit.position + Vector3.up * (body + .06f);
            Vector3 top = hit.position + Vector3.up * Mathf.Max(body + .06f, height - body);
            if (Physics.CheckCapsule(bottom, top, body, ~0, QueryTriggerInteraction.Ignore)) continue;
            at = hit.position;
            return true;
        }
        return false;
    }

    private static string KindName(CalmPoseKind kind) => kind == CalmPoseKind.Eat ? "먹기" : kind == CalmPoseKind.Doze ? "졸기" : "쉬기"; // dev-text

    private IEnumerator Waking()
    {
        yield return new WaitForSeconds(Settle);
        if (player != null && !player.IsDead)
        {
            Vector3 feet = player.transform.position;
            switch (wake)
            {
                case Wake.Noise:
                    Noise.Make(feet, NoiseRadius, player.gameObject);
                    yield return new WaitForSeconds(NoiseGap);
                    if (player != null) Noise.Make(player.transform.position, NoiseRadius, player.gameObject);
                    break;
                case Wake.Shot:
                    for (int i = 0; i < made.Count; i++)
                        if (only < 0 || only == i) Shoot(made[i]);
                    break;
                case Wake.Stone:
                    Noise.Make(stone, StoneRadius, player.gameObject, true);
                    break;
            }
        }
        Destroy(gameObject, 1f);
    }

    // A bullet from the player's eyes to its chest, as a gun's would land: the hit wakes it (ZombieMind.Struck) and reels it.
    private void Shoot(EnemyHealth enemy)
    {
        if (enemy == null || !enemy.IsAlive || player == null) return;
        Vector3 from = player.transform.position + Vector3.up * 1.5f;
        Vector3 chest = enemy.transform.position + Vector3.up * 1.2f;
        Vector3 way = (chest - from).normalized;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), 1f, DamageKind.Attack, chest, way, player.gameObject,
            method: DamageMethod.Bullet, origin: from));
    }
}
