using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gameplay "it got hit" for bullets, so shooting an enemy is a felt advantage and not only a number:
/// every bullet nudges the enemy back along the shot and briefly slows it; a headshot cancels its attack wind-up
/// (short stun); enough body damage in a short window does the same. After an interrupt there is a cooldown, so
/// sustained fire cannot stun-lock. Bullets also wear down posture (WeaponData → Posture Damage) without
/// cancelling attacks on their own; a posture break staggers as usual (execution).
/// Called by EnemyHealth after damage is applied. Tuning: GunFeel → Enemy hit reaction, per weapon Flinch Multiplier.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyHitReaction : MonoBehaviour
{
    private NavMeshAgent agent;
    private MeleeCombatTarget target;
    private float baseSpeed = -1f;
    private float slowRemaining, slowDuration = 1f, pushRemaining, pushDuration = 1f, interruptCooldown, recentDamage;
    private Vector3 pushDistance;

    public float SpeedMultiplier { get; private set; } = 1f;
    public float InterruptCooldown => interruptCooldown;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        target = GetComponent<MeleeCombatTarget>();
        if (agent != null) baseSpeed = agent.speed;
        enabled = false;
    }

    /// <summary>applied = health actually removed by this hit.</summary>
    public void OnHit(DamageHit hit, float applied, float maximumHealth)
    {
        var settings = GunFeelSettings.Instance;
        if (!settings.enemyFlinch || hit.Method != DamageMethod.Bullet || applied <= 0f) return;
        float flinch = FlinchOf(hit);
        bool head = hit.IsCritical;

        if (target != null && hit.PostureDamage > 0f)
            target.ApplyPostureDamage(hit.PostureDamage * (head ? settings.headshotPostureMultiplier : 1f), 0f, interrupt: false);

        Vector3 direction = Vector3.ProjectOnPlane(hit.Direction, Vector3.up);
        if (direction.sqrMagnitude > .0001f && flinch > 0f)
        {
            pushDistance = direction.normalized * ((head ? settings.flinchPushHead : settings.flinchPushBody) * flinch);
            pushDuration = pushRemaining = settings.flinchPushSeconds;
        }
        if (flinch > 0f) { slowDuration = slowRemaining = settings.flinchSlowSeconds; }

        recentDamage += applied;
        if (interruptCooldown <= 0f && target != null)
        {
            float threshold = maximumHealth * settings.bodyInterruptFraction / Mathf.Max(.1f, flinch);
            float stun = head && settings.headshotInterruptStun > 0f ? settings.headshotInterruptStun
                : recentDamage >= threshold ? settings.bodyInterruptStun : 0f;
            if (stun > 0f)
            {
                target.Interrupt(stun);
                interruptCooldown = settings.interruptCooldown;
                recentDamage = 0f;
            }
        }
        enabled = true;
    }

    private static float FlinchOf(DamageHit hit)
    {
        if (hit.Source == null) return 1f;
        var weapon = hit.Source.GetComponentInChildren<WeaponController>();
        return weapon != null && weapon.EquippedData != null ? weapon.EquippedData.FlinchMultiplier : 1f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        var settings = GunFeelSettings.Instance;
        interruptCooldown = Mathf.Max(0f, interruptCooldown - dt);
        recentDamage *= Mathf.Exp(-dt / Mathf.Max(.05f, settings.bodyInterruptWindow));
        bool usable = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && (target == null || !target.IsKnockedDown);

        if (pushRemaining > 0f)
        {
            float slice = Mathf.Min(dt, pushRemaining);
            // Linearly decaying speed: most of the push lands in the first frames, like an impact.
            float before = pushRemaining / pushDuration, after = (pushRemaining - slice) / pushDuration;
            Vector3 step = pushDistance * (before * before - after * after);
            pushRemaining -= slice;
            if (usable && step.sqrMagnitude > 1e-8f)
            {
                if (NavMesh.Raycast(agent.nextPosition, agent.nextPosition + step, out var edge, agent.areaMask))
                    step = step.normalized * Mathf.Max(0f, Vector3.Distance(agent.nextPosition, edge.position) - .02f);
                agent.Move(step);
            }
        }

        slowRemaining = Mathf.Max(0f, slowRemaining - dt);
        SpeedMultiplier = slowRemaining > 0f ? Mathf.Lerp(1f, settings.flinchSlow, slowRemaining / slowDuration) : 1f;
        if (agent != null && baseSpeed >= 0f) agent.speed = baseSpeed * SpeedMultiplier;

        if (pushRemaining <= 0f && slowRemaining <= 0f && interruptCooldown <= 0f && recentDamage < .5f)
        {
            recentDamage = 0f;
            enabled = false;
        }
    }

    private void OnDisable()
    {
        SpeedMultiplier = 1f;
        if (agent != null && baseSpeed >= 0f) agent.speed = baseSpeed;
    }
}
