using System;
using System.Collections.Generic;
using HudAddons;
using UnityEngine;

/// <summary>
/// Who is ready to set off, at the top middle of the hub's HUD (the user, 2026-11-12: "로비에서 HUD로 가운데 위에
/// 플레이어들 준비했는지 TF2 MVM처럼 볼 수 있게. 내가 준비했는지 안 했는지 헷갈릴 수도 있으니까. 그냥 프로필 사진 띄우고
/// 준비했으면 체크 표시"). A box for everyone in the room, in seat order (the host first) so every PC shows the same row:
/// their profile picture (this PC's own from the menu's profile, ProfilePicture; a friend's as their PC sent it,
/// PlayerNetworkPose.Portrait; the name's first letter until there is one) and under it, in the same box, their name after
/// a short bar of their colour, this PC's own named 나. Once a player is ready (HubLobby.IsReady: live from the host's word
/// in a room, at once for this PC's own press) a white plate with a check lands on their picture's corner; until then the
/// picture sits dim. At the end, how many are ready, its box lit white once everyone is. Alone it is one box.
/// <para>Drawn in the player HUD's parts (HudSheet: the portrait's box, the class UI's bar of the player's colour, the
/// selector's lit box), upright, moving with the HUD's sway (the user, 2026-11-22: "위에 저거나 … 다 우리 hud랑 통일성 있게
/// 리디자인"; it was a leaning dark card with a pink rail and pink plates). Hidden while the results play over the stage
/// (ResultsShow).</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class HubReadyBoard : MonoBehaviour
{
    // Its top this far under the screen's top, clear of the alerts' first band (AlertHUD).
    private const float Top = 20f, Frame = 4f, Photo = 76f, Strip = 26f, Gap = 8f, CountWide = 92f, Plate = 28f, NameCell = 14.4f,
        CountCell = 40f;
    // The check lands this much bigger and settles in LandSeconds; it goes in GoSeconds. A box's flash lasts FlashSeconds. An
    // unready picture shows at this much.
    private const float LandScale = 1.6f, LandSeconds = .14f, GoSeconds = .08f, FlashSeconds = .25f, UnreadyShow = .4f, ShadeRate = 14f;
    private const int Order = 28;

    private static readonly Comparison<PlayerMovement> BySeat = (a, b) => MatchResults.SeatOf(a).CompareTo(MatchResults.SeatOf(b));

    // One player's place on the board.
    private sealed class Tile
    {
        public PlayerMovement Who;
        public bool Ready;
        public float ChangedAt = -99f, Shown = UnreadyShow;
    }

    private readonly List<Tile> tiles = new List<Tile>();
    private readonly List<PlayerMovement> party = new List<PlayerMovement>();
    private HudSheet sheet;
    private string count = "";
    private int shownParty = -1, shownReady = -1;

    /// <summary>The board on its own overlay (HubLobby makes it and takes it away with the hub).</summary>
    public static HubReadyBoard Make()
    {
        var sheet = new HudSheet("Hub Ready Board", Order);
        var board = sheet.Overlay.gameObject.AddComponent<HubReadyBoard>();
        board.sheet = sheet;
        return board;
    }

    private void LateUpdate()
    {
        if (sheet == null) return;
        Gather();
        // Out of the way while the results play over the stage or the mirror's wardrobe is open, and until a player is here.
        bool show = party.Count > 0 && !ResultsShow.IsPlaying && !HubDressing.IsOpen && !LoadingScreen.Visible;
        sheet.Visible = show;
        if (!show) return;
        while (tiles.Count < party.Count) tiles.Add(new Tile());

        float tileWide = Photo + Frame * 2f, tall = Frame + Photo + Strip;
        float wide = party.Count * (tileWide + Gap) + CountWide;
        Vector2 sway = HudSheet.Sway;
        float x = (sheet.Width - wide) * .5f + sway.x, y = Top + sway.y;
        sheet.Begin();
        int ready = 0;
        for (int i = 0; i < party.Count; i++)
        {
            if (Draw(tiles[i], party[i], x, y, tileWide, tall)) ready++;
            x += tileWide + Gap;
        }
        Count(ready, x, y, tall);
        sheet.End();
    }

    // Everyone in the party, as the hub's objective counts them (HubLobby), host first.
    private void Gather()
    {
        party.Clear();
        foreach (var someone in PlayerRoster.All)
            if (someone != null && (someone.IsLocal || someone.IsNetworkDriven)) party.Add(someone);
        party.Sort(BySeat);
    }

    // One player's box: their picture, their name and the check. True when they are ready.
    private bool Draw(Tile tile, PlayerMovement someone, float x, float y, float wide, float tall)
    {
        bool ready = HubLobby.IsReady(someone), mine = someone.IsLocal;
        someone.TryGetComponent(out PlayerTally seat);
        string name = mine ? GameSettings.Current.DisplayName : seat != null ? seat.DisplayName : someone.name;
        // This PC's own picture as chosen in the menu; a friend's once their PC has sent it; else the letter.
        Texture picture = mine ? ProfilePicture.Current : someone.TryGetComponent(out PlayerNetworkPose pose) ? pose.Portrait : null;
        if (tile.Who != someone)
        {
            // Someone new in this place (a join, a leave): shown as they are, without the landing.
            tile.Who = someone;
            tile.Ready = ready;
            tile.ChangedAt = -99f;
            tile.Shown = ready ? 1f : UnreadyShow;
        }
        else if (tile.Ready != ready)
        {
            tile.Ready = ready;
            tile.ChangedAt = Smooth.Now;
        }
        tile.Shown = Smooth.Lerp(ShadeRate, tile.Shown, ready ? 1f : UnreadyShow);
        float since = Smooth.Now - tile.ChangedAt;

        sheet.Box(x, y, wide, tall);
        float px = x + Frame, py = y + Frame;
        Color32 tint = Gm.WithAlpha(HudSheet.White, 255f * tile.Shown);
        if (picture != null) sheet.Picture(picture, px, py, Photo, Photo, tint);
        else sheet.Text(ProfilePicture.Initial(name), Photo * .5f, px + Photo * .5f, py + Photo * .5f, tint, TextAlign.Center, TextAlign.Center, true);
        if (ready && since < FlashSeconds)
            sheet.Box(x, y, wide, tall, Gm.WithAlpha(HudSheet.White, 150f * (1f - since / FlashSeconds)));

        // The name after a short bar of the player's own colour, as the class UI writes the class's.
        float middle = y + Frame + Photo + Strip * .5f;
        sheet.Fill(px + 2f, middle - 6f, 3f, 12f, seat != null ? (Color32)seat.Color : HudSheet.White);
        string label = mine ? Texts.Get("hub.ready.me") : name;
        sheet.Text(sheet.Fit(label, NameCell, Photo - 12f, true), NameCell, px + 10f, middle, ready ? HudSheet.White : sheet.Grey,
            TextAlign.Left, TextAlign.Center, true);

        // The check's white plate on the picture's bottom right corner.
        float size = ready ? Mathf.Lerp(LandScale, 1f, KitReveal.EaseOut(Mathf.Clamp01(since / LandSeconds)))
            : 1f - Mathf.Clamp01(since / GoSeconds);
        if (size <= 0f) return ready;
        float plate = Plate * size, cx = px + Photo - Plate * .5f - 3f, cy = py + Photo - Plate * .5f - 3f;
        sheet.Box(cx - plate * .5f, cy - plate * .5f, plate, plate, HudSheet.White, 4f);
        float mark = plate * .68f;
        sheet.Check(cx - mark * .5f, cy - mark * .5f, mark, HudSheet.Ink);
        return ready;
    }

    // How many are ready; everyone, the box lights white and the number turns black.
    private void Count(int ready, float x, float y, float tall)
    {
        if (ready != shownReady || party.Count != shownParty)
        {
            shownReady = ready;
            shownParty = party.Count;
            count = Texts.Format("hub.ready.count", ready, party.Count);
        }
        bool all = ready >= party.Count;
        if (all) sheet.Box(x, y, CountWide, tall, HudSheet.Lit);
        else sheet.Box(x, y, CountWide, tall);
        float middle = x + CountWide * .5f;
        string title = Texts.Get("hub.ready.title");
        if (all)
        {
            sheet.Plain(title, NameCell, middle, y + 20f, HudSheet.Ink, TextAlign.Center, TextAlign.Center, true);
            sheet.Plain(count, CountCell, middle, y + tall * .5f + 8f, HudSheet.Ink, TextAlign.Center, TextAlign.Center, true);
        }
        else
        {
            sheet.Text(title, NameCell, middle, y + 20f, sheet.Grey, TextAlign.Center, TextAlign.Center, true);
            sheet.Text(count, CountCell, middle, y + tall * .5f + 8f, HudSheet.White, TextAlign.Center, TextAlign.Center, true);
        }
    }
}
