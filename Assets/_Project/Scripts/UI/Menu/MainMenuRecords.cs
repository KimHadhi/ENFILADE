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
    private const float LeftWidth = 660f, RightX = 820f, TileWidth = 246f, TileHeight = 118f, TileGap = 14f, RowHeight = 48f,
        RowGap = 8f, PlateHeight = 104f, PlateGap = 14f, Slant = 16f;
    private static readonly float[] PlateTilts = { 1.5f, -1f, 1f, -1.5f };

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
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", content, new Vector2(edge, 26f), new Vector2(LeftWidth, 80f)), "기록", 64f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(KitStyle.TopLeft("Where", content, new Vector2(edge + 4f, 108f), new Vector2(LeftWidth, 22f)), "이 PC에 남은 기록", 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);

        var chapters = GameTables.RowIds(CampaignChapter.TableName).ToArray();
        int escaped = chapters.Count(map => PlayerRecords.ChapterOf(map) is PlayerRecords.Chapter chapter && chapter.escapes > 0);
        Caption("캠페인", new Vector2(edge, 160f), chapters.Length > 0 ? escaped + " / " + chapters.Length + " 장 탈출" : "");
        float y = 196f, tiltAbove = 0f;
        for (int i = 0; i < chapters.Length; i++)
        {
            float tilt = PlateTilts[i % PlateTilts.Length];
            // Each plate turns its own way about its left end, so one turning up under one turning down met it at the
            // right end (the second and third lay over each other; user, 2026-11-10). It starts that much lower instead,
            // which keeps PlateGap between the two at their nearest.
            if (i > 0) y += Mathf.Max(0f, LeftWidth * (Mathf.Sin(tilt * Mathf.Deg2Rad) - Mathf.Sin(tiltAbove * Mathf.Deg2Rad)));
            Plate(chapters[i], new Vector2(edge, y), tilt, reveal, at);
            at += .06f;
            y += PlateHeight + PlateGap;
            tiltAbove = tilt;
        }
    }

    private void Plate(string map, Vector2 where, float tilt, KitReveal reveal, float at)
    {
        var chapter = PlayerRecords.ChapterOf(map);
        bool done = chapter != null && chapter.escapes > 0;
        Split(MapTable.DisplayName(map), out string number, out string name);
        var plate = KitStyle.TopLeft("Chapter " + map, content, where, new Vector2(LeftWidth, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        // A chapter not escaped yet is greyed, not faded out: at a fifth of white its number and line could hardly be
        // seen, and only the first chapter seemed to have a record at all (user, 2026-11-10).
        Color waiting = KitStyle.With(KitStyle.Text, .45f);
        KitSkew.On(KitStyle.Fill(KitStyle.Stretch("Plate", plate), KitStyle.With(KitStyle.Ice, done ? .09f : .06f)), Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), done ? KitStyle.Accent : waiting), Slant);
        KitStyle.Flat(KitStyle.TopLeft("Number", plate, new Vector2(26f, 0f), new Vector2(110f, PlateHeight)), number, 66f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Accent : waiting);
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(140f, 14f), new Vector2(LeftWidth - 280f, 42f)), name, 30f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Text : KitStyle.With(KitStyle.Text, .8f));
        string line = done
            ? "탈출 " + chapter.escapes + "번   ·   최고 " + Clock(chapter.best) + "   ·   최소 죽음 " + Mathf.Max(0, chapter.fewestDeaths)
            : "아직 탈출 못 함";
        KitStyle.Flat(KitStyle.TopLeft("Line", plate, new Vector2(142f, 58f), new Vector2(LeftWidth - 170f, 26f)), line, 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        if (done)
        {
            var stamp = KitStyle.TopLeft("Stamp", plate, new Vector2(LeftWidth - 120f, 24f), new Vector2(92f, 38f));
            stamp.localRotation = Quaternion.Euler(0f, 0f, 8f);
            KitStyle.Fill(stamp, KitStyle.Accent);
            KitStyle.Flat(stamp, "탈출", 20f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
        KitSkew.On(lid, Slant);
        reveal.Card(plate, lid, at);
    }

    // The totals as two rows of four tiles.
    private void BuildTotals(PlayerRecords.Data records, KitReveal reveal, ref float at)
    {
        Caption("합계", new Vector2(RightX, 40f), "");
        long kills = Math.Max(0L, records.kills);
        string share = kills > 0 ? Mathf.RoundToInt(100f * records.headshots / kills) + "%" : "";
        var tiles = new (string label, string value, string note)[]
        {
            ("처치", Count(records.kills), ""),
            ("헤드샷", Count(records.headshots), share),
            ("플레이 시간", Hours(records.seconds), ""),
            ("판", Count(records.games), records.escapes > 0 ? "탈출 " + Count(records.escapes) : ""),
            ("살려 준 동료", Count(records.revives), ""),
            ("쓰러짐", Count(records.downs), ""),
            ("죽음", Count(records.deaths), ""),
            ("전멸", Count(records.wipes), ""),
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
        Caption("최근 판", new Vector2(RightX, 354f), "");
        if (records.recent.Count == 0)
        {
            KitStyle.Flat(KitStyle.TopLeft("Empty", content, new Vector2(RightX, 388f), new Vector2(width, RowHeight)),
                "아직 기록이 없어요. 한 판 하고 오세요.", 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
            return;
        }
        float y = 388f;
        foreach (var game in records.recent)
        {
            var row = KitStyle.TopLeft("Game", content, new Vector2(RightX, y), new Vector2(width, RowHeight));
            KitStyle.Fill(row, KitStyle.With(KitStyle.Ice, .05f));
            var tag = KitStyle.TopLeft("Result", row, new Vector2(10f, 9f), new Vector2(76f, 30f));
            KitStyle.Fill(tag, game.escaped ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .12f));
            KitStyle.Flat(tag, game.escaped ? "탈출" : "나감", 15f, TextAlignmentOptions.Center, game.escaped ? KitStyle.Ink : KitStyle.Text);
            Cell(row, MapTable.DisplayName(game.map), 106f, 380f, KitStyle.Text, 18f);
            Cell(row, Clock(game.seconds), 500f, 120f, KitStyle.Soft, 16f);
            Cell(row, "처치 " + game.kills, 630f, 130f, KitStyle.Soft, 16f);
            Cell(row, "죽음 " + game.deaths, 770f, 110f, KitStyle.Soft, 16f);
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
        if (time.TotalHours >= 1d) return ((int)time.TotalHours) + "시간 " + time.Minutes + "분";
        if (time.TotalMinutes >= 1d) return time.Minutes + "분";
        return time.Seconds + "초";
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
