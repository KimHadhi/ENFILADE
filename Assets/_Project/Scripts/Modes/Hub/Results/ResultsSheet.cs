using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The results' last page (memo 37, "연출 마지막엔 KF2처럼 한 장 안에 최고 데미지, 최고 힐, 최고 처치 이런 것들 보기 좋게"):
/// one straight sheet over the darkened hub, as the menus' data pages are (the records' ledger: no leaning plates).
/// Left, a tile per title won (ResultsAwards: what, who, how much); right, this player's own numbers, money as the one
/// number with its banknote (the user's rule), and under them the class's insignia, climbing if the chapter took it up a
/// rank (RankClimb, stage 6E: insignia only, no points). Along the foot: where the party goes next and the key to go on.
/// The tiles drop in one after another; it closes by itself after Tables/Hub sheetSeconds or on a key (ResultsShow).
/// </summary>
[DisallowMultipleComponent]
public sealed class ResultsSheet : MonoBehaviour
{
    private const int Order = 86;
    private const float Width = 1500f, Height = 780f, Header = 120f, Foot = 64f, Gap = 14f;
    // Where the insignia stands on RankClimb's own canvas (px from the screen's middle): under the player's numbers.
    private static readonly Vector2 InsigniaAt = new Vector2(560f, -205f);

    private readonly List<(RectTransform tile, float at)> tiles = new List<(RectTransform, float)>();
    private CanvasGroup group;
    private RectTransform sheet;
    private Image timer;
    private RankClimb climb;
    private float age, seconds;

    /// <summary>It has had its time.</summary>
    public bool Done => age >= seconds;
    /// <summary>How long it has been up (real s).</summary>
    public float Age => age;

