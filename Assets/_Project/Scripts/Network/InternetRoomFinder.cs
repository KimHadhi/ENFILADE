using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Asks Unity's lobby which internet rooms exist. A relay hides the host, so the ping is an estimate: this PC's round
/// trip to the room's relay region plus the host's.
/// </summary>
public sealed class InternetRoomFinder : IRoomList
{
    // The lobby service allows one query a second per player.
    private const float AskInterval = 5f, QuickestAsk = 1f;
    private List<RoomInfo> rooms = new List<RoomInfo>();
    private Dictionary<string, int> regions;
    private float nextAsk;
    private bool asking, closed, answered, wanted;

    public IEnumerable<RoomInfo> Rooms => rooms;
    public string Problem { get; private set; } = "";
    // The background asks every few seconds don't count; the first one and a refresh do.
    public bool Searching => !answered || wanted;

    public void Refresh()
    {
        nextAsk = Mathf.Min(nextAsk, Time.unscaledTime + QuickestAsk);
        wanted = true;
    }

    public void Tick()
    {
        if (!asking && Time.unscaledTime >= nextAsk) Ask();
    }

    public void Dispose() => closed = true;

    private async void Ask()
    {
        asking = true;
        bool asked = wanted;
        try
        {
            await OnlineServices.SignIn();
            if (regions == null) regions = (await OnlineServices.MeasureRelayRegions()).ToDictionary(region => region.Region, region => region.AverageLatencyMs);
            var found = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
            if (closed) return;
            rooms = found.Sessions.Select(Describe).ToList();
            Problem = "";
        }
        catch (Exception e) when (OnlineServices.IsServiceError(e))
        {
            if (closed) return;
            if (Problem.Length == 0) Debug.LogWarning($"[InternetRoomFinder] {e.Message}");
            rooms = new List<RoomInfo>();
            Problem = Texts.Get("net.rooms.failed");
        }
        finally
        {
            asking = false;
            answered = true;
            if (asked) wanted = false;
            nextAsk = Time.unscaledTime + AskInterval;
        }
    }

    private RoomInfo Describe(ISessionInfo session)
    {
        var room = new RoomInfo
        {
            id = session.Id, title = session.Name, internet = true,
            maxPlayers = session.MaxPlayers, players = session.MaxPlayers - session.AvailableSlots
        };
        var properties = session.Properties;
        if (properties == null) return room;
        if (properties.TryGetValue(InternetRoom.MapKey, out var map)) room.map = map.Value;
        if (properties.TryGetValue(InternetRoom.RegionKey, out var region) && regions != null && regions.TryGetValue(region.Value, out int mine)
            && properties.TryGetValue(InternetRoom.LatencyKey, out var host) && int.TryParse(host.Value, out int theirs))
            room.ping = mine + theirs;
        return room;
    }
}
