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
    private ShoveCounter counter;
    [field: SerializeField, HideInInspector] public float Health { get; private set; }
    [SerializeField, HideInInspector] private bool healthInitialized;
    public bool IsAlive => Health > 0f;
    /// <summary>On the ground, crawling and waiting to be got up (PlayerLife). Their health is then what they have left down there.</summary>
    public bool IsDowned { get; private set; }
    public event Action<DamageHit, DamageResult> HitResolved;
    public event Action Died;
    /// <summary>A hit took the last of the shield (backlog 321). Raised after HitResolved, on the owner's PC only.</summary>
    public event Action<DamageHit> ShieldBroken;
    /// <summary>How many times the shield has broken, for HUDs that poll rather than listen (AdaptiveHealthHUD).</summary>
    public int ShieldBreaks { get; private set; }
    /// <summary>Of the last damaging hit, what the shield took and what health lost, set before HitResolved: a hit the shield
    /// took whole shows no blood, only the shield's light (PlayerDamageFeedback, ShieldFeedback, PlayerNetworkCombat).</summary>
    public float LastShieldTaken { get; private set; }
    public float LastHealthTaken { get; private set; }
    /// <summary>The last damaging hit never reached health: the shield took all of it.</summary>
    public bool LastHitShielded => LastShieldTaken > 0f && LastHealthTaken <= 0f;

    internal void Initialize(PlayerMovement player)
    {
        movement = player;
        if (!healthInitialized) { Health = MaximumHealth; healthInitialized = true; }
        else Health = Mathf.Clamp(Health, 0f, MaximumHealth);
        seen.Clear(); recent.Clear();
        if (Application.isPlaying && player.IsLocal && GetComponent<PlayerDamageFeedback>() == null)
            gameObject.AddComponent<PlayerDamageFeedback>();
        if (Application.isPlaying && player.IsLocal && GetComponent<LowHealthGrade>() == null)
            gameObject.AddComponent<LowHealthGrade>();
        if (Application.isPlaying && player.IsLocal && GetComponent<ShieldFeedback>() == null)
            GameTables.Bind(PlayerMovement.TableName, "Shield", gameObject.AddComponent<ShieldFeedback>());
        // A shambler's grab holds them (Guts & Blackpowder's wrestle); their own PC judges it, so only there.
        if (Application.isPlaying && player.IsLocal && GetComponent<PlayerHold>() == null)
            GameTables.Bind(PlayerMovement.TableName, "Hold", gameObject.AddComponent<PlayerHold>());
        // A timed shove counters a grab or a leap (G&B-3); judged on their own PC too.
        if (Application.isPlaying && player.IsLocal && GetComponent<ShoveCounter>() == null)
            GameTables.Bind(PlayerMovement.TableName, "Counter", gameObject.AddComponent<ShoveCounter>());
        // An enemy throws off whoever stands on its head; the rider's own PC moves them, so only there.
        if (Application.isPlaying && player.IsLocal && GetComponent<HeadStandShakeOff>() == null)
            GameTables.Bind(PlayerMovement.TableName, "HeadStand", gameObject.AddComponent<HeadStandShakeOff>());
    }

    // Call only after a melee/projectile/hitscan attack actually intersects the player.
    public DamageResult ReceiveHit(DamageHit hit)
    {
        // Someone else's copy: their health and their parry are decided on their own PC. With friendly fire on, a blow from
        // this PC's own player goes to them there (backlog 721); it shows here when their PC reports the hurt.
        if (movement != null && movement.IsNetworkDriven)
        {
            FriendlyFire.PassOn(movement, hit);
            return DamageResult.Ignored;
        }
        if (!isActiveAndEnabled || !IsAlive || hit.Amount <= 0f || float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount) || hit.Id <= 0) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        // A Schießhund's duel (HoundDuel): a called-out foe's blow on anyone but its hound lands on nothing, and so does any
        // other foe's on a hound holding the Ace card.
        if (HoundDuel.Blocks(movement, hit))
        {
            HitResolved?.Invoke(hit, DamageResult.Invulnerable);
            return DamageResult.Invulnerable;
        }
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
        // A grab or a leap right after their shove is countered (G&B-3).
        if (counter == null) counter = GetComponent<ShoveCounter>();
        if (counter != null && counter.TryHitCounter(hit))
        {
            HitResolved?.Invoke(hit, DamageResult.Countered);
            return DamageResult.Countered;
        }
        // After the dodge, so a perfect evade still counts while countering.
        if (movement != null && movement.PerfectDefense != null && movement.PerfectDefense.Shields)
        {
            HitResolved?.Invoke(hit, DamageResult.Invulnerable);
            return DamageResult.Invulnerable;
        }
        var melee = movement != null ? movement.MeleeCombat : null;
        if (melee != null && melee.TryDefend(hit, out DamageResult defense))
        {
            HitResolved?.Invoke(hit, defense);
            return defense;
        }
        // A hold taking hold costs nothing: the bites that follow do (DamageHit.TakesHold).
        float amount = hit.TakesHold && !hit.IsFatal ? 0f : hit.Amount;
        // A Knight takes less from an enemy's blow, and less again while its taunt holds (PlayerClass); a fall is a fall.
        if (hit.Kind == DamageKind.Attack) amount *= PlayerClass.TakenFactor(movement);
        float absorbed = hit.IsFatal ? 0f : Mathf.Min(Shield, amount);
        bool shieldBroke = absorbed > 0f && absorbed >= Shield;
        shield = shieldBroke ? 0f : Shield - absorbed;
        float healthBefore = Health;
        LoseHealth(hit.IsFatal ? Health : amount - absorbed);
        LastShieldTaken = absorbed;
        LastHealthTaken = Mathf.Max(0f, healthBefore - Health);
        melee?.NotifyDamaged();
        HitResolved?.Invoke(hit, DamageResult.Damaged);
        if (shieldBroke) { ShieldBreaks++; ShieldBroken?.Invoke(hit); }
        // Flesh only when health was reached; the shield's own sound is ShieldFeedback's.
        if ((movement == null || movement.IsLocal) && LastHealthTaken > 0f) CombatHitAudio.PlayPlayerHit(hit);
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

    /// <summary>
    /// The whole shield goes at once and breaks as if a hit had taken it (ShieldBroken: ShieldFeedback's break, sound and
    /// alert): the price a pinned player pays to burst out from under a runner (PlayerHold). False with no shield left.
    /// </summary>
    public bool SpendShield(in DamageHit cause)
    {
        if (Shield <= 0f || !IsAlive) return false;
        shield = 0f;
        ShieldBreaks++;
        ShieldBroken?.Invoke(cause);
        return true;
    }
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
