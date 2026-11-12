using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared ownership for counter slowdown and dash push hit-stop; never unpauses the game. Alone it slows the world. A
/// room's world can't slow for one player, so there time slows only for this player and the enemy they're up against,
/// as in Ghost of Tsushima: Legends: anyone else sees those two caught in slow time and keeps their own. Pace is this
/// player's share of it; the enemy's runs in EnemyNetwork.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatTimeEffects : MonoBehaviour
{
    private struct Request { public Object owner; public float scale, remaining; public EnemyNetwork partner; }
    // Shorter than this, a hit stop would be over before the other PCs heard of it.
    private const float SharedFrom = .15f;
    private readonly List<Request> requests = new List<Request>();
    private float baseline = 1f, fixedBaseline = .02f, applied = 1f, appliedFixed = .02f;
    private bool ownsTime;
    private PlayerBody body;

    /// <summary>How fast this player's own time runs in a room. Alone it stays 1 and the world slows instead.</summary>
    public float Pace { get; private set; } = 1f;

    /// <summary>partner is who the slowdown is shared with in a room: the enemy this player is up against.</summary>
    public void RequestSlowdown(Object owner, float scale, float seconds, Component partner = null)
    {
        if (!Application.isPlaying || Time.timeScale <= 0f) return;
        bool room = GameSession.IsOnline;
        if (!room && !ownsTime) { baseline = Time.timeScale; fixedBaseline = Time.fixedDeltaTime; applied = baseline; ownsTime = true; }
        requests.RemoveAll(item => item.owner == owner);
        scale = Mathf.Clamp(scale, .01f, 1f);
        EnemyNetwork shared = null;
        if (room && partner != null && seconds >= SharedFrom && partner.TryGetComponent(out shared)) shared.ShareSlow(scale, seconds);
        requests.Add(new Request { owner = owner, scale = scale, remaining = Mathf.Max(.01f, seconds), partner = shared });
        Apply();
    }
    public void Release(Object owner)
    {
        foreach (var item in requests)
            if (item.owner == owner && item.partner != null) item.partner.ShareSlow(1f, 0f);
        requests.RemoveAll(item => item.owner == owner);
        Apply();
    }
    private void Update() => Tick(Time.unscaledDeltaTime);
    public void Tick(float dt)
    {
        if ((!ownsTime && requests.Count == 0) || Time.timeScale <= 0f) return;
        // An external time system took ownership; do not restore our stale value over it.
        if (ownsTime && !Mathf.Approximately(Time.timeScale, applied)) { Relinquish(); return; }
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            var item = requests[i]; item.remaining -= Mathf.Max(0f, dt);
            if (item.owner == null || item.remaining <= 0f) requests.RemoveAt(i); else requests[i] = item;
        }
        Apply();
    }
    private void Apply()
    {
        if (GameSession.IsOnline) { SetPace(Slowest(1f)); return; }
        if (!ownsTime || Time.timeScale <= 0f) return;
        if (!Mathf.Approximately(Time.timeScale, applied)) { Relinquish(); return; }
        float next = Slowest(baseline);
        Time.timeScale = applied = next;
        Time.fixedDeltaTime = fixedBaseline * next / Mathf.Max(.01f, baseline);
        appliedFixed = Time.fixedDeltaTime;
        if (requests.Count == 0) ownsTime = false;
    }
    private float Slowest(float start)
    {
        foreach (var item in requests) start = Mathf.Min(start, item.scale);
        return start;
    }
    private void SetPace(float pace)
    {
        if (Mathf.Approximately(pace, Pace)) return;
        Pace = pace;
        if (body == null) body = GetComponent<PlayerBody>();
        if (body != null) body.SetPace(pace);
    }
    private void OnDisable()
    {
        requests.Clear();
        SetPace(1f);
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
