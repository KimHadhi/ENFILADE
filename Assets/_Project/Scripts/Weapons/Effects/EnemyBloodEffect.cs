using UnityEngine;

/// <summary>
/// Prefab blood particles and a fading blood mark where a shot from the parent WeaponController hits an enemy. The mark
/// is a projected decal riding the hitbox it hit (BloodDecals): it was a sprite hung 3 cm off the hitbox, which flickered
/// against other marks and the scenery and stuck out of the body at a slant (the user, 2026-11-14).
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyBloodEffect : MonoBehaviour
{
    [Header("Enemy Detection")]
    [SerializeField] private LayerMask enemyLayers;

    [Header("Blood Particles")]
    [SerializeField] private ParticleSystem bloodParticlePrefab;
    [SerializeField, Min(0.1f)] private float particleCleanupTime = 3f;
    [SerializeField, Min(0f), Tooltip("How far off the body the particles start, so they aren't born inside it (m).")]
    private float surfaceOffset = 0.03f;

    [Header("Blood Mark")]
    [SerializeField, Min(0.1f)] private float bloodMarkLifetime = 5f;
    [SerializeField, Min(.01f), Tooltip("Across (m). The sprite it replaced measured this: a 3.6 m sprite at 0.05 scale.")]
    private float bloodMarkSize = .18f;

    private WeaponController weapon;

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += Play;
    }

    private void Play(WeaponShot shot)
    {
        Bleed(shot.Trace, shot.Direction);
        if (shot.Later == null) return;
        // A bullet that went on (AttackTrace.Bullet) bleeds every enemy it hit after the first, and comes out of the far
        // side of each one it went through.
        foreach (var leg in shot.Later)
        {
            var onward = leg.Onward;
            if (onward.HasExit && onward.Exit.collider != null)
            {
                Bleed(onward.Exit, onward.Direction);
                // Blood bursts out of the back along the bullet, so a round through a body reads at a glance.
                if (IsEnemy(onward.Exit.collider.transform)) ImpactVfx.ExitWound(onward.Exit.point, onward.Direction, onward.Exit.collider);
            }
            Bleed(leg.Trace, onward.Direction);
        }
    }

    private void Bleed(in AttackTraceResult trace, Vector3 direction)
    {
        if (trace.HasHit) Bleed(trace.Hit, direction);
    }

    private void Bleed(in RaycastHit hit, Vector3 direction)
    {
        if (hit.collider == null || !IsEnemy(hit.collider.transform))
            return;

        Vector3 normal = hit.normal.sqrMagnitude > 0.001f
            ? hit.normal.normalized
            : -direction.normalized;

        // A particle system sprays along its local Y.
        Quaternion particleRotation =
            Quaternion.FromToRotation(Vector3.up, normal);

        SpawnBloodParticles(hit.point + normal * surfaceOffset, particleRotation);
        // Painted into the body at the point hit, its box reaching the skin under the hitbox.
        BloodDecals.Mark(hit.collider.transform, hit.point, normal, bloodMarkSize, bloodMarkLifetime);
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

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= Play;
    }
}
