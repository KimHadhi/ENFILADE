using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>The server list: internet and LAN rooms with their map, players and ping. Pick one, or join by address.</summary>
public sealed class ServerBrowserView
{
    private const float MapWidth = 220f, ReachWidth = 80f, PlayersWidth = 90f, PingWidth = 110f, RowHeight = 40f, DoubleClick = .35f;
    private static readonly Color Selected = new Color(.95f, .63f, .27f, .28f);
    private readonly RectTransform shade, panel, list;
    private readonly TextMeshProUGUI empty, status, joinLabel;
    private IRoomList rooms;
    private string shown = "", chosen;
    private float chosenAt;

    public bool IsOpen => shade.gameObject.activeSelf;

    public ServerBrowserView(Transform canvas)
    {
        panel = W.Window(canvas, "Server Browser", 1120f, out shade);
        W.Size(W.Label(panel, "서버 찾기", 36f, W.Text), height: 54f);
        AddColumns(W.Rect("Header", panel), "방 제목", "맵", "연결", "인원", "핑", W.Muted);

        list = W.Rect("Rooms", panel);
        W.Size(list, height: RowHeight * 8f);
        var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 2f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperLeft;

        empty = W.Label(panel, "찾은 방이 없습니다. 방이 열려 있는지 확인하세요.", 19f, W.Muted);
        W.Size(empty, height: 30f);

        var settings = GameSettings.Current;
        if (GameSession.ParseAddress(settings.lastAddress) == null) settings.lastAddress = GameSession.DefaultAddress;
        W.TextInput(W.Row(panel, "주소로 참가"), settings.lastAddress, address => settings.lastAddress = address);
        status = W.Label(panel, string.Empty, 19f, W.Accent);
        W.Size(status, height: 28f);

        var buttons = W.ButtonRow(panel);
        W.ActionButton(buttons, "새로고침", false, () => rooms?.Refresh());
        W.ActionButton(buttons, "닫기", false, Close);
        var join = W.ActionButton(buttons, "참가", true, ToggleJoin);
        joinLabel = join.GetComponentInChildren<TextMeshProUGUI>();
    }

    public void Open()
    {
        rooms = new CombinedRoomList(new InternetRoomFinder(), new LanRoomFinder());
        chosen = null; shown = "";
        Rebuild();
        shade.gameObject.SetActive(true);
    }

    public void Close()
    {
        shade.gameObject.SetActive(false);
        StopSearching();
    }

    /// <summary>Lets go of the network. Safe while the scene is closing, unlike touching the window.</summary>
    public void StopSearching()
    {
        rooms?.Dispose();
        rooms = null;
    }

    public void Tick()
    {
        if (!IsOpen) return;
        rooms.Tick();
        string now = string.Join("|", rooms.Rooms.Select(Describe));
        if (now != shown) { shown = now; Rebuild(); }
        status.text = GameSession.Status.Length > 0 ? GameSession.Status : rooms.Problem;
        joinLabel.text = GameSession.IsOnline ? "접속 취소" : "참가";
    }

    private static string Describe(RoomInfo room) =>
        $"{room.id},{room.title},{room.map},{room.players},{room.maxPlayers},{room.ping / 10}";

    private void Rebuild()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Object.Destroy(list.GetChild(i).gameObject);
        var found = rooms != null ? rooms.Rooms.OrderBy(room => room.title).ToList() : new List<RoomInfo>();
        foreach (var room in found) AddRow(room);
        if (found.All(room => room.id != chosen)) chosen = null;
        empty.gameObject.SetActive(found.Count == 0);
    }

    private void AddRow(RoomInfo room)
    {
        var row = W.Rect(room.id, list);
        W.Size(row, height: RowHeight);
        var image = W.Fill(row, room.id == chosen ? Selected : W.Surface);
        string ping = room.ping < 0 ? "-" : room.internet ? $"약 {room.ping} ms" : room.ping + " ms";
        AddColumns(row, room.title, MapTable.DisplayName(room.map), room.internet ? "인터넷" : "LAN",
            $"{room.players}/{room.maxPlayers}", ping, W.Text);
        var button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => Choose(room));
    }

    private void Choose(RoomInfo room)
    {
        bool twice = chosen == room.id && Time.unscaledTime - chosenAt < DoubleClick;
        chosen = room.id;
        chosenAt = Time.unscaledTime;
        shown = "";
        if (twice) ToggleJoin();
    }

    private void ToggleJoin()
    {
        if (GameSession.IsOnline) { GameSession.Leave(); return; }
        var room = rooms?.Rooms.FirstOrDefault(found => found.id == chosen);
        if (room != null && room.internet) { GameSession.JoinInternet(room); return; }
        if (room != null) GameSettings.Current.lastAddress = room.address;
        GameSession.Join(GameSettings.Current.lastAddress);
        if (GameSession.IsOnline) GameSettings.Save();
    }

    private static void AddColumns(RectTransform row, string title, string map, string reach, string players, string ping, Color color)
    {
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f; layout.padding = new RectOffset(12, 12, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        // The row's own layout asks to stretch, so it has to be told it stays one line high.
        W.Size(row, height: RowHeight).flexibleHeight = 0f;
        W.Size(W.Label(row, title, 20f, color), flexibleWidth: 1f);
        W.Size(W.Label(row, map, 20f, color), width: MapWidth);
        W.Size(W.Label(row, reach, 20f, color), width: ReachWidth);
        W.Size(W.Label(row, players, 20f, color), width: PlayersWidth);
        W.Size(W.Label(row, ping, 20f, color), width: PingWidth);
    }
}
