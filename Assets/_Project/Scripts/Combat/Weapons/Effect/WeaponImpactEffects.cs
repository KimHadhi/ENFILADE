
using System.Collections.Generic;
using UnityEngine;

/// <summary>Sparks, dust and fading impact images where a shot from the parent WeaponController hits a non-enemy surface.</summary>
[DisallowMultipleComponent]
public sealed class WeaponImpactEffects : MonoBehaviour
{
    [Header("Hit Effects")]
    [Tooltip("적의 Layer를 지정하세요. 적의 자식 Collider도 부모 Layer를 검사합니다.")]
    [SerializeField] private LayerMask enemyLayers;

    [Tooltip("적 이외의 물체에 맞았을 때 생성할 스파크 파티클")]
    [SerializeField] private ParticleSystem wallSparksPrefab;

    [Tooltip("적 이외의 물체에 맞았을 때 생성할 먼지 파티클")]
    [SerializeField] private ParticleSystem wallDustPrefab;

    [Header("Hit Effect Images")]
    [Tooltip("충돌 시 생성할 화염 이미지 프리팹")]
    [SerializeField] private GameObject fireImagePrefab;

    [Tooltip("충돌 시 생성할 첫 번째 먼지 이미지 프리팹")]
    [SerializeField] private GameObject dustImagePrefab;

    [Tooltip("랜덤으로 선택할 두 번째 먼지 이미지 프리팹")]
    [SerializeField] private GameObject dustImagePrefab2;

    [Tooltip("화염 이미지가 사라질 때까지 걸리는 시간")]
    [SerializeField, Min(0.01f)] private float fireImageLifetime = 0.25f;

    [Tooltip("먼지 이미지가 사라질 때까지 걸리는 시간")]
    [SerializeField, Min(0.01f)] private float dustImageLifetime = 1.0f;

    [Tooltip("충격 효과가 표면에서 살짝 떨어지는 거리")]
    [SerializeField, Min(0f)] private float hitEffectOffset = 0.015f;

    [Tooltip("스파크 파티클의 방출 시간")]
    [SerializeField, Min(0.01f)] private float sparksLifetime = 0.2f;

    [Tooltip("먼지 파티클의 방출 시간")]
    [SerializeField, Min(0.1f)] private float dustLifetime = 1.2f;

    private sealed class ActiveImpactImage
    {
        public GameObject instance;
        public SpriteRenderer sprite;
        public Color originalColor;
        public float elapsed;
        public float lifetime;
    }

    private readonly List<ActiveImpactImage> activeImpactImages = new();
    private WeaponController weapon;

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += ShowHitEffects;
    }

    private void Update()
    {
        UpdateImpactImages();
    }

    // 적이 아닌 표면에 맞았을 때 효과 생성
    private void ShowHitEffects(WeaponShot shot)
    {
        if (!shot.Trace.HasHit || shot.Trace.Hit.collider == null)
            return;

        RaycastHit hit = shot.Trace.Hit;
        Collider hitCollider = hit.collider;

        // 적과 적의 부모는 제외
        if (IsEnemy(hitCollider.transform))
            return;

        Vector3 position = hit.point + hit.normal * hitEffectOffset;

        Quaternion rotation = hit.normal.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(hit.normal)
            : Quaternion.identity;

        // 두 먼지 이미지 중 하나를 무작위로 선택
        GameObject selectedDustImage = dustImagePrefab;

        if (dustImagePrefab2 != null && Random.value < 0.5f)
        {
            selectedDustImage = dustImagePrefab2;
        }

        SpawnHitImage(
            selectedDustImage,
            position + hit.normal * 0.003f,
            rotation,
            dustImageLifetime,
            1);

        SpawnHitImage(
            fireImagePrefab,
            position + hit.normal * 0.006f,
            rotation,
            fireImageLifetime,
            2);

        // 기존 파티클 효과 유지
        SpawnHitParticle(
            wallSparksPrefab,
            position,
            rotation,
            sparksLifetime,
            0.03f);

        SpawnHitParticle(
            wallDustPrefab,
            position,
            rotation,
            dustLifetime,
            0.15f);
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

    // 이미지 생성 및 페이드아웃
    private void SpawnHitImage(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime,
        int sortingOrder)
    {
        if (prefab == null)
            return;

        GameObject instance = Instantiate(
            prefab,
            position,
            rotation);

        SpriteRenderer sprite =
            instance.GetComponentInChildren<SpriteRenderer>();

        if (sprite == null)
        {
            Debug.LogWarning(
                $"피격 이미지 프리팹 '{prefab.name}'에 SpriteRenderer가 없습니다.",
                prefab);

            Destroy(instance);
            return;
        }

        sprite.sortingOrder = sortingOrder;

        ActiveImpactImage activeImage = new ActiveImpactImage
        {
            instance = instance,
            sprite = sprite,
            originalColor = sprite.color,
            elapsed = 0f,
            lifetime = Mathf.Max(0.01f, lifetime)
        };

        activeImpactImages.Add(activeImage);
    }

    private void UpdateImpactImages()
    {
        for (int i = activeImpactImages.Count - 1; i >= 0; i--)
        {
            ActiveImpactImage image = activeImpactImages[i];

            if (image.instance == null || image.sprite == null)
            {
                activeImpactImages.RemoveAt(i);
                continue;
            }

            image.elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(
                image.elapsed / image.lifetime);

            Color color = image.originalColor;
            color.a = image.originalColor.a * (1f - progress);

            image.sprite.color = color;

            if (progress >= 1f)
            {
                Destroy(image.instance);
                activeImpactImages.RemoveAt(i);
            }
        }
    }

    private void SpawnHitParticle(
        ParticleSystem prefab,
        Vector3 position,
        Quaternion rotation,
        float particleLifetime,
        float emissionDuration)
    {
        if (prefab == null)
            return;

        ParticleSystem effect = Instantiate(
            prefab,
            position,
            rotation);

        var main = effect.main;
        main.stopAction = ParticleSystemStopAction.None;

        // Play On Awake may already be running; duration requires a fully stopped system.
        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        main.loop = false;
        main.duration = emissionDuration;
        main.startLifetime = particleLifetime;

        // 파티클이 시간이 지나면서 투명해지도록 설정
        var colorOverLifetime = effect.colorOverLifetime;
        colorOverLifetime.enabled = true;

        Gradient fadeGradient = new Gradient();

        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        colorOverLifetime.color =
            new ParticleSystem.MinMaxGradient(fadeGradient);

        effect.Play();

        Destroy(
            effect.gameObject,
            emissionDuration + particleLifetime + 0.1f);
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= ShowHitEffects;
        ClearImpactImages();
    }

    private void OnDestroy()
    {
        ClearImpactImages();
    }

    private void ClearImpactImages()
    {
        foreach (ActiveImpactImage image in activeImpactImages)
        {
            if (image.instance != null)
                Destroy(image.instance);
        }

        activeImpactImages.Clear();
    }
}
