using System;
using System.Linq;
using UnityEngine;
using W = SettingsWidgets;

/// <summary>The window for making a room: its title, how many can join and who can find it. The map comes from the main menu.</summary>
public sealed class CreateRoomView
{
    private const int TitleLimit = 32;
    private static readonly int[] Sizes = { 2, 4, 6, 8, 10 };
    private static string[] Reaches => new[] { Texts.Get("room.create.reach.internet"), Texts.Get("room.create.reach.lan") };
    private readonly RectTransform shade, panel;
    private readonly Action<string, int, bool> create;

    public CreateRoomView(Transform canvas, Action<string, int, bool> create)
    {
        this.create = create;
        panel = W.Window(canvas, "Create Room", 700f, out shade);
    }

    /// <summary>Showing rebuilds the window, so it reflects the map chosen just before.</summary>
    public void Show(string map)
    {
        W.ClearContent(panel);
        var settings = GameSettings.Current;
        if (string.IsNullOrWhiteSpace(settings.roomTitle)) settings.roomTitle = Texts.Format("room.create.default_title", settings.DisplayName);
        int size =Mathf.Max(0, Array.IndexOf(Sizes, settings.maxPlayers));

        W.Size(W.Label(panel, Texts.Get("room.create.title"), 36f, W.Text), height: 54f);
        W.TextInput(W.Row(panel, Texts.Get("room.create.name")), settings.roomTitle, title => settings.roomTitle = title).characterLimit = TitleLimit;
        W.Choice(W.Row(panel, Texts.Get("room.create.max_players")), Sizes.Select(n => Texts.Format("room.create.players", n)).ToArray(), size,
            i => settings.maxPlayers = Sizes[i]);
        W.Choice(W.Row(panel, Texts.Get("room.create.reach")), Reaches, settings.roomOnInternet ? 0 : 1, i => settings.roomOnInternet = i == 0);
        W.Size(W.Label(panel, Texts.Format("room.create.map", MapTable.DisplayName(map)), 20f, W.Muted), height: 34f);
        var buttons = W.ButtonRow(panel);
        W.ActionButton(buttons, Texts.Get("common.cancel"), false, Hide);
        W.ActionButton(buttons, Texts.Get("room.create.confirm"), true, Confirm);
        shade.gameObject.SetActive(true);
    }

    public bool IsOpen => shade.gameObject.activeSelf;

    public void Close() => Hide();

    private void Hide() => shade.gameObject.SetActive(false);

    private void Confirm()
    {
        var settings = GameSettings.Current;
        settings.roomTitle = settings.roomTitle.Trim();
        if (settings.roomTitle.Length == 0) settings.roomTitle = Texts.Format("room.create.default_title", settings.DisplayName);
        settings.maxPlayers = Sizes.Contains(settings.maxPlayers) ? settings.maxPlayers : 4;
        Hide();
        create(settings.roomTitle, settings.maxPlayers, settings.roomOnInternet);
    }
}
