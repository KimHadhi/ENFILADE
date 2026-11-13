using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The counter's slow motion and the hit stops: each is asked for under its owner's name for some real seconds, and the
/// slowest one asked for is in force. Alone it slows the world, as a hold on WorldTime: it sits under a pause and beside
/// the kill streak's slow motion without any of them undoing another. A room's world can't slow for one player, so there
/// time slows only for this player and the enemy they're up against, as in Ghost of Tsushima: Legends: anyone else sees
/// those two caught in slow time and keeps their own. Pace is this player's share of it; the enemy's runs in EnemyNetwork.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatTimeEffects : MonoBehaviour
{
    private struct Request { public Object owner; public float scale, remaining; public EnemyNetwork partner; }
    // Shorter than this, a hit stop would be over before the other PCs heard of it.
    private const float SharedFrom = .15f;
    private readonly List<Request> requests = new List<Request>();
    private PlayerBody body;

    /// <summary>How fast this player's own time runs in a room. Alone it stays 1 and the world slows instead.</summary>
    public float Pace { get; private set; } = 1f;

    /// <summary>partner is who the slowdown is shared with in a room: the enemy this player is up against.</summary>
    public void RequestSlowdown(Object owner, float scale, float seconds, Component partner = null)
    {
        if (!Application.isPlaying || GamePause.IsPaused) return;
        requests.RemoveAll(item => item.owner == owner);
        scale = Mathf.Clamp(scale, .01f, 1f);
        EnemyNetwork shared = null;
        if (GameSession.IsOnline && partner != null && seconds >= SharedFrom && partner.TryGetComponent(out shared)) shared.ShareSlow(scale, seconds);
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

    private void Update()
    {
        // The seconds asked for are real ones, but they wait out a pause.
        if (requests.Count == 0 || GamePause.IsPaused) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            var item = requests[i];
            item.remaining -= dt;
            if (item.owner == null || item.remaining <= 0f) requests.RemoveAt(i);
            else requests[i] = item;
        }
        Apply();
    }

    private void Apply()
    {
        float slowest = 1f;
        foreach (var item in requests) slowest = Mathf.Min(slowest, item.scale);
        bool room = GameSession.IsOnline;
        SetPace(room ? slowest : 1f);
        if (!room && requests.Count > 0) WorldTime.Hold(this, slowest);
        else WorldTime.Release(this);
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
        Apply();
    }
}
