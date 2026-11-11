using System;
using UnityEngine; // 추가됨

public enum DodgeKind { Step, Micro }

/// <summary>Shared, deterministic invulnerability window. No collider is ever disabled.</summary>
public sealed class DodgeController
{
    private float remaining, cooldown, heldOffUntil = -1f;
    private bool successEmitted;
    public DodgeKind Kind { get; private set; }
    public int LeanSide { get; private set; }
    public int Activation { get; private set; }
    public float CooldownRemaining => cooldown;
    public bool IsInvulnerable => remaining > 0f;
    public bool HasSucceeded => successEmitted;
    public bool CanStart => Time.unscaledTime >= heldOffUntil && cooldown <= 0f && (remaining <= 0f || successEmitted);
    public event Action<DodgeKind, int> Succeeded;
    /// <summary>A dodge began (kind, lean side): the white air streaks of a plain dodge go with it (DodgeFlash).</summary>
    public event Action<DodgeKind, int> Started;

    /// <summary>No dodge starts for this many real seconds from now (0 lets them start again at once): the counter's dash
    /// is a committed move, so a dodge pressed during it neither moves the body nor opens another slowed window.</summary>
    public void HoldOff(float seconds) { heldOffUntil = Time.unscaledTime + Math.Max(0f, seconds); }

    public bool TryStart(DodgeKind kind, int side, float duration, float reuseDelay)
    {
        if (!CanStart) return false;
        Kind = kind; LeanSide = side; successEmitted = false;
        Activation++;
        remaining = Math.Max(0f, duration);
        cooldown = Math.Max(remaining, reuseDelay);

        // [카메라 연출 추가] 닷지 발동 시 순간적인 FOV 펀치와 방향별 기울임
        // Q/E already moves and rolls the camera through MovementLean. Its defensive
        // micro window must not also add the full step-dodge zoom/rotation impulse.
        var camFx = kind == DodgeKind.Step ? PlayerRoster.Local?.GetComponent<PlayerCameraEffects>() : null;
        if (camFx != null)
        {
            // side: -1(좌), 1(우), 0(후방)
            // 측면 회피는 롤(z축) 회전, 후방 회피는 피치(x축)가 살짝 들리도록 설정
            float rollImpulse = side != 0 ? side * -10f : 0f; // 기존 -4에서 -10으로 증가
            float pitchImpulse = side == 0 ? -5f : 0f;        // 기존 -2.5에서 -5로 증가
            camFx.ApplyCameraImpulse(new Vector3(pitchImpulse, 0f, rollImpulse), 15f); // FOV 8에서 15로 증가
        }

        Started?.Invoke(kind, side);
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
    public void Reset() { remaining = cooldown = 0f; successEmitted = false; heldOffUntil = -1f; }
}
