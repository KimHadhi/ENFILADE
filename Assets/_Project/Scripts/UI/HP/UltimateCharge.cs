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
    // What fills the gauge is what a player gives the fight, not how many times their gun went off (the user, 2026-11-12,
    // on the old charge a hit: "굉장히 옛날에 만들어 둔 건데 개편해도 될 듯"). A hit counted 3% a pellet, so a shotgun's
    // blast filled nine times a rifle's shot and the Ghost, the class that grows by its cards, drew them last, while a
    // heal, a resupply or a parry filled nothing. Now an enemy's health taken counts, the same for any gun (overkill
    // left out, so a headshot is worth the zombie it ends, not its damage number), and so does looking after the team,
    // which counts for more: a dose is one of four in a syringe only a shop or a Support refills, while a shot costs
    // nothing (the user, 2026-11-12: "힐러가 치료 100하려면 주사기 2.5번 써야 하는데 남들은 그냥 일반좀비 헤드샷 딸깍하면
    // 되잖아"). Numbers: Player.csv Ultimate.*.
    [SerializeField, Min(0), Tooltip("Charge per 100 health an enemy loses to this player's hit (overkill not counted). A shambler has 100.")]
    private float chargePerHundred = 4f;
    [SerializeField, Min(0), Tooltip("Charge per 100 health this player gives back to friends (a Détente's dose or treatment).")]
    private float chargePerHundredHealed = 15f;
    [SerializeField, Min(0), Tooltip("Charge per resupply handed to a friend (a Support's).")]
    private float chargePerResupply = 6f;
    [SerializeField, Min(0), Tooltip("Charge per enemy blow parried.")]
    private float chargePerParry = 3f;
    [SerializeField, Min(0), Tooltip("Charge per friend got up from downed or brought back from the dead.")]
    private float chargePerRevive = 15f;
    [SerializeField, Min(0), Tooltip("Charge per 100 health a Support's hammer puts back into a barricade.")]
    private float chargePerHundredRepaired = 5f;

    /// <summary>A way this player looked after the team, which charges the gauge as fighting does (AwardHelp).</summary>
    public enum Help { Healed, Resupplied, Parried, Revived, Repaired }

    /// <summary>
    /// This PC's player's hit took <paramref name="taken"/> health from an enemy (a zombie or a range target, as its own
    /// copy judged it, before the host's word): the gauge fills by it.
    /// </summary>
    public static void AwardEnemyHit(DamageHit hit, float taken)
    {
        // An execution counts as the health it took (a fall never comes from a player).
        if (hit.Kind == DamageKind.Fall || hit.Source == null || !(taken > 0f)) return;
        var player = hit.Source.GetComponentInParent<PlayerMovement>();
        if (player == null || !player.IsLocal || player.Ultimate == null || player.DamageReceiver == null || !player.DamageReceiver.IsAlive) return;
        var gauge = player.Ultimate;
        gauge.AddCharge(taken / 100f * gauge.chargePerHundred * PlayerPerks.ChargeFactor); // faster with the charge perk
    }

    /// <summary>
    /// This PC's player looked after the team: <paramref name="amount"/> is the health given back for a heal, and how many
    /// and a barricade's mend, and how many for the rest (resupplies, parries, friends got up).
    /// </summary>
    public static void AwardHelp(Help help, float amount = 1f)
    {
        var player = PlayerRoster.Local;
        if (!(amount > 0f) || player == null || !player.IsLocal || player.Ultimate == null) return;
        var gauge = player.Ultimate;
        float each = help switch
        {
            Help.Healed => gauge.chargePerHundredHealed / 100f,
            Help.Resupplied => gauge.chargePerResupply,
            Help.Parried => gauge.chargePerParry,
            Help.Repaired => gauge.chargePerHundredRepaired / 100f,
            _ => gauge.chargePerRevive,
        };
        gauge.AddCharge(amount * each * PlayerPerks.ChargeFactor);
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
