using System;
using UnityEngine;

/// <summary>Per-character ultimate resource. Ability code grants charge and calls TryConsume.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStats))]
public sealed class UltimateCharge : MonoBehaviour, ITableBound
{
    // HUD-local values remain useful outside Play; gameplay values belong to the player.
    public static UltimateCharge ResolveHUDSource(Component view, UltimateCharge authored)
    {
        if (!Application.IsPlaying(view.gameObject)) return authored;
        var hud = view.GetComponentInParent<AdaptiveHealthHUD>();
        var player = hud != null ? hud.ResolvePlayer() : null;
        return player != null && player.Ultimate != null ? player.Ultimate : authored;
    }
    [SerializeField, Min(0)] private float currentCharge;
    [SerializeField, Min(0), Tooltip("Charge granted per accepted hit on a living enemy, including lethal hits.")]
    private float chargePerEnemyHit = 3f;

    public static void AwardEnemyHit(DamageHit hit)
    {
        if (hit.Kind != DamageKind.Attack || hit.Source == null) return;
        var player = hit.Source.GetComponentInParent<PlayerMovement>();
        if (player == null || player.Ultimate == null || player.DamageReceiver == null || !player.DamageReceiver.IsAlive) return;
        player.Ultimate.AddCharge(player.Ultimate.chargePerEnemyHit);
    }

    private PlayerStats stats;
    public PlayerStats Stats
    {
        get
        {
            if (stats == null)
            {
                // Repair already-loaded scene instances when this dependency is added by a script reload.
                stats = GetComponent<PlayerStats>() ?? gameObject.AddComponent<PlayerStats>();
                stats.Changed += Publish;
            }
            return stats;
        }
    }
    public float MaximumCharge => Stats.UltimateCapacityPercent;
    public float RequiredCharge => Stats.UltimateCostPercent;
    public float CurrentCharge => Mathf.Clamp(NonNegative(currentCharge), 0f, MaximumCharge);
    public bool IsReady => MaximumCharge > 0f && CurrentCharge >= RequiredCharge;
    public float FillRatio => MaximumCharge > 0f ? CurrentCharge / MaximumCharge : 0f;
    public float ReadinessRatio => MaximumCharge <= 0f ? 0f : RequiredCharge <= 0f ? 1f : Mathf.Clamp01(CurrentCharge / RequiredCharge);
    public float ThresholdRatio => MaximumCharge > 0f ? Mathf.Clamp01(RequiredCharge / MaximumCharge) : 0f;
    public event Action Changed;
    public event Action<bool> ReadinessChanged;

    private bool dirty = true, published, previousReady;
    private float previousCurrent, previousMaximum, previousRequired;

    private void OnEnable() { dirty = true; _ = Stats; }
    // Stat changes must still clamp stored charge while the player/resource is disabled.
    private void OnDestroy() { if (stats != null) stats.Changed -= Publish; }
    private void OnValidate() { dirty = true; }
    void ITableBound.OnTableApplied() => OnValidate();
    private void Update() { if (dirty) Publish(); }

    public void SetValues(float current, float maximum, float required)
    {
        currentCharge = NonNegative(current);
        Stats.SetUltimateLimits(maximum, required);
        Publish();
    }

    /// <summary>Level-up changes preserve stored charge, clamping only if capacity shrinks.</summary>
    public void SetLimits(float maximum, float required) => SetValues(CurrentCharge, maximum, required);

    public void SetCurrentCharge(float current)
    {
        currentCharge = NonNegative(current);
        Publish();
    }

    public void AddCharge(float amount)
    {
        double sum = (double)CurrentCharge + NonNegative(amount);
        SetCurrentCharge((float)Math.Min(sum, MaximumCharge));
    }

    /// <summary>Spend only the required amount. Overflow remains for the next cast.</summary>
    public bool TryConsume()
    {
        if (!IsReady) return false;
        SetCurrentCharge(CurrentCharge - RequiredCharge);
        return true;
    }

    private void Publish()
    {
        float maximumCharge = MaximumCharge;
        float requiredCharge = RequiredCharge;
        currentCharge = CurrentCharge;
        dirty = false;
        bool ready = IsReady;
        bool changed = !published || previousCurrent != currentCharge || previousMaximum != maximumCharge || previousRequired != requiredCharge;
        bool readinessChanged = !published || ready != previousReady;
        previousCurrent = currentCharge; previousMaximum = maximumCharge; previousRequired = requiredCharge;
        previousReady = ready; published = true;
        if (changed) Changed?.Invoke();
        if (readinessChanged) ReadinessChanged?.Invoke(ready);
    }

    private static float NonNegative(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
}
