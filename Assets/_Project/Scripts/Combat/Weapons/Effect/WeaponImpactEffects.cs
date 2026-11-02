using UnityEngine;

/// <summary>The surface's impact effect and bullet hole where a shot from the parent WeaponController hits anything but a body.</summary>
[DisallowMultipleComponent]
public sealed class WeaponImpactEffects : MonoBehaviour
{
    [Tooltip("적의 Layer를 지정하세요. 적의 자식 Collider도 부모 Layer를 검사합니다.")]
    [SerializeField] private LayerMask enemyLayers;

    private WeaponController weapon;

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += ShowHitEffects;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= ShowHitEffects;
    }

    // 적이 아닌 표면에 맞았을 때 효과 생성
    private void ShowHitEffects(WeaponShot shot)
    {
        if (!shot.Trace.HasHit || shot.Trace.Hit.collider == null)
            return;

        RaycastHit hit = shot.Trace.Hit;
        Collider hitCollider = hit.collider;

        // 적과 적의 부모는 제외. 시체도 벽이 아니라 몸이다
        if (IsEnemy(hitCollider.transform) || hitCollider.GetComponentInParent<RagdollCorpse>() != null)
            return;

        ImpactPool.PlaySurface(hitCollider, hit.point, hit.normal);
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
}
