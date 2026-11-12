using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Who is ready to set off, at the top middle of the hub's HUD (the user, 2026-11-12: "로비에서 HUD로 가운데 위에
/// 플레이어들 준비했는지 TF2 MVM처럼 볼 수 있게. 내가 준비했는지 안 했는지 헷갈릴 수도 있으니까. 그냥 프로필 사진 띄우고
/// 준비했으면 체크 표시"). A tile for everyone in the room, in seat order (the host first) so every PC shows the same row:
/// their profile picture (this PC's own from the menu's profile, ProfilePicture; a friend's as their PC sent it,
/// PlayerNetworkPose.Portrait; the name's first letter on pink until there is one) and their name under it, this PC's
/// own tile named 나 in pink so there is no wondering which one is mine. Once a player is ready (HubLobby.IsReady: live
/// from the host's word in a room, at once for this PC's own press) a pink plate with a white check slams onto their
/// tile's corner and the tile flashes white; until then the picture sits a little dim. At the end, how many are ready,
/// which turns pink with one flash of the board once everyone is. Alone it is one tile. In the HUD's black, white and
/// pink, every motion short and settled at once. Drawn through the HUD camera like the rest of the HUD, leaning its own
/// small way (the HUD's Persona tilt, Settings → HUD 기울기); hidden while the results play over the stage (ResultsShow).
/// </summary>
[DisallowMultipleComponent]
public sealed class HubReadyBoard : MonoBehaviour
{
    // Its top this far under the screen's top (px at 1080p), clear of the alerts' first band (AlertHUD, 168 px down).
    private const float Top = 22f, Pad = 14f, Mat = 72f, Inset = 4f, Slot = 92f, NameGap = 5f, NameHeight = 20f,
        CountWidth = 74f, CheckSize = 32f, CheckIn = 9f, Tilt = 1.5f;
    // The check lands this much bigger and turned, and settles in LandSeconds; it goes in GoSeconds. A tile's flash lasts
    // FlashSeconds, the board's AllSeconds. An unready picture is dimmed this much.
    private const float LandScale = 1.9f, LandTurn = -18f, LandSeconds = .14f, GoSeconds = .08f, FlashSeconds = .22f,
        AllSeconds = .3f, UnreadyShade = .45f;

    private static readonly Comparison<PlayerMovement> BySeat = (a, b) => MatchResults.SeatOf(a).CompareTo(MatchResults.SeatOf(b));

    // One player's place on the board.
    private sealed class Tile
    {
        public RectTransform Root, Check;
        public RawImage Picture;
        public TMP_Text Letter, Name;
        public Image Shade, Flash, Foot;
        public PlayerMovement Who;
        public bool Ready;
        public float ChangedAt = -99f;
    }

    private readonly List<Tile> tiles = new List<Tile>();
    private readonly List<PlayerMovement> party = new List<PlayerMovement>();
    private RectTransform root;
    private Image flash;
    private TMP_Text count;
    private int shownParty = -1, shownReady = -1;
    private bool allShown;

    /// <summary>The board on its own HUD canvas (HubLobby makes it and takes it away with the hub).</summary>
    public static HubReadyBoard Make()
    {
        var host = new GameObject("Hub Ready Board", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupHud(host, 30);
        var board = host.AddComponent<HubReadyBoard>();
        board.Build();
        return board;
    }

    private void Build()
    {
        root = FeelUI.Rect("Board", transform);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -Top);
        KitStyle.Cut(KitStyle.Stretch("Back", root), KitStyle.Panel);
        var rail = FeelUI.Rect("Rail", root);
        rail.anchorMin = new Vector2(0f, 1f);
        rail.anchorMax = Vector2.one;
        rail.pivot = new Vector2(.5f, 1f);
        rail.sizeDelta = new Vector2(0f, KitStyle.AccentWidth);
        rail.anchoredPosition = Vector2.zero;
        KitStyle.Fill(rail, KitStyle.Accent);
        KitStyle.Flat(Right("Word", new Vector2(Pad, Pad + 8f), new Vector2(CountWidth, 18f)), Texts.Get("hub.ready.title"), 15f,
            TextAlignmentOptions.MidlineRight, KitStyle.Soft).fontStyle = FontStyles.Normal;
        count = KitStyle.Flat(Right("Count", new Vector2(Pad, Pad + 28f), new Vector2(CountWidth, 44f)), "", 38f,
            TextAlignmentOptions.MidlineRight, KitStyle.Ice);
        flash = KitStyle.Fill(KitStyle.Stretch("Flash", root), Color.clear);
        KitStyle.Shape(flash);
        root.gameObject.SetActive(false);
    }

