using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ResultsShow: The lineup's screen. One part of the class; the rest is in ResultsShow.cs and the files beside it.
public sealed partial class ResultsShow
{
    private void BuildCanvas()
    {
        var host = new GameObject("Results Lineup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas = host.GetComponent<RectTransform>();
        var layer = host.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = Order;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        lineup = host.AddComponent<CanvasGroup>();
        lineup.blocksRaycasts = lineup.interactable = false;
        // Black until the hub is up, then it lifts.
        curtain = KitStyle.Fill(KitStyle.Stretch("Curtain", canvas), Color.black);
        var wait = FeelUI.Rect("Waiting", canvas);
        wait.anchorMin = wait.anchorMax = wait.pivot = new Vector2(1f, 0f);
        wait.anchoredPosition = new Vector2(-56f, 48f);
        wait.sizeDelta = new Vector2(520f, 30f);
        waitCaption = KitStyle.Flat(wait, Texts.Get("results.wait"), 18f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        waitCaption.fontStyle = FontStyles.Normal;
    }

    private void BuildLineup()
    {
        // Letterbox bars, as the escape's banner has them.
        Bar(true);
        Bar(false);
        header = KitStyle.TopLeft("Header", canvas, new Vector2(80f, 40f), new Vector2(900f, 120f));
        header.localRotation = Quaternion.Euler(0f, 0f, 2f);
        KitStyle.Flat(KitStyle.TopLeft("Caption", header, Vector2.zero, new Vector2(600f, 30f)), Texts.Get("results.caption"), 20f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        KitStyle.Flat(KitStyle.TopLeft("Chapter", header, new Vector2(0f, 30f), new Vector2(900f, 64f)), MatchResults.Title, 52f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        KitStyle.Flat(KitStyle.TopLeft("Time", header, new Vector2(4f, 94f), new Vector2(600f, 26f)),
            Texts.Format("results.time", Clock(MatchResults.Seconds), Escaped()), 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        var skip = FeelUI.Rect("Skip", canvas);
        skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1f, 0f);
        skip.anchoredPosition = new Vector2(-56f, 104f);
        skip.sizeDelta = new Vector2(360f, 30f);
        KitStyle.Flat(skip, Texts.Get("results.skip"), 17f, TextAlignmentOptions.MidlineRight, KitStyle.Soft).fontStyle = FontStyles.Normal;

        // Each player's shot in turn, the last-ranked first and the best last; alone, one longer shot.
        float shot = Mathf.Max(.6f, HubLobby.Number("showShot", 2.4f)) * (seats.Count == 1 ? 1.4f : 1f);
        float at = IntroSeconds;
        for (int i = seats.Count - 1; i >= 0; i--)
        {
            var seat = seats[i];
            seat.ShotAt = at;
            seat.ShotLength = shot;
            seat.Side = shots.Count % 2 == 0 ? 1f : -1f;
            seat.Best = i == 0 && ResultsAwards.Of(won, seat.Entry).Count > 0;
            BuildSpot(seat);
            shots.Add(seat);
            at += shot;
        }
        // Then the whole row together, everyone's name under them and the chapter's highlights along the foot (not alone:
        // the one shot was all of them, and its highlights ride it once its camera has settled).
        groupAt = at;
        bool row = seats.Count > 1;
        showEnd = row ? groupAt + Mathf.Max(.3f, HubLobby.Number("showHold", 1.6f)) : groupAt;
        highlightsAt = row ? groupAt + .1f : seats[0].ShotAt + HubLobby.Number("showTitlesAt", .9f) + .2f;
        foreach (var seat in seats) BuildPlate(seat);
        BuildHighlights();
        // The white blink of a cut, over everything but the curtain.
        cut = KitStyle.Fill(KitStyle.Stretch("Cut", canvas), Color.clear);
    }

    private void Bar(bool top)
    {
        var bar = KitStyle.Fill(FeelUI.Rect(top ? "Top bar" : "Bottom bar", canvas), KitStyle.Ink);
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rect.pivot = new Vector2(.5f, top ? 1f : 0f);
        rect.sizeDelta = new Vector2(0f, 96f);
        rect.SetAsFirstSibling();
    }

    // One player's shot on the screen: down the left their titles, at the foot their name, class and three numbers (the
    // player stands on the right third, where the shot's camera puts them).
    private void BuildSpot(Seat seat)
    {
        seat.Spot = KitStyle.Stretch("Spot " + seat.Entry.Name, canvas);
        seat.SpotGroup = seat.Spot.gameObject.AddComponent<CanvasGroup>();
        seat.SpotGroup.alpha = 0f;
        var titles = ResultsAwards.Of(won, seat.Entry);
        for (int i = 0; i < Mathf.Min(titles.Count, MostTitles); i++)
            seat.Slams.Add(BuildTitle(seat.Spot, titles[i], i));

        // The name block at the foot on the left, above the bottom bar.
        var block = KitStyle.TopLeft("Name", seat.Spot, Vector2.zero, new Vector2(820f, 150f));
        block.anchorMin = block.anchorMax = block.pivot = new Vector2(0f, 0f);
        block.anchoredPosition = new Vector2(80f, 118f);
        seat.Name = block;
        KitStyle.Bar("Seat", block, new Vector2(0f, 14f), new Vector2(8f, 92f), seat.Entry.Color);
        KitStyle.Flat(KitStyle.TopLeft("Player", block, new Vector2(24f, 0f), new Vector2(760f, 76f)), seat.Entry.Name, 66f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        string under = ClassGrowth.TitleOf(seat.Entry.ClassId) + (seat.Entry.Escaped ? "" : "  ·  " + Texts.Get("results.left"));
        KitStyle.Flat(KitStyle.TopLeft("Class", block, new Vector2(28f, 74f), new Vector2(760f, 30f)), under, 22f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        KitStyle.Flat(KitStyle.TopLeft("Numbers", block, new Vector2(28f, 108f), new Vector2(780f, 34f)), Numbers(seat.Entry), 24f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        if (!seat.Best) return;
        // The best of the room: a pink MVP chip over the name.
        var chip = KitStyle.TopLeft("MVP", block, new Vector2(30f, -34f), new Vector2(96f, 32f));
        KitSkew.On(KitStyle.Fill(chip, KitStyle.Accent), 8f);
        KitStyle.Flat(chip, Texts.Get("results.mvp"), 20f, TextAlignmentOptions.Center, KitStyle.Ink);
    }

    // A player's three numbers under their name: kills, then what they did most of the rest (damage, or healing for one who
    // healed a lot), and headshots or revives.
    private static string Numbers(MatchResults.Entry entry)
    {
        string Stat(string id, string value) => Texts.Get(id) + "  <b>" + value + "</b>";
        var parts = new List<string> { Stat("results.mine.kills", entry.Kills.ToString("N0")) };
        parts.Add(entry.Healing > entry.Damage * .5f
            ? Stat("results.mine.healing", Mathf.RoundToInt(entry.Healing).ToString("N0"))
            : Stat("results.mine.damage", Mathf.RoundToInt(entry.Damage).ToString("N0")));
        parts.Add(entry.Revives > entry.Headshots
            ? Stat("results.mine.revives", entry.Revives.ToString("N0"))
            : Stat("results.mine.headshots", entry.Headshots.ToString("N0")));
        return string.Join("   ·   ", parts);
    }

    // The name plate under a player in the closing row: their colour, name and class.
    private void BuildPlate(Seat seat)
    {
        seat.Plate = FeelUI.Rect("Plate " + seat.Entry.Name, canvas);
        seat.Plate.sizeDelta = new Vector2(230f, 52f);
        seat.PlateGroup = seat.Plate.gameObject.AddComponent<CanvasGroup>();
        seat.PlateGroup.alpha = 0f;
        var back = KitStyle.Cut(KitStyle.Stretch("Back", seat.Plate), KitStyle.With(KitStyle.Night, .9f));
        KitSkew.On(back, 10f);
        KitStyle.Bar("Seat", seat.Plate, new Vector2(10f, 8f), new Vector2(5f, 36f), seat.Entry.Color);
        KitStyle.Flat(KitStyle.TopLeft("Name", seat.Plate, new Vector2(24f, 2f), new Vector2(200f, 30f)), seat.Entry.Name, 22f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        KitStyle.Flat(KitStyle.TopLeft("Class", seat.Plate, new Vector2(24f, 28f), new Vector2(200f, 20f)),
            ClassGrowth.TitleOf(seat.Entry.ClassId) + (seat.Entry.Escaped ? "" : "  ·  " + Texts.Get("results.left")), 14f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
    }

    // A title down the left of a player's shot: a slanted pink plate with the title and its number, its joke under it.
    private RectTransform BuildTitle(RectTransform spot, ResultsAwards.Won title, int stack)
    {
        var root = KitStyle.TopLeft("Title " + stack, spot, new Vector2(80f, 250f + stack * TitleGap), new Vector2(480f, 96f));
        root.pivot = new Vector2(0f, .5f);
        root.anchoredPosition += new Vector2(0f, -48f);
        root.localRotation = Quaternion.Euler(0f, 0f, stack % 2 == 0 ? 3f : -2f);
        var plate = KitStyle.Cut(KitStyle.TopLeft("Plate", root, Vector2.zero, new Vector2(480f, 62f)), KitStyle.Accent);
        KitSkew.On(plate, 16f);
        KitStyle.Flat(KitStyle.TopLeft("Label", root, new Vector2(20f, 2f), new Vector2(300f, 58f)), title.Award.Label, 30f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ink);
        var number = KitStyle.Flat(KitStyle.TopLeft("Number", root, new Vector2(280f, 2f), new Vector2(184f, 58f)), title.Shown, 32f,
            TextAlignmentOptions.MidlineRight, KitStyle.Ink);
        number.characterSpacing = -2f;
        if (title.Award.IsMoney)
        {
            var icon = MoneyIcon.Make(root, 26f);
            icon.color = KitStyle.Ink;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
            icon.rectTransform.anchoredPosition = new Vector2(464f - number.preferredWidth - 20f, -31f);
        }
        var joke = KitStyle.Flat(KitStyle.TopLeft("Line", root, new Vector2(22f, 64f), new Vector2(450f, 28f)), title.Award.Line, 17f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        joke.fontStyle = FontStyles.Italic;
        root.localScale = Vector3.zero;
        return root;
    }

    // Along the bottom: the chapter's highlights, counting up.
    private void BuildHighlights()
    {
        highlights = FeelUI.Rect("Highlights", canvas);
        highlights.anchorMin = highlights.anchorMax = new Vector2(.5f, 0f);
        highlights.pivot = new Vector2(.5f, 0f);
        highlights.anchoredPosition = new Vector2(0f, 14f);
        highlights.sizeDelta = new Vector2(1500f, 70f);
        var shown = new List<(string id, double value, string stat)>();
        double streak = 0d, farthest = 0d, kills = 0d, money = 0d;
        foreach (var entry in MatchResults.Entries)
        {
            streak = System.Math.Max(streak, entry.BestStreak);
            farthest = System.Math.Max(farthest, entry.Farthest);
            kills += entry.Kills;
            if (entry.IsLocal) money = entry.Earned;
        }
        if (streak > 1) shown.Add(("results.high.streak", streak, "streak"));
        if (farthest > 0) shown.Add(("results.high.farthest", farthest, "farthest"));
        shown.Add((MatchResults.Entries.Count > 1 ? "results.high.kills.team" : "results.high.kills", kills, "kills"));
        shown.Add(("results.high.money", money, "money"));
        float width = 1500f / Mathf.Max(1, shown.Count);
        var awards = ResultsAwards.All();
        for (int i = 0; i < shown.Count; i++)
        {
            var cell = KitStyle.TopLeft("Highlight", highlights, new Vector2(i * width, 0f), new Vector2(width - 16f, 70f));
            KitStyle.Flat(KitStyle.TopLeft("Label", cell, Vector2.zero, new Vector2(width - 16f, 22f)), Texts.Get(shown[i].id), 15f,
                TextAlignmentOptions.Center, KitStyle.Soft).fontStyle = FontStyles.Normal;
            var value = KitStyle.Flat(KitStyle.TopLeft("Value", cell, new Vector2(0f, 20f), new Vector2(width - 16f, 48f)), "0", 36f,
                TextAlignmentOptions.Center, KitStyle.Ice);
            var award = awards.Find(a => a.Stat == shown[i].stat) ?? new ResultsAwards.Award { Stat = shown[i].stat };
            counters.Add((value, shown[i].value, award));
        }
        highlights.localScale = Vector3.zero;
    }
}
