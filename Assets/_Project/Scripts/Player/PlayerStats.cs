using System;
using UnityEngine;

/// <summary>Player stat values in percentage points: 120 capacity stores 1.2 default casts.</summary>
[DisallowMultipleComponent]
public sealed class PlayerStats : MonoBehaviour
{
    [Header("Ultimate (%)")]
    [SerializeField, Min(0), Tooltip("Maximum stored ultimate charge. 120 means 120%.")]
    private float ultimateCapacityPercent = 120f;
    [SerializeField, Min(0), Tooltip("Charge spent per cast. 100 means 100%, independently of capacity.")]
    private float ultimateCostPercent = 100f;

    public float UltimateCapacityPercent => NonNegative(ultimateCapacityPercent);
    public float UltimateCostPercent => NonNegative(ultimateCostPercent);
    public event Action Changed;

    private bool dirty = true, published;
    private float previousCapacity, previousCost;

    private void OnEnable() { dirty = true; }
    private void OnValidate() { dirty = true; }
    private void Update() { if (dirty) Publish(); }

    public void SetUltimateCapacity(float percent) => SetUltimateLimits(percent, UltimateCostPercent);
    public void SetUltimateCost(float percent) => SetUltimateLimits(UltimateCapacityPercent, percent);

    public void SetUltimateLimits(float capacityPercent, float costPercent)
    {
        ultimateCapacityPercent = NonNegative(capacityPercent);
        ultimateCostPercent = NonNegative(costPercent);
        Publish();
    }

    private void Publish()
    {
        ultimateCapacityPercent = UltimateCapacityPercent;
        ultimateCostPercent = UltimateCostPercent;
        dirty = false;
        bool changed = !published || previousCapacity != ultimateCapacityPercent || previousCost != ultimateCostPercent;
        previousCapacity = ultimateCapacityPercent;
        previousCost = ultimateCostPercent;
        published = true;
        if (changed) Changed?.Invoke();
    }

    private static float NonNegative(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
}
