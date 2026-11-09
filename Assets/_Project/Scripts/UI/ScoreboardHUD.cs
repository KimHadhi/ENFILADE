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
/// in a room.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScoreboardHUD : MonoBehaviour
{
    private const string ShowAction = "UI/OpenObjectiveBoard";
    private const int RowCount = 10, WeaponColumns = 3;
    private const float FadeSeconds = .16f, TableWidth = 1250f, RowHeight = 48f, RowGap = 4f, HeaderY = 96f, RefreshSeconds = .2f;
    private const float PlayerColumn = 18f, NumberWidth = 70f, WeaponWidth = 130f, IconHeight = 30f, ScoreWidth = 96f, PingWidth = 50f;
    // Left edges across the table.
    private const float KillsAt = 400f, ExecutionsAt = 470f, DownsAt = 540f, DeathsAt = 610f, WeaponsAt = 690f, ScoreAt = 1084f, PingAt = 1190f;
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
        earned = Text("Earned", footer, 36f, new Vector2(0f, -38f), 900f, TextAlignmentOptions.Center);
        played = Text("Played", footer, 19f, new Vector2(0f, -74f), 900f, TextAlignmentOptions.Center);
        played.color = Soft;
        // This player's perk cards (stage 6C), so what they hold is a Tab away.
        perkCaption = Text("Perks Caption", footer, 16f, new Vector2(0f, -110f), 900f, TextAlignmentOptions.Center);
        perkCaption.color = Soft;
        perkCaption.text = "내 능력";
        perkRow = FeelUI.Rect("Perks", footer);
        perkRow.anchoredPosition = new Vector2(0f, -148f);
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
        Cell("Player", row, PlayerColumn, 0f, 300f, 16f, TextAlignmentOptions.Left).text = "플레이어";
        Cell("Kills", row, KillsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = "처치";
        Cell("Executions", row, ExecutionsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = "처형";
        Cell("Downs", row, DownsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = "쓰러짐";
        Cell("Deaths", row, DeathsAt, 0f, NumberWidth, 16f, TextAlignmentOptions.Center).text = "사망";
        Cell("Primary", row, WeaponsAt, 0f, WeaponWidth, 16f, TextAlignmentOptions.Center).text = "주무기";
        Cell("Secondary", row, WeaponsAt + WeaponWidth, 0f, WeaponWidth, 16f, TextAlignmentOptions.Center).text = "보조";
        Cell("Melee", row, WeaponsAt + WeaponWidth * 2f, 0f, WeaponWidth, 16f, TextAlignmentOptions.Center).text = "근접";
        Cell("Score", row, ScoreAt, 0f, ScoreWidth, 16f, TextAlignmentOptions.Center).text = "돈";
        Cell("Ping", row, PingAt, 0f, PingWidth, 16f, TextAlignmentOptions.Center).text = "핑";
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
        row.score = Cell("Score", row.root, ScoreAt, 0f, ScoreWidth, 19f, TextAlignmentOptions.Right);
        row.money = MoneyIcon.Make(row.root, 18f);
        row.money.rectTransform.anchorMin = row.money.rectTransform.anchorMax = new Vector2(0f, .5f);
        row.money.rectTransform.anchoredPosition = new Vector2(ScoreAt + 11f, 0f);
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
        wave.text = waves != null && waves.IsRunning ? "웨이브 " + waves.CurrentWave : "대기 중";
        title.text = MapTable.DisplayName(SceneManager.GetActiveScene().name);
        shown.Clear();
        foreach (var player in PlayerRoster.All)
            if (player.IsInitialized && (player.IsLocal || player.IsNetworkDriven) && shown.Count < rows.Count) shown.Add(player);
        subtitle.text = GameSession.IsOnline ? shown.Count + "명 플레이 중" : "혼자 하기";
        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < shown.Count;
            rows[i].root.gameObject.SetActive(used);
            if (used) Fill(rows[i], shown[i]);
        }
        footer.anchoredPosition = table.anchoredPosition + new Vector2(0f, -HeaderY - 36f - shown.Count * (RowHeight + RowGap) + RowHeight * .5f - 8f);
        earned.text = "돈  " + Wallet.Format(Wallet.Balance);
        int seconds = Mathf.FloorToInt(Time.timeSinceLevelLoad);
        played.text = "플레이 시간 " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
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
