using System;
using UnityEngine;
using W = SettingsWidgets;

/// <summary>
/// Asked before joining a modded room (the user, 2026-11-21: "게스트 입장에선 모딩된 서버라고 들어가기 전에 알려 줘서 유저가
/// 예 아니오로 접속할 수 있게"): which mods change the game's numbers there (the host's, RoomTables) and that the
/// player's own mods' tables are left out while there. Yes joins, No stays. Over the server browser.
/// </summary>
public sealed class ModdedServerView
{
    private readonly RectTransform shade, panel;
    private Action yes;

    public ModdedServerView(Transform canvas) => panel = W.Window(canvas, "Modded Server", 640f, out shade);

    public bool IsOpen => shade.gameObject.activeSelf;

    /// <summary>Asks about a room whose host plays with these mods (their names, comma-joined); yes joins.</summary>
    public void Ask(string roomTitle, string mods, Action join)
    {
        yes = join;
        W.ClearContent(panel);
        W.Size(W.Label(panel, Texts.Get("servers.modded.title"), 32f, W.Text), height: 50f);
        string what = string.IsNullOrEmpty(roomTitle) ? "" : "<b>" + roomTitle.Replace("<", "<​") + "</b>\n";
        var body = W.Label(panel, what + Texts.Format("servers.modded.body", (mods ?? "").Replace("<", "<​")), 19f, W.Muted);
        body.enableWordWrapping = true;
        W.Size(body, height: 150f);
        var buttons = W.ButtonRow(panel);
        W.ActionButton(buttons, Texts.Get("common.no"), false, Close);
        W.ActionButton(buttons, Texts.Get("common.yes"), true, Yes);
        shade.gameObject.SetActive(true);
    }

    public void Close() => shade.gameObject.SetActive(false);

    private void Yes()
    {
        Close();
        var join = yes;
        yes = null;
        join?.Invoke();
    }
}
