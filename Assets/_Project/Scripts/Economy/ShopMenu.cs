using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Stage 6B: the rest-stop shop, a menu opened with [F] at a rest stop's counter (CampaignRun), the user's pick over
/// guns hung on the wall. It wears the menus' kit (KitStyle, after the user's main-menu photo) and moves like the user's
/// reference video (KitReveal: frame lines draw, the title slides in, the money counts up, the cards grow out pink one
/// by one and settle dark; KitHover's corner brackets; the X turns red and spins as the panel slides away left). Each
/// thing shows its price as the money icon and a number (Tables/Shop); a click buys it with this PC's own money (Wallet),
/// and the game runs on meanwhile. A gun goes into the gun slot in hand, else an empty gun slot, else the first, and
/// comes out unless the hands are full; one already held isn't sold again. Buying touches only this PC's player, so it
/// needs nobody else: a friend sees the gun once it is drawn (the stance carries it) and the money left on the
/// scoreboard (PlayerTally). While open the cursor is free and the player's move and combat input wait, as for the chat;
/// F or the X closes it (Esc too on the first page), and so does walking away, going down or dying.
/// <para>
/// Pages (2026-11-13, the user through the GM: "상점 스크롤이 너무 많아서 뭐 살 수가 없는데 총은 권총, 샷건, 소총, 이런 걸로
/// 분류 나눠서 들어가서 사게 하고 첫 화면에서 탄약, 쉴드, 체력 보충 바로 있으면 안 돼?"): with 27 guns the one long column ran
/// off the screen. The first page holds the refills as tiles at the top (ammo, shield and health big, with how much is
/// held now; the class's throwable, and a Détente's syringe, only for that class) and under them a plate for each gun
/// category (Shop.csv Category rows) with how many guns it sells, their prices and whether one of them is held. A plate
/// opens a page of only its guns, cheapest first, with a plate back at its top; a page taller than the panel scrolls
/// (KitScroll). Number keys pick what a page shows in its reading order (1 to 9, 0, - and =, each on its key box), Esc or
/// Backspace goes back. A gun's category is its Shop.csv `category` cell, else what its numbers say (AutoGroup), so a
/// modder's gun finds its plate. A held gun and one the money doesn't reach read so at a glance: a white 가진 총 tag on
/// a dimmed card; a red price with what is short beside it.
/// </para>
/// A buy answers on every channel at once (user, 2026-11-10: with only a line of small print they took a buy for
/// nothing and paid for ammo three times): the till rings (KitSound), the money box flashes while its number runs down
/// with what was paid falling away under it, the card reads 구매 완료, and the kill feed keeps a line. A refusal buzzes.
/// Ammo is only sold while a held gun is short of it (탄약 가득 on the tile otherwise): it used to take the money
/// whether or not anything was filled.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ShopMenu : MonoBehaviour
{
    public const string TableName = "Shop";
    // Syringe: a Détente's syringe filled back up (DetenteKit), offered to that class only.
    // Grenade: the class throwable (PlayerGrenades), as many as the row's amount, offered while the class has one.
    // Category: a gun category, a plate on the first page; its amount is its line in the sort by numbers (AutoGroup).
    private enum Kind { Gun, Ammo, Heal, Shield, Syringe, Grenade, Category }
    // The categories the sort by numbers ends in (the ids of Shop.csv's Category rows).
    private const string Pistol = "Pistol", Smg = "SMG", Shotgun = "Shotgun", Rifle = "Rifle", Marksman = "Marksman", Mg = "MG";

    private sealed class Item
    {
        public string Id, Name, Detail, Group = "";
        public Kind Kind;
        public WeaponData Weapon;
        public long Price;
        public float Amount;
        public int Order;
    }

    // A gun category: its plate on the first page and its own page of guns.
    private sealed class GunGroup
    {
        public string Id, Name;
        public int Order;
        public float Threshold;
        public readonly List<Item> Guns = new List<Item>();
    }

    // One thing a page shows that a click or a number key picks: an item to buy, a category to open, or the way back.
    private sealed class Row
    {
        public Item Item;
        public GunGroup Group;
        public bool Back;
        public string Detail = "";
        public TextMeshProUGUI Name, Info, Count, Price, Tag;
        public Image Plate, Bar, TagBack, Flash, Picture;
        public Color PlateColor, BarColor, FlashColor;
        public float FlashAt = float.NegativeInfinity, BoughtAt = float.NegativeInfinity;
        // What the changing labels last showed, so their words are made again only when it changes.
        public int TagState = -1, CountNow = int.MinValue, CountMost = int.MinValue;
        public long ShortBy = long.MinValue;
    }

    // How far from the counter the shop stays open (m), and how long a message stays (s).
    private const float Reach = 3.2f, MessageSeconds = 2.2f;
    // Layout (px at 1920x1080): the panel hangs at the screen's left like the reference's, as tall as its pages need and
    // never taller than the screen leaves (ScreenRoom above and below it).
    private const float Width = 560f, Margin = 48f, Inner = 20f, HeaderHeight = 84f, SectionHeight = 30f, FooterHeight = 46f, ScreenRoom = 40f;
    // The first page: the refills as tiles (ammo, shield and health big, three to a row; the class's own two to a row),
    // then the categories' plates two to a row. A category's page: the plate back, then a card for each gun. Everything
    // leans by Slant (KitSkew; clicks follow the lean) and carries its key box.
    private const float TileHeight = 112f, SmallTileHeight = 62f, GroupHeight = 84f, Gap = 10f, SectionGap = 14f, BackHeight = 40f,
        BackWidth = 132f, CardHeight = 58f, CardGap = 6f, Slant = 10f, KeySize = 22f, PictureHeight = 34f;
    // The panel grows to show this many guns of a category; a category with more scrolls on its page.
    private const int MostCards = 8;
    // When things come in (s after opening; a page turned to brings its cards in sooner).
    private const float CardsFrom = .22f, PageFrom = .02f, CardStagger = .035f, CountFrom = .1f, CountSeconds = .45f;
    // The money box: its narrowest and widest (px; it grows leftward with the number, then the number shrinks), the room
    // the banknote and the edges take beside the number, the number's size, and the X's box beside it.
    private const float MoneyWidth = 150f, MoneyWidest = 250f, MoneyPad = 48f, MoneySize = 20f, CloseSize = 34f;
    // After a buy: how long the card reads 구매 완료 and what was paid falls away under the money (s), how far it falls
    // (px), and how fast the money number runs down to what is left (1/s).
    private const float BoughtSeconds = 1.3f, SpentSeconds = 1.1f, SpentFall = 5f, MoneyRun = 12f;
    // The keys that pick what a page shows, in its reading order: the number row (1 to 9, 0, then - and =) or the pad's.
    private static readonly Key[] RowKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7,
        Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus, Key.Equals };
    private static readonly Key[] PadKeys = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7,
        Key.Numpad8, Key.Numpad9, Key.Numpad0, Key.NumpadMinus, Key.NumpadPlus };
    private static readonly string[] KeyNames = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

    private static ShopMenu instance;
    private static int closedFrame = -1;

    /// <summary>The shop is open on this PC (closing included).</summary>
    public static bool IsOpen => instance != null && instance.open;
    /// <summary>The shop has the keyboard (open, or closed by a key this very frame): Esc mustn't also open the menu.</summary>
    public static bool TakesKeyboard => IsOpen || Time.frameCount == closedFrame;

    private readonly List<Item> stock = new List<Item>();
    private readonly List<GunGroup> groups = new List<GunGroup>();
    private readonly List<Row> rows = new List<Row>();
    // What the number keys pick on the page shown, in its reading order.
    private readonly List<Row> keyed = new List<Row>();
    private PlayerMovement player;
    private Vector3 counter;
    private GameObject canvas;
    private RectTransform panel, sweep;
    private KitReveal reveal, pageReveal;
    private KitScroll page;
    // The category whose page is shown (null: the first page), and the screen the panel was laid out for.
    private GunGroup shownGroup;
    private Vector2Int builtFor;
    private SinglePlayerChatManager chat;
    private bool chatLooked;
    private RectTransform moneyIcon, moneyBox, spentBox, closeBox;
    private Image closeFill, moneyFlash;
    private TextMeshProUGUI money, spent, message;
    private float messageLeft, spentAt = float.NegativeInfinity;
    // The money shown: counts up as the panel opens, runs down after a buy (won).
    private double moneyShown;
    private bool open, playerMapWas, combatMapWas;
    // The frame it opened on: the F that opened it still reads as pressed in this frame's Update, which would close it again
    // at once whenever this Update runs after the counter's (the same trap the gun's customize menu fell into, 2026-11-09).
    private int openedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        closedFrame = -1;
    }

    /// <summary>Opens the shop for this PC's <paramref name="who"/> at the counter at <paramref name="at"/>.</summary>
    public static void Open(PlayerMovement who, Vector3 at)
    {
        if (who == null || IsOpen) return;
        if (instance == null)
        {
            instance = new GameObject("Shop Menu").AddComponent<ShopMenu>();
            RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Ui, "Screens");
        }
        instance.Show(who, at);
    }

    private void Show(PlayerMovement who, Vector3 at)
    {
        player = who;
        counter = at;
        shownGroup = null;
        chatLooked = false;
        // Laid out afresh at each opening: the first page's tiles follow the class (its throwable, a Détente's syringe),
        // and the panel's height the stock and the screen.
        Build();
        canvas.SetActive(true);
        open = true;
        openedFrame = Time.frameCount;
        spentAt = float.NegativeInfinity;
        moneyShown = 0d;
        reveal.Open();
        Say(null, KitStyle.Text);
        ShowPage(null, CardsFrom);
        Refresh();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        HoldInput(true);
    }

    // The X goes red and turns while the panel slides away; the cursor and the player's input come back once it's gone.
    private void BeginClose()
    {
        if (!open || reveal.Closing) return;
        closeFill.color = KitStyle.Red;
        reveal.Close(FinishClose);
    }

    private void FinishClose()
    {
        if (!open) return;
        open = false;
        closedFrame = Time.frameCount;
        if (canvas != null) canvas.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        HoldInput(false);
        if (SettingsMenu.IsOpen) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // The player's move and combat input wait while shopping, as for the chat; whatever was on comes back after.
    private void HoldInput(bool hold)
    {
        var actions = InputSystem.actions;
        if (actions == null) return;
        var moves = actions.FindActionMap("Player", false);
        var combat = actions.FindActionMap("Combat", false);
        if (hold)
        {
            playerMapWas = moves != null && moves.enabled;
            combatMapWas = combat != null && combat.enabled;
            moves?.Disable();
            combat?.Disable();
            return;
        }
        if (playerMapWas) moves?.Enable();
        if (combatMapWas) combat?.Enable();
    }

    private void Update()
    {
        if (!open) return;
        if (reveal.Closing)
        {
            closeBox.localRotation = Quaternion.Euler(0f, 0f, Mathf.MoveTowards(closeBox.localEulerAngles.z, 45f, 45f / KitReveal.CloseSeconds * Time.unscaledDeltaTime));
            return;
        }
        var keyboard = Keyboard.current;
        // Esc goes back a page, and from the first page closes; the interact key that opened it (Player/Interact, read
        // from its keys while the shop holds the map) closes from any page. An Esc that closed the console is the console's.
        bool escape = Time.frameCount != openedFrame && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !DevConsole.TakesKeyboard;
        bool closing = (escape && shownGroup == null) || (Time.frameCount != openedFrame && KeyBindings.Pressed("Player/Interact"));
        if (closing || player == null || !player.IsInitialized || player.IsDead || player.DamageReceiver.IsDowned
            || (player.transform.position - counter).sqrMagnitude > Reach * Reach)
        {
            BeginClose();
            return;
        }
        if (Screen.width != builtFor.x || Screen.height != builtFor.y) Relayout();
        if (messageLeft > 0f && (messageLeft -= Time.unscaledDeltaTime) <= 0f) Say(null, KitStyle.Text);
        bool keys = keyboard != null && !KeysBlocked();
        if (shownGroup != null && (escape || (keys && keyboard.backspaceKey.wasPressedThisFrame))) GoBack();
        else if (keys) PickByKey(keyboard);
        Refresh();
    }

    private void OnDestroy()
    {
        if (open) FinishClose();
        if (instance == this) instance = null;
    }
}
