using System;
using System.Collections.Generic;
using System.Globalization;
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
public sealed class ShopMenu : MonoBehaviour
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

    // ---- Picking ----

    // The chat, the console, the settings, the pause menu or the key window has the keyboard: its number keys are theirs.
    private bool KeysBlocked()
    {
        if (DevConsole.TakesKeyboard || SettingsMenu.IsOpen || GamePause.IsPaused || KeyBindings.Busy) return true;
        // Looked for once an opening: a scene without a chat mustn't be searched every frame.
        if (chat == null && !chatLooked)
        {
            chat = FindAnyObjectByType<SinglePlayerChatManager>();
            chatLooked = true;
        }
        return chat != null && chat.BlocksMissionInputThisFrame;
    }

    // A number key picks what the page shows under that key, as a click on it would.
    private void PickByKey(Keyboard keyboard)
    {
        for (int i = 0; i < keyed.Count && i < RowKeys.Length; i++)
        {
            if (!keyboard[RowKeys[i]].wasPressedThisFrame && !keyboard[PadKeys[i]].wasPressedThisFrame) continue;
            Pick(keyed[i]);
            return;
        }
    }

    private void Pick(Row row)
    {
        if (!open || reveal.Closing || row == null) return;
        if (row.Back) GoBack();
        else if (row.Group != null)
        {
            KitSound.Play(KitSound.Cue.Ping, .3f);
            ShowPage(row.Group, PageFrom);
        }
        else if (row.Item != null) Buy(row);
    }

    private void GoBack()
    {
        if (shownGroup == null) return;
        KitSound.Play(KitSound.Cue.Ping, .3f);
        ShowPage(null, PageFrom);
    }

    // ---- Buying ----

    private void Buy(Row row)
    {
        if (!open || reveal.Closing || player == null) return;
        var item = row.Item;
        string why = Refusal(item);
        if (why == null && !Wallet.TrySpend(item.Price)) why = Texts.Get("shop.refuse.money");
        if (why == null && !Hand(item)) { Wallet.Unspend(item.Price); why = Texts.Get("shop.refuse.now"); }
        if (why != null)
        {
            Say(why, KitStyle.Red);
            Pulse(row, new Color(KitStyle.Red.r, KitStyle.Red.g, KitStyle.Red.b, .35f));
            KitSound.Play(KitSound.Cue.Refuse);
            return;
        }
        string paid = "-" + Wallet.Format(item.Price);
        Say(Texts.Format("shop.bought", item.Name, paid), KitStyle.Accent);
        Pulse(row, KitStyle.Flash);
        row.BoughtAt = spentAt = Time.unscaledTime;
        spent.text = paid;
        KitPulse.Play(moneyFlash, KitStyle.Flash, .45f);
        KitSound.Play(KitSound.Cue.Buy);
        CombatFeedHUD.Notice(player, Texts.Format("shop.bought.feed", item.Name, paid), false);
    }

    // A card lights up again and drains, pink for a buy, red for a refusal.
    private static void Pulse(Row row, Color color)
    {
        row.FlashAt = Time.unscaledTime;
        row.FlashColor = color;
    }

    // Why it can't be bought now, or null.
    private string Refusal(Item item)
    {
        var slots = player.GetComponent<PlayerWeaponSlots>();
        var receiver = player.DamageReceiver;
        switch (item.Kind)
        {
            case Kind.Gun:
                if (item.Weapon == null || slots == null) return Texts.Get("shop.refuse.unsold");
                if (Holds(slots, item.Weapon)) return Texts.Get("shop.refuse.owned");
                if (player.CombatInputLocked) return Texts.Get("shop.refuse.now");
                return null;
            case Kind.Ammo:
                return slots == null || !HoldsAnyGun(slots) ? Texts.Get("shop.refuse.nogun") : !slots.NeedsAmmo() ? Texts.Get("shop.refuse.ammofull") : null;
            case Kind.Heal:
                return receiver.Health >= receiver.MaximumHealth - .5f ? Texts.Get("shop.refuse.healthfull") : null;
            case Kind.Shield:
                return receiver.Shield >= item.Amount - .5f ? Texts.Get("shop.refuse.shield") : null;
            case Kind.Syringe:
                return DetenteKit.Local == null ? Texts.Get("shop.refuse.notdetente") : DetenteKit.Local.Full ? Texts.Get("shop.refuse.syringefull") : null;
            case Kind.Grenade:
                return PlayerGrenades.Local == null || PlayerGrenades.Local.Kind == Throwable.None ? Texts.Get("shop.refuse.nogrenade")
                    : !PlayerGrenades.HasRoom(player) ? Texts.Get("shop.refuse.grenadefull") : null;
        }
        return null;
    }

    private bool Hand(Item item)
    {
        var slots = player.GetComponent<PlayerWeaponSlots>();
        var receiver = player.DamageReceiver;
        switch (item.Kind)
        {
            case Kind.Gun:
                int slot = GunSlotFor(slots);
                if (!slots.TrySetWeapon(slot, item.Weapon)) return false;
                if (!slots.HandsFull) slots.SelectSlot(slot);
                return true;
            case Kind.Ammo:
                return slots.RefillAmmo() > 0;
            case Kind.Heal:
                receiver.Heal(item.Amount);
                return true;
            case Kind.Shield:
                receiver.SetShield(Mathf.Max(receiver.Shield, item.Amount));
                return true;
            case Kind.Syringe:
                return DetenteKit.Local != null && DetenteKit.Local.Refill(item.Amount);
            case Kind.Grenade:
                return PlayerGrenades.Refill(player, Mathf.Max(1, Mathf.RoundToInt(item.Amount))) > 0;
        }
        return false;
    }

    // The gun slot in hand, else an empty gun slot, else the first.
    private static int GunSlotFor(PlayerWeaponSlots slots)
    {
        int held = slots.SelectedSlot;
        if (held >= 0 && PlayerWeaponSlots.GetSlotCategory(held) == WeaponCategory.Ranged) return held;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged && slots.GetWeapon(i) == null) return i;
        return 0;
    }

    private static bool Holds(PlayerWeaponSlots slots, WeaponData gun)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) if (slots.GetWeapon(i) == gun) return true;
        return false;
    }

    private static bool HoldsAnyGun(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) if (slots.GetWeapon(i)?.Category == WeaponCategory.Ranged) return true;
        return false;
    }

    // ---- What it sells ----

    private void LoadStock()
    {
        stock.Clear();
        groups.Clear();
        var guns = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && !guns.ContainsKey(entry.weapon.name)) guns[entry.weapon.name] = entry.weapon;
        var byId = new Dictionary<string, GunGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !Enum.TryParse(Text(row, "kind"), true, out Kind kind)) continue;
            // A Texts row the cell names (shop.<id>.name), or plain words; empty takes the gun's name.
            string name = Texts.Cell(Text(row, "name"));
            float amount = Number(row, "amount", 0f);
            int order = (int)Number(row, "order", 0f);
            if (kind == Kind.Category)
            {
                if (byId.ContainsKey(id)) continue;
                var group = new GunGroup { Id = id, Name = name.Length > 0 ? name : id, Order = order, Threshold = amount };
                byId[id] = group;
                groups.Add(group);
                continue;
            }
            WeaponData weapon = null;
            if (kind == Kind.Gun && !guns.TryGetValue(Text(row, "weapon"), out weapon)) continue;
            stock.Add(new Item
            {
                Id = id, Kind = kind, Weapon = weapon, Price = (long)Number(row, "price", 0f), Amount = amount, Order = order,
                Group = kind == Kind.Gun ? Text(row, "category") : "",
                Name = name.Length > 0 ? name : weapon != null ? weapon.DisplayName : id,
                Detail = DetailOf(kind, weapon, amount)
            });
        }
        stock.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        // Each gun under its category: its own cell, else what its numbers say. An id no Category row names gets a plate
        // of its own after the others (its id read as a Texts id or as words).
        foreach (var item in stock)
        {
            if (item.Kind != Kind.Gun) continue;
            string id = item.Group.Length > 0 ? item.Group : AutoGroup(item.Weapon, byId);
            if (!byId.TryGetValue(id, out var group))
            {
                group = new GunGroup { Id = id, Name = Texts.Cell(id), Order = int.MaxValue };
                byId[id] = group;
                groups.Add(group);
            }
            item.Group = group.Id;
            group.Guns.Add(item);
        }
        groups.RemoveAll(group => group.Guns.Count == 0);
        groups.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        // Cheapest first within a category; the table's order between guns of one price.
        foreach (var group in groups)
            group.Guns.Sort((a, b) => a.Price != b.Price ? a.Price.CompareTo(b.Price)
                : a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
    }

    /// <summary>
    /// The category a gun with an empty cell goes under, from its own numbers, so a modder's gun finds its plate. Each
    /// Category row's amount is its line (the default here when it has none). Tried in this order, the first that fits
    /// taking the gun: Shotgun, that many pellets or more; Marksman, single shots by default reaching that far (m; the
    /// line the pack looks draw for marksmen, PackFx class.marksmanRange); a light gun (a pistol's sound, the way the body
    /// knows a pistol, or penetration up to SMG's) is a Pistol when it fires single shots or is automatic from a magazine
    /// up to Pistol's (a machine pistol: the X18), else an SMG; MG, automatic from a magazine that big or bigger (belt-fed);
    /// anything else a Rifle. The table names every gun of its own; this sorts only those it leaves empty.
    /// </summary>
    private static string AutoGroup(WeaponData gun, Dictionary<string, GunGroup> byId)
    {
        float Line(string id, float fallback) => byId.TryGetValue(id, out var group) && group.Threshold > 0f ? group.Threshold : fallback;
        if (gun == null) return Rifle;
        bool single = gun.FireMode == WeaponFireMode.SemiAutomatic || gun.FireMode == WeaponFireMode.Fanning;
        if (gun.Pellets >= Mathf.Max(2f, Line(Shotgun, 2f))) return Shotgun;
        if (single && gun.Range >= Line(Marksman, 120f)) return Marksman;
        if (gun.SoundProfile == GunSoundProfile.Pistol || gun.Penetration <= Line(Smg, 14f))
            return single || gun.MagazineSize <= Line(Pistol, 20f) ? Pistol : Smg;
        if (gun.FireMode == WeaponFireMode.Automatic && gun.MagazineSize >= Line(Mg, 75f)) return Mg;
        return Rifle;
    }

    // What a card says under its name: a gun its shot's damage and its magazine, a refill what it gives.
    private static string DetailOf(Kind kind, WeaponData weapon, float amount)
    {
        switch (kind)
        {
            case Kind.Gun:
                if (weapon == null) return "";
                int damage = Mathf.RoundToInt(weapon.Damage);
                // A shotgun's shot is its pellets: 26×16.
                string shot = weapon.Pellets > 1 ? Texts.Format("shop.gun.pellets", damage, weapon.Pellets) : damage.ToString(CultureInfo.InvariantCulture);
                return Texts.Format("shop.gun.detail", shot, weapon.MagazineSize);
            case Kind.Ammo:
                return Texts.Get("shop.detail.ammo");
            case Kind.Syringe:
                return Texts.Format("shop.detail.syringe", amount.ToString("0"));
            case Kind.Grenade:
                // The class's own throwable by name (세열 수류탄 +1) where the class has one.
                string count = Mathf.Max(1f, amount).ToString("0");
                var info = PlayerGrenades.Local != null ? Throwables.Of(PlayerGrenades.Local.Kind) : null;
                return info != null ? Texts.Format("shop.detail.grenade.named", info.Name, count) : Texts.Format("shop.detail.grenade", count);
            case Kind.Heal:
                return Texts.Format("shop.detail.heal", amount.ToString("0"));
            default:
                return Texts.Format("shop.detail.shield", amount.ToString("0"));
        }
    }

    // Whether the first page offers a refill: a class's own only to that class.
    private static bool Offered(Item item)
    {
        switch (item.Kind)
        {
            case Kind.Syringe: return DetenteKit.Local != null;
            case Kind.Grenade: return PlayerGrenades.Local != null && PlayerGrenades.Local.Kind != Throwable.None;
            default: return true;
        }
    }

    // Ammo, health and the shield are what a fight runs out of: the big tiles.
    private static bool IsBig(Kind kind) => kind == Kind.Ammo || kind == Kind.Heal || kind == Kind.Shield;

    private static string Text(IReadOnlyDictionary<string, string> row, string key) => TableCells.Text(row, key);

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback) => TableCells.Number(row, key, fallback);

    // Each kind's bar down a card's left side: guns white (the pink is the menus' one accent, and too near heal's red),
    // ammo cyan, heal red, shield green.
    private static Color AccentOf(Kind kind) => kind == Kind.Gun ? KitStyle.Text : kind == Kind.Ammo ? KitStyle.Cyan
        : kind == Kind.Heal || kind == Kind.Syringe ? KitStyle.Red : kind == Kind.Grenade ? new Color(1f, .62f, .3f) : KitStyle.Green;

    // ---- Screen ----

    private void Refresh()
    {
        // The money counts up as the panel opens, then follows the balance: at once when it grows, running down in the
        // accent when something was just paid for, so the eye catches the number changing.
        float counting = Mathf.Clamp01((reveal.Elapsed - CountFrom) / CountSeconds);
        long balance = Wallet.Balance;
        if (counting < 1f) moneyShown = balance * (double)KitReveal.EaseOut(counting);
        else if (moneyShown - balance < 1d) moneyShown = balance;
        else moneyShown = Math.Max(balance, moneyShown - Math.Max(1d, (moneyShown - balance) * MoneyRun * Time.unscaledDeltaTime));
        money.text = Wallet.Format((long)Math.Round(moneyShown));
        money.color = counting >= 1f && moneyShown - balance >= 1d ? KitStyle.Accent : KitStyle.Text;
        // The box grows leftward with the number (100,000 and up ran out of it; user, 2026-11-10); past its widest the
        // number shrinks instead. The banknote stands just left of the number.
        float natural = money.GetPreferredValues(money.text).x * MoneySize / Mathf.Max(1f, money.fontSize);
        float room = MoneyWidest - MoneyPad, width = Mathf.Min(natural, room);
        money.fontSize = natural > room ? MoneySize * room / natural : MoneySize;
        float box = Mathf.Max(MoneyWidth, width + MoneyPad);
        moneyBox.sizeDelta = new Vector2(box, CloseSize);
        moneyBox.anchoredPosition = new Vector2(Width - Inner - CloseSize - 8f - box, -18f);
        moneyIcon.anchoredPosition = new Vector2(-width - 18f, 0f);
        float now = Time.unscaledTime;
        // What was just paid falls away under the money box and fades.
        float falling = Mathf.Clamp01((now - spentAt) / SpentSeconds);
        spent.color = KitStyle.With(KitStyle.Accent, 1f - falling * falling);
        spentBox.anchoredPosition = new Vector2(Width - Inner - CloseSize - 18f - spentBox.sizeDelta.x, -(18f + CloseSize + 1f + SpentFall * falling));
        var slots = player != null ? player.GetComponent<PlayerWeaponSlots>() : null;
        foreach (var row in rows)
        {
            if (row.Back) continue;
            if (row.Group != null) RefreshGroup(row, slots, balance);
            else if (row.Item != null) RefreshItem(row, slots, balance, now);
            // A buy's pulse, once the page's opening has let go of the card's flash.
            if (pageReveal == null || pageReveal.enabled || now - row.FlashAt > KitReveal.DrainSeconds + .05f) continue;
            var flash = row.FlashColor;
            flash.a *= 1f - Mathf.Clamp01((now - row.FlashAt) / KitReveal.DrainSeconds);
            row.Flash.color = flash;
        }
    }

    // An item's card as things stand: just bought, held, nothing to do (full), short of money, or for sale. A held gun's
    // card dims with a white 가진 총 tag; a gun the money doesn't reach shows its price red and what is short.
    private void RefreshItem(Row row, PlayerWeaponSlots slots, long balance, float now)
    {
        var item = row.Item;
        bool bought = now - row.BoughtAt < BoughtSeconds;
        bool held = item.Kind == Kind.Gun && slots != null && item.Weapon != null && Holds(slots, item.Weapon);
        string note = player == null || held ? "" : Note(item, slots);
        long shortBy = item.Price - balance;
        bool idle = held || note.Length > 0;
        row.Name.color = idle ? KitStyle.Soft : KitStyle.Text;
        row.Price.color = idle ? KitStyle.Soft : shortBy > 0 ? KitStyle.Red : KitStyle.Text;
        row.Plate.color = held ? KitStyle.With(KitStyle.Night, .92f) : row.PlateColor;
        row.Bar.color = held ? KitStyle.Ice : row.BarColor;
        if (row.Picture != null) row.Picture.color = held ? KitStyle.Soft : KitStyle.Text;
        if (row.Tag != null)
        {
            // A gun card's tag: 구매 완료 for a moment, then 가진 총; else what the money is short by.
            int state = bought ? 1 : held ? 2 : shortBy > 0 ? 3 : 0;
            if (state == row.TagState && (state != 3 || shortBy == row.ShortBy)) return;
            row.TagState = state;
            row.ShortBy = shortBy;
            row.Tag.text = state == 1 ? Texts.Get("shop.note.bought") : state == 2 ? Texts.Get("shop.note.owned")
                : state == 3 ? Texts.Format("shop.note.short", Wallet.Format(shortBy)) : "";
            row.Tag.color = state == 1 ? KitStyle.Accent : state == 2 ? KitStyle.Ink : KitStyle.Red;
            row.TagBack.color = state == 2 ? KitStyle.Ice : Color.clear;
            return;
        }
        // A tile's small print gives way to what stops a buy (탄약 가득, 체력 가득 …) and, for a moment, to 구매 완료.
        row.Info.text = bought ? Texts.Get("shop.note.bought") : note.Length > 0 ? note : row.Detail;
        row.Info.color = bought ? KitStyle.Accent : note.Length > 0 ? KitStyle.Text : KitStyle.Soft;
        if (row.Count == null) return;
        if (!Amounts(item, out int have, out int most)) have = most = -1;
        if (have == row.CountNow && most == row.CountMost) return;
        row.CountNow = have;
        row.CountMost = most;
        row.Count.text = have < 0 ? "" : have.ToString(CultureInfo.InvariantCulture) + " / " + most.ToString(CultureInfo.InvariantCulture);
    }

    // A category's plate: 가진 총 while a gun of it is held; its prices red while even its cheapest is out of reach.
    private static void RefreshGroup(Row row, PlayerWeaponSlots slots, long balance)
    {
        bool held = false;
        if (slots != null)
            foreach (var gun in row.Group.Guns)
                if (gun.Weapon != null && Holds(slots, gun.Weapon)) { held = true; break; }
        row.Price.color = row.Group.Guns[0].Price > balance ? KitStyle.Red : KitStyle.Soft;
        int state = held ? 2 : 0;
        if (state == row.TagState) return;
        row.TagState = state;
        row.Tag.text = held ? Texts.Get("shop.note.owned") : "";
        row.Tag.color = KitStyle.Ink;
        row.TagBack.color = held ? KitStyle.Ice : Color.clear;
    }

    // How much of a refill is held now, for the tiles with a number to show: health, the shield, the throwables, the syringe.
    private bool Amounts(Item item, out int now, out int most)
    {
        now = most = 0;
        var receiver = player != null ? player.DamageReceiver : null;
        switch (item.Kind)
        {
            case Kind.Heal:
                if (receiver == null) return false;
                now = Mathf.CeilToInt(receiver.Health - .01f);
                most = Mathf.RoundToInt(receiver.MaximumHealth);
                return true;
            case Kind.Shield:
                if (receiver == null) return false;
                now = Mathf.CeilToInt(receiver.Shield - .01f);
                most = Mathf.Max(now, Mathf.RoundToInt(item.Amount));
                return true;
            case Kind.Grenade:
                if (PlayerGrenades.Local == null) return false;
                now = PlayerGrenades.Local.Count;
                most = PlayerGrenades.Local.Carry;
                return true;
            case Kind.Syringe:
                if (DetenteKit.Local == null) return false;
                now = Mathf.RoundToInt(DetenteKit.Local.Syringe);
                most = Mathf.RoundToInt(DetenteKit.Local.SyringeMax);
                return true;
        }
        return false;
    }

    private string Note(Item item, PlayerWeaponSlots slots)
    {
        if (item.Kind == Kind.Gun && slots != null && item.Weapon != null && Holds(slots, item.Weapon)) return Texts.Get("shop.note.owned");
        if (item.Kind == Kind.Ammo && slots != null && HoldsAnyGun(slots) && !slots.NeedsAmmo()) return Texts.Get("shop.note.ammofull");
        if (item.Kind == Kind.Heal && player.DamageReceiver.Health >= player.DamageReceiver.MaximumHealth - .5f) return Texts.Get("shop.note.healthfull");
        if (item.Kind == Kind.Shield && player.DamageReceiver.Shield >= item.Amount - .5f) return Texts.Get("shop.note.shield");
        if (item.Kind == Kind.Syringe) return DetenteKit.Local == null ? Texts.Get("shop.note.notdetente") : DetenteKit.Local.Full ? Texts.Get("shop.note.syringefull") : "";
        if (item.Kind == Kind.Grenade)
            return PlayerGrenades.Local == null || PlayerGrenades.Local.Kind == Throwable.None ? Texts.Get("shop.note.nogrenade")
                : !PlayerGrenades.HasRoom(player) ? Texts.Get("shop.note.grenadefull") : "";
        return "";
    }

    private void Say(string text, Color color)
    {
        if (message == null) return;
        message.text = text ?? Texts.Get(shownGroup != null ? "shop.hint.back" : "shop.hint.close");
        message.color = text != null ? color : KitStyle.Soft;
        messageLeft = text != null ? MessageSeconds : 0f;
    }

    // ---- Building ----

    // The canvas and the panel's frame, title, money and X; the pages go in between (ShowPage).
    private void Build()
    {
        if (canvas != null) Destroy(canvas);
        rows.Clear();
        keyed.Clear();
        page = null;
        pageReveal = null;
        LoadStock();
        canvas = new GameObject("Shop", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.hideFlags = HideFlags.DontSave;
        canvas.transform.SetParent(transform, false);
        var layer = canvas.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = 95;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        builtFor = new Vector2Int(Screen.width, Screen.height);

        float height = PanelHeight();
        panel = FeelUI.Rect("Panel", canvas.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, .5f);
        panel.pivot = new Vector2(0f, .5f);
        panel.anchoredPosition = new Vector2(Margin, 0f);
        panel.sizeDelta = new Vector2(Width, height);
        KitStyle.Fill(panel, KitStyle.Panel);
        reveal = panel.gameObject.AddComponent<KitReveal>();

        // Frame lines: down the left and right edges, and under the title.
        var left = KitStyle.Bar("Left Line", panel, Vector2.zero, new Vector2(KitStyle.LineWidth, height), KitStyle.Line).rectTransform;
        reveal.Line(left, true, 0f);
        var right = KitStyle.Bar("Right Line", panel, new Vector2(Width - KitStyle.LineWidth, 0f), new Vector2(KitStyle.LineWidth, height * .35f), KitStyle.Line).rectTransform;
        reveal.Line(right, true, .1f);
        var under = KitStyle.Bar("Title Line", panel, new Vector2(Inner, HeaderHeight - 10f), new Vector2(Width - Inner * 2f, 1f), KitStyle.Line).rectTransform;
        reveal.Line(under, false, .06f, .3f);

        // The title and its line of sections slide in from the left behind a mask.
        var titleMask = KitStyle.TopLeft("Title Mask", panel, new Vector2(Inner, 14f), new Vector2(300f, 56f));
        titleMask.gameObject.AddComponent<RectMask2D>();
        var titleBox = KitStyle.TopLeft("Title", titleMask, Vector2.zero, new Vector2(300f, 34f));
        KitStyle.Label(titleBox, Texts.Get("shop.title"), 30f, TextAlignmentOptions.Left);
        reveal.Slide(titleBox, new Vector2(300f, 0f), .05f);
        var subBox = KitStyle.TopLeft("Sections", titleMask, new Vector2(0f, 36f), new Vector2(300f, 18f));
        KitStyle.Label(subBox, Texts.Get("shop.subtitle"), 14f, TextAlignmentOptions.Left, KitStyle.Accent);
        reveal.Slide(subBox, new Vector2(300f, 0f), .1f);

        // The money in a dark box, and the X. The box is sized to its number every frame (Refresh), so what is inside
        // hangs from its right edge: the number 10 px in, the banknote left of the number, a lid that flashes on a buy.
        moneyBox = KitStyle.TopLeft("Money", panel, new Vector2(Width - Inner - CloseSize - 8f - MoneyWidth, 18f), new Vector2(MoneyWidth, CloseSize));
        KitStyle.Fill(moneyBox, KitStyle.Card);
        moneyFlash = KitStyle.Fill(KitStyle.Stretch("Flash", moneyBox), Color.clear);
        var moneyText = KitStyle.Stretch("Value", moneyBox);
        moneyText.offsetMax = new Vector2(-10f, 0f);
        money = KitStyle.Label(moneyText, "0", MoneySize, TextAlignmentOptions.Right);
        var holder = FeelUI.Rect("Icon Holder", moneyBox);
        holder.anchorMin = holder.anchorMax = new Vector2(1f, .5f);
        holder.anchoredPosition = new Vector2(-10f, 0f);
        moneyIcon = MoneyIcon.Make(holder, 20f).rectTransform;
        // What a buy cost, under the money box's right end (Refresh moves and fades it).
        spentBox = KitStyle.TopLeft("Spent", panel, new Vector2(Width - Inner - CloseSize - 18f - 190f, 18f + CloseSize + 1f), new Vector2(190f, 18f));
        spent = KitStyle.Label(spentBox, "", 14f, TextAlignmentOptions.Right, Color.clear);
        closeBox = KitStyle.TopLeft("Close", panel, new Vector2(Width - Inner - CloseSize, 18f), new Vector2(CloseSize, CloseSize));
        closeBox.pivot = new Vector2(.5f, .5f);
        closeBox.anchoredPosition += new Vector2(CloseSize * .5f, -CloseSize * .5f);
        closeFill = KitStyle.Fill(closeBox, KitStyle.Card);
        closeFill.raycastTarget = true;
        KitStyle.Label(closeBox, "X", 18f, TextAlignmentOptions.Center);
        var closeButton = closeBox.gameObject.AddComponent<Button>();
        closeButton.transition = Selectable.Transition.None;
        closeButton.onClick.AddListener(BeginClose);
        closeBox.gameObject.AddComponent<KitHover>();

        sweep = KitStyle.Bar("Sweep", panel, Vector2.zero, new Vector2(Width, 1f), KitStyle.Line).rectTransform;
        reveal.Sweep(sweep, new Vector2(0f, -HeaderHeight), new Vector2(0f, -(height - FooterHeight)), .15f, .5f);
        var footer = KitStyle.TopLeft("Message", panel, new Vector2(Inner, height - FooterHeight), new Vector2(Width - Inner * 2f, FooterHeight));
        message = KitStyle.Label(footer, "", 15f, TextAlignmentOptions.Center, KitStyle.Soft);
        message.fontStyle = FontStyles.Normal;
    }

    // The screen changed shape while open (a window resized, the display mode changed): laid out again, on the same page.
    private void Relayout()
    {
        string shownId = shownGroup?.Id;
        Build();
        canvas.SetActive(true);
        reveal.Open(false);
        Say(null, KitStyle.Text);
        ShowPage(shownId != null ? groups.Find(group => group.Id == shownId) : null, PageFrom);
    }

    // As tall as the first page or a category's page needs (up to MostCards guns; more scroll), never taller than the
    // screen leaves: 21:9 is 935 px tall to 16:9's 1080, and an ultrawide less.
    private float PanelHeight()
    {
        float body = FrontHeight();
        foreach (var group in groups) body = Mathf.Max(body, GroupPageHeight(Mathf.Min(group.Guns.Count, MostCards)));
        float natural = HeaderHeight + 2f * KitScroll.Pad + body + FooterHeight;
        return Mathf.Max(HeaderHeight + FooterHeight + 160f, Mathf.Min(natural, CanvasHeight() - 2f * ScreenRoom));
    }

    // The canvas's height in its own px: the screen scaled from 1920x1080, width and height weighed alike (the scaler's
    // match of .5), worked out here because the canvas sizes itself only when it is next drawn.
    private static float CanvasHeight()
    {
        float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
        return h / Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / 1920f, 2f), Mathf.Log(h / 1080f, 2f), .5f));
    }

    // The first page's height: what BuildFront lays out.
    private float FrontHeight()
    {
        int big = 0, small = 0;
        foreach (var item in stock)
            if (item.Kind != Kind.Gun && Offered(item)) { if (IsBig(item.Kind)) big++; else small++; }
        float y = 0f;
        if (big + small > 0) y = SectionHeight + Lines(big, 3) * (TileHeight + Gap) + Lines(small, 2) * (SmallTileHeight + Gap) - Gap;
        if (groups.Count > 0) y += (y > 0f ? SectionGap : 0f) + SectionHeight + Lines(groups.Count, 2) * (GroupHeight + Gap) - Gap;
        return y;
    }

    // A category's page's height with this many guns: what BuildGroup lays out.
    private static float GroupPageHeight(int guns) => BackHeight + Gap + guns * (CardHeight + CardGap) - (guns > 0 ? CardGap : 0f);

    private static int Lines(int count, int across) => across <= 0 ? 0 : (count + across - 1) / across;

    // Puts a page into the panel, the first (group null) or a category's, its cards growing in from `from` (s); a message
    // still showing (what was just bought) stays.
    private void ShowPage(GunGroup group, float from)
    {
        shownGroup = group;
        if (page != null) Destroy(page.gameObject);
        rows.Clear();
        keyed.Clear();
        page = KitScroll.Make("Page", panel, Vector2.zero, Vector2.one, new Vector2(Inner, FooterHeight), new Vector2(-Inner, -HeaderHeight));
        // Under the opening's sweep line and the message.
        page.transform.SetSiblingIndex(sweep.GetSiblingIndex());
        pageReveal = page.gameObject.AddComponent<KitReveal>();
        pageReveal.Hide();
        float width = Width - Inner * 2f;
        page.Fit(group == null ? BuildFront(page.Rows, width, from) : BuildGroup(page.Rows, group, width, from));
        page.Offset = 0f;
        if (messageLeft <= 0f) Say(null, KitStyle.Text);
        pageReveal.Open(false);
    }

    // The first page: the refills (big tiles, then the class's own), then the gun categories. Returns its height.
    private float BuildFront(RectTransform parent, float width, float from)
    {
        var big = new List<Item>();
        var small = new List<Item>();
        foreach (var item in stock)
            if (item.Kind != Kind.Gun && Offered(item)) (IsBig(item.Kind) ? big : small).Add(item);
        float y = 0f, at = from;
        if (big.Count + small.Count > 0)
        {
            SectionLabel(parent, Texts.Get("shop.section.supplies"), y, width);
            y += SectionHeight;
            y = Tiles(parent, big, 3, TileHeight, y, width, ref at);
            y = Tiles(parent, small, 2, SmallTileHeight, y, width, ref at) - Gap;
        }
        if (groups.Count > 0)
        {
            if (y > 0f) y += SectionGap;
            SectionLabel(parent, Texts.Get("shop.section.guns"), y, width);
            y += SectionHeight;
            float plate = (width - Gap) * .5f;
            for (int i = 0; i < groups.Count; i++)
            {
                rows.Add(GroupPlate(parent, groups[i], new Vector2((i % 2) * (plate + Gap), y + (i / 2) * (GroupHeight + Gap)), plate, at));
                at += CardStagger;
            }
            y += Lines(groups.Count, 2) * (GroupHeight + Gap) - Gap;
        }
        return y;
    }

    // A category's page: the plate back to the first page with the category's name beside it, then its guns cheapest
    // first. Returns its height.
    private float BuildGroup(RectTransform parent, GunGroup group, float width, float from)
    {
        var back = Plate(parent, "Back", Vector2.zero, new Vector2(BackWidth, BackHeight), KitStyle.Accent, out var plate);
        back.Back = true;
        KeyBox(plate, Texts.Get("shop.key.back"), new Vector2(14f, (BackHeight - KeySize) * .5f));
        KitStyle.Label(KitStyle.TopLeft("Label", plate, new Vector2(62f, 0f), new Vector2(BackWidth - 70f, BackHeight)), Texts.Get("shop.back"), 17f,
            TextAlignmentOptions.MidlineLeft);
        Finish(back, plate, from);
        rows.Add(back);
        var count = KitStyle.Label(KitStyle.TopLeft("Count", parent, new Vector2(width - 110f, 0f), new Vector2(110f, BackHeight)),
            Texts.Format("shop.cat.count", group.Guns.Count), 14f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        count.fontStyle = FontStyles.Normal;
        Fit(KitStyle.Label(KitStyle.TopLeft("Title", parent, new Vector2(BackWidth + 18f, 0f), new Vector2(width - BackWidth - 136f, BackHeight)),
            group.Name, 24f, TextAlignmentOptions.MidlineLeft), 15f);
        float y = BackHeight + Gap, at = from + CardStagger;
        foreach (var item in group.Guns)
        {
            rows.Add(GunCard(parent, item, y, width, at));
            at += CardStagger;
            y += CardHeight + CardGap;
        }
        return y - CardGap;
    }

    private static void SectionLabel(RectTransform parent, string text, float y, float width)
    {
        var header = KitStyle.TopLeft("Section", parent, new Vector2(0f, y), new Vector2(width, SectionHeight));
        KitStyle.Label(header, text, 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
    }

    // Refill tiles `across` to a row (fewer share the row's width), each `height` tall, from `y`; returns the y under
    // them, a Gap below the last row.
    private float Tiles(RectTransform parent, List<Item> items, int across, float height, float y, float width, ref float at)
    {
        if (items.Count == 0) return y;
        int columns = Mathf.Min(across, items.Count);
        float w = (width - Gap * (columns - 1)) / columns;
        for (int i = 0; i < items.Count; i++)
        {
            var where = new Vector2((i % columns) * (w + Gap), y + (i / columns) * (height + Gap));
            rows.Add(Tile(parent, items[i], where, new Vector2(w, height), at));
            at += CardStagger;
        }
        return y + Lines(items.Count, columns) * (height + Gap);
    }

    // A refill tile: its key, what it is and gives, how much is held now at its top right, its price. A big tile stacks
    // them (key, then name, small print, price at its foot); a small one sets them side by side.
    private Row Tile(RectTransform parent, Item item, Vector2 at, Vector2 size, float from)
    {
        var row = Plate(parent, "Tile " + item.Id, at, size, AccentOf(item.Kind), out var plate);
        row.Item = item;
        row.Detail = item.Detail;
        float w = size.x, h = size.y;
        bool big = h >= TileHeight - 1f;
        KeyBox(plate, NextKey(row), new Vector2(14f, big ? 10f : 8f));
        var count = KitStyle.TopLeft("Count", plate, new Vector2(w - (big ? 84f : 74f), big ? 9f : 7f), new Vector2(big ? 70f : 60f, KeySize));
        row.Count = KitStyle.Label(count, "", 13f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        row.Count.fontStyle = FontStyles.Normal;
        if (big)
        {
            row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(14f, 34f), new Vector2(w - 28f, 28f)), item.Name, 21f,
                TextAlignmentOptions.MidlineLeft), 14f);
            row.Info = Small(KitStyle.TopLeft("Detail", plate, new Vector2(14f, 62f), new Vector2(w - 28f, 18f)), item.Detail);
            row.Price = PriceLabel(plate, Wallet.Format(item.Price), new Vector2(14f, h - 32f), new Vector2(w - 28f, 26f), 21f);
        }
        else
        {
            row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(44f, 5f), new Vector2(w - 124f, 26f)), item.Name, 18f,
                TextAlignmentOptions.MidlineLeft), 13f);
            row.Info = Small(KitStyle.TopLeft("Detail", plate, new Vector2(44f, 33f), new Vector2(w - 134f, 20f)), item.Detail);
            row.Price = PriceLabel(plate, Wallet.Format(item.Price), new Vector2(w - 88f, 30f), new Vector2(74f, 26f), 19f);
        }
        Finish(row, plate, from);
        return row;
    }

    // A category's plate: its key and name, how many guns it sells and a 가진 총 tag while one of them is held, the
    // cheapest and dearest of them, and a chevron: it opens a page.
    private Row GroupPlate(RectTransform parent, GunGroup group, Vector2 at, float width, float from)
    {
        var row = Plate(parent, "Group " + group.Id, at, new Vector2(width, GroupHeight), KitStyle.With(KitStyle.Ice, .35f), out var plate);
        row.Group = group;
        KeyBox(plate, NextKey(row), new Vector2(14f, 11f));
        row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(44f, 5f), new Vector2(width - 82f, 34f)), group.Name, 24f,
            TextAlignmentOptions.MidlineLeft), 15f);
        row.Info = Small(KitStyle.TopLeft("Count", plate, new Vector2(16f, 41f), new Vector2(64f, 18f)), Texts.Format("shop.cat.count", group.Guns.Count));
        Tag(row, plate, new Vector2(84f, 40f), new Vector2(66f, 20f));
        long cheapest = group.Guns[0].Price, dearest = group.Guns[group.Guns.Count - 1].Price;
        string prices = cheapest == dearest ? Wallet.Format(cheapest) : Texts.Format("shop.cat.prices", Wallet.Format(cheapest), Wallet.Format(dearest));
        // The prices after the banknote, left under the count.
        var icon = MoneyIcon.Make(plate, 14f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
        icon.rectTransform.anchoredPosition = new Vector2(24f, -69f);
        row.Price = KitStyle.Label(KitStyle.TopLeft("Prices", plate, new Vector2(36f, 60f), new Vector2(width - 76f, 18f)), prices, 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        Chevron(plate, new Vector2(width - 24f, GroupHeight * .5f));
        Finish(row, plate, from);
        return row;
    }

    // A gun's card on its category's page: its key, its picture, its name with its shot and magazine under it, a tag
    // (구매 완료, 가진 총, or what the money is short by) and its price.
    private Row GunCard(RectTransform parent, Item item, float y, float width, float from)
    {
        var row = Plate(parent, "Gun " + item.Id, new Vector2(0f, y), new Vector2(width, CardHeight), AccentOf(Kind.Gun), out var plate);
        row.Item = item;
        row.Detail = item.Detail;
        KeyBox(plate, NextKey(row), new Vector2(14f, (CardHeight - KeySize) * .5f));
        float x = 46f;
        // The gun's own picture (WeaponIcons: every gun on one canvas, so each shows at its size).
        var picture = WeaponIcons.For(item.Weapon);
        if (picture != null)
        {
            var box = KitStyle.TopLeft("Picture", plate, new Vector2(x, (CardHeight - PictureHeight) * .5f), new Vector2(PictureHeight * WeaponIcons.Aspect, PictureHeight));
            row.Picture = box.gameObject.AddComponent<Image>();
            row.Picture.sprite = picture;
            row.Picture.preserveAspect = true;
            row.Picture.raycastTarget = false;
            x += PictureHeight * WeaponIcons.Aspect + 8f;
        }
        float named = width - 168f - x;
        row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(x, 6f), new Vector2(named, 26f)), item.Name, 19f,
            TextAlignmentOptions.MidlineLeft), 13f);
        row.Info = Small(KitStyle.TopLeft("Detail", plate, new Vector2(x, 33f), new Vector2(named, 18f)), item.Detail);
        Tag(row, plate, new Vector2(width - 164f, (CardHeight - 22f) * .5f), new Vector2(70f, 22f));
        row.Price = PriceLabel(plate, Wallet.Format(item.Price), new Vector2(width - 90f, 0f), new Vector2(76f, CardHeight), 20f);
        Finish(row, plate, from);
        return row;
    }

    // A dark plate leaning by Slant (KitSkew, so clicks follow the lean: KitHit) with its kind's bar down its left side.
    private static Row Plate(RectTransform parent, string name, Vector2 at, Vector2 size, Color bar, out RectTransform plate)
    {
        plate = KitStyle.TopLeft(name, parent, at, size);
        var row = new Row { PlateColor = KitStyle.Card, BarColor = bar };
        row.Plate = KitStyle.Fill(KitStyle.Stretch("Plate", plate), KitStyle.Card);
        row.Plate.raycastTarget = true;
        KitSkew.On(row.Plate, Slant);
        row.Bar = KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(KitStyle.AccentWidth, size.y), bar);
        KitSkew.On(row.Bar, Slant);
        return row;
    }

    // The pink lid a plate grows in under (and a buy's pulse lights), its click and its hover.
    private void Finish(Row row, RectTransform plate, float from)
    {
        row.Flash = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
        KitSkew.On(row.Flash, Slant);
        pageReveal.Card(plate, row.Flash, from);
        var button = plate.gameObject.AddComponent<Button>();
        button.targetGraphic = row.Plate;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Pick(row));
        plate.gameObject.AddComponent<KitHover>();
    }

    // The next number key on the page for a row, or null once they run out (it is still clicked).
    private string NextKey(Row row)
    {
        keyed.Add(row);
        return keyed.Count <= KeyNames.Length ? KeyNames[keyed.Count - 1] : null;
    }

    // The key that picks it: a small white box with the key in black (the perk cards' look).
    private static void KeyBox(RectTransform plate, string key, Vector2 at)
    {
        if (string.IsNullOrEmpty(key)) return;
        var box = KitStyle.TopLeft("Key", plate, at, new Vector2(Mathf.Max(KeySize, 12f + 9f * key.Length), KeySize));
        KitSkew.On(KitStyle.Fill(box, KitStyle.Ice), 4f);
        KitStyle.Flat(box, key, 14f, TextAlignmentOptions.Center, KitStyle.Ink);
    }

    // A tag box Refresh fills: white with black words for 가진 총, words alone for the rest, nothing while it has nothing to say.
    private static void Tag(Row row, RectTransform plate, Vector2 at, Vector2 size)
    {
        var box = KitStyle.TopLeft("Tag", plate, at, size);
        row.TagBack = KitStyle.Fill(box, Color.clear);
        KitSkew.On(row.TagBack, 4f);
        row.Tag = Fit(KitStyle.Flat(box, "", 12f, TextAlignmentOptions.Center, KitStyle.Ink), 9f);
    }

    // A price right-aligned in its box, the banknote just left of the number.
    private static TextMeshProUGUI PriceLabel(RectTransform plate, string price, Vector2 at, Vector2 size, float fontSize)
    {
        var box = KitStyle.TopLeft("Price", plate, at, size);
        var label = KitStyle.Label(box, price, fontSize, TextAlignmentOptions.MidlineRight);
        var icon = MoneyIcon.Make(box, fontSize * .9f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(-label.GetPreferredValues(price).x - fontSize * .45f - 5f, 0f);
        return label;
    }

    // A small > at `center` (px from the plate's top left): the plate opens a page.
    private static void Chevron(RectTransform plate, Vector2 center)
    {
        for (int i = 0; i < 2; i++)
        {
            var arm = KitStyle.TopLeft("Chevron", plate, center, new Vector2(11f, 2.5f));
            arm.pivot = new Vector2(1f, .5f);
            arm.anchoredPosition = new Vector2(center.x + 4f, -center.y);
            arm.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            KitStyle.Fill(arm, KitStyle.Soft);
        }
    }

    // Shrinks a label to fit its box on one line, down to `smallest`.
    private static TextMeshProUGUI Fit(TextMeshProUGUI label, float smallest)
    {
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = smallest;
        label.fontSizeMax = label.fontSize;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    // The small print under a name: soft, not bold, shrinking to fit.
    private static TextMeshProUGUI Small(RectTransform box, string text)
    {
        var label = KitStyle.Label(box, text, 12.5f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        label.fontStyle = FontStyles.Normal;
        return Fit(label, 10f);
    }
}
