using UnityEngine;

/// <summary>Exact critically damped impulse response. No overshooting integration on long frames.</summary>
public sealed class RecoilSpring
{
    public Vector3 Value { get; private set; }
    public Vector3 Velocity { get; private set; }
    public void Kick(Vector3 peak, float response)
    {
        // An isolated impulse peaks at t = 1 / response with approximately the requested amplitude.
        Velocity += peak * (Mathf.Exp(1f) * Mathf.Max(1f, response));
    }
    public void Tick(float dt, float response)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        float omega = Mathf.Max(1f, response);
        Vector3 spring = Velocity + omega * Value;
        float decay = Mathf.Exp(-omega * dt);
        Value = (Value + spring * dt) * decay;
        Velocity = (Velocity - omega * spring * dt) * decay;
        if (Value.sqrMagnitude < 1e-10f && Velocity.sqrMagnitude < 1e-8f) Reset();
    }
    public void Reset() { Value = Velocity = Vector3.zero; }
}
