using System;
using System.Linq;
using UnityEngine;
using W = SettingsWidgets;

/// <summary>The window for making a room: its title and how many can join. The map comes from the main menu.</summary>
public sealed class CreateRoomView
{
    private const int TitleLimit = 32;
    private static readonly int[] Sizes = { 2, 4, 6, 8, 10 };
    private readonly RectTransform shade, panel;
    private readonly Action<string, int> create;

    public CreateRoomView(Transform canvas, Action<string, int> create)
    {
        this.create = create;
        panel = W.Window(canvas, "Create Room", 700f, out shade);
    }

    /// <summary>Showing rebuilds the window, so it reflects the map chosen just before.</summary>
    public void Show(string map)
    {
        for (int i = panel.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);
        var settings = GameSettings.Current;
        if (string.IsNullOrWhiteSpace(settings.roomTitle)) settings.roomTitle = Environment.UserName + "의 방";
        int size = Mathf.Max(0, Array.IndexOf(Sizes, settings.maxPlayers));

        W.Size(W.Label(panel, "방 만들기", 36f, W.Text), height: 54f);
        W.TextInput(W.Row(panel, "방 제목"), settings.roomTitle, title => settings.roomTitle = title).characterLimit = TitleLimit;
        W.Choice(W.Row(panel, "최대 인원"), Sizes.Select(n => n + "명").ToArray(), size, i => settings.maxPlayers = Sizes[i]);
        W.Size(W.Label(panel, "맵: " + MapTable.DisplayName(map), 20f, W.Muted), height: 34f);
        var buttons = W.ButtonRow(panel);
        W.ActionButton(buttons, "취소", false, Hide);
        W.ActionButton(buttons, "만들기", true, Confirm);
        shade.gameObject.SetActive(true);
    }

    private void Hide() => shade.gameObject.SetActive(false);

    private void Confirm()
    {
        var settings = GameSettings.Current;
        settings.roomTitle = settings.roomTitle.Trim();
        if (settings.roomTitle.Length == 0) settings.roomTitle = Environment.UserName + "의 방";
        settings.maxPlayers = Sizes.Contains(settings.maxPlayers) ? settings.maxPlayers : 4;
        Hide();
        create(settings.roomTitle, settings.maxPlayers);
    }
}
