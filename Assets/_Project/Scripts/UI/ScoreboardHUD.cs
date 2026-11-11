using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Held on Tab, in the manner of PAYDAY 2's: the world blurs and darkens behind the objectives at the top left, the map
/// and wave at the top, a line for each player with their numbers, weapons and ping, and what this run has earned below.
/// Built up front so opening it never hitches. Everyone's numbers come from PlayerTally, so it reads the same alone and
/// in a room. Under the table, this player's class and its rank (RankProgress): the insignia this game's growth adds up
/// to, the bar to the next (white what was held, pink this game's kills, pale pink the share of the money held), what
/// this game has brought and what a kill is worth, so growth can be read while playing (the user, 2026-11-11).
/// </summary>
[DisallowMultipleComponent]
public sealed class ScoreboardHUD : MonoBehaviour
{
    private const string ShowAction = "UI/OpenObjectiveBoard";
    private const int RowCount = 10, WeaponColumns = 3;
    private const float FadeSeconds = .16f, TableWidth = 1250f, RowHeight = 48f, RowGap = 4f, HeaderY = 96f, RefreshSeconds = .2f;
    private const float PlayerColumn = 18f, NumberWidth = 70f, WeaponWidth = 130f, IconHeight = 30f, ScoreWidth = 110f, PingWidth = 50f;
    // Left edges across the table.
    private const float KillsAt = 400f, ExecutionsAt = 470f, DownsAt = 540f, DeathsAt = 610f, WeaponsAt = 690f, ScoreAt = 1074f, PingAt = 1190f;
    // The money column: its number's size, the banknote's, and the gap kept between the two (px).
    private const float ScoreSize = 19f, MoneyIconSize = 18f, MoneyGap = 5f;
    private const float GoodPing = 40f, FairPing = 80f;
    private static readonly Color Plate = new Color(0f, 0f, 0f, .62f), Soft = new Color(.82f, .84f, .88f), Gold = new Color(1f, .77f, .25f);
    private static readonly Color PingGood = new Color(.3f, 1f, .35f), PingFair = new Color(1f, .85f, .3f), PingBad = new Color(1f, .35f, .3f);

    private sealed class WeaponCell { public Image icon; public TextMeshProUGUI label; }
    private sealed class Row
    {
        public RectTransform root;
        public Image seat;
        public TextMeshProUGUI name, character, kills, executions, downs, deaths, score, ping;
        public RawImage money;
        public readonly WeaponCell[] weapons = new WeaponCell[WeaponColumns];
    }

