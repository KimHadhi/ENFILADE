using System.Collections.Generic;
using UnityEngine;

/// <summary>Prefab blood particles and a fading blood mark where a shot from the parent WeaponController hits an enemy.</summary>
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

    private WeaponController weapon;

    private sealed class ActiveBloodMark
    {
        public GameObject instance;
        public SpriteRenderer sprite;
        public Color originalColor;
        public float elapsed;
        public float lifetime;
    }

    private readonly List<ActiveBloodMark> activeMarks = new();

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += Play;
    }

    private void Play(WeaponShot shot)
    {
        if (!shot.Trace.HasHit || shot.Trace.Hit.collider == null)
            return;

        RaycastHit hit = shot.Trace.Hit;

        if (!IsEnemy(hit.collider.transform))
            return;

        Vector3 normal = hit.normal.sqrMagnitude > 0.001f
            ? hit.normal.normalized
            : -shot.Direction.normalized;

        Vector3 position = hit.point + normal * surfaceOffset;

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
            return;

        ParticleSystem particles = Instantiate(
            bloodParticlePrefab,
            position,
            rotation);

        particles.Play();

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
            return;

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
            Debug.LogWarning($"혈흔 프리팹 '{bloodMarkPrefab.name}'에 SpriteRenderer가 없습니다.", bloodMarkPrefab);
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

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= Play;
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
