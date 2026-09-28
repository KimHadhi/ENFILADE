using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerDamageReceiver : MonoBehaviour, IDamageReceiver
{
    [SerializeField, Min(1f)] private float maximumHealth = 100f;
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private PlayerMovement movement;
    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;
    public event Action<DamageHit, DamageResult> HitResolved;
    public event Action Died;

    internal void Initialize(PlayerMovement player)
    { movement = player; Health = maximumHealth; seen.Clear(); recent.Clear(); }

    // Call only after a melee/projectile/hitscan attack actually intersects the player.
    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (!isActiveAndEnabled || !IsAlive || hit.Amount <= 0f || hit.Id <= 0) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        if (hit.Kind == DamageKind.Attack && movement != null && movement.Abilities.Dodge.TryEvade())
        {
            HitResolved?.Invoke(hit, DamageResult.Evaded);
            return DamageResult.Evaded;
        }
        Health = Mathf.Max(0f, Health - hit.Amount);
        HitResolved?.Invoke(hit, DamageResult.Damaged);
        if (!IsAlive) { movement?.ResetVelocity(); Died?.Invoke(); }
        return DamageResult.Damaged;
    }

    public void RestoreHealth()
    { Health = maximumHealth; seen.Clear(); recent.Clear(); movement?.ResetVelocity(); }

    // Called only after the reward-only step trail intersects an attack.
    // A miss can refund cooldown, but can never damage the real body.
    internal DamageResult ReceiveDodgeNearMiss(DamageHit hit)
    {
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
