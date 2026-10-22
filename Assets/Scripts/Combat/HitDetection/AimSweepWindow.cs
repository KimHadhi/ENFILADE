using System;
using UnityEngine;

/// <summary>Samples the moving aim throughout an attack, including the arc between rendered frames.</summary>
public sealed class AimSweepWindow
{
    private readonly Action<Vector3, Vector3> sample;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private float remaining;
    public bool IsActive => remaining > 0f;
    public AimSweepWindow(Action<Vector3, Vector3> sample) { this.sample = sample; }
    public void Begin(Transform aim, float duration)
    {
        previousPosition = aim.position; previousRotation = aim.rotation;
        remaining = Mathf.Max(.001f, duration);
        sample(previousPosition, previousRotation * Vector3.forward);
    }
    public void Tick(float dt, Transform aim)
    {
        if (!IsActive || aim == null || dt < 0f) return;
        // Only trace the part of this frame inside the active window, even after a long frame.
        float fraction = dt > 0f ? Mathf.Min(1f, remaining / dt) : 1f;
        Vector3 position = Vector3.Lerp(previousPosition, aim.position, fraction);
        Quaternion rotation = Quaternion.Slerp(previousRotation, aim.rotation, fraction);
        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(
            Vector3.Distance(previousPosition, position) / .08f,
            Quaternion.Angle(previousRotation, rotation) / 5f)), 1, 96);
        for (int i = 1; i <= steps && IsActive; i++)
        {
            float t = (float)i / steps;
            sample(Vector3.Lerp(previousPosition, position, t),
                Quaternion.Slerp(previousRotation, rotation, t) * Vector3.forward);
        }
        previousPosition = position; previousRotation = rotation;
        remaining = Mathf.Max(0f, remaining - dt);
    }
    public void Cancel() { remaining = 0f; }
}
