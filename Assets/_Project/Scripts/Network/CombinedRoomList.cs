using System.Collections.Generic;
using System.Linq;

/// <summary>Several ways of finding rooms, shown as one list.</summary>
public sealed class CombinedRoomList : IRoomList
{
    private readonly IRoomList[] lists;

    public CombinedRoomList(params IRoomList[] lists) => this.lists = lists;

    public IEnumerable<RoomInfo> Rooms => lists.SelectMany(list => list.Rooms);
    public string Problem => lists.Select(list => list.Problem).FirstOrDefault(problem => !string.IsNullOrEmpty(problem)) ?? "";

    public void Tick() { foreach (var list in lists) list.Tick(); }
    public void Refresh() { foreach (var list in lists) list.Refresh(); }
    public void Dispose() { foreach (var list in lists) list.Dispose(); }
}
