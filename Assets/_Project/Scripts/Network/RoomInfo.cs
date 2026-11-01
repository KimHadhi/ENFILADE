using System;
using System.Collections.Generic;

/// <summary>What the server list shows about one room. The serialized fields travel in a room's LAN reply.</summary>
[Serializable]
public sealed class RoomInfo
{
    /// <summary>Tells rooms apart: one PC answers from several addresses.</summary>
    public string id;
    public string title, map;
    public int players, maxPlayers;
    /// <summary>Where to join: an address on the LAN.</summary>
    [NonSerialized] public string address;
    /// <summary>Round trip in ms, or below zero when it can't be measured before joining.</summary>
    [NonSerialized] public int ping = -1;
    [NonSerialized] public float lastSeen;
}

/// <summary>A live list of rooms the server browser shows, wherever they were found.</summary>
public interface IRoomList : IDisposable
{
    IEnumerable<RoomInfo> Rooms { get; }
    void Tick();
    void Refresh();
}
