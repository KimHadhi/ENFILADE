using UnityEngine;

/// <summary>
/// The surface's impact effect and bullet hole where a shot from the parent WeaponController hits anything but a body.
/// A bullet that went on (AttackTrace.Bullet) also leaves a hole on the far side of each wall it came through, an impact
/// wherever it hit next, and ArcCW's ricochet sound where it glanced off.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponImpactEffects : MonoBehaviour
{
    // Sound bank Bullets, Art/Audio/Bullets/Ricochet; volume and pitch in Sounds.csv (Bullets/Ricochet).
    private const string BulletBank = "Bullets", RicochetKey = "Ricochet";
    // ArcCW plays it at Source's default 75 dB, heard across a street.
    private const float RicochetReach = 30f;

    [Tooltip("적의 Layer를 지정하세요. 적의 자식 Collider도 부모 Layer를 검사합니다.")]
    [SerializeField] private LayerMask enemyLayers;

    private WeaponController weapon;
    private static SoundBank bullets;

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += ShowHitEffects;
        // Loaded with the gun, not on the first ricochet: its clips come in with it (preloaded).
        if (bullets == null) bullets = SoundBank.Load(BulletBank);
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= ShowHitEffects;
    }

    // 적이 아닌 표면에 맞았을 때 효과 생성
    private void ShowHitEffects(WeaponShot shot)
    {
        ShowSurface(shot.Trace);
        if (shot.Later == null) return;
        foreach (var leg in shot.Later)
        {
            var onward = leg.Onward;
            // A body's far side bleeds instead (EnemyBloodEffect).
            if (onward.HasExit && onward.Exit.collider != null && !IsBody(onward.Exit.collider))
                ImpactPool.PlaySurface(onward.Exit.collider, onward.Exit.point, onward.Exit.normal);
            if (onward.Ricochet) Ricochet(onward.From);
            ShowSurface(leg.Trace);
        }
    }

    private void ShowSurface(in AttackTraceResult trace)
    {
        if (!trace.HasHit || trace.Hit.collider == null || IsBody(trace.Hit.collider)) return;
        ImpactPool.PlaySurface(trace.Hit.collider, trace.Hit.point, trace.Hit.normal);
    }

    // 적과 적의 부모는 제외. 시체도 벽이 아니라 몸이다
    private bool IsBody(Collider hitCollider)
        => IsEnemy(hitCollider.transform) || hitCollider.GetComponentInParent<RagdollCorpse>() != null;

    private static void Ricochet(Vector3 point)
    {
        if (bullets == null) bullets = SoundBank.Load(BulletBank);
        if (bullets == null) return;
        var sound = bullets.SettingsOf(RicochetKey);
        FeelAudio.PlayClipAt(bullets.Pick(RicochetKey), point, sound.Volume, sound.RandomPitch(), RicochetReach);
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
