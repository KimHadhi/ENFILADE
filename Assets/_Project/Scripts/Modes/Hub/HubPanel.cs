using HudAddons;
using UnityEngine;

/// <summary>
/// The hub's corner of the HUD (memo 46): the chapter the party will set off to (its picture, its number, its name), and,
/// once everyone is ready, the count to setting off, big in a lit box. At the top right, in the player HUD's parts
/// (HudSheet), upright and moving with its sway (the user, 2026-11-22: "맵 선택이나 다 우리 hud랑 통일성 있게 리디자인"; it
/// was a leaning dark card with a pink rail and number). Who is ready is the objective line's (HubLobby) and the board's
/// over the way out.
/// </summary>
[DisallowMultipleComponent]
public sealed class HubPanel : MonoBehaviour
{
    // Under the money and the feed's first rows at the top right.
    private const float Wide = 432f, Frame = 5f, PictureWide = 160f, PictureTall = 90f, Edge = 48f, Top = 190f, Pad = 14f,
        SmallCell = 14.4f, TitleCell = 24f, NumberCell = 16.8f, CountCell = 64f, FlashSeconds = .3f;
    private const int Order = 28;

    private HudSheet sheet;
    private Texture picture;
    private string shownChapter, number = "", title = "", count = "", caption = "", hint = "";
    private int shownCount = int.MinValue;
    private float changedAt = -99f, countedAt = -99f;

    public static HubPanel Make()
    {
        var sheet = new HudSheet("Hub Panel", Order);
        var panel = sheet.Overlay.gameObject.AddComponent<HubPanel>();
        panel.sheet = sheet;
        return panel;
    }

    /// <summary>This frame's chapter and count (negative: not counting).</summary>
    public void Show(string chapter, float countdown)
    {
        if (chapter != shownChapter)
        {
            shownChapter = chapter;
            HubDisplays.SplitChapter(MapTable.DisplayName(chapter), out number, out title);
            picture = MapTable.Picture(chapter);
            caption = Texts.Get("hub.panel.next");
            changedAt = Smooth.Now;
        }
        int now = countdown >= 0f ? Mathf.CeilToInt(countdown) : -1;
        if (now == shownCount) return;
        shownCount = now;
        count = now >= 0 ? now.ToString() : "";
        hint = now >= 0 ? Texts.Get("hub.panel.going") : Texts.Get(GameSession.IsOnline ? "hub.panel.hint.room" : "hub.panel.hint.solo");
        if (now >= 0) countedAt = Smooth.Now;
    }

    private void LateUpdate()
    {
        if (sheet == null) return;
        bool show = shownChapter != null && PlayerRoster.Local != null && !ResultsShow.IsPlaying && !HubDressing.IsOpen && !LoadingScreen.Visible;
        sheet.Visible = show;
        if (!show)
        {
            HudRoom.Free(this);
            return;
        }
        float tall = PictureTall + Frame * 2f;
        Vector2 sway = HudSheet.Sway;
        float x = sheet.Width - Edge - Wide + sway.x, y = Top + sway.y;
        sheet.Begin();
        sheet.Box(x, y, Wide, tall);
        if (picture != null) sheet.Picture(picture, x + Frame, y + Frame, PictureWide, PictureTall, HudSheet.White);
        else sheet.Fill(x + Frame, y + Frame, PictureWide, PictureTall, new Color32(255, 255, 255, 20));

        bool counting = shownCount >= 0;
        float left = x + Frame + PictureWide + Pad, right = x + Wide - Pad;
        if (counting) right -= tall;
        sheet.Text(caption, SmallCell, left, y + 22f, sheet.Grey, TextAlign.Left, TextAlign.Center, true);
        // The chapter's number on a small lit plate, then its name.
        float numberWide = sheet.Measure(number, NumberCell, true) + 12f, nameY = y + tall * .5f;
        sheet.Box(left, nameY - 11f, numberWide, 22f, HudSheet.Lit, 4f);
        sheet.Plain(number, NumberCell, left + numberWide * .5f, nameY, HudSheet.Ink, TextAlign.Center, TextAlign.Center, true);
        float nameLeft = left + numberWide + 8f;
        sheet.Text(sheet.Fit(title, TitleCell, right - nameLeft, true), TitleCell, nameLeft, nameY, HudSheet.White, TextAlign.Left, TextAlign.Center, true);
        sheet.Text(sheet.Fit(hint, SmallCell, right - left), SmallCell, left, y + tall - 20f, sheet.Grey);

        // The count in a lit box at the right end, its number landing big and settling.
        if (counting)
        {
            float side = tall - Frame * 2f, bx = x + Wide - Frame - side;
            float land = 1f + .25f * (1f - Mathf.Clamp01((Smooth.Now - countedAt) / .1f));
            sheet.Box(bx, y + Frame, side, side, HudSheet.Lit);
            sheet.Plain(count, CountCell * land, bx + side * .5f, y + tall * .5f, HudSheet.Ink, TextAlign.Center, TextAlign.Center, true);
        }
        float flash = 1f - Mathf.Clamp01((Smooth.Now - changedAt) / FlashSeconds);
        if (flash > 0f) sheet.Box(x, y, Wide, tall, Gm.WithAlpha(HudSheet.White, 110f * flash));
        sheet.End();
        // The card stays over the right edge's column of callout windows, so they stand under it: in play (2026-11-13)
        // the HUD overlap check measured a callout window 12.4 px into it.
        HudRoom.Take(this, Edge, Edge + Wide, Top, Top + tall, HudRoom.Way.Under);
    }

    private void OnDisable() => HudRoom.Free(this);
}
