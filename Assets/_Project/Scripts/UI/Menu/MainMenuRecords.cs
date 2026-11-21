using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// The title screen's 기록 page (PlayerRecords), filling the space between the top and bottom bars over a dark veil:
/// "기록" at the top left, the totals as tiles and the last games as rows under it. Every piece grows in as it opens.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuRecords : MonoBehaviour
{
    // The heading's width, and how far down the totals start (under the heading).
    private const float LeftWidth = 660f, SideTop = 150f;
    // The tiles and game rows as laid out at their own origin, and the room kept under the lowest row (the page is 972 px
    // high between the bars).
    private const float RightX = 0f, TileWidth = 246f, TileHeight = 118f, TileGap = 14f, GameHeight = 48f, GameGap = 8f,
        PageFoot = 32f;
    // The lowest anything reaches: the page's own height less PageFoot (940 of the 972 px a 16:9 screen gives it). A fixed
    // 940 ran the games under the hint bar on a 21:9 screen, whose page is about 830 px high.
    private float PageBottom => (root != null && root.rect.height > 0f ? root.rect.height : 972f) - PageFoot;
    // The totals as laid out for 1920 px across, made smaller (FitSide) where the screen is narrower (16:10, 4:3), its
    // tiles and games then reaching down by its own scale.
    private float sideScale = 1f;
    private float SideBottom => (PageBottom - SideTop) / sideScale;
    private const float RightWidth = TileWidth * 4f + TileGap * 3f;
    // How many games the list shows.
    private const int TotalsGames = 8;

    // The faint how-to notes.
    private static readonly Color Hint = KitStyle.With(KitStyle.Text, .34f);

    private RectTransform root, content, side;
    private float edge;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    public static MainMenuRecords Make(RectTransform canvas, float edge, float top, float bottom)
    {
        var root = FeelUI.Rect("Records", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(0f, bottom);
        root.offsetMax = new Vector2(0f, -top);
        // The veil also catches the presses on empty space (OnPointerClick).
        KitStyle.Fill(root, KitStyle.With(KitStyle.Night, .9f)).raycastTarget = true;
        var records = root.gameObject.AddComponent<MainMenuRecords>();
        records.root = root;
        records.edge = edge;
        root.gameObject.SetActive(false);
        return records;
    }

    /// <summary>Builds the page from the records as they are now and grows it in.</summary>
    public void Open()
    {
        if (content != null) Destroy(content.gameObject);
        root.gameObject.SetActive(true);
        content = KitStyle.Stretch("Content", root);
        side = null;
        var reveal = content.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        BuildHeading();
        reveal.Open();
        BuildSide(.2f);
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    // The heading at the top left.
    private void BuildHeading()
    {
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", content, new Vector2(edge, 26f), new Vector2(LeftWidth, 80f)), Texts.Get("records.title"), 64f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(KitStyle.TopLeft("Where", content, new Vector2(edge + 4f, 108f), new Vector2(LeftWidth, 22f)), Texts.Get("records.where"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
    }

    // The totals and the last games, built anew each time the page opens and grown in.
    private void BuildSide(float at)
    {
        if (side != null) Destroy(side.gameObject);
        side = KitStyle.Stretch("Side", content);
        FitSide();
        var reveal = side.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        var records = PlayerRecords.Current;
        BuildTotals(records, reveal, ref at);
        BuildRecent(records, reveal, ref at);
        reveal.Open();
    }

    // Laid out for a 1920 px wide page; where the page is narrower it grows smaller about its top left until its right
    // edge is back on the screen.
    private void FitSide()
    {
        float width = root.rect.width > 0f ? root.rect.width : 1920f;
        sideScale = Mathf.Clamp((width - 2f * edge) / RightWidth, .6f, 1f);
        side.pivot = new Vector2(0f, 1f);
        side.localScale = new Vector3(sideScale, sideScale, 1f);
        side.anchoredPosition = new Vector2(edge, -SideTop);
    }

    // The totals as two rows of four tiles.
    private void BuildTotals(PlayerRecords.Data records, KitReveal reveal, ref float at)
    {
        Caption(side, Texts.Get("records.totals"), new Vector2(RightX, 40f), RightWidth, Texts.Get("records.totals.hint"), true);
        long kills = Math.Max(0L, records.kills);
        string share = kills > 0 ? Mathf.RoundToInt(100f * records.headshots / kills) + "%" : "";
        var tiles = new (string label, string value, string note)[]
        {
            (Texts.Get("records.kills"), Count(records.kills), ""),
            (Texts.Get("records.headshots"), Count(records.headshots), share),
            (Texts.Get("records.play_time"), Hours(records.seconds), ""),
            (Texts.Get("records.games"), Count(records.games), records.escapes > 0 ? Texts.Format("records.games.escapes", Count(records.escapes)) : ""),
            (Texts.Get("records.revives"), Count(records.revives), ""),
            (Texts.Get("records.downs"), Count(records.downs), ""),
            (Texts.Get("records.deaths"), Count(records.deaths), ""),
            (Texts.Get("records.wipes"), Count(records.wipes), ""),
        };
        Tiles(tiles, 68f, reveal, ref at);
    }

    // The last games, newest first.
    private void BuildRecent(PlayerRecords.Data records, KitReveal reveal, ref float at)
    {
        Caption(side, Texts.Get("records.recent"), new Vector2(RightX, 354f), RightWidth);
        Games(records.recent.Take(TotalsGames), 388f, Texts.Get("records.recent.empty"), reveal, ref at);
    }

    // Tiles four to a row from top down.
    private void Tiles((string label, string value, string note)[] tiles, float top, KitReveal reveal, ref float at)
    {
        for (int i = 0; i < tiles.Length; i++)
        {
            int column = i % 4, row = i / 4;
            var where = new Vector2(RightX + column * (TileWidth + TileGap), top + row * (TileHeight + TileGap));
            Tile(tiles[i].label, tiles[i].value, tiles[i].note, where, reveal, at);
            at += .03f;
        }
    }

    private void Tile(string label, string value, string note, Vector2 where, KitReveal reveal, float at)
    {
        var tile = KitStyle.TopLeft("Tile " + label, side, where, new Vector2(TileWidth, TileHeight));
        KitStyle.Cut(tile, KitStyle.With(KitStyle.Ice, .06f));
        KitStyle.Flat(KitStyle.TopLeft("Label", tile, new Vector2(18f, 12f), new Vector2(TileWidth - 36f, 22f)), label, 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        Shrink(KitStyle.Flat(KitStyle.TopLeft("Value", tile, new Vector2(16f, 38f), new Vector2(TileWidth - 32f, 56f)), value, 44f,
            TextAlignmentOptions.MidlineLeft), 26f);
        if (!string.IsNullOrEmpty(note))
            KitStyle.Flat(KitStyle.TopLeft("Note", tile, new Vector2(18f, 88f), new Vector2(TileWidth - 36f, 22f)), note, 15f,
                TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        reveal.Card(tile, KitButtons.Lid(tile), at);
    }

    // Games as rows from top down, never past the page's foot, or the empty line when there are none.
    private void Games(IEnumerable<PlayerRecords.Game> games, float top, string empty, KitReveal reveal, ref float at)
    {
        float y = top;
        foreach (var game in games)
        {
            if (game == null) continue;
            if (y + GameHeight > SideBottom) break;
            GameRow(game, y, reveal, at);
            at += .04f;
            y += GameHeight + GameGap;
        }
        if (y > top) return;
        var none = KitStyle.Flat(KitStyle.TopLeft("Empty", side, new Vector2(RightX, top), new Vector2(RightWidth, GameHeight)), empty, 18f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        none.fontStyle = FontStyles.Normal;
    }

    // One game as a row: escaped or left, its map, time, kills and deaths; when it ended at the right.
    private void GameRow(PlayerRecords.Game game, float y, KitReveal reveal, float at)
    {
        var row = KitStyle.TopLeft("Game", side, new Vector2(RightX, y), new Vector2(RightWidth, GameHeight));
        KitStyle.Fill(row, KitStyle.With(KitStyle.Ice, .05f));
        var tag = KitStyle.TopLeft("Result", row, new Vector2(10f, 9f), new Vector2(76f, 30f));
        KitStyle.Fill(tag, game.escaped ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .12f));
        KitStyle.Flat(tag, Texts.Get(game.escaped ? "records.escaped" : "records.left"), 15f, TextAlignmentOptions.Center,
            game.escaped ? KitStyle.Ink : KitStyle.Text);
        Cell(row, MapTable.DisplayName(game.map), 106f, 380f, KitStyle.Text, 18f);
        Cell(row, Clock(game.seconds), 500f, 120f, KitStyle.Soft, 16f);
        Cell(row, Texts.Format("records.recent.kills", game.kills), 630f, 130f, KitStyle.Soft, 16f);
        Cell(row, Texts.Format("records.recent.deaths", game.deaths), 770f, 110f, KitStyle.Soft, 16f);
        var when = KitStyle.Flat(KitStyle.TopLeft("When", row, new Vector2(RightWidth - 170f, 0f), new Vector2(156f, GameHeight)), When(game.when), 15f,
            TextAlignmentOptions.MidlineRight, KitStyle.Faint);
        when.fontStyle = FontStyles.Normal;
        reveal.Card(row, KitButtons.Lid(row), at);
    }

    private static void Cell(RectTransform row, string text, float x, float width, Color color, float size)
    {
        var label = KitStyle.Flat(KitStyle.TopLeft("Cell", row, new Vector2(x, 0f), new Vector2(width, GameHeight)), text, size,
            TextAlignmentOptions.MidlineLeft, color);
        label.fontStyle = FontStyles.Normal;
    }

    // A small ice caption across width, with a note at its right end: pink for a count, faint for a how-to.
    private static void Caption(RectTransform parent, string text, Vector2 where, float width, string note = "", bool howTo = false)
    {
        var label = KitStyle.Flat(KitStyle.TopLeft("Caption " + text, parent, where, new Vector2(400f, 22f)), text, 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        label.characterSpacing = 4f;
        if (string.IsNullOrEmpty(note)) return;
        var right = KitStyle.Flat(KitStyle.TopLeft("Note", parent, where + new Vector2(width - 520f, 0f), new Vector2(520f, 22f)), note,
            howTo ? 14f : 15f, TextAlignmentOptions.MidlineRight, howTo ? Hint : KitStyle.Accent);
        if (howTo) right.fontStyle = FontStyles.Normal;
    }

    // Lets a value shrink down to smallest to fit its box (a long time, a big count) instead of running over.
    private static void Shrink(TMP_Text label, float smallest)
    {
        label.fontSizeMax = label.fontSize;
        label.fontSizeMin = smallest;
        label.enableAutoSizing = true;
    }

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // 12:34, or 1:02:03 past an hour; "--" for none.
    private static string Clock(float seconds)
    {
        if (seconds <= 0f) return "--";
        var time = TimeSpan.FromSeconds(Mathf.Round(seconds));
        return time.TotalHours >= 1d
            ? ((int)time.TotalHours) + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00")
            : time.Minutes + ":" + time.Seconds.ToString("00");
    }

    // "12시간 34분", "34분" or "45초".
    private static string Hours(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0d, Math.Round(seconds)));
        if (time.TotalHours >= 1d) return Texts.Format("records.time.hours", (int)time.TotalHours, time.Minutes);
        if (time.TotalMinutes >= 1d) return Texts.Format("records.time.minutes", time.Minutes);
        return Texts.Format("records.time.seconds", time.Seconds);
    }

    // "2026-11-09 21:47" → "11.09 21:47".
    private static string When(string stamp)
    {
        return DateTime.TryParseExact(stamp, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.ToString("MM.dd HH:mm", CultureInfo.InvariantCulture) : stamp ?? "";
    }
}
