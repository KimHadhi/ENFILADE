using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's 기록 page (PlayerRecords), filling the space between the top and bottom bars over a dark veil:
/// at the left "기록" and the campaign, one slanted plate per chapter (its number pink once escaped, how often, the
/// fastest time and the fewest deaths, a pink 탈출 stamp; greyed and "아직" until then, but still easy to read); at the
/// right the totals as tiles
/// (kills, headshots with their share, play time, games with escapes, friends got up, going down, deaths, wipes) and
/// the last few games as rows (escaped or left, map, time, kills, deaths, when). Every piece grows in as it opens.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuRecords : MonoBehaviour
{
    // PlateGap: the least room between two chapter plates where they come nearest (KitStack).
    private const float LeftWidth = 660f, RightX = 820f, TileWidth = 246f, TileHeight = 118f, TileGap = 14f, RowHeight = 48f,
        RowGap = 8f, PlateHeight = 104f, PlateGap = 26f, Slant = 16f;
    // Each chapter plate's own tilt, indent and width, so the stack reads as pieces laid one by one, not a list.
    private static readonly float[] PlateTilts = { 1.5f, -1f, 1f, -1.5f }, PlateIndents = { 0f, 22f, 8f, 30f },
        PlateWidths = { 660f, 630f, 646f, 618f };

    private RectTransform root, content;
    private float edge;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    public static MainMenuRecords Make(RectTransform canvas, float edge, float top, float bottom)
    {
        var root = FeelUI.Rect("Records", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(0f, bottom);
        root.offsetMax = new Vector2(0f, -top);
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
        var reveal = content.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        float at = .05f;
        var records = PlayerRecords.Current;
        BuildCampaign(reveal, ref at);
        BuildTotals(records, reveal, ref at);
        BuildRecent(records, reveal, ref at);
        reveal.Open();
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    // The heading and one plate per chapter of the campaign, in the chapter table's order.
    private void BuildCampaign(KitReveal reveal, ref float at)
    {
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", content, new Vector2(edge, 26f), new Vector2(LeftWidth, 80f)), Texts.Get("records.title"), 64f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(KitStyle.TopLeft("Where", content, new Vector2(edge + 4f, 108f), new Vector2(LeftWidth, 22f)), Texts.Get("records.where"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);

        var chapters = GameTables.RowIds(CampaignChapter.TableName).ToArray();
        int escaped = chapters.Count(map => PlayerRecords.ChapterOf(map) is PlayerRecords.Chapter chapter && chapter.escapes > 0);
        Caption(Texts.Get("records.campaign"), new Vector2(edge, 160f),
            chapters.Length > 0 ? Texts.Format("records.campaign.escaped", escaped, chapters.Length) : "");
        float y = 196f, lastX = 0f, lastWidth = 0f, lastTilt = 0f;
        for (int i = 0; i < chapters.Length; i++)
        {
            float tilt = PlateTilts[i % PlateTilts.Length], x = PlateIndents[i % PlateIndents.Length], width = PlateWidths[i % PlateWidths.Length];
            // Each plate turns its own way about its left end, so one turning up under one turning down met it at the
            // right end (the second and third lay over each other; user, 2026-11-10). KitStack starts it low enough to
            // keep PlateGap between the two at their nearest, which a gap of 14 left looking cramped (user, 2026-11-11).
            if (i > 0) y = KitStack.Below(lastX, y, lastWidth, PlateHeight, lastTilt, x, width, tilt, PlateGap);
            Plate(chapters[i], new Vector2(edge + x, y), width, tilt, reveal, at);
            at += .06f;
            lastX = x; lastWidth = width; lastTilt = tilt;
        }
    }

    private void Plate(string map, Vector2 where, float width, float tilt, KitReveal reveal, float at)
    {
        var chapter = PlayerRecords.ChapterOf(map);
        bool done = chapter != null && chapter.escapes > 0;
        Split(MapTable.DisplayName(map), out string number, out string name);
        var plate = KitStyle.TopLeft("Chapter " + map, content, where, new Vector2(width, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        // A chapter not escaped yet is greyed, not faded out: at a fifth of white its number and line could hardly be
        // seen, and only the first chapter seemed to have a record at all (user, 2026-11-10).
        Color waiting = KitStyle.With(KitStyle.Text, .45f);
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Plate", plate), KitStyle.With(KitStyle.Ice, done ? .09f : .06f)), Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), done ? KitStyle.Accent : waiting), Slant);
        KitStyle.Flat(KitStyle.TopLeft("Number", plate, new Vector2(26f, 0f), new Vector2(110f, PlateHeight)), number, 66f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Accent : waiting);
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(140f, 14f), new Vector2(width - 280f, 42f)), name, 30f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Text : KitStyle.With(KitStyle.Text, .8f));
        string line = done
            ? Texts.Format("records.chapter.line", chapter.escapes, Clock(chapter.best), Mathf.Max(0, chapter.fewestDeaths))
            : Texts.Get("records.chapter.waiting");
        KitStyle.Flat(KitStyle.TopLeft("Line", plate, new Vector2(142f, 58f), new Vector2(width - 170f, 26f)), line, 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        if (done)
        {
            var stamp = KitStyle.TopLeft("Stamp", plate, new Vector2(width - 120f, 24f), new Vector2(92f, 38f));
            stamp.localRotation = Quaternion.Euler(0f, 0f, 8f);
            KitStyle.Fill(stamp, KitStyle.Accent);
            KitStyle.Flat(stamp, Texts.Get("records.escaped"), 20f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
        KitSkew.On(lid, Slant);
        reveal.Card(plate, lid, at);
    }

    // The totals as two rows of four tiles.
    private void BuildTotals(PlayerRecords.Data records, KitReveal reveal, ref float at)
    {
        Caption(Texts.Get("records.totals"), new Vector2(RightX, 40f), "");
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
        for (int i = 0; i < tiles.Length; i++)
        {
            int column = i % 4, row = i / 4;
            var where = new Vector2(RightX + column * (TileWidth + TileGap), 68f + row * (TileHeight + TileGap));
            Tile(tiles[i].label, tiles[i].value, tiles[i].note, where, reveal, at);
            at += .03f;
        }
    }

    private void Tile(string label, string value, string note, Vector2 where, KitReveal reveal, float at)
    {
        var tile = KitStyle.TopLeft("Tile " + label, content, where, new Vector2(TileWidth, TileHeight));
        KitStyle.Cut(tile, KitStyle.With(KitStyle.Ice, .06f));
        KitStyle.Flat(KitStyle.TopLeft("Label", tile, new Vector2(18f, 12f), new Vector2(TileWidth - 36f, 22f)), label, 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        KitStyle.Flat(KitStyle.TopLeft("Value", tile, new Vector2(16f, 38f), new Vector2(TileWidth - 32f, 56f)), value, 44f,
            TextAlignmentOptions.MidlineLeft);
        if (!string.IsNullOrEmpty(note))
            KitStyle.Flat(KitStyle.TopLeft("Note", tile, new Vector2(18f, 88f), new Vector2(TileWidth - 36f, 22f)), note, 15f,
                TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        reveal.Card(tile, KitButtons.Lid(tile), at);
    }

    // The last games, newest first.
    private void BuildRecent(PlayerRecords.Data records, KitReveal reveal, ref float at)
    {
        float width = TileWidth * 4f + TileGap * 3f;
        Caption(Texts.Get("records.recent"), new Vector2(RightX, 354f), "");
        if (records.recent.Count == 0)
        {
            KitStyle.Flat(KitStyle.TopLeft("Empty", content, new Vector2(RightX, 388f), new Vector2(width, RowHeight)),
                Texts.Get("records.recent.empty"), 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
            return;
        }
        float y = 388f;
        foreach (var game in records.recent)
        {
            var row = KitStyle.TopLeft("Game", content, new Vector2(RightX, y), new Vector2(width, RowHeight));
            KitStyle.Fill(row, KitStyle.With(KitStyle.Ice, .05f));
            var tag = KitStyle.TopLeft("Result", row, new Vector2(10f, 9f), new Vector2(76f, 30f));
            KitStyle.Fill(tag, game.escaped ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .12f));
            KitStyle.Flat(tag, Texts.Get(game.escaped ? "records.escaped" : "records.left"), 15f, TextAlignmentOptions.Center,
                game.escaped ? KitStyle.Ink : KitStyle.Text);
            Cell(row, MapTable.DisplayName(game.map), 106f, 380f, KitStyle.Text, 18f);
            Cell(row, Clock(game.seconds), 500f, 120f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.kills", game.kills), 630f, 130f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.deaths", game.deaths), 770f, 110f, KitStyle.Soft, 16f);
            var when = KitStyle.Flat(KitStyle.TopLeft("When", row, new Vector2(width - 170f, 0f), new Vector2(156f, RowHeight)), When(game.when), 15f,
                TextAlignmentOptions.MidlineRight, KitStyle.Faint);
            when.fontStyle = FontStyles.Normal;
            reveal.Card(row, KitButtons.Lid(row), at);
            at += .04f;
            y += RowHeight + RowGap;
        }
    }

    private static void Cell(RectTransform row, string text, float x, float width, Color color, float size)
    {
        var label = KitStyle.Flat(KitStyle.TopLeft("Cell", row, new Vector2(x, 0f), new Vector2(width, RowHeight)), text, size,
            TextAlignmentOptions.MidlineLeft, color);
        label.fontStyle = FontStyles.Normal;
    }

    // A small ice caption with an optional note to its right.
    private void Caption(string text, Vector2 where, string note)
    {
        var label = KitStyle.Flat(KitStyle.TopLeft("Caption " + text, content, where, new Vector2(400f, 22f)), text, 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        label.characterSpacing = 4f;
        if (string.IsNullOrEmpty(note)) return;
        KitStyle.Flat(KitStyle.TopLeft("Note", content, where + new Vector2(LeftWidth - 300f, 0f), new Vector2(300f, 22f)), note, 15f,
            TextAlignmentOptions.MidlineRight, KitStyle.Accent);
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

    // "1장 · 옥상 탈출" → "01" and "옥상 탈출".
    private static void Split(string display, out string number, out string name)
    {
        number = "--";
        name = display ?? "";
        int dot = name.IndexOf('·');
        if (dot < 0) return;
        string digits = new string(name.Substring(0, dot).Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return;
        number = int.Parse(digits).ToString("00");
        name = name.Substring(dot + 1).Trim();
    }
}
