using System;
using UnityEngine;

/// <summary>Per-character ultimate resource. Ability code grants charge and calls TryConsume.</summary>
[DisallowMultipleComponent]
public sealed class UltimateCharge : MonoBehaviour
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
    [SerializeField, Min(0)] private float maximumCharge = 120f;
    [SerializeField, Min(0)] private float requiredCharge = 100f;

    public float MaximumCharge => NonNegative(maximumCharge);
    public float RequiredCharge => NonNegative(requiredCharge);
    public float CurrentCharge => Mathf.Clamp(NonNegative(currentCharge), 0f, MaximumCharge);
    public bool IsReady => MaximumCharge > 0f && CurrentCharge >= RequiredCharge;
    public float FillRatio => MaximumCharge > 0f ? CurrentCharge / MaximumCharge : 0f;
    public float ReadinessRatio => MaximumCharge <= 0f ? 0f : RequiredCharge <= 0f ? 1f : Mathf.Clamp01(CurrentCharge / RequiredCharge);
    public float ThresholdRatio => MaximumCharge > 0f ? Mathf.Clamp01(RequiredCharge / MaximumCharge) : 0f;
    public event Action Changed;
    public event Action<bool> ReadinessChanged;

    private bool dirty = true, published, previousReady;
    private float previousCurrent, previousMaximum, previousRequired;

    private void OnEnable() { dirty = true; }
    private void OnValidate() { dirty = true; }
    private void Update() { if (dirty) Publish(); }

    public void SetValues(float current, float maximum, float required)
    {
        maximumCharge = NonNegative(maximum);
        requiredCharge = NonNegative(required);
        currentCharge = Mathf.Clamp(NonNegative(current), 0f, maximumCharge);
        Publish();
    }

    /// <summary>Level-up changes preserve stored charge, clamping only if capacity shrinks.</summary>
    public void SetLimits(float maximum, float required) => SetValues(CurrentCharge, maximum, required);

    public void AddCharge(float amount)
    {
        double sum = (double)CurrentCharge + NonNegative(amount);
        SetValues((float)Math.Min(sum, MaximumCharge), MaximumCharge, RequiredCharge);
    }

    /// <summary>Spend only the required amount. Overflow remains for the next cast.</summary>
    public bool TryConsume()
    {
        if (!IsReady) return false;
        SetValues(CurrentCharge - RequiredCharge, MaximumCharge, RequiredCharge);
        return true;
    }

    private void Publish()
    {
        maximumCharge = MaximumCharge;
        requiredCharge = RequiredCharge;
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