    public static ResultsSheet Open(List<ResultsAwards.Won> won)
    {
        var host = new GameObject("Results Sheet", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var page = host.AddComponent<ResultsSheet>();
        page.Build(won);
        return page;
    }

    private void OnDestroy()
    {
        if (climb != null) Destroy(climb.gameObject);
    }

    private void Update()
    {
        // A hitch pauses the sheet rather than eating its time (ResultsShow.Step).
        age += ResultsShow.Step();
        group.alpha = Mathf.Clamp01(age / .2f);
        sheet.localScale = Vector3.one * Mathf.Lerp(.97f, 1f, Ease.Out(age / .25f));
        foreach (var (tile, at) in tiles)
        {
            float t = Ease.Out((age - at) / .18f);
            tile.localScale = new Vector3(1f, Mathf.Lerp(.6f, 1f, t), 1f);
            var fade = tile.GetComponent<CanvasGroup>();
            if (fade != null) fade.alpha = t;
        }
        if (timer != null) timer.rectTransform.localScale = new Vector3(1f - Mathf.Clamp01(age / Mathf.Max(.1f, seconds)), 1f, 1f);
    }

    private void Build(List<ResultsAwards.Won> won)
    {
        seconds = Mathf.Max(2f, HubLobby.Number("sheetSeconds", 12f));
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Order;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        group.alpha = 0f;
        KitStyle.Fill(KitStyle.Stretch("Veil", transform), KitStyle.With(KitStyle.Night, .88f));

        sheet = FeelUI.Rect("Sheet", transform);
        sheet.sizeDelta = new Vector2(Width, Height);
        KitStyle.Cut(KitStyle.Stretch("Back", sheet), KitStyle.Panel);
        KitStyle.Bar("Rail", sheet, Vector2.zero, new Vector2(Width, KitStyle.AccentWidth), KitStyle.Accent);
        BuildHeader();
        BuildAwards(won);
        BuildMine();
        BuildFoot();
        if (MatchResults.Climb != null)
            climb = RankClimb.Play(MatchResults.Climb, seconds, InsigniaAt);
        KitSound.Play(KitSound.Cue.Unseen, .6f);
    }

    private void BuildHeader()
    {
        KitStyle.Flat(KitStyle.TopLeft("Caption", sheet, new Vector2(40f, 22f), new Vector2(600f, 26f)), Texts.Get("results.caption"), 18f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        KitStyle.Flat(KitStyle.TopLeft("Chapter", sheet, new Vector2(40f, 46f), new Vector2(900f, 56f)), MatchResults.Title, 42f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        var time = KitStyle.Flat(KitStyle.TopLeft("Time", sheet, new Vector2(Width - 560f, 34f), new Vector2(520f, 30f)),
            Texts.Format("results.time", ResultsShow.Clock(MatchResults.Seconds), ResultsShow.Escaped()), 20f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        time.fontStyle = FontStyles.Normal;
        KitStyle.Ticks("Scale", sheet, new Vector2(40f, Header - 10f), 60, 24f, 5, 8f, KitStyle.Faint);
    }

    // A tile per title: what (pink) and its joke, who (in their colour), and how much (big).
    private void BuildAwards(List<ResultsAwards.Won> won)
    {
        const float left = 40f, area = 900f, tileHeight = 92f;
        float tileWidth = (area - Gap) * .5f;
        KitStyle.Flat(KitStyle.TopLeft("Awards", sheet, new Vector2(left, Header + 6f), new Vector2(area, 24f)), Texts.Get("results.sheet.awards"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        if (won.Count == 0)
        {
            KitStyle.Flat(KitStyle.TopLeft("None", sheet, new Vector2(left, Header + 40f), new Vector2(area, 40f)), Texts.Get("results.sheet.none"), 22f,
                TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
            return;
        }
        int most = Mathf.FloorToInt((Height - Header - Foot - 40f) / (tileHeight + Gap)) * 2;
        for (int i = 0; i < Mathf.Min(won.Count, most); i++)
        {
            var title = won[i];
            var tile = KitStyle.TopLeft("Award " + title.Award.Id, sheet,
                new Vector2(left + (i % 2) * (tileWidth + Gap), Header + 36f + (i / 2) * (tileHeight + Gap)), new Vector2(tileWidth, tileHeight));
            tile.pivot = new Vector2(.5f, 1f);
            tile.anchoredPosition += new Vector2(tileWidth * .5f, 0f);
            tile.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
            KitStyle.Cut(KitStyle.Stretch("Back", tile), KitStyle.Card);
            KitStyle.Bar("Edge", tile, Vector2.zero, new Vector2(KitStyle.AccentWidth, tileHeight), title.Entry.Color);
            KitStyle.Flat(KitStyle.TopLeft("What", tile, new Vector2(18f, 8f), new Vector2(tileWidth - 30f, 24f)), title.Award.Label, 19f,
                TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
            var joke = KitStyle.Flat(KitStyle.TopLeft("Joke", tile, new Vector2(18f, 32f), new Vector2(tileWidth - 30f, 18f)), title.Award.Line, 13f,
                TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
            joke.fontStyle = FontStyles.Italic;
            KitStyle.Flat(KitStyle.TopLeft("Who", tile, new Vector2(18f, 54f), new Vector2(tileWidth * .55f, 32f)), title.Entry.Name, 22f,
                TextAlignmentOptions.MidlineLeft, title.Entry.Color);
            var value = KitStyle.Flat(KitStyle.TopLeft("Value", tile, new Vector2(tileWidth * .45f, 44f), new Vector2(tileWidth * .55f - 18f, 44f)),
                title.Shown, 34f, TextAlignmentOptions.MidlineRight, KitStyle.Ice);
            if (title.Award.IsMoney) Banknote(tile, value, tileWidth - 18f, 66f, 24f);
            tiles.Add((tile, .2f + i * .05f));
        }
    }

    // This player's own numbers down the right: a straight ledger.
    private void BuildMine()
    {
        const float left = 980f, width = 480f, row = 30f;
        MatchResults.Entry me = null;
        foreach (var entry in MatchResults.Entries) if (entry.IsLocal) { me = entry; break; }
        KitStyle.Flat(KitStyle.TopLeft("Mine", sheet, new Vector2(left, Header + 6f), new Vector2(width, 24f)), Texts.Get("results.sheet.mine"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        if (me == null) return;
        var lines = new List<(string id, string value)>
        {
            ("results.mine.kills", me.Kills.ToString("N0")),
            ("results.mine.headshots", me.Headshots.ToString("N0")),
            ("results.mine.damage", Mathf.RoundToInt(me.Damage).ToString("N0")),
            ("results.mine.healing", Mathf.RoundToInt(me.Healing).ToString("N0")),
            ("results.mine.revives", me.Revives.ToString("N0")),
            ("results.mine.streak", me.BestStreak.ToString("N0")),
            ("results.mine.farthest", me.Farthest > 0f ? me.Farthest.ToString("0.0") + " m" : "-"),
            ("results.mine.downs", me.Downs.ToString("N0")),
        };
        float y = Header + 36f;
        for (int i = 0; i < lines.Count; i++, y += row)
        {
            KitStyle.Flat(KitStyle.TopLeft("Label", sheet, new Vector2(left, y), new Vector2(width * .6f, row)), Texts.Get(lines[i].id), 18f,
                TextAlignmentOptions.MidlineLeft, KitStyle.Text).fontStyle = FontStyles.Normal;
            KitStyle.Flat(KitStyle.TopLeft("Value", sheet, new Vector2(left + width * .5f, y), new Vector2(width * .5f, row)), lines[i].value, 20f,
                TextAlignmentOptions.MidlineRight, KitStyle.Ice);
            KitStyle.Bar("Rule", sheet, new Vector2(left, y + row - 1f), new Vector2(width, 1f), KitStyle.With(KitStyle.Ice, .08f));
        }
        // The money made: the one number, with its banknote (the user's rule for money).
        KitStyle.Flat(KitStyle.TopLeft("Money label", sheet, new Vector2(left, y + 4f), new Vector2(width * .6f, 34f)), Texts.Get("results.mine.money"), 18f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text).fontStyle = FontStyles.Normal;
        var money = KitStyle.Flat(KitStyle.TopLeft("Money", sheet, new Vector2(left + width * .4f, y + 2f), new Vector2(width * .6f, 38f)),
            Wallet.Format(me.Earned), 28f, TextAlignmentOptions.MidlineRight, KitStyle.Ice);
        Banknote(sheet, money, left + width, y + 21f, 26f);
    }

    // The banknote just left of a right-aligned number whose right edge is at x (px from the parent's left), centred at y.
    private static void Banknote(RectTransform parent, TMP_Text number, float right, float y, float size)
    {
        var icon = MoneyIcon.Make(parent, size);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
        icon.rectTransform.anchoredPosition = new Vector2(right - number.preferredWidth - size * .5f - 8f, -y);
    }

    private void BuildFoot()
    {
        string next = HubLobby.IsActive ? HubLobby.Chapter : MatchResults.Next;
        string words = MatchResults.CampaignDone ? Texts.Get("results.sheet.done") : Texts.Format("results.sheet.next", MapTable.DisplayName(next));
        KitStyle.Bar("Rule", sheet, new Vector2(40f, Height - Foot), new Vector2(Width - 80f, 1f), KitStyle.With(KitStyle.Ice, .12f));
        KitStyle.Flat(KitStyle.TopLeft("Next", sheet, new Vector2(40f, Height - Foot + 12f), new Vector2(900f, 34f)), words, 20f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        var key = KitStyle.Flat(KitStyle.TopLeft("Key", sheet, new Vector2(Width - 440f, Height - Foot + 12f), new Vector2(400f, 34f)),
            Texts.Get("results.sheet.key"), 18f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        key.fontStyle = FontStyles.Normal;
        timer = KitStyle.Bar("Timer", sheet, new Vector2(40f, Height - 6f), new Vector2(Width - 80f, 3f), KitStyle.Accent);
        timer.rectTransform.pivot = new Vector2(0f, 1f);
    }
}
