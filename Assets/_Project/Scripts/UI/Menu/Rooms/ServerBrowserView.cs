using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The server list, in the menus' kit look (2026-11-09): "서버 찾기" tilted with how many rooms are open beside it, a
/// spinner while it looks, 새로고침 and 닫기 at the top right; each room a slanted card (the map's picture, the room's
/// title with 인터넷 or LAN under it, the map, the players, the ping as signal bars and a number), the chosen one dark
/// with a thin pink edge; along the bottom, joining by address and the pink 참가 (Enter). A room pressed twice is joined
/// at once.
/// </summary>
public sealed class ServerBrowserView
{
    private const float Width = 1240f, RowHeight = 76f, RowGap = 8f, ThumbWidth = 112f, ThumbHeight = 63f, MapWidth = 250f,
        PlayersWidth = 90f, PingWidth = 130f, Slant = 12f, DoubleClick = .35f;
    private const int RowsShown = 6;
    private static string NoRooms => Texts.Get("servers.empty");
    private static string Looking => Texts.Get("servers.looking");
    private static readonly Color CardClear = KitStyle.With(KitStyle.Ice, .05f), CardDark = KitStyle.With(KitStyle.Night, .92f),
        CardGrey = KitStyle.With(KitStyle.Ice, .3f);
    private readonly RectTransform shade, panel, list;
    private readonly TextMeshProUGUI empty, status, joinLabel, count;
    private readonly BusySpinner spinner;
    private IRoomList rooms;
    private string shown = "", chosen;
    private float chosenAt;

    public bool IsOpen => shade.gameObject.activeSelf;

