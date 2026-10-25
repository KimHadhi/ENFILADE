
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyBloodEffect : MonoBehaviour
{
    [Header("Enemy Detection")]
    [SerializeField] private LayerMask enemyLayers;

    [Header("Blood Particles")]
    [SerializeField] private ParticleSystem bloodParticlePrefab;
    [SerializeField, Min(0.1f)] private float particleCleanupTime = 3f;

    [Header("Blood Mark")]
    [SerializeField] private GameObject bloodMarkPrefab;
    [SerializeField, Min(0.1f)] private float bloodMarkLifetime = 5f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.03f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private sealed class ActiveBloodMark
    {
        public GameObject instance;
        public SpriteRenderer sprite;
        public Color originalColor;
        public float elapsed;
        public float lifetime;
    }

    private readonly List<ActiveBloodMark> activeMarks = new();

    public void Play(WeaponShot shot)
    {
        if (!shot.Trace.HasHit || shot.Trace.Hit.collider == null)
        {
            Log("[Blood] 명중한 대상이 없습니다.");
            return;
        }

        RaycastHit hit = shot.Trace.Hit;

        Log($"[Blood] 피격 대상: {hit.collider.name}");

        if (!IsEnemy(hit.collider.transform))
        {
            Log("[Blood] 적 Layer 판정 실패");
            return;
        }

        Vector3 normal = hit.normal.sqrMagnitude > 0.001f
            ? hit.normal.normalized
            : -shot.Direction.normalized;

        Vector3 position = hit.point + normal * surfaceOffset;

        // Scene 창에서 피격 위치와 표면 방향 확인
        Debug.DrawRay(hit.point, normal * 0.5f, Color.magenta, 3f);
        Debug.DrawLine(hit.point, position, Color.green, 3f);

        Log($"[Blood] 적 판정 성공 / 위치: {position}");

        // Particle System의 기본 분사 축은 로컬 Y축
        Quaternion particleRotation =
            Quaternion.FromToRotation(Vector3.up, normal);

        // SpriteRenderer의 앞면은 로컬 Z축 기준
        Quaternion markRotation = Quaternion.LookRotation(normal);

        SpawnBloodParticles(position, particleRotation);
        SpawnBloodMark(hit.collider.transform, position, markRotation);
    }

    private bool IsEnemy(Transform target)
    {
        Transform current = target;

        while (current != null)
        {
            int layerBit = 1 << current.gameObject.layer;

            if ((enemyLayers.value & layerBit) != 0)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void SpawnBloodParticles(
        Vector3 position,
        Quaternion rotation)
    {
        if (bloodParticlePrefab == null)
        {
            Log("[Blood] 오류: Blood Particle Prefab이 비어 있습니다.");
            return;
        }

        ParticleSystem particles = Instantiate(
            bloodParticlePrefab,
            position,
            rotation);

        particles.Play();

        Log($"[Blood] 파티클 생성: {particles.name}");

        Destroy(
            particles.gameObject,
            Mathf.Max(0.1f, particleCleanupTime));
    }

    private void SpawnBloodMark(
        Transform hitTransform,
        Vector3 position,
        Quaternion rotation)
    {
        if (bloodMarkPrefab == null)
        {
            Log("[Blood] 오류: Blood Mark Prefab이 비어 있습니다.");
            return;
        }

        if (hitTransform == null)
            return;

        GameObject instance = Instantiate(
            bloodMarkPrefab,
            position,
            rotation);

        // 생성 후 부모를 설정해도 월드 위치와 회전을 유지
        instance.transform.SetParent(hitTransform, true);

        SpriteRenderer sprite =
            instance.GetComponentInChildren<SpriteRenderer>();

        if (sprite == null)
        {
            Log("[Blood] 오류: 혈흔 프리팹에 SpriteRenderer가 없습니다.");
            Destroy(instance);
            return;
        }

        ActiveBloodMark mark = new ActiveBloodMark
        {
            instance = instance,
            sprite = sprite,
            originalColor = sprite.color,
            elapsed = 0f,
            lifetime = Mathf.Max(0.1f, bloodMarkLifetime)
        };

        activeMarks.Add(mark);

        Log($"[Blood] 혈흔 이미지 생성: {instance.name}");
    }

    private void Update()
    {
        UpdateBloodMarks();
    }

    private void UpdateBloodMarks()
    {
        for (int i = activeMarks.Count - 1; i >= 0; i--)
        {
            ActiveBloodMark mark = activeMarks[i];

            if (mark.instance == null || mark.sprite == null)
            {
                activeMarks.RemoveAt(i);
                continue;
            }

            mark.elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(
                mark.elapsed / mark.lifetime);

            Color color = mark.originalColor;
            color.a = mark.originalColor.a * (1f - progress);
            mark.sprite.color = color;

            if (progress >= 1f)
            {
                Destroy(mark.instance);
                activeMarks.RemoveAt(i);
            }
        }
    }

    private void Log(string message)
    {
        if (debugLogs)
            Debug.Log(message, this);
    }

    private void OnDisable()
    {
        ClearBloodMarks();
    }

    private void OnDestroy()
    {
        ClearBloodMarks();
    }

    private void ClearBloodMarks()
    {
        foreach (ActiveBloodMark mark in activeMarks)
        {
            if (mark.instance != null)
                Destroy(mark.instance);
        }

        activeMarks.Clear();
    }
}