    /// <summary>How open the Tab screen is, 0-1. The mission bar makes way as it comes up.</summary>
    public static float Openness { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOpenness() => Openness = 0f;

    private readonly List<Row> rows = new List<Row>();
    private readonly List<PlayerMovement> shown = new List<PlayerMovement>();
    private CanvasGroup group;
    private RectTransform table, footer, perkRow;
    private TextMeshProUGUI wave, title, subtitle, earned, played, perkCaption;
    // The class's rank block (RankProgress): its insignia, the bar's parts, the next insignia and the lines about it.
    private const float RankY = -74f, RankBar = 520f, RankBarLeft = -276f, RankBadge = 64f, RankNext = 40f, RankDrop = 124f;
    private RectTransform rankHeld, rankKills, rankKept;
    private RawImage rankBadge, rankNext;
    private TextMeshProUGUI rankTitle, rankLeft, rankRight, rankWorth;
    private bool perksDirty = true;
    private ObjectiveLogPanel objectives;
    private Volume blur;
    private WaveManager waves;
    private MissionTracker tracker;
    private InputAction show;
    private bool ownsShow;
    private float refreshLeft;

    private void Start()
    {
        waves = FindFirstObjectByType<WaveManager>();
        tracker = FindFirstObjectByType<MissionTracker>();
        var input = GetComponent<PlayerInput>();
        show = (input != null ? input.actions : InputSystem.actions)?.FindAction(ShowAction, false);
        ownsShow = show != null && !show.enabled;
        if (ownsShow) show.Enable();
        var host = new GameObject("Scoreboard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        FeelUI.SetupOverlay(host, 95);
        group = host.GetComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        group.alpha = 0f;
        var dim = FeelUI.Rect("Dim", host.transform);
        dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one;
        var dimFill = dim.gameObject.AddComponent<Image>();
        dimFill.color = new Color(0f, 0f, 0f, .5f);
        dimFill.raycastTarget = false;

        objectives = new ObjectiveLogPanel(host.transform);
        wave = Text("Wave", host.transform, 20f, new Vector2(0f, 300f), 600f, TextAlignmentOptions.Center);
        title = Text("Map", host.transform, 46f, new Vector2(0f, 258f), 900f, TextAlignmentOptions.Center);
        subtitle = Text("Players", host.transform, 22f, new Vector2(0f, 214f), 900f, TextAlignmentOptions.Center);
        subtitle.color = Gold;

        table = FeelUI.Rect("Table", host.transform);
        table.sizeDelta = new Vector2(TableWidth, 0f);
        table.anchoredPosition = new Vector2(0f, HeaderY + 60f);
        Header();
        for (int i = 0; i < RowCount; i++) rows.Add(BuildRow(i));

        footer = FeelUI.Rect("Footer", host.transform);
        var line = FeelUI.Rect("Line", footer);
        line.sizeDelta = new Vector2(TableWidth, 3f);
        var lineFill = line.gameObject.AddComponent<Image>();
        lineFill.color = new Color(1f, 1f, 1f, .55f);
        lineFill.raycastTarget = false;
        BuildRank();
        earned = Text("Earned", footer, 36f, new Vector2(0f, -38f - RankDrop), 900f, TextAlignmentOptions.Center);
        played = Text("Played", footer, 19f, new Vector2(0f, -74f - RankDrop), 900f, TextAlignmentOptions.Center);
        played.color = Soft;
        // This player's perk cards (stage 6C), so what they hold is a Tab away.
        perkCaption = Text("Perks Caption", footer, 16f, new Vector2(0f, -110f - RankDrop), 900f, TextAlignmentOptions.Center);
        perkCaption.color = Soft;
        perkCaption.text = Texts.Get("score.perks");
        perkRow = FeelUI.Rect("Perks", footer);
        perkRow.anchoredPosition = new Vector2(0f, -148f - RankDrop);
        PlayerPerks.Changed += MarkPerks;

        blur = host.AddComponent<Volume>();
        blur.isGlobal = true;
        blur.priority = 210f;
        blur.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave;
        var depth = profile.Add<DepthOfField>(false);
        depth.mode.Override(DepthOfFieldMode.Gaussian);
        depth.gaussianStart.Override(0f);
        depth.gaussianEnd.Override(.5f);
        depth.gaussianMaxRadius.Override(1.5f);
        depth.highQualitySampling.Override(true);
        blur.sharedProfile = profile;
        host.SetActive(false);
    }

    // The rank block under the line: insignia, class, bar with its next insignia, and two small lines under the bar.
    private void BuildRank()
    {
        rankBadge = RankImage("Rank Insignia", new Vector2(RankBarLeft - 14f - RankBadge * .5f, RankY), RankBadge, Color.white);
        rankTitle = Text("Rank Class", footer, 22f, new Vector2(RankBarLeft + 200f, RankY + 22f), 400f, TextAlignmentOptions.Left);
        RankBox("Rank Bar", RankBar, new Color(1f, 1f, 1f, .14f));
        rankHeld = RankBox("Rank Held", 0f, new Color(1f, 1f, 1f, .72f));
        rankKept = RankBox("Rank Kept", 0f, new Color(1f, .62f, .74f, .85f));
        rankKills = RankBox("Rank This Game", 0f, KitStyle.Accent);
        rankNext = RankImage("Rank Next", new Vector2(RankBarLeft + RankBar + 14f + RankNext * .5f, RankY), RankNext, new Color(1f, 1f, 1f, .35f));
        rankRight = Text("Rank To Next", footer, 15f, new Vector2(RankBarLeft + RankBar - 160f, RankY + 22f), 320f, TextAlignmentOptions.Right);
        rankRight.color = Soft;
        rankLeft = Text("Rank Game", footer, 15f, new Vector2(RankBarLeft + 260f, RankY - 22f), 520f, TextAlignmentOptions.Left);
        rankWorth = Text("Rank Worth", footer, 14f, new Vector2(RankBarLeft + 260f, RankY - 42f), 520f, TextAlignmentOptions.Left);
        rankWorth.color = Soft;
    }

    private RawImage RankImage(string name, Vector2 at, float size, Color tint)
    {
        var rect = FeelUI.Rect(name, footer);
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.color = tint;
        image.raycastTarget = false;
        return image;
    }

    private RectTransform RankBox(string name, float width, Color tint)
    {
        var rect = FeelUI.Rect(name, footer);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(RankBarLeft, RankY);
        rect.sizeDelta = new Vector2(width, 8f);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = tint;
        image.raycastTarget = false;
        return rect;
    }

    private void RefreshRank()
    {
        var view = RankProgress.Now();
        rankBadge.texture = ClassGrowth.Insignia(view.Rank);
        rankBadge.enabled = rankBadge.texture != null;
        rankNext.texture = view.Top ? null : ClassGrowth.Insignia(view.Rank + 1);
        rankNext.enabled = rankNext.texture != null;
        rankTitle.text = view.Title;
        float held = view.StoredShare, total = view.Share;
        float kills = view.Earned > 0 ? Mathf.Lerp(held, total, view.Kills / (float)view.Earned) : held;
        RankSpan(rankHeld, 0f, held);
        RankSpan(rankKills, held, kills);
        RankSpan(rankKept, kills, total);
        var settings = GunFeelSettings.Instance;
        string game = view.Playing ? Texts.Format("score.rank.game", view.Earned.ToString("N0"))
            + (view.Kept > 0 ? "  <color=#" + ColorUtility.ToHtmlStringRGB(Soft) + ">" + Texts.Format("score.rank.split", view.Kills.ToString("N0"), view.Kept.ToString("N0")) + "</color>" : "")
            : "";
        rankLeft.text = game;
        rankWorth.text = Texts.Format("score.rank.kill", settings.killScore.ToString("N0"), (settings.killScore + settings.headshotBonus).ToString("N0"));
        rankRight.text = view.Climb > 0 ? "<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + ">" + Texts.Format("score.rank.climb", view.Climb) + "</color>  "
            + (view.Top ? Texts.Get("score.rank.top") : Texts.Format("score.rank.next", view.ToNext.ToString("N0")))
            : view.Top ? Texts.Get("score.rank.top") : Texts.Format("score.rank.next", view.ToNext.ToString("N0"));
    }

    private static void RankSpan(RectTransform part, float from, float to)
    {
        part.anchoredPosition = new Vector2(RankBarLeft + RankBar * from, RankY);
        part.sizeDelta = new Vector2(Mathf.Max(0f, RankBar * (to - from)), 8f);
    }

    private static TextMeshProUGUI Text(string name, Transform parent, float size, Vector2 position, float width, TextAlignmentOptions alignment)
    {
        var text = FeelUI.ScoreLabel(name, parent, size, alignment);
        text.rectTransform.sizeDelta = new Vector2(width, size * 1.4f);
        text.rectTransform.anchoredPosition = position;
        return text;
    }

    // Cells hang from the table's left edge, so a column is its x and width.
    private static TextMeshProUGUI Cell(string name, Transform parent, float x, float y, float width, float size, TextAlignmentOptions alignment)
    {
        var text = Text(name, parent, size, Vector2.zero, width, alignment);
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, .5f);
        text.rectTransform.anchoredPosition = new Vector2(x + width * .5f, y);
        return text;
    }

    private void Header()
    {
        var row = FeelUI.Rect("Header", table);
        row.sizeDelta = new Vector2(TableWidth, 24f);
        row.anchoredPosition = new Vector2(0f, -HeaderY);
        Cell("Player", row, PlayerColumn, 0f, 300f, 16f, TextAlignmentOptions.Left).text = Texts.Get("score.col.player");
        Cell("Kills", row, KillsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.kills");
        Cell("Executions", row, ExecutionsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.executions");
        Cell("Downs", row, DownsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.downs");
        Cell("Deaths", row, DeathsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.deaths");
        Cell("Primary", row, WeaponsAt, 0f, WeaponWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.primary");
        Cell("Secondary", row, WeaponsAt + WeaponWidth, 0f, WeaponWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.secondary");
        Cell("Melee", row, WeaponsAt + WeaponWidth * 2f, 0f, WeaponWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.melee");
        Cell("Score", row, ScoreAt, 0f, ScoreWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.money");
        Cell("Ping", row, PingAt, 0f, PingWidth, 16f, TextAlignmentOptions.Center).text = Texts.Get("score.col.ping");
    }

    private Row BuildRow(int index)
    {
        var row = new Row { root = FeelUI.Rect("Row", table) };
        row.root.sizeDelta = new Vector2(TableWidth, RowHeight);
        row.root.anchoredPosition = new Vector2(0f, -HeaderY - 36f - index * (RowHeight + RowGap));
        var plate = row.root.gameObject.AddComponent<Image>();
        plate.color = Plate;
        plate.raycastTarget = false;
        var seat = FeelUI.Rect("Seat", row.root);
        seat.anchorMin = new Vector2(0f, 0f); seat.anchorMax = new Vector2(0f, 1f); seat.pivot = new Vector2(0f, .5f);
        seat.sizeDelta = new Vector2(5f, 0f);
        row.seat = seat.gameObject.AddComponent<Image>();
        row.seat.raycastTarget = false;
        row.name = Cell("Name", row.root, PlayerColumn, 9f, 370f, 21f, TextAlignmentOptions.Left);
        row.character = Cell("Character", row.root, PlayerColumn, -13f, 370f, 13f, TextAlignmentOptions.Left);
        row.character.color = Soft;
        row.kills = Cell("Kills", row.root, KillsAt, 0f, NumberWidth, 20f, TextAlignmentOptions.Center);
        row.executions = Cell("Executions", row.root, ExecutionsAt, 0f, NumberWidth, 20f, TextAlignmentOptions.Center);
        row.downs = Cell("Downs", row.root, DownsAt, 0f, NumberWidth, 20f, TextAlignmentOptions.Center);
        row.deaths = Cell("Deaths", row.root, DeathsAt, 0f, NumberWidth, 20f, TextAlignmentOptions.Center);
        // Money: the banknote at the column's left, the number to its right (MoneyIcon, stage 6A).
        row.score = Cell("Score", row.root, ScoreAt, 0f, ScoreWidth, ScoreSize, TextAlignmentOptions.Right);
        row.money = MoneyIcon.Make(row.root, MoneyIconSize);
        row.money.rectTransform.anchorMin = row.money.rectTransform.anchorMax = new Vector2(0f, .5f);
        row.money.rectTransform.anchoredPosition = new Vector2(ScoreAt + 2f + MoneyIconSize * .5f, 0f);
        row.ping = Cell("Ping", row.root, PingAt, 0f, PingWidth, 19f, TextAlignmentOptions.Center);
        for (int i = 0; i < WeaponColumns; i++)
        {
            float x = WeaponsAt + WeaponWidth * i;
            var label = Cell("Weapon", row.root, x, 0f, WeaponWidth, 14f, TextAlignmentOptions.Center);
            label.color = Soft;
            var icon = FeelUI.Rect("Icon", row.root);
            icon.anchorMin = icon.anchorMax = new Vector2(0f, .5f);
            icon.anchoredPosition = new Vector2(x + WeaponWidth * .5f, 0f);
            icon.sizeDelta = new Vector2(WeaponWidth - 14f, IconHeight);
            var image = icon.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            row.weapons[i] = new WeaponCell { icon = image, label = label };
        }
        row.root.gameObject.SetActive(false);
        return row;
    }

    private void LateUpdate()
    {
        if (group == null) return;
        // The cursor is free while chat or a menu is open, and Tab belongs to them then.
        bool held = show != null && show.IsPressed() && Cursor.lockState == CursorLockMode.Locked && Application.isFocused;
        float next = Mathf.MoveTowards(group.alpha, held ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
        if (next <= 0f && group.alpha <= 0f) return;
        if (!group.gameObject.activeSelf) { group.gameObject.SetActive(true); refreshLeft = 0f; }
        group.alpha = next;
        blur.weight = next;
        Openness = next;
        objectives.SetReveal(next);
        if (next <= 0f) { group.gameObject.SetActive(false); return; }
        refreshLeft -= Time.unscaledDeltaTime;
        if (refreshLeft > 0f) return;
        refreshLeft = RefreshSeconds;
        Refresh();
    }

    private void Refresh()
    {
        wave.text = waves != null && waves.IsRunning ? Texts.Format("score.wave", waves.CurrentWave) : Texts.Get("score.waiting");
        title.text = MapTable.DisplayName(SceneManager.GetActiveScene().name);
        shown.Clear();
        foreach (var player in PlayerRoster.All)
            if (player.IsInitialized && (player.IsLocal || player.IsNetworkDriven) && shown.Count < rows.Count) shown.Add(player);
        subtitle.text = GameSession.IsOnline ? Texts.Format("score.players", shown.Count) : Texts.Get("score.solo");
        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < shown.Count;
            rows[i].root.gameObject.SetActive(used);
            if (used) Fill(rows[i], shown[i]);
        }
        footer.anchoredPosition = table.anchoredPosition + new Vector2(0f, -HeaderY - 36f - shown.Count * (RowHeight + RowGap) + RowHeight * .5f - 8f);
        earned.text = Texts.Format("score.money", Wallet.Format(Wallet.Balance));
        RefreshRank();
        int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad);
        played.text = Texts.Format("score.played", (seconds / 60).ToString("00"), (seconds % 60).ToString("00"));
        if (tracker == null) tracker = FindFirstObjectByType<MissionTracker>();
        objectives.Refresh(tracker);
        if (perksDirty)
        {
            perksDirty = false;
            var badges = PerkBadge.Row(perkRow, PlayerPerks.Held(), 44f, 10f);
            perkCaption.gameObject.SetActive(badges.Count > 0);
        }
    }

    private static void Fill(Row row, PlayerMovement player)
    {
        if (!player.TryGetComponent(out PlayerTally tally)) return;
        row.seat.color = tally.Color;
        row.name.text = tally.DisplayName;
        row.name.color = tally.Color;
        var body = player.GetComponent<PlayerBody>();
        row.character.text = body != null && body.Skin != null ? body.Skin.DisplayName : "";
        row.kills.text = tally.Kills.ToString();
        row.executions.text = tally.Executions.ToString();
        row.downs.text = tally.Downs.ToString();
        row.deaths.text = tally.Deaths.ToString();
        // The money they hold, the only money number in play (Wallet; stage 6A).
        row.score.text = Wallet.Format(tally.Score);
        // A sum too long for the column at full size (100,000 and up ran over the banknote and out of the column; user,
        // 2026-11-10) shrinks until it stands clear of the banknote at the column's left.
        row.score.fontSize = ScoreSize;
        float room = ScoreWidth - 2f - MoneyIconSize - MoneyGap, wide = row.score.GetPreferredValues(row.score.text).x;
        if (wide > room) { row.score.fontSize = ScoreSize * room / wide; wide = room; }
        // The banknote and the number as one group in the middle of the column, however many figures (user, 2026-11-11
        // playtest: a three-figure sum sat far off to the right of the banknote).
        float group = MoneyIconSize + MoneyGap + wide, left = ScoreAt + Mathf.Max(0f, (ScoreWidth - group) * .5f);
        row.money.rectTransform.anchoredPosition = new Vector2(left + MoneyIconSize * .5f, 0f);
        row.score.alignment = TextAlignmentOptions.Left;
        row.score.rectTransform.anchoredPosition = new Vector2(left + MoneyIconSize + MoneyGap + row.score.rectTransform.sizeDelta.x * .5f, 0f);
        row.ping.text = tally.Ping.ToString();
        row.ping.color = tally.Ping < GoodPing ? PingGood : tally.Ping < FairPing ? PingFair : PingBad;
        var slots = player.GetComponent<PlayerWeaponSlots>();
        for (int i = 0; i < WeaponColumns; i++)
        {
            var data = slots != null ? slots.GetWeapon(i) : null;
            bool image = data != null && data.HudIcon != null;
            row.weapons[i].icon.gameObject.SetActive(image);
            if (image) row.weapons[i].icon.sprite = data.HudIcon;
            row.weapons[i].label.text = image ? "" : data != null ? data.DisplayName : "-";
        }
    }

    private void MarkPerks() => perksDirty = true;

    private void OnDestroy()
    {
        PlayerPerks.Changed -= MarkPerks;
        Openness = 0f;
        if (ownsShow) show?.Disable();
        if (group != null) Destroy(group.gameObject);
    }
}