    public ServerBrowserView(Transform canvas)
    {
        panel = W.Window(canvas, "Server Browser", Width, out shade);

        // The title row: the name tilted, how many rooms are open, the spinner, and the tools at the right.
        var head = W.Rect("Head", panel);
        W.Size(head, height: 64f);
        var title = KitStyle.TopLeft("Title", head, Vector2.zero, new Vector2(300f, 64f));
        title.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(title, Texts.Get("servers.title"), 44f, TextAlignmentOptions.MidlineLeft);
        count = KitStyle.Flat(KitStyle.TopLeft("Count", head, new Vector2(230f, 20f), new Vector2(240f, 32f)), "", 18f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        spinner = new BusySpinner(head, 26f, W.Accent);
        spinner.Rect.anchorMin = spinner.Rect.anchorMax = new Vector2(0f, .5f);
        spinner.Rect.anchoredPosition = new Vector2(470f, 0f);
        var tools = KitStyle.TopLeft("Tools", head, new Vector2(Width - 88f - 330f, 10f), new Vector2(330f, 44f));
        var toolRow = tools.gameObject.AddComponent<HorizontalLayoutGroup>();
        toolRow.spacing = 10f;
        toolRow.childAlignment = TextAnchor.MiddleRight;
        toolRow.childControlWidth = toolRow.childControlHeight = true;
        toolRow.childForceExpandWidth = toolRow.childForceExpandHeight = false;
        W.Size(W.ActionButton(tools, Texts.Get("servers.refresh"), false, () => rooms?.Refresh()), 150f, 42f);
        W.Size(W.ActionButton(tools, Texts.Get("common.close"), false, Close), 150f, 42f);

        var header = W.Rect("Header", panel);
        Columns(header, "", Texts.Get("servers.column.title"), Texts.Get("servers.column.map"), Texts.Get("servers.column.players"),
            Texts.Get("servers.column.ping"), 15f, W.Muted, 24f);

        var scroll = W.Rect("List", panel);
        W.Size(scroll, height: RowsShown * (RowHeight + RowGap));
        var scrollLayout = scroll.gameObject.AddComponent<VerticalLayoutGroup>();
        scrollLayout.childControlWidth = scrollLayout.childControlHeight = true;
        scrollLayout.childForceExpandWidth = scrollLayout.childForceExpandHeight = true;
        list = W.ScrollArea(scroll);
        list.GetComponent<VerticalLayoutGroup>().spacing = RowGap;

        empty = W.Label(panel, NoRooms, 19f, W.Muted);
        W.Size(empty, height: 30f);
        status = W.Label(panel, string.Empty, 18f, W.Accent);
        W.Size(status, height: 26f);

        // Along the bottom: joining by address, the hint, and 참가.
        var bottom = W.Rect("Bottom", panel);
        W.Size(bottom, height: 56f);
        var bottomRow = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
        bottomRow.spacing = 14f;
        bottomRow.childAlignment = TextAnchor.MiddleLeft;
        bottomRow.childControlWidth = bottomRow.childControlHeight = true;
        bottomRow.childForceExpandWidth = false;
        bottomRow.childForceExpandHeight = false;
        W.Size(W.Label(bottom, Texts.Get("servers.address"), 19f, W.Muted), width: 120f, height: 40f);
        var settings = GameSettings.Current;
        if (GameSession.ParseAddress(settings.lastAddress) == null) settings.lastAddress = GameSession.DefaultAddress;
        var address = W.TextInput(bottom, settings.lastAddress, typed => settings.lastAddress = typed);
        W.Size(address, width: 280f, height: 38f, flexibleWidth: 0f);
        W.Size(W.Rect("Gap", bottom), flexibleWidth: 1f);
        var hint = W.Label(bottom, Texts.Get("servers.hint"), 16f, W.Muted, TextAlignmentOptions.MidlineRight);
        W.Size(hint, width: 220f, height: 40f);
        var join = W.ActionButton(bottom, Texts.Get("servers.join"), true, ToggleJoin);
        W.Size(join, 210f, 50f);
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
        bool looking = rooms.Searching;
        spinner.Show(looking);
        spinner.Tick(Time.unscaledTime);
        int open = rooms.Rooms.Count();
        count.text = open > 0 ? Texts.Format("servers.count", open) : looking ? Texts.Get("servers.count.looking") : "";
        empty.text = looking ? Looking : NoRooms;
        joinLabel.text = Texts.Get(GameSession.IsOnline ? "servers.join.cancel" : "servers.join");
        JoinByKey();
    }

    // Enter joins the chosen room (or the address) unless the address box has the keyboard.
    private void JoinByKey()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || (!keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame)) return;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponent<TMP_InputField>() != null) return;
        ToggleJoin();
    }

    private static string Describe(RoomInfo room) =>
        $"{room.id},{room.title},{room.map},{room.players},{room.maxPlayers},{room.ping / 10}";

    private void Rebuild()
    {
        for (int i = list.childCount - 1; i >= 0; i--) Object.Destroy(list.GetChild(i).gameObject);
        var found = rooms != null ? rooms.Rooms.OrderBy(room => room.title).ToList() : new List<RoomInfo>();
        if (found.All(room => room.id != chosen)) chosen = null;
        foreach (var room in found) AddRow(room);
        empty.gameObject.SetActive(found.Count == 0);
    }

    // One room as a slanted card.
    private void AddRow(RoomInfo room)
    {
        bool on = room.id == chosen;
        var row = W.Rect(room.id, list);
        W.Size(row, height: RowHeight).flexibleHeight = 0f;
        Behind(row, "Edge", 2f, on ? KitStyle.With(KitStyle.Accent, .7f) : Color.clear);
        var plate = Behind(row, "Plate", 0f, on ? CardDark : CardClear);
        plate.raycastTarget = true;
        var bar = KitStyle.Bar("Bar", row, Vector2.zero, new Vector2(4f, RowHeight), on ? KitStyle.Accent : CardGrey);
        KitSkew.On(bar, Slant);
        bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.padding = new RectOffset(18, 22, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        Thumb(row, room.map);
        string grey = "#" + ColorUtility.ToHtmlStringRGB(W.Muted);
        string reach = Texts.Get(room.internet ? "servers.internet" : "servers.lan");
        var title = W.Label(row, $"<b>{Escape(room.title)}</b>\n<size=13><color={grey}>{reach}</color></size>", 20f, W.Text);
        title.lineSpacing = -6f;
        W.Size(title, height: RowHeight, flexibleWidth: 1f);
        W.Size(W.Label(row, MapTable.DisplayName(room.map), 18f, W.Muted), width: MapWidth, height: RowHeight);
        W.Size(W.Label(row, $"<b>{room.players}</b><color={grey}> / {room.maxPlayers}</color>", 19f, W.Text), width: PlayersWidth, height: RowHeight);
        Ping(row, room.ping);

        var button = row.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = plate;
        button.onClick.AddListener(() => Choose(room));
    }

    // A slanted fill behind the row's contents, out of its layout, <paramref name="grow"/> px bigger all round.
    private static Image Behind(RectTransform row, string name, float grow, Color color)
    {
        var rect = W.Rect(name, row);
        KitStyle.Stretch(rect);
        rect.offsetMin = new Vector2(-grow * .5f, -grow * .5f);
        rect.offsetMax = new Vector2(grow * .5f, grow * .5f);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var image = KitStyle.Fill(rect, color);
        KitSkew.On(image, Slant);
        return image;
    }

    // The map's picture (or its icon), cut to the card's height.
    private static void Thumb(RectTransform row, string map)
    {
        var frame = W.Rect("Thumb", row);
        W.Size(frame, width: ThumbWidth, height: ThumbHeight);
        KitStyle.Fill(frame, KitStyle.Card);
        frame.gameObject.AddComponent<RectMask2D>();
        var picture = MapTable.Picture(map);
        if (picture != null)
        {
            var image = W.Rect("Picture", frame);
            KitStyle.Stretch(image);
            var raw = image.gameObject.AddComponent<RawImage>();
            raw.texture = picture;
            raw.raycastTarget = false;
            // Fill the card's shape from the middle of the picture.
            float aspect = (float)picture.width / Mathf.Max(1, picture.height), want = ThumbWidth / ThumbHeight;
            raw.uvRect = aspect > want ? new Rect((1f - want / aspect) * .5f, 0f, want / aspect, 1f) : new Rect(0f, (1f - aspect / want) * .5f, 1f, aspect / want);
            return;
        }
        KitButtons.IconImage(frame, "Map", 28f, KitStyle.Faint);
    }

    // Four signal bars, more lit the better the ping, then the number.
    private static void Ping(RectTransform row, int measured)
    {
        var box = W.Rect("Ping", row);
        W.Size(box, width: PingWidth, height: RowHeight);
        int shown = measured < 0 ? -1 : PingDisplay.Shown(measured);
        int lit = shown < 0 ? 0 : shown < 40 ? 4 : shown < 80 ? 3 : shown < 150 ? 2 : 1;
        for (int i = 0; i < 4; i++)
        {
            float height = 6f + i * 4f;
            var bar = KitStyle.TopLeft("Bar", box, new Vector2(i * 7f, RowHeight * .5f + 9f - height), new Vector2(4f, height));
            KitStyle.Fill(bar, i < lit ? KitStyle.Text : KitStyle.With(KitStyle.Ice, .2f));
        }
        var number = KitStyle.TopLeft("Number", box, new Vector2(38f, 0f), new Vector2(PingWidth - 38f, RowHeight));
        KitStyle.Flat(number, shown < 0 ? "-" : shown + " ms", 16f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
    }

    // Room titles are typed by players: keep them from turning into rich-text tags.
    private static string Escape(string text) => (text ?? "").Replace("<", "<​");

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

    // Column titles over the cards, lined up with what the cards hold.
    private static void Columns(RectTransform row, string thumb, string title, string map, string players, string ping, float size, Color color,
        float height)
    {
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.padding = new RectOffset(18, 38, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        W.Size(row, height: height).flexibleHeight = 0f;
        W.Size(W.Label(row, thumb, size, color), width: ThumbWidth);
        W.Size(W.Label(row, title, size, color), flexibleWidth: 1f);
        W.Size(W.Label(row, map, size, color), width: MapWidth);
        W.Size(W.Label(row, players, size, color), width: PlayersWidth);
        W.Size(W.Label(row, ping, size, color), width: PingWidth);
    }
}
