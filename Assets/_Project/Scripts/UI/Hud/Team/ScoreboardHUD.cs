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
/// at the top, a line for each player with their numbers, weapons and ping, and what this run has earned below.
/// Built up front so opening it never hitches. Everyone's numbers come from PlayerTally, so it reads the same alone and
/// in a room. Under the table, this player's class and its rank (RankProgress): the insignia this game's growth adds up
/// to, the bar to the next (white what was held, pink this game's kills, pale pink the share of the money held), what
/// this game has brought and what a kill is worth, so growth can be read while playing (the user, 2026-11-11).
/// A right click while it is held brings the mouse pointer out onto it, and another puts it back (the user, 2026-11-18:
/// "Tab 키를 누른 상태에서 우클릭 입력 시 마우스 커서가 표시되도록 … 카드 툴팁 확인, 팀원 덱 열람, 무기 드랍 등 UI
/// 상호작용 지원"). The board takes only the pointer from the player (InputHold, Held.Cursor: the view and the trigger go
/// with the mouse, the legs stay theirs), so they walk on while they read, and letting Tab go gives the pointer back.
/// What can be pointed at is each part's own business (the cards' badges, a row's deck); a part that takes no pointer
/// stays no raycast target, so the board never swallows a click meant for nothing.
/// Under the table is a deck: the cards of the class played and everyone's cards, each badge in its deck's colour, and a
/// badge pointed at says what its card does (PerkBadge, PerkTooltip). It is this player's own until a row's deck chip is
/// pressed, and then that player's, as their PC sends it (PlayerTally.Cards); pressed again, or the player gone, it is
/// this player's own again (the user, 2026-11-18: "Tab 현황판에서 팀원 옆 덱(Deck) 아이콘 클릭 시 해당 팀원이 현재 보유한
/// 카드 목록 열람").
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ScreenKeys)]
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
        // The class they play: its icon in its colour before the name, its title before the character's under it
        // (the user, 2026-11-18: "Tab 현황판에서 아군이 선택한 클래스 확인 가능하도록"). The id was already sent to every
        // PC (PlayerNetworkScore → PlayerTally.ClassId); the row never drew it.
        public RawImage classIcon;
        // Their deck: a chip at the end of the name's column, two cards (the front one in their class's colour) and how
        // many cards they hold. Pressed, the deck under the table is theirs.
        public Image deckPlate, deckCard;
        public TextMeshProUGUI deckCount;
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
    private TextMeshProUGUI title, subtitle, earned, played, perkCaption;
    // The class's rank block (RankProgress): its insignia, the bar's parts, the next insignia and the lines about it.
    private const float RankY = -74f, RankBar = 520f, RankBarLeft = -276f, RankBadge = 64f, RankNext = 40f, RankDrop = 124f;
    private RectTransform rankHeld, rankKills, rankKept;
    private RawImage rankBadge, rankNext;
    private TextMeshProUGUI rankTitle, rankLeft, rankRight, rankWorth;
    // Whose deck is under the table (null: this PC's own player's), and what of it is drawn there now.
    private PlayerMovement viewed;
    private PlayerTally drawnDeck;
    private int drawnCards = -1;
    private static readonly Color DeckOff = new Color(.055f, .055f, .065f, .86f), DeckOn = new Color(1f, 1f, 1f, .2f);
    private ObjectiveLogPanel objectives;
    private Volume blur;
    private MissionTracker tracker;
    private InputAction show;
    private bool ownsShow;
    private float refreshLeft;
    // The pointer is out on the board: this holds it (InputHold) until a second right click or Tab let go. And the frame
    // it came out in, whose right click is not also the one that puts it away.
    private bool pointer;
    private int pointerFrame = -1;
    private TextMeshProUGUI pointerHint;

    private void Start()
    {
        tracker = SceneOne<MissionTracker>.Get();
        var input = GetComponent<PlayerInput>();
        show = (input != null ? input.actions : InputSystem.actions).OrNull()?.FindAction(ShowAction, false);
        ownsShow = show != null && !show.enabled;
        if (ownsShow) show.Enable();
        // The raycaster is for the pointer a right click brings out; until then the group takes no pointer at all.
        var host = new GameObject("Scoreboard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup),
            typeof(GraphicRaycaster));
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
        title = Text("Map", host.transform, 46f, new Vector2(0f, 258f), 900f, TextAlignmentOptions.Center);
        subtitle = Text("Players", host.transform, 22f, new Vector2(0f, 214f), 900f, TextAlignmentOptions.Center);
        subtitle.color = Gold;

        table = FeelUI.Rect("Table", host.transform);
        table.sizeDelta = new Vector2(TableWidth, 0f);
        table.anchoredPosition = new Vector2(0f, HeaderY + 60f);
        Header();
        for (int i = 0; i < RowCount; i++) rows.Add(BuildRow(i));
        // What the right mouse button does here, over the table's right end.
        pointerHint = Text("Pointer Hint", table, GameTables.Number(HudCamera.TableName, "Board", "hintSize", 15f),
            new Vector2(TableWidth * .5f - 230f, -HeaderY + GameTables.Number(HudCamera.TableName, "Board", "hintRise", 30f)), 460f,
            TextAlignmentOptions.Right);
        pointerHint.color = Soft;
        pointerHint.text = Texts.Get("score.pointer.show");

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
        // What a kill is worth in experience: the hit feedback addon's kill row (HitFeedback.csv Points.kill, every kind
        // alike, a headshot's too) at Money.csv experiencePerScore, not its money.
        string killWorth = Mathf.RoundToInt((float)(HudAddons.HitFeedbackModule.KillPoints * Wallet.ExperiencePerScore)).ToString("N0");
        rankWorth.text = Texts.Format("score.rank.kill", killWorth, killWorth);
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
        // The class icon stands where the player column began; the name and the line under it start past it and end
        // short of the kills column.
        float classSize = Mathf.Max(0f, GameTables.Number(HudCamera.TableName, "Board", "classIcon", 30f));
        float textLeft = PlayerColumn + classSize + Mathf.Max(0f, GameTables.Number(HudCamera.TableName, "Board", "classGap", 8f));
        // The deck chip stands at the column's right end, and the name ends short of it.
        float deckWidth = Mathf.Max(0f, GameTables.Number(HudCamera.TableName, "Board", "deckWidth", 46f));
        float deckLeft = KillsAt - 10f - deckWidth;
        float textWidth = deckLeft - textLeft - 8f;
        BuildDeck(row, index, deckLeft, deckWidth, Mathf.Max(0f, GameTables.Number(HudCamera.TableName, "Board", "deckHeight", 30f)));
        var classRect = FeelUI.Rect("Class", row.root);
        classRect.anchorMin = classRect.anchorMax = new Vector2(0f, .5f);
        classRect.anchoredPosition = new Vector2(PlayerColumn + classSize * .5f, 0f);
        classRect.sizeDelta = new Vector2(classSize, classSize);
        row.classIcon = classRect.gameObject.AddComponent<RawImage>();
        row.classIcon.raycastTarget = false;
        row.classIcon.enabled = false;
        row.name = Cell("Name", row.root, textLeft, 9f, textWidth, 21f, TextAlignmentOptions.Left);
        row.character = Cell("Character", row.root, textLeft, -13f, textWidth, 13f, TextAlignmentOptions.Left);
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
            // A weapon cell takes no pointer: giving or dropping a gun from the board waits for its rule (the user,
            // 2026-11-18: 추후 논의). When that is settled the cell becomes a raycast target here and asks the rule.
            row.weapons[i] = new WeaponCell { icon = image, label = label };
        }
        row.root.gameObject.SetActive(false);
        return row;
    }

    // A row's deck chip: a leaning plate that takes the pointer where it is drawn (KitSkew filters its square), an edge
    // that lights under the pointer, two small cards and the number of cards held.
    private void BuildDeck(Row row, int index, float left, float width, float height)
    {
        const float Slant = 6f;
        var chip = FeelUI.Rect("Deck", row.root);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, .5f);
        chip.anchoredPosition = new Vector2(left + width * .5f, 0f);
        chip.sizeDelta = new Vector2(width, height);
        var edgeRect = KitStyle.Stretch("Edge", chip);
        edgeRect.sizeDelta = new Vector2(2f, 2f);
        var edge = KitStyle.Fill(edgeRect, Color.white);
        KitSkew.On(edge, Slant);
        row.deckPlate = KitStyle.Fill(KitStyle.Stretch("Plate", chip), DeckOff);
        KitSkew.On(row.deckPlate, Slant);
        row.deckPlate.raycastTarget = true;
        KitStyle.Bar("Card Behind", chip, new Vector2(9f, height * .5f - 9f), new Vector2(11f, 15f), KitStyle.Faint);
        row.deckCard = KitStyle.Bar("Card", chip, new Vector2(13f, height * .5f - 6f), new Vector2(11f, 15f), Soft);
        row.deckCount = KitStyle.Flat(KitStyle.TopLeft("Count", chip, new Vector2(26f, 0f), new Vector2(Mathf.Max(0f, width - 30f), height)), "0", 15f,
            TextAlignmentOptions.Midline);
        var button = chip.gameObject.AddComponent<Button>();
        // The edge is what shows the pointer on the chip: unseen until then.
        button.targetGraphic = edge;
        var tint = button.colors;
        tint.normalColor = tint.selectedColor = tint.disabledColor = new Color(1f, 1f, 1f, 0f);
        tint.highlightedColor = new Color(1f, 1f, 1f, .85f);
        tint.pressedColor = KitStyle.Accent;
        tint.fadeDuration = .06f;
        button.colors = tint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => View(index));
    }

    // A row's deck chip was pressed: the deck under the table is that player's; their own row, or the same chip again,
    // makes it this PC's own player's.
    private void View(int index)
    {
        var player = index >= 0 && index < shown.Count ? shown[index] : null;
        viewed = player == null || player.IsLocal || player == viewed ? null : player;
        refreshLeft = 0f;
    }

    // The board is up: Tab is held and the window has the focus. Tab belongs to whatever other screen has the pointer
    // (the chat, a menu, the shop); the board's own hold of the pointer doesn't count, it stays up under it. Until
    // 2026-11-18 this asked whether the pointer was locked, so the board could never show one.
    private bool Up => show != null && show.IsPressed() && Application.isFocused && !InputHold.AnyBesides(this, Held.Cursor);

    // The right click that brings the pointer out is the board's, and the right button is the guard key too: the pointer
    // is taken here, before anything reads the mouse this frame (FrameOrder.ScreenKeys), so the gun, the fists and the
    // class keys find that click already a screen's. Taken in LateUpdate, as it was until 2026-11-18, the click had been
    // read by all of them first: a held grenade's pin went back, a barricade's plan was called off, the sights came up
    // for a frame. The click that puts the pointer away is the other way round: it is let go of in LateUpdate, after
    // everything has read this frame with the pointer still out.
    private void Update()
    {
        if (group == null || pointer || !Up || Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame) return;
        SetPointer(true);
        pointerFrame = Time.frameCount;
    }

    private void LateUpdate()
    {
        if (group == null) return;
        bool held = Up;
        if (pointer && (!held || (pointerFrame != Time.frameCount && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)))
            SetPointer(false);
        float next = Mathf.MoveTowards(group.alpha, held ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
        if (next <= 0f && group.alpha <= 0f) return;
        if (!group.gameObject.activeSelf) { group.gameObject.SetActive(true); refreshLeft = 0f; }
        group.alpha = next;
        blur.weight = next;
        Openness = next;
        objectives.SetReveal(next);
        // Closed: the next time it opens, the deck under the table is this player's own again.
        if (next <= 0f) { group.gameObject.SetActive(false); viewed = null; return; }
        refreshLeft -= Time.unscaledDeltaTime;
        if (refreshLeft > 0f) return;
        refreshLeft = RefreshSeconds;
        Refresh();
    }

    // Brings the pointer out onto the board or puts it back. Only the pointer is taken (Held.Cursor: the view and the
    // trigger go with the mouse); the player's legs stay theirs, so the board can be read on the move.
    private void SetPointer(bool on)
    {
        if (pointer == on) return;
        pointer = on;
        if (on) InputHold.Take(this, Held.Cursor);
        else InputHold.Release(this);
        group.interactable = group.blocksRaycasts = on;
        if (pointerHint != null) pointerHint.text = Texts.Get(on ? "score.pointer.hide" : "score.pointer.show");
        // A pointer put away never "leaves" the badge it was on: its card's panel is taken down here.
        if (!on) PerkTooltip.HideOn(group.transform);
    }

    private void Refresh()
    {
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
        if (tracker == null) tracker = SceneOne<MissionTracker>.Get();
        objectives.Refresh(tracker);
        RefreshDeck();
    }

    // The deck under the table: this PC's own player's, or the player's whose chip was pressed while they are still on
    // the board. Drawn again only when it is another deck or its cards changed (a pick, a class picked, their PC's word).
    private void RefreshDeck()
    {
        if (viewed != null && (!shown.Contains(viewed) || !viewed.TryGetComponent(out PlayerTally _))) viewed = null;
        TryGetComponent(out PlayerTally own);
        var deck = viewed != null ? viewed.GetComponent<PlayerTally>() : own;
        if (deck == null) return;
        var cards = deck.Cards;
        if (deck == drawnDeck && deck.CardsVersion == drawnCards) return;
        drawnDeck = deck;
        drawnCards = deck.CardsVersion;
        PerkTooltip.HideOn(group.transform);
        PerkBadge.Row(perkRow, cards, 44f, 10f, true, TableWidth);
        // "내 능력" over this player's own cards (nothing while they hold none), "<name>의 능력" over another's.
        string none = cards.Count == 0 ? "  ·  " + Texts.Get("score.perks.none") : "";
        perkCaption.text = deck == own ? Texts.Get("score.perks") : Texts.Format("score.perks.of", PlayerTally.ColoredNameOf(viewed)) + none;
        perkCaption.gameObject.SetActive(deck != own || cards.Count > 0);
    }

    private void Fill(Row row, PlayerMovement player)
    {
        if (!player.TryGetComponent(out PlayerTally tally)) return;
        row.seat.color = tally.Color;
        row.name.text = tally.DisplayName;
        row.name.color = tally.Color;
        var body = player.GetComponent<PlayerBody>();
        string character = body != null && body.Skin != null ? body.Skin.DisplayName : "";
        // Their class: how a class shows is ClassLook's (colour from ClassColours.csv, icon, title), a mod's class too.
        string title = ClassLook.Title(tally.ClassId);
        Color classColour = ClassLook.Colour(tally.ClassId, Soft);
        var classPicture = ClassLook.Icon(tally.ClassId);
        row.classIcon.texture = classPicture;
        row.classIcon.color = classColour;
        row.classIcon.enabled = classPicture != null;
        row.character.text = title.Length == 0 ? character
            : "<color=#" + ColorUtility.ToHtmlStringRGB(classColour) + ">" + title + "</color>" + (character.Length > 0 ? "  ·  " + character : "");
        // Their deck's chip: the front card in their class's colour, how many cards, lit while theirs is the deck shown.
        int held = tally.Cards.Count;
        row.deckCard.color = classColour;
        row.deckCount.text = held.ToString();
        row.deckCount.color = held > 0 ? KitStyle.Text : Soft;
        row.deckPlate.color = viewed == player ? DeckOn : DeckOff;
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
            // Its picture (WeaponIcons), else its name.
            var picture = WeaponIcons.For(data);
            bool image = picture != null;
            row.weapons[i].icon.gameObject.SetActive(image);
            if (image) row.weapons[i].icon.sprite = picture;
            row.weapons[i].label.text = image ? "" : data != null ? data.DisplayName : "-";
        }
    }

    // Switched off with the pointer out (a scene's end, the player's body taken away): the pointer goes back.
    private void OnDisable() => SetPointer(false);

    private void OnDestroy()
    {
        Openness = 0f;
        if (ownsShow) show?.Disable();
        if (group != null) Destroy(group.gameObject);
    }
}
