using UnityEngine;

/// <summary>
/// Shove fatigue after Left 4 Dead 2: every shove counts, and from the start-th one in a row the wait before the next
/// grows, reaching the longest at the most-th and staying there while the spam goes on. Each recovery period without a
/// shove takes one off the count. The gun's crowd shove and the fists' tap shove share it, and a dash push counts as
/// several shoves and waits longer the more tired it is.
/// </summary>
public sealed class ShoveFatigue
{
    private float count, idle, rest, restTotal;

    public int Count => Mathf.CeilToInt(count);
    /// <summary>Seconds before the next shove, and the share of the last wait still left (0 when ready).</summary>
    public float Rest => rest;
    public float RestShare => restTotal > 0f ? rest / restTotal : 0f;

    internal void Tick(float dt, MovementAbilitySettings settings)
    {
        rest = Mathf.Max(0f, rest - dt);
        if (count <= 0f) return;
        idle += dt;
        float recovery = Mathf.Max(.05f, settings.shoveFatigueRecovery);
        while (idle >= recovery && count > 0f) { idle -= recovery; count = Mathf.Max(0f, count - 1f); }
    }

    /// <summary>Counts a shove and returns the wait before the next one.</summary>
    internal float Spend(float interval, MovementAbilitySettings settings)
    {
        count += 1f; idle = 0f;
        rest = restTotal = Mathf.Lerp(interval, Mathf.Max(interval, settings.shoveFatigueInterval), Tiredness(settings));
        return rest;
    }

    /// <summary>Counts a dash push and returns the wait before the next dash. The shoves keep their own wait.</summary>
    internal float SpendDash(MovementAbilitySettings settings)
    {
        count += settings.dashPushFatigueCost; idle = 0f;
        float interval = settings.dashPushCooldown;
        return Mathf.Lerp(interval, Mathf.Max(interval, settings.dashPushFatigueCooldown), Tiredness(settings));
    }

    private float Tiredness(MovementAbilitySettings settings)
    {
        int start = Mathf.Max(1, settings.shoveFatigueStart), most = Mathf.Max(start, settings.shoveFatigueMost);
        return count < start ? 0f : Mathf.InverseLerp(start - 1, most, count);
    }

    internal void Reset() { count = idle = rest = restTotal = 0f; }
}
