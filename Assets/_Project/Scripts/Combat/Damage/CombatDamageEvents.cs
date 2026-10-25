using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>What one accepted hit actually did to a target's health.</summary>
public readonly struct CombatDamageReport
{
    public readonly Component Target;
    public readonly DamageHit Hit;
    public readonly float HealthBefore, HealthAfter, MaximumHealth;
    public CombatDamageReport(Component target, DamageHit hit, float before, float after, float maximum)
    { Target = target; Hit = hit; HealthBefore = before; HealthAfter = after; MaximumHealth = maximum; }
    /// <summary>Health actually removed (overkill excluded).</summary>
    public float Applied => Mathf.Max(0f, HealthBefore - HealthAfter);
    public bool IsCritical => Hit.IsCritical;
    /// <summary>Death blow: this hit took the target from alive to zero health (executions included).</summary>
    public bool IsKillingBlow => HealthBefore > 0f && HealthAfter <= 0f;
}

/// <summary>
/// Presentation hook for damage dealt to enemies and practice targets. Receivers call Report after
/// subtracting health; UI such as damage popups subscribes. Gameplay must not depend on this event.
/// </summary>
public static class CombatDamageEvents
{
    public static event Action<CombatDamageReport> Damaged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => Damaged = null;

    public static void Report(Component target, DamageHit hit, float healthBefore, float healthAfter, float maximumHealth)
    {
        if (target == null || !(healthBefore > healthAfter)) return;
        var handlers = Damaged;
        if (handlers == null) return;
        var report = new CombatDamageReport(target, hit, healthBefore, healthAfter, maximumHealth);
        // A presentation failure must never interrupt damage or the death callback that follows.
        foreach (Action<CombatDamageReport> handler in handlers.GetInvocationList())
        {
            try { handler(report); }
            catch (Exception exception) { Debug.LogException(exception, target); }
        }
    }

    private static readonly Regex SpawnSuffix = new Regex(@"\s*\((Clone|Console)\)", RegexOptions.Compiled);
    /// <summary>Health bar label: ICombatDisplayName if present, otherwise the object name without spawn suffixes.</summary>
    public static string DisplayNameOf(Component target)
    {
        if (target == null) return string.Empty;
        string authored = target.TryGetComponent(out ICombatDisplayName named) ? named.DisplayName : null;
        if (!string.IsNullOrWhiteSpace(authored)) return authored.Trim();
        return SpawnSuffix.Replace(target.gameObject.name, string.Empty).Trim();
    }
}
