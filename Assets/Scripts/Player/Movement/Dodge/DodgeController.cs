using System;

public enum DodgeKind { Step, Micro }

/// <summary>Shared, deterministic invulnerability window. No collider is ever disabled.</summary>
public sealed class DodgeController
{
    private float remaining, cooldown;
    private bool successEmitted;
    public DodgeKind Kind { get; private set; }
    public int LeanSide { get; private set; }
    public int Activation { get; private set; }
    public float CooldownRemaining => cooldown;
    public bool IsInvulnerable => remaining > 0f;
    public bool CanStart => cooldown <= 0f && (remaining <= 0f || successEmitted);
    public event Action<DodgeKind, int> Succeeded;

    public bool TryStart(DodgeKind kind, int side, float duration, float reuseDelay)
    {
        if (!CanStart) return false;
        Kind = kind; LeanSide = side; successEmitted = false;
        Activation++;
        remaining = Math.Max(0f, duration);
        cooldown = Math.Max(remaining, reuseDelay);
        return true;
    }
    public void Tick(float dt) { remaining = Math.Max(0f, remaining - dt); cooldown = Math.Max(0f, cooldown - dt); }
    public bool TryEvade()
    {
        if (!IsInvulnerable) return false;
        if (!successEmitted)
        {
            successEmitted = true; cooldown = 0f;
            Succeeded?.Invoke(Kind, LeanSide);
        }
        return true;
    }
    public void CancelWindow() { remaining = 0f; }
    public void Reset() { remaining = cooldown = 0f; successEmitted = false; }
}
