using System;
using UnityEngine; // 추가됨

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

        // [카메라 연출 추가] 닷지 발동 시 순간적인 FOV 펀치와 방향별 기울임
        var camFx = UnityEngine.Object.FindAnyObjectByType<PlayerCameraEffects>();
        if (camFx != null)
        {
            // side: -1(좌), 1(우), 0(후방)
            // 측면 회피는 롤(z축) 회전, 후방 회피는 피치(x축)가 살짝 들리도록 설정
            float rollImpulse = side != 0 ? side * -10f : 0f; // 기존 -4에서 -10으로 증가
            float pitchImpulse = side == 0 ? -5f : 0f;        // 기존 -2.5에서 -5로 증가
            camFx.ApplyCameraImpulse(new Vector3(pitchImpulse, 0f, rollImpulse), 15f); // FOV 8에서 15로 증가
        }

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