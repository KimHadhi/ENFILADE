using UnityEngine;

/// <summary>
/// Something that answers each bullet that strikes it, from any gun on this PC (its own player's, or a friend's copy
/// replaying their shot), as the surface's impact plays (WeaponImpactEffects): the hub's steel targets ring, swing and
/// take a lead splash (SteelTarget). `push` is the gun's prop push (Weapons.csv propImpulse) times the
/// share of it the bullet still carried. Damage stays the receiver's (IDamageReceiver), judged on the shooter's PC.
/// </summary>
public interface IBulletStruck { void Struck(Collider part, Vector3 point, Vector3 normal, Vector3 incoming, float push); }

/// <summary>
/// The surface's impact effect and bullet hole where a shot from the parent WeaponController hits anything but a body.
/// A bullet that went on (AttackTrace.Bullet) also bursts out of the far side of each wall it came through, bigger than
/// where it went in and thrown along its way (ImpactPool.PlayExit), tears through it with the material's sound, makes an
/// impact wherever it hit next, and plays ArcCW's ricochet sound where it glanced off. Each impact is told which way its
/// bullet was going, so a hole shot at a slant comes out long (BulletHoles). What it hit hears of it too when it answers
/// its own bullets (IBulletStruck: a steel target rings and swings, whoever fired).
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponImpactEffects : MonoBehaviour
{
    // Sound bank Bullets: Art/Audio/Bullets/Ricochet, and Art/Audio/Bullets/Penetrate/<surface> for going through, found
    // along the surface's fallbacks; volume and pitch in Sounds.csv (Bullets/...). No clips there: no sound.
    private const string BulletBank = "Bullets", RicochetKey = "Ricochet", PenetrateEvent = "Penetrate";
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
        ShowSurface(shot.Trace, shot.Direction);
        if (shot.Later == null) return;
        var entered = shot.Trace;
        foreach (var leg in shot.Later)
        {
            var onward = leg.Onward;
            if (onward.Ricochet) Ricochet(onward.From);
            else if (leg.Through != null && !IsBody(leg.Through) && entered.HasHit) Penetrate(leg.Through, entered.Hit.point);
            // A body's far side bleeds instead (EnemyBloodEffect).
            if (onward.HasExit && onward.Exit.collider != null && !IsBody(onward.Exit.collider))
                ImpactPool.PlayExit(onward.Exit.collider, onward.Exit.point, onward.Exit.normal, onward.Direction);
            // From here the bullet goes the way it left the last thing it met.
            ShowSurface(leg.Trace, onward.Direction, leg.Share);
            entered = leg.Trace;
        }
    }

    // Tearing through: the material's sound where the bullet went in.
    private static void Penetrate(Collider through, Vector3 point)
    {
        if (bullets == null) bullets = SoundBank.Load(BulletBank);
        if (bullets == null) return;
        string key = bullets.Find(PenetrateEvent, Surfaces.Of(through), out _);
        if (key == null) return;
        var sound = bullets.SettingsOf(key);
        FeelAudio.PlayClipAt(bullets.Pick(key), point, sound.Volume, sound.RandomPitch(), RicochetReach);
    }

    private void ShowSurface(in AttackTraceResult trace, Vector3 incoming, float share = 1f)
    {
        if (!trace.HasHit || trace.Hit.collider == null || IsBody(trace.Hit.collider)) return;
        ImpactPool.PlaySurface(trace.Hit.collider, trace.Hit.point, trace.Hit.normal, incoming);
        // Something that answers its own bullets hears of this one, pushed as hard as the gun pushes a prop.
        if (trace.Hit.collider.GetComponentInParent<IBulletStruck>() is IBulletStruck struck)
            struck.Struck(trace.Hit.collider, trace.Hit.point, trace.Hit.normal, incoming, Push * share);
    }

    // The gun's push on what its bullet hits (Weapons.csv propImpulse), as WeaponController pushes a prop.
    private float Push => weapon != null && weapon.EquippedData != null ? weapon.EquippedData.PropImpulse : 1f;

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