    // A rect hanging from the board's top right: `at` is how far left and down (px).
    private RectTransform Right(string name, Vector2 at, Vector2 size)
    {
        var rect = FeelUI.Rect(name, root);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-at.x, -at.y);
        rect.sizeDelta = size;
        return rect;
    }

    private Tile BuildTile()
    {
        var tile = new Tile { Root = KitStyle.TopLeft("Player", root, Vector2.zero, new Vector2(Mat, Mat + NameGap + NameHeight)) };
        KitStyle.Cut(KitStyle.TopLeft("Mat", tile.Root, Vector2.zero, new Vector2(Mat, Mat)), KitStyle.With(KitStyle.Night, .96f));
        var picture = KitStyle.TopLeft("Picture", tile.Root, new Vector2(Inset, Inset), new Vector2(Mat - Inset * 2f, Mat - Inset * 2f));
        tile.Picture = picture.gameObject.AddComponent<RawImage>();
        tile.Picture.raycastTarget = false;
        // The letter on pink while there is no picture (as ProfilePicture.Paint shows it), under the dimming.
        tile.Letter = KitStyle.Flat(picture, "", 34f, TextAlignmentOptions.Center, KitStyle.Ink);
        tile.Shade = KitStyle.Fill(KitStyle.Stretch("Shade", picture), KitStyle.With(KitStyle.Night, UnreadyShade));
        tile.Foot = KitStyle.Bar("Foot", tile.Root, new Vector2(Inset, Mat - Inset - 3f), new Vector2(Mat - Inset * 2f, 3f), KitStyle.Faint);
        tile.Flash = KitStyle.Fill(KitStyle.TopLeft("Flash", tile.Root, Vector2.zero, new Vector2(Mat, Mat)), Color.clear);
        KitStyle.Shape(tile.Flash);
        // The check's plate sits on the picture's bottom right corner and turns about its own middle as it lands.
        tile.Check = FeelUI.Rect("Check", tile.Root);
        tile.Check.anchorMin = tile.Check.anchorMax = new Vector2(0f, 1f);
        tile.Check.anchoredPosition = new Vector2(Mat - CheckIn, -(Mat - CheckIn));
        tile.Check.sizeDelta = new Vector2(CheckSize, CheckSize);
        KitStyle.Cut(KitStyle.Stretch("Plate", tile.Check), KitStyle.Accent);
        HudShapes.Make<HubCheckMark>("Mark", tile.Check, KitStyle.Ice).rectTransform.sizeDelta = new Vector2(CheckSize - 12f, CheckSize - 12f);
        tile.Check.gameObject.SetActive(false);
        var name = KitStyle.TopLeft("Name", tile.Root, new Vector2((Mat - Slot) * .5f + 2f, Mat + NameGap), new Vector2(Slot - 4f, NameHeight));
        tile.Name = KitStyle.Flat(name, "", 15f, TextAlignmentOptions.Center, KitStyle.Soft);
        tile.Name.textWrappingMode = TextWrappingModes.NoWrap;
        tile.Name.overflowMode = TextOverflowModes.Ellipsis;
        return tile;
    }

    private void LateUpdate()
    {
        if (root == null) return;
        Gather();
        // Out of the way while the results play over the stage, and until a player is here.
        bool show = party.Count > 0 && !ResultsShow.IsPlaying;
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;
        if (party.Count != shownParty) Lay();
        int readyNow = 0;
        for (int i = 0; i < party.Count; i++) if (Show(tiles[i], party[i])) readyNow++;
        Count(readyNow);
        root.localRotation = Quaternion.Euler(0f, 0f, Tilt * GameSettings.Current.hudTilt);
    }

    // Everyone in the party, as the hub's objective counts them (HubLobby), host first.
    private void Gather()
    {
        party.Clear();
        foreach (var someone in PlayerRoster.All)
            if (someone != null && (someone.IsLocal || someone.IsNetworkDriven)) party.Add(someone);
        party.Sort(BySeat);
    }

    // A tile for each, side by side, the count at the end; the board as wide as that.
    private void Lay()
    {
        shownParty = party.Count;
        shownReady = -1;
        while (tiles.Count < party.Count) tiles.Add(BuildTile());
        for (int i = 0; i < tiles.Count; i++)
        {
            bool used = i < party.Count;
            if (tiles[i].Root.gameObject.activeSelf != used) tiles[i].Root.gameObject.SetActive(used);
            if (!used) { tiles[i].Who = null; continue; }
            tiles[i].Root.anchoredPosition = new Vector2(Pad + i * Slot + (Slot - Mat) * .5f, -(Pad + KitStyle.AccentWidth));
        }
        root.sizeDelta = new Vector2(Pad * 2f + party.Count * Slot + CountWidth,
            Pad * 2f + KitStyle.AccentWidth + Mat + NameGap + NameHeight);
    }

    // One player's tile this frame: their picture, their name and the check. True when they are ready.
    private static bool Show(Tile tile, PlayerMovement someone)
    {
        bool ready = HubLobby.IsReady(someone), mine = someone.IsLocal;
        string name = mine ? GameSettings.Current.DisplayName : someone.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : someone.name;
        // This PC's own picture as chosen in the menu; a friend's once their PC has sent it; else the letter on pink.
        var picture = mine ? ProfilePicture.Current : someone.TryGetComponent(out PlayerNetworkPose pose) ? pose.Portrait : null;
        if (tile.Picture.texture != picture || tile.Who != someone)
        {
            tile.Picture.texture = picture;
            tile.Picture.color = picture != null ? Color.white : KitStyle.Accent;
        }
        string letter = picture != null ? "" : ProfilePicture.Initial(name);
        if (tile.Letter.text != letter) tile.Letter.text = letter;
        string label = mine ? Texts.Get("hub.ready.me") : name;
        if (tile.Name.text != label) tile.Name.text = label;
        Color ink = mine ? KitStyle.Accent : KitStyle.Soft;
        if (tile.Name.color != ink) tile.Name.color = ink;
        if (tile.Who != someone)
        {
            // Someone new in this place (a join, a leave): shown as they are, without the landing.
            tile.Who = someone;
            tile.Ready = ready;
            tile.ChangedAt = -99f;
            Settle(tile);
        }
        else if (tile.Ready != ready)
        {
            tile.Ready = ready;
            tile.ChangedAt = Time.unscaledTime;
            if (ready) KitPulse.Play(tile.Flash, KitStyle.With(Color.white, .85f), FlashSeconds);
            Settle(tile);
        }
        Animate(tile);
        return ready;
    }

    // What a tile's state shows that doesn't move: the dimming, the foot bar's colour, the check put up.
    private static void Settle(Tile tile)
    {
        tile.Shade.enabled = !tile.Ready;
        tile.Foot.color = tile.Ready ? KitStyle.Accent : KitStyle.Faint;
        if (tile.Ready && !tile.Check.gameObject.activeSelf) tile.Check.gameObject.SetActive(true);
    }

    // The check slams down big and turned, and settles; it shrinks away quickly once they stop being ready.
    private static void Animate(Tile tile)
    {
        float since = Time.unscaledTime - tile.ChangedAt;
        if (tile.Ready)
        {
            float eased = KitReveal.EaseOut(Mathf.Clamp01(since / LandSeconds));
            tile.Check.localScale = Vector3.one * Mathf.Lerp(LandScale, 1f, eased);
            tile.Check.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(LandTurn, 0f, eased));
            return;
        }
        if (!tile.Check.gameObject.activeSelf) return;
        float left = 1f - Mathf.Clamp01(since / GoSeconds);
        tile.Check.localScale = Vector3.one * left;
        if (left <= 0f) tile.Check.gameObject.SetActive(false);
    }

    // How many are ready; everyone, it turns pink and the board flashes once.
    private void Count(int readyNow)
    {
        if (readyNow == shownReady) return;
        shownReady = readyNow;
        bool all = readyNow >= party.Count;
        count.text = Texts.Format("hub.ready.count", readyNow, party.Count);
        count.color = all ? KitStyle.Accent : KitStyle.Ice;
        if (all && !allShown) KitPulse.Play(flash, KitStyle.Flash, AllSeconds);
        allShown = all;
    }
}
