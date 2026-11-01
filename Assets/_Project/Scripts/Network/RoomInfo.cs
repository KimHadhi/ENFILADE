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
    /// <summary>Where to join a LAN room. An internet room is joined by its id.</summary>
    [NonSerialized] public string address;
    [NonSerialized] public bool internet;
    /// <summary>Round trip in ms, estimated for an internet room, or below zero when unknown.</summary>
    [NonSerialized] public int ping = -1;
    [NonSerialized] public float lastSeen;
}

/// <summary>A live list of rooms the server browser shows, wherever they were found.</summary>
public interface IRoomList : IDisposable
{
    IEnumerable<RoomInfo> Rooms { get; }
    /// <summary>Why the list may be missing rooms, or empty when it's fine.</summary>
    string Problem { get; }
    void Tick();
    void Refresh();
}
