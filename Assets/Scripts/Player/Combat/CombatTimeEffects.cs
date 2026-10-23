using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared ownership for counter slowdown and tackle hit-stop; never unpauses the game.</summary>
[DisallowMultipleComponent]
public sealed class CombatTimeEffects : MonoBehaviour
{
    private struct Request { public Object owner; public float scale, remaining; }
    private readonly List<Request> requests = new List<Request>();
    private float baseline = 1f, fixedBaseline = .02f, applied = 1f, appliedFixed = .02f;
    private bool ownsTime;
    public void RequestSlowdown(Object owner, float scale, float seconds)
    {
        if (!Application.isPlaying || Time.timeScale <= 0f) return;
        if (!ownsTime) { baseline = Time.timeScale; fixedBaseline = Time.fixedDeltaTime; applied = baseline; ownsTime = true; }
        requests.RemoveAll(item => item.owner == owner);
        requests.Add(new Request { owner = owner, scale = Mathf.Clamp(scale, .01f, 1f), remaining = Mathf.Max(.01f, seconds) });
        Apply();
    }
    public void Release(Object owner) { requests.RemoveAll(item => item.owner == owner); Apply(); }
    private void Update() => Tick(Time.unscaledDeltaTime);
    public void Tick(float dt)
    {
        if (!ownsTime || Time.timeScale <= 0f) return;
        // An external time system took ownership; do not restore our stale value over it.
        if (!Mathf.Approximately(Time.timeScale, applied)) { Relinquish(); return; }
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            var item = requests[i]; item.remaining -= Mathf.Max(0f, dt);
            if (item.owner == null || item.remaining <= 0f) requests.RemoveAt(i); else requests[i] = item;
        }
        Apply();
    }
    private void Apply()
    {
        if (!ownsTime || Time.timeScale <= 0f) return;
        if (!Mathf.Approximately(Time.timeScale, applied)) { Relinquish(); return; }
        float next = baseline;
        foreach (var item in requests) next = Mathf.Min(next, item.scale);
        Time.timeScale = applied = next;
        Time.fixedDeltaTime = fixedBaseline * next / Mathf.Max(.01f, baseline);
        appliedFixed = Time.fixedDeltaTime;
        if (requests.Count == 0) ownsTime = false;
    }
    private void OnDisable()
    {
        requests.Clear();
        if (ownsTime && Time.timeScale <= 0f) Time.fixedDeltaTime = fixedBaseline;
        Apply(); ownsTime = false;
    }
    private void Relinquish()
    {
        if (Mathf.Approximately(Time.fixedDeltaTime, appliedFixed))
            Time.fixedDeltaTime = fixedBaseline;
        requests.Clear(); ownsTime = false;
    }
}
