using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerDamageReceiver : MonoBehaviour, IDamageReceiver, ITableBound
{
    // The health bar rounds, so anything it would show as 0 is 0. Nobody stands there at "0" on a leftover sliver.
    private const float LeastHealth = .5f;
    [SerializeField, Min(1f)] private float maximumHealth = 100f;
    public float MaximumHealth => ValidMaximum(maximumHealth);
    [SerializeField, Min(0f)] private float shield;
    public float Shield => NonNegative(shield);
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private PlayerMovement movement;
    [field: SerializeField, HideInInspector] public float Health { get; private set; }
    [SerializeField, HideInInspector] private bool healthInitialized;
    public bool IsAlive => Health > 0f;
    /// <summary>On the ground, crawling and waiting to be got up (PlayerLife). Their health is then what they have left down there.</summary>
    public bool IsDowned { get; private set; }
    public event Action<DamageHit, DamageResult> HitResolved;
    public event Action Died;

    internal void Initialize(PlayerMovement player)
    {
        movement = player;
        if (!healthInitialized) { Health = MaximumHealth; healthInitialized = true; }
        else Health = Mathf.Clamp(Health, 0f, MaximumHealth);
        seen.Clear(); recent.Clear();
        if (Application.isPlaying && player.IsLocal && GetComponent<PlayerDamageFeedback>() == null)
            gameObject.AddComponent<PlayerDamageFeedback>();
    }

    // Call only after a melee/projectile/hitscan attack actually intersects the player.
    public DamageResult ReceiveHit(DamageHit hit)
    {
        // Someone else's copy: their health and their parry are decided on their own PC.
        if (movement != null && movement.IsNetworkDriven) return DamageResult.Ignored;
        if (!isActiveAndEnabled || !IsAlive || hit.Amount <= 0f || float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount) || hit.Id <= 0) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        if (movement != null && movement.Execution != null && movement.Execution.IsExecuting)
        {
            HitResolved?.Invoke(hit, DamageResult.Invulnerable);
            return DamageResult.Invulnerable;
        }
        if (hit.Kind == DamageKind.Attack && movement != null && movement.Abilities.Dodge.TryEvade())
        {
            HitResolved?.Invoke(hit, DamageResult.Evaded);
            return DamageResult.Evaded;
        }
        var melee = movement != null ? movement.MeleeCombat : null;
        if (melee != null && melee.TryDefend(hit, out DamageResult defense))
        {
            HitResolved?.Invoke(hit, defense);
            return defense;
        }
        float absorbed = hit.IsFatal ? 0f : Mathf.Min(Shield, hit.Amount);
        shield = Shield - absorbed;
        LoseHealth(hit.IsFatal ? Health : hit.Amount - absorbed);
        melee?.NotifyDamaged();
        HitResolved?.Invoke(hit, DamageResult.Damaged);
        if (movement == null || movement.IsLocal) CombatHitAudio.PlayPlayerHit(hit);
        if (!IsAlive && (hit.IsFatal || !TryGetComponent(out PlayerLife life) || !life.TryGoDown())) Die();
        return DamageResult.Damaged;
    }

    private void LoseHealth(float amount)
    {
        Health -= amount;
        if (Health < LeastHealth) Health = 0f;
    }

    private void Die()
    {
        // Someone who dies on the ground stays lying there.
        bool wasDowned = IsDowned;
        SetDowned(false);
        if (!wasDowned) movement?.ResetVelocity();
        Died?.Invoke();
    }

    private void SetDowned(bool downed)
    {
        IsDowned = downed;
        if (TryGetComponent(out PlayerWeaponSlots slots)) slots.HoldSidearm(downed);
    }

    /// <summary>Health ran out with someone still standing: they go down with this much left.</summary>
    internal void GoDown(float downedHealth)
    {
        Health = downedHealth;
        SetDowned(true);
        movement?.ResetVelocity();
        GetComponent<PlayerDamageFeedback>()?.Clear();
    }

    /// <summary>Time on the ground wears them down.</summary>
    internal void Bleed(float amount)
    {
        if (!IsDowned) return;
        LoseHealth(amount);
        if (!IsAlive) Die();
    }

    /// <summary>Someone got them up, with this much health.</summary>
    internal void GetUp(float health)
    {
        Health = Mathf.Clamp(health, 1f, MaximumHealth);
        SetDowned(false);
        movement?.Abilities.ReturnToLocomotion();
    }

    /// <summary>On someone else's copy: the health their own PC reports.</summary>
    internal void ShowHealth(float health) => Health = Mathf.Clamp(health, 0f, MaximumHealth);
    /// <summary>On someone else's copy: whether they are down, as their own PC reports.</summary>
    internal void ShowDowned(bool downed) => IsDowned = downed;

    public void RestoreHealth()
    {
        Health = MaximumHealth; shield = 0f; healthInitialized = true; seen.Clear(); recent.Clear();
        SetDowned(false);
        movement?.ResetVelocity();
        GetComponent<PlayerDamageFeedback>()?.Clear();
    }

    public void SetShield(float amount) { shield = NonNegative(amount); }
    public void AddShield(float amount) { shield = (float)Math.Min(float.MaxValue, (double)Shield + NonNegative(amount)); }
    private static float NonNegative(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

    /// <summary>Change capacity while preserving current HP unless refill is requested.</summary>
    public void SetMaximumHealth(float maximum, bool refill = false)
    {
        maximumHealth = ValidMaximum(maximum);
        if (refill) RestoreHealth();
        else if (healthInitialized) Health = Mathf.Clamp(Health, 0f, maximumHealth);
    }

    /// <summary>Heal a living player. Use RestoreHealth for respawning.</summary>
    public void Heal(float amount)
    {
        if (!IsAlive || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;
        Health = (float)Math.Min(MaximumHealth, (double)Health + amount);
    }

    private void OnValidate()
    {
        maximumHealth = ValidMaximum(maximumHealth);
        shield = NonNegative(shield);
        if (healthInitialized) Health = Mathf.Clamp(Health, 0f, maximumHealth);
    }

    void ITableBound.OnTableApplied() => OnValidate();

    private static float ValidMaximum(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 100f : Mathf.Max(1f, value);

    // Called only after the reward-only step trail intersects an attack.
    // A miss can refund cooldown, but can never damage the real body.
    internal DamageResult ReceiveDodgeNearMiss(DamageHit hit)
    {
        if (movement != null && movement.Execution != null && movement.Execution.IsExecuting) return DamageResult.Ignored;
        if (!isActiveAndEnabled || !IsAlive || hit.Amount <= 0f || hit.Id <= 0 || hit.Kind != DamageKind.Attack
            || movement == null || !movement.Abilities.StepTrail.IsActive) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        if (!movement.Abilities.Dodge.TryEvade()) return DamageResult.Ignored;
        HitResolved?.Invoke(hit, DamageResult.Evaded);
        return DamageResult.Evaded;
    }
}
