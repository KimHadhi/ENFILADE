using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The title screen's 기록 page (PlayerRecords), filling the space between the top and bottom bars over a dark veil.
/// At the left "기록" and the campaign as a straight ledger, every chapter one row of the same width and height (the
/// user asked for a tidy record book instead of plates leaning their own ways, 2026-11-11): its number (pink once
/// escaped), name, how many games, and three columns (escapes, fastest, fewest deaths); a thin route down their left
/// with a diamond per chapter (filled once escaped, a pink outline with a core for the next one to beat, grey after
/// it); under them the campaign as a whole (the sum of the fastest escapes, chapters escaped without dying, time in
/// chapters). At the right the totals as tiles and the last games as rows. Pressing a chapter turns the right side into
/// that chapter's page (its picture drifting under its number and name, its own tiles, its last games with the fastest
/// marked); pressing it again, the page's empty space or Esc brings the totals back. Every piece grows in as it opens.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuRecords : MonoBehaviour, IPointerClickHandler
{
    // The left column's width, the room at its left for the route (the rows start past it), a chapter row and the room
    // between rows, where the rows start, a row's stat column, and the campaign strip's height.
    private const float LeftWidth = 660f, RouteWidth = 30f, RowHeight = 92f, RowGap = 14f, LedgerTop = 196f, ColumnWidth = 92f,
        SummaryHeight = 96f;
    // Where the right side starts, its tiles and game rows, the chapter picture's height, and the lowest anything reaches
    // (the page is 972 px high between the bars).
    private const float RightX = 820f, TileWidth = 246f, TileHeight = 118f, TileGap = 14f, GameHeight = 48f, GameGap = 8f,
        BannerHeight = 232f, PageFoot = 32f, SideGap = 40f;
    // The lowest anything reaches: the page's own height less PageFoot (940 of the 972 px a 16:9 screen gives it). A fixed
    // 940 ran the games under the hint bar on a 21:9 screen, whose page is about 830 px high.
    private float PageBottom => (root != null && root.rect.height > 0f ? root.rect.height : 972f) - PageFoot;
    // The right side as laid out for 1920 px across, or moved left and made smaller (FitSide) where the screen is narrower
    // (16:10, 4:3), its tiles and games then reaching down by its own scale.
    private float sideScale = 1f;
    private float SideBottom => PageBottom / sideScale;
    private const float RightWidth = TileWidth * 4f + TileGap * 3f, RowWidth = LeftWidth - RouteWidth;
    // How many games the totals list and a chapter's list show (PlayerRecords keeps more).
    private const int TotalsGames = 8, ChapterGames = 5;
    // How much of the chapter picture shows across, and how fast it drifts from side to side.
    private const float PictureZoom = .9f, PanSpeed = .22f;

    // Greyed values (a chapter not escaped yet is greyed, not faded out, so it still reads; user, 2026-11-10), the faint
    // how-to notes, and the thin rules between columns.
    private static readonly Color Dim = KitStyle.With(KitStyle.Text, .42f), Hint = KitStyle.With(KitStyle.Text, .34f),
        Rule = KitStyle.With(KitStyle.Ice, .12f);

    private enum Mark { Done, Next, Later }

    // A chapter's row, kept to show which one is picked.
    private sealed class Row
    {
        public string Map;
        public bool Done;
        public Image Back, Rim, Point;
    }

    private readonly List<Row> rows = new List<Row>();
    private RectTransform root, content, side, sheet;
    private RawImage picture;
    private float edge;
    private string picked;

    public bool IsOpen => root != null && root.gameObject.activeSelf;
    /// <summary>A chapter's page is up at the right (Esc brings the totals back before it leaves the page).</summary>
    public bool ShowsChapter => IsOpen && picked != null;

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

    /// <summary>Builds the page from the records as they are now, the totals at the right, and grows it in.</summary>
    public void Open()
    {
        if (content != null) Destroy(content.gameObject);
        root.gameObject.SetActive(true);
        content = KitStyle.Stretch("Content", root);
        side = sheet = null;
        picture = null;
        picked = null;
        rows.Clear();
        var reveal = content.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        float at = .05f;
        BuildCampaign(reveal, ref at);
        reveal.Open();
        BuildSide(.2f);
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    /// <summary>Back from a chapter's page to the totals.</summary>
    public void ShowTotals()
    {
        if (picked != null) Pick(null);
    }

    // A press on the page's empty space (not on a chapter, not inside the chapter's page) brings the totals back. Rows
    // are buttons and take their own presses; everything else falls through to the veil this sits on.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (picked == null || eventData.button != PointerEventData.InputButton.Left) return;
        if (sheet != null && RectTransformUtility.RectangleContainsScreenPoint(sheet, eventData.position, eventData.pressEventCamera)) return;
        Pick(null);
    }

    private void Update()
    {
        if (picture != null && picture.enabled) Pan();
    }

    // Shows the chapter's page at the right, or the totals again for null or for the chapter already up.
    private void Pick(string map)
    {
        picked = map == picked ? null : map;
        foreach (var row in rows) Paint(row);
        // A pressed row stays selected for the UI's Submit (Enter), which would press it again.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        BuildSide(0f);
    }

    // The heading, the chapters as a ledger in the chapter table's order with the route at their left, and the campaign
    // strip under them.
    private void BuildCampaign(KitReveal reveal, ref float at)
    {
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", content, new Vector2(edge, 26f), new Vector2(LeftWidth, 80f)), Texts.Get("records.title"), 64f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(KitStyle.TopLeft("Where", content, new Vector2(edge + 4f, 108f), new Vector2(LeftWidth, 22f)), Texts.Get("records.where"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);

        var chapters = GameTables.RowIds(CampaignChapter.TableName).ToArray();
        var lines = chapters.Select(PlayerRecords.ChapterOf).ToArray();
        Caption(content, Texts.Get("records.campaign"), new Vector2(edge, 160f), LeftWidth,
            chapters.Length > 0 ? Texts.Format("records.campaign.escaped", lines.Count(Escaped), chapters.Length) : "");
        Route(lines, reveal, at);
        for (int i = 0; i < chapters.Length; i++)
        {
            Ledger(chapters[i], lines[i], RowTop(i), reveal, at);
            at += .06f;
        }
        float below = RowTop(chapters.Length) - RowGap + 36f;
        if (chapters.Length > 0 && below + 30f + SummaryHeight <= PageBottom) Summary(lines, below, reveal, at);
    }

    private static float RowTop(int index) => LedgerTop + index * (RowHeight + RowGap);

    private static bool Escaped(PlayerRecords.Chapter line) => line != null && line.escapes > 0;

    // The campaign's route down the rows' left: a thin line through a diamond per chapter, pink between two chapters both
    // escaped. A filled diamond is escaped, a pink outline with a core the next one to beat, a grey outline one after it
    // (the user's nested-diamond glyph reference, 2026-11-10).
    private void Route(PlayerRecords.Chapter[] lines, KitReveal reveal, float at)
    {
        float x = edge + RouteWidth * .3f;
        int next = Array.FindIndex(lines, chapter => !Escaped(chapter));
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            float from = RowTop(i) + RowHeight * .5f, to = RowTop(i + 1) + RowHeight * .5f;
            bool run = Escaped(lines[i]) && Escaped(lines[i + 1]);
            var track = KitStyle.Bar("Route", content, new Vector2(x - 1f, from), new Vector2(2f, to - from),
                run ? KitStyle.With(KitStyle.Accent, .7f) : KitStyle.With(KitStyle.Ice, .14f));
            reveal.Line(track.rectTransform, true, at + i * .06f, .2f);
        }
        for (int i = 0; i < lines.Length; i++)
            Diamond(new Vector2(x, RowTop(i) + RowHeight * .5f), Escaped(lines[i]) ? Mark.Done : i == next ? Mark.Next : Mark.Later);
    }

    private void Diamond(Vector2 centre, Mark mark)
    {
        Square("Node", centre, 12f, mark == Mark.Later ? KitStyle.With(KitStyle.Ice, .38f) : KitStyle.Accent);
        if (mark == Mark.Done) return;
        Square("Hollow", centre, 8f, KitStyle.Night);
        if (mark == Mark.Next) Square("Core", centre, 4f, KitStyle.Accent);
    }

    // A small square turned 45° about its middle, centre px right and down from the page's top left.
    private Image Square(string name, Vector2 centre, float size, Color color)
    {
        var rect = FeelUI.Rect(name, content);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(centre.x, -centre.y);
        rect.sizeDelta = new Vector2(size, size);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return KitStyle.Fill(rect, color);
    }

    // One chapter as a ledger row: its number and name, how many games, then escapes, fastest and fewest deaths in
    // columns. Pressing it shows the chapter's page at the right; pressing it again goes back to the totals.
    private void Ledger(string map, PlayerRecords.Chapter line, float y, KitReveal reveal, float at)
    {
        bool done = Escaped(line);
        Split(MapTable.DisplayName(map), out string number, out string name, out string label);
        var rect = KitStyle.TopLeft("Chapter " + map, content, new Vector2(edge + RouteWidth, y), new Vector2(RowWidth, RowHeight));
        var row = new Row { Map = map, Done = done };
        // Picked: the lobby's chosen look (a dark plate in a thin pink rim) and a pink point toward the chapter's page.
        var rim = KitStyle.Stretch("Rim", rect);
        rim.sizeDelta = new Vector2(2f, 2f);
        row.Rim = KitStyle.Cut(rim, KitStyle.With(KitStyle.Accent, .85f));
        row.Back = KitStyle.Cut(KitStyle.Stretch("Back", rect), Resting(done));
        row.Back.raycastTarget = true;

        KitStyle.Flat(KitStyle.TopLeft("Number", rect, new Vector2(22f, 0f), new Vector2(84f, RowHeight)), number, 50f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Accent : Dim);
        float columns = RowWidth - 18f - ColumnWidth * 3f, textWidth = columns - 122f;
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", rect, new Vector2(110f, 16f), new Vector2(textWidth, 36f)), name, 28f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Text : KitStyle.With(KitStyle.Text, .8f));
        title.overflowMode = CombatUIAssets.ClippedOverflow(title);
        int games = line?.games ?? 0;
        string played = games > 0 ? Texts.Format("records.row.games", Count(games)) : Texts.Get("records.row.never");
        var under = KitStyle.Flat(KitStyle.TopLeft("Games", rect, new Vector2(111f, 54f), new Vector2(textWidth, 22f)),
            string.IsNullOrEmpty(label) ? played : label + "   ·   " + played, 14f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        under.fontStyle = FontStyles.Normal;
        Column(rect, columns, Texts.Get("records.col.escapes"), Count(line?.escapes ?? 0), done);
        Column(rect, columns + ColumnWidth, Texts.Get("records.col.best"), done ? Clock(line.best) : "--", done);
        Column(rect, columns + ColumnWidth * 2f, Texts.Get("records.col.fewest"), done ? Count(line.fewestDeaths) : "--", done);

        var point = FeelUI.Rect("Point", rect);
        point.anchorMin = point.anchorMax = new Vector2(1f, .5f);
        point.pivot = new Vector2(.5f, .5f);
        point.anchoredPosition = new Vector2(1f, 0f);
        point.sizeDelta = new Vector2(12f, 12f);
        point.localRotation = Quaternion.Euler(0f, 0f, 45f);
        row.Point = KitStyle.Fill(point, KitStyle.Accent);

        reveal.Card(rect, KitStyle.Cut(KitStyle.Stretch("Flash", rect), KitStyle.Flash), at);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = row.Back;
        var colours = button.colors;
        colours.normalColor = colours.highlightedColor = colours.selectedColor = Color.white;
        colours.pressedColor = new Color(.78f, .78f, .8f);
        colours.fadeDuration = .05f;
        button.colors = colours;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => Pick(map));
        rect.gameObject.AddComponent<KitHover>();
        rows.Add(row);
        Paint(row);
    }

    // A row's look for whether it is the picked one.
    private void Paint(Row row)
    {
        bool on = row.Map == picked;
        row.Rim.enabled = on;
        row.Point.enabled = on;
        row.Back.color = on ? KitStyle.With(KitStyle.Night, .95f) : Resting(row.Done);
    }

    private static Color Resting(bool done) => KitStyle.With(KitStyle.Ice, done ? .085f : .05f);

    // One of a row's stat columns: a thin rule at its left, its name small and its value under it, both centred.
    private static void Column(RectTransform row, float x, string label, string value, bool done)
    {
        KitStyle.Bar("Rule", row, new Vector2(x, 22f), new Vector2(1f, RowHeight - 44f), Rule);
        var name = KitStyle.Flat(KitStyle.TopLeft("Label", row, new Vector2(x, 17f), new Vector2(ColumnWidth, 20f)), label, 13f,
            TextAlignmentOptions.Center, KitStyle.Soft);
        name.fontStyle = FontStyles.Normal;
        Shrink(KitStyle.Flat(KitStyle.TopLeft("Value", row, new Vector2(x + 4f, 39f), new Vector2(ColumnWidth - 8f, 36f)), value, 26f,
            TextAlignmentOptions.Center, done ? KitStyle.Text : Dim), 16f);
    }

    // Under the chapters, the campaign as a whole: the sum of each chapter's fastest escape once every one is escaped,
    // how many were escaped without dying, and the time spent in chapters.
    private void Summary(PlayerRecords.Chapter[] lines, float y, KitReveal reveal, float at)
    {
        Caption(content, Texts.Get("records.summary"), new Vector2(edge, y), LeftWidth);
        var strip = KitStyle.TopLeft("Campaign", content, new Vector2(edge + RouteWidth, y + 30f), new Vector2(RowWidth, SummaryHeight));
        KitStyle.Cut(strip, KitStyle.With(KitStyle.Ice, .05f));
        bool all = lines.All(Escaped);
        float sum = all ? lines.Sum(chapter => chapter.best) : 0f;
        int clean = lines.Count(chapter => Escaped(chapter) && chapter.fewestDeaths == 0);
        double inside = lines.Where(chapter => chapter != null).Sum(chapter => chapter.seconds);
        float third = RowWidth / 3f;
        Figure(strip, 0f, third, Texts.Get("records.summary.best"), all ? Clock(sum) : "--",
            Texts.Get(all ? "records.summary.best.note" : "records.summary.best.waiting"), all);
        Figure(strip, third, third, Texts.Get("records.summary.clean"), clean + " / " + lines.Length, Texts.Get("records.summary.clean.note"), clean > 0);
        Figure(strip, third * 2f, third, Texts.Get("records.summary.time"), Hours(inside), Texts.Get("records.summary.time.note"), inside > 0d);
        reveal.Card(strip, KitButtons.Lid(strip), at);
    }

    // One third of the campaign strip: a rule at its left (not the first), its name, its value large, a faint note.
    private static void Figure(RectTransform strip, float x, float width, string label, string value, string note, bool lit)
    {
        if (x > 0f) KitStyle.Bar("Rule", strip, new Vector2(x, 18f), new Vector2(1f, SummaryHeight - 36f), Rule);
        var name = KitStyle.Flat(KitStyle.TopLeft("Label", strip, new Vector2(x + 22f, 12f), new Vector2(width - 34f, 20f)), label, 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        name.fontStyle = FontStyles.Normal;
        Shrink(KitStyle.Flat(KitStyle.TopLeft("Value", strip, new Vector2(x + 20f, 32f), new Vector2(width - 32f, 38f)), value, 30f,
            TextAlignmentOptions.MidlineLeft, lit ? KitStyle.Text : Dim), 18f);
        var small = KitStyle.Flat(KitStyle.TopLeft("Note", strip, new Vector2(x + 22f, 70f), new Vector2(width - 34f, 18f)), note, 12f,
            TextAlignmentOptions.MidlineLeft, Hint);
        small.fontStyle = FontStyles.Normal;
        small.overflowMode = CombatUIAssets.ClippedOverflow(small);
    }

    // The right side: the totals and the last games, or the picked chapter's page. Built anew on every change and grown
    // in, so a press visibly swaps it.
    private void BuildSide(float at)
    {
        if (side != null) Destroy(side.gameObject);
        side = KitStyle.Stretch("Side", content);
        FitSide();
        sheet = null;
        picture = null;
        var reveal = side.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        var records = PlayerRecords.Current;
        if (picked == null)
        {
            BuildTotals(records, reveal, ref at);
            BuildRecent(records, reveal, ref at);
        }
        else BuildChapter(picked, records, reveal, ref at);
        reveal.Open();
    }

    // The right side was laid out for a 1920 px wide page (x 820 to 1846). Where the page is narrower it moves left, never
    // onto the ledger, and grows smaller about its top left until its right edge is back on the screen: at 16:10 its right
    // ran 25 px past the edge, at 4:3 nearly 200.
    private void FitSide()
    {
        float width = root.rect.width > 0f ? root.rect.width : 1920f;
        float from = edge + LeftWidth + SideGap, room = width - edge - from;
        sideScale = Mathf.Clamp(room / RightWidth, .6f, 1f);
        float x = Mathf.Max(from, Mathf.Min(RightX, width - edge - RightWidth * sideScale));
        side.pivot = new Vector2(0f, 1f);
        side.localScale = new Vector3(sideScale, sideScale, 1f);
        side.anchoredPosition = new Vector2(x - RightX * sideScale, 0f);
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
        Games(records.recent.Take(TotalsGames), 388f, false, null, Texts.Get("records.recent.empty"), reveal, ref at);
    }

    // The picked chapter's page: its picture with its number, name and dates; its own tiles; its last games.
    private void BuildChapter(string map, PlayerRecords.Data records, KitReveal reveal, ref float at)
    {
        var line = PlayerRecords.ChapterOf(map);
        bool done = Escaped(line);
        // Presses inside the page are not on empty space (OnPointerClick).
        sheet = KitStyle.TopLeft("Sheet", side, new Vector2(RightX, 30f), new Vector2(RightWidth, SideBottom - 30f));
        Caption(side, Texts.Get("records.chapter.caption"), new Vector2(RightX, 40f), RightWidth, Texts.Get("records.chapter.hint"), true);
        Banner(map, line, done, reveal, at);
        at += .06f;

        int games = line?.games ?? 0, escapes = line?.escapes ?? 0;
        long kills = line?.kills ?? 0;
        string tries = games > 0 ? Texts.Format("records.chapter.tries", Count(games), Mathf.Min(100, Mathf.RoundToInt(100f * escapes / games))) : "";
        string share = kills > 0 ? Texts.Format("records.chapter.headshots", Mathf.RoundToInt(100f * line.headshots / kills)) : "";
        var tiles = new (string label, string value, string note)[]
        {
            (Texts.Get("records.col.escapes"), Count(escapes), tries),
            (Texts.Get("records.col.best"), done ? Clock(line.best) : "--", done ? Day(line.bestWhen) : ""),
            (Texts.Get("records.col.fewest"), done ? Count(line.fewestDeaths) : "--", done && line.fewestDeaths == 0 ? Texts.Get("records.chapter.clean") : ""),
            (Texts.Get("records.play_time"), Hours(line?.seconds ?? 0d), ""),
            (Texts.Get("records.kills"), Count(kills), share),
            (Texts.Get("records.chapter.most_kills"), Count(line?.mostKills ?? 0), ""),
            (Texts.Get("records.downs"), Count(line?.downs ?? 0), ""),
            (Texts.Get("records.deaths"), Count(line?.deaths ?? 0), ""),
        };
        float top = 68f + BannerHeight + TileGap;
        Tiles(tiles, top, reveal, ref at);
        float below = top + 2f * (TileHeight + TileGap) - TileGap + 36f;
        Caption(side, Texts.Get("records.chapter.games"), new Vector2(RightX, below), RightWidth);
        Games(records.recent.Where(game => game != null && game.map == map).Take(ChapterGames), below + 34f, true, line,
            Texts.Get("records.chapter.games.empty"), reveal, ref at);
    }

    // The chapter's picture across the top of its page, drifting slowly and darkened toward the left under its big
    // number, its name and its dates; a chip at the top right says how often it was escaped, or that it is not yet.
    private void Banner(string map, PlayerRecords.Chapter line, bool done, KitReveal reveal, float at)
    {
        Split(MapTable.DisplayName(map), out string number, out string name, out _);
        var banner = KitStyle.TopLeft("Banner", side, new Vector2(RightX, 68f), new Vector2(RightWidth, BannerHeight));
        KitStyle.Cut(banner, KitStyle.Card);
        banner.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        picture = KitStyle.Stretch("Picture", banner).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        picture.texture = MapTable.Picture(map);
        picture.enabled = picture.texture != null;
        // Not escaped yet: the picture greyer, as the chapter's number is.
        picture.color = done ? Color.white : new Color(.55f, .55f, .58f);
        if (picture.enabled) Pan();
        else
        {
            var icon = KitButtons.IconImage(banner, "Map", 72f, KitStyle.Faint);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1f, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(-180f, 0f);
        }
        var shade = KitStyle.Stretch("Shade", banner).gameObject.AddComponent<RawImage>();
        shade.texture = KitShapes.Fade;
        shade.color = KitStyle.With(KitStyle.Night, .94f);
        shade.raycastTarget = false;
        KitStyle.Fill(KitStyle.Stretch("Dim", banner), KitStyle.With(KitStyle.Night, .2f));

        var big = KitStyle.Flat(KitStyle.TopLeft("Number", banner, new Vector2(30f, 14f), new Vector2(260f, 120f)), "", 112f,
            TextAlignmentOptions.MidlineLeft, done ? KitStyle.Accent : Dim);
        big.characterSpacing = -4f;
        KitScramble.On(big).Decode(number, at + .12f, .35f);
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", banner, new Vector2(36f, 132f), new Vector2(RightWidth - 260f, 50f)), "", 42f,
            TextAlignmentOptions.MidlineLeft);
        KitScramble.On(title).Decode(name, at + .16f, .45f);
        var dates = KitStyle.Flat(KitStyle.TopLeft("Dates", banner, new Vector2(38f, 186f), new Vector2(RightWidth - 260f, 24f)), Dates(line), 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        dates.fontStyle = FontStyles.Normal;

        var chip = KitStyle.TopLeft("Escapes", banner, new Vector2(RightWidth - 28f - 170f, 24f), new Vector2(170f, 38f));
        KitStyle.Fill(chip, done ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .12f));
        KitStyle.Flat(chip, done ? Texts.Format("records.chapter.escaped", Count(line.escapes)) : Texts.Get("records.chapter.waiting"), 16f,
            TextAlignmentOptions.Center, done ? KitStyle.Ink : KitStyle.Soft);
        reveal.Card(banner, KitStyle.Cut(KitStyle.Stretch("Flash", banner), KitStyle.Flash), at);
    }

    // The picture fills the banner without stretching, a little zoomed in so it can drift from side to side.
    private void Pan()
    {
        var texture = picture.texture;
        if (texture == null) return;
        float aspect = texture.width / (float)Mathf.Max(1, texture.height);
        float up = Mathf.Min(1f, PictureZoom * aspect * BannerHeight / RightWidth), margin = (1f - PictureZoom) * .5f;
        float drift = Mathf.Sin(Time.unscaledTime * PanSpeed) * margin;
        picture.uvRect = new Rect(margin + drift, (1f - up) * .5f, PictureZoom, up);
    }

    // "첫 탈출 11.10 15:29   ·   마지막 판 11.11 08:31", as much of it as is known.
    private static string Dates(PlayerRecords.Chapter line)
    {
        var parts = new List<string>();
        if (line != null && !string.IsNullOrEmpty(line.firstEscape)) parts.Add(Texts.Format("records.chapter.first", When(line.firstEscape)));
        if (line != null && !string.IsNullOrEmpty(line.last)) parts.Add(Texts.Format("records.chapter.last", When(line.last)));
        return parts.Count > 0 ? string.Join("   ·   ", parts) : Texts.Get("records.row.never");
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
    private void Games(IEnumerable<PlayerRecords.Game> games, float top, bool chapterPage, PlayerRecords.Chapter line, string empty,
        KitReveal reveal, ref float at)
    {
        float y = top;
        foreach (var game in games)
        {
            if (game == null) continue;
            if (y + GameHeight > SideBottom) break;
            bool best = chapterPage && game.escaped && Escaped(line) && Mathf.Abs(game.seconds - line.best) < .05f;
            GameRow(game, y, chapterPage, best, reveal, at);
            at += .04f;
            y += GameHeight + GameGap;
        }
        if (y > top) return;
        var none = KitStyle.Flat(KitStyle.TopLeft("Empty", side, new Vector2(RightX, top), new Vector2(RightWidth, GameHeight)), empty, 18f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        none.fontStyle = FontStyles.Normal;
    }

    // One game as a row: escaped or left, then on the totals its map, time, kills and deaths, and on a chapter's page
    // its time (최고 beside the fastest escape), kills, headshots, going down and deaths; when it ended at the right.
    private void GameRow(PlayerRecords.Game game, float y, bool chapterPage, bool best, KitReveal reveal, float at)
    {
        var row = KitStyle.TopLeft("Game", side, new Vector2(RightX, y), new Vector2(RightWidth, GameHeight));
        KitStyle.Fill(row, KitStyle.With(KitStyle.Ice, .05f));
        var tag = KitStyle.TopLeft("Result", row, new Vector2(10f, 9f), new Vector2(76f, 30f));
        KitStyle.Fill(tag, game.escaped ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .12f));
        KitStyle.Flat(tag, Texts.Get(game.escaped ? "records.escaped" : "records.left"), 15f, TextAlignmentOptions.Center,
            game.escaped ? KitStyle.Ink : KitStyle.Text);
        if (chapterPage)
        {
            Cell(row, Clock(game.seconds), 106f, 92f, KitStyle.Text, 18f);
            if (best)
            {
                var mark = KitStyle.TopLeft("Best", row, new Vector2(200f, 12f), new Vector2(50f, 24f));
                KitStyle.Fill(mark, KitStyle.With(KitStyle.Accent, .18f));
                KitStyle.Flat(mark, Texts.Get("records.recent.best"), 13f, TextAlignmentOptions.Center, KitStyle.Accent);
            }
            Cell(row, Texts.Format("records.recent.kills", game.kills), 280f, 120f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.headshots", game.headshots), 410f, 130f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.downs", game.downs), 550f, 120f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.deaths", game.deaths), 680f, 110f, KitStyle.Soft, 16f);
        }
        else
        {
            Cell(row, MapTable.DisplayName(game.map), 106f, 380f, KitStyle.Text, 18f);
            Cell(row, Clock(game.seconds), 500f, 120f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.kills", game.kills), 630f, 130f, KitStyle.Soft, 16f);
            Cell(row, Texts.Format("records.recent.deaths", game.deaths), 770f, 110f, KitStyle.Soft, 16f);
        }
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

    // "2026-11-09 21:47" → "11.09"; empty when not known.
    private static string Day(string stamp)
    {
        return DateTime.TryParseExact(stamp, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.ToString("MM.dd", CultureInfo.InvariantCulture) : "";
    }

    // "1장 · 옥상 탈출" → "01", "옥상 탈출" and "1장"; a name with no chapter in front keeps "--" and no label.
    private static void Split(string display, out string number, out string name, out string label)
    {
        number = "--";
        label = "";
        name = display ?? "";
        int dot = name.IndexOf('·');
        if (dot < 0) return;
        string head = name.Substring(0, dot).Trim();
        string digits = new string(head.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return;
        number = int.Parse(digits).ToString("00");
        label = head;
        name = name.Substring(dot + 1).Trim();
    }
}
