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
/// by one and settle dark; KitHover's corner brackets; the X turns red and spins as the panel slides away left). Guns,
/// then supplies (ammo for every gun held, a med kit, a shield), each with its price as the money icon and a number
/// (Tables/Shop); a click buys it with this PC's own money (Wallet), and the game runs on meanwhile. A gun goes into the
/// gun slot in hand, else an empty gun slot, else the first, and comes out unless the hands are full; one already held
/// isn't sold again. Buying touches only this PC's player, so it needs nobody else: a friend sees the gun once it is drawn
/// (the stance carries it). While open the cursor is free and the player's move and combat input wait, as for the chat;
/// F, Esc or the X closes it, and so does walking away, going down or dying.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShopMenu : MonoBehaviour
{
    public const string TableName = "Shop";
    private enum Kind { Gun, Ammo, Heal, Shield }

    private sealed class Item
    {
        public string Name, Detail;
        public Kind Kind;
        public WeaponData Weapon;
        public long Price;
        public float Amount;
        public int Order;
    }

    private sealed class Row
    {
        public Item Item;
        public TextMeshProUGUI Name, Note, Price;
        public Image Flash;
        public float FlashAt = float.NegativeInfinity;
        public Color FlashColor;
    }

    // How far from the counter the shop stays open (m), and how long a message stays (s).
    private const float Reach = 3.2f, MessageSeconds = 2.2f;
    // Layout (px at 1920x1080): the panel hangs at the screen's left like the reference's.
    private const float Width = 560f, Margin = 48f, Inner = 20f, HeaderHeight = 84f, SectionHeight = 30f, CardHeight = 50f, CardGap = 6f, FooterHeight = 46f;
    // When things come in (s after opening).
    private const float CardsFrom = .22f, CardStagger = .035f, CountFrom = .1f, CountSeconds = .45f;

    private static ShopMenu instance;
    private static int closedFrame = -1;

    /// <summary>The shop is open on this PC (closing included).</summary>
    public static bool IsOpen => instance != null && instance.open;
    /// <summary>The shop has the keyboard (open, or closed by a key this very frame): Esc mustn't also open the menu.</summary>
    public static bool TakesKeyboard => IsOpen || Time.frameCount == closedFrame;

    private readonly List<Item> stock = new List<Item>();
    private readonly List<Row> rows = new List<Row>();
    private PlayerMovement player;
    private Vector3 counter;
    private GameObject canvas;
    private KitReveal reveal;
    private RectTransform moneyIcon, closeBox;
    private Image closeFill;
    private TextMeshProUGUI money, message;
    private float messageLeft;
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
        if (instance == null) instance = new GameObject("Shop Menu").AddComponent<ShopMenu>();
        instance.Show(who, at);
    }

    private void Show(PlayerMovement who, Vector3 at)
    {
        player = who;
        counter = at;
        if (canvas == null) Build();
        canvas.SetActive(true);
        open = true;
        openedFrame = Time.frameCount;
        closeFill.color = KitStyle.Card;
        closeBox.localRotation = Quaternion.identity;
        Say(null, KitStyle.Text);
        reveal.Open();
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
        bool closing = keyboard != null && Time.frameCount != openedFrame
            && (keyboard.escapeKey.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame);
        if (closing || player == null || !player.IsInitialized || player.IsDead || player.DamageReceiver.IsDowned
            || (player.transform.position - counter).sqrMagnitude > Reach * Reach)
        {
            BeginClose();
            return;
        }
        if (messageLeft > 0f && (messageLeft -= Time.unscaledDeltaTime) <= 0f) Say(null, KitStyle.Text);
        Refresh();
    }

    private void OnDestroy()
    {
        if (open) FinishClose();
        if (instance == this) instance = null;
    }

    // ---- Buying ----

    private void Buy(Row row)
    {
        if (!open || reveal.Closing || player == null) return;
        var item = row.Item;
        string why = Refusal(item);
        if (why == null && !Wallet.TrySpend(item.Price)) why = "돈이 모자라요";
        if (why == null && !Hand(item)) { Wallet.Unspend(item.Price); why = "지금은 살 수 없어요"; }
        if (why != null) { Say(why, KitStyle.Red); Pulse(row, new Color(KitStyle.Red.r, KitStyle.Red.g, KitStyle.Red.b, .35f)); return; }
        Say(item.Name + " 샀어요", KitStyle.Accent);
        Pulse(row, KitStyle.Flash);
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
                if (item.Weapon == null || slots == null) return "팔지 않는 총이에요";
                if (Holds(slots, item.Weapon)) return "이미 가진 총이에요";
                if (player.CombatInputLocked) return "지금은 살 수 없어요";
                return null;
            case Kind.Ammo:
                return slots == null || !HoldsAnyGun(slots) ? "채울 총이 없어요" : null;
            case Kind.Heal:
                return receiver.Health >= receiver.MaximumHealth - .5f ? "체력이 가득해요" : null;
            case Kind.Shield:
                return receiver.Shield >= item.Amount - .5f ? "실드가 이미 있어요" : null;
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
        var guns = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && !guns.ContainsKey(entry.weapon.name)) guns[entry.weapon.name] = entry.weapon;
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !Enum.TryParse(Text(row, "kind"), true, out Kind kind)) continue;
            WeaponData weapon = null;
            if (kind == Kind.Gun && !guns.TryGetValue(Text(row, "weapon"), out weapon)) continue;
            string name = Text(row, "name");
            float amount = Number(row, "amount", 0f);
            stock.Add(new Item
            {
                Kind = kind, Weapon = weapon, Price = (long)Number(row, "price", 0f), Amount = amount,
                Order = (int)Number(row, "order", 0f), Name = name.Length > 0 ? name : weapon != null ? weapon.DisplayName : id,
                Detail = kind == Kind.Gun ? "총" : kind == Kind.Ammo ? "가진 총 모두" : kind == Kind.Heal ? "체력 " + amount.ToString("0") + " 회복" : "실드 " + amount.ToString("0")
            });
        }
        stock.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key) => row.TryGetValue(key, out string text) ? text.Trim() : "";

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    // Each kind's bar down a card's left side: guns white (the pink is the menus' one accent, and too near heal's red),
    // ammo cyan, heal red, shield green.
    private static Color AccentOf(Kind kind) => kind == Kind.Gun ? KitStyle.Text : kind == Kind.Ammo ? KitStyle.Cyan
        : kind == Kind.Heal ? KitStyle.Red : KitStyle.Green;

    // ---- Screen ----

    private void Refresh()
    {
        // The money counts up as the panel opens, then reads the balance.
        float counting = Mathf.Clamp01((reveal.Elapsed - CountFrom) / CountSeconds);
        long shown = counting < 1f ? (long)(Wallet.Balance * KitReveal.EaseOut(counting)) : Wallet.Balance;
        money.text = Wallet.Format(shown);
        float width = money.GetPreferredValues(money.text).x;
        moneyIcon.anchoredPosition = new Vector2(-width - 18f, 0f);
        var slots = player != null ? player.GetComponent<PlayerWeaponSlots>() : null;
        float now = Time.unscaledTime;
        foreach (var row in rows)
        {
            string note = player == null ? "" : Note(row.Item, slots);
            row.Note.text = note;
            bool affordable = Wallet.Balance >= row.Item.Price;
            row.Price.color = note.Length > 0 ? KitStyle.Soft : affordable ? KitStyle.Text : KitStyle.Red;
            row.Name.color = note.Length > 0 ? KitStyle.Soft : KitStyle.Text;
            // A buy's pulse, once the opening has let go of the card's flash.
            if (reveal.enabled || now - row.FlashAt > KitReveal.DrainSeconds + .05f) continue;
            var flash = row.FlashColor;
            flash.a *= 1f - Mathf.Clamp01((now - row.FlashAt) / KitReveal.DrainSeconds);
            row.Flash.color = flash;
        }
    }

    private string Note(Item item, PlayerWeaponSlots slots)
    {
        if (item.Kind == Kind.Gun && slots != null && item.Weapon != null && Holds(slots, item.Weapon)) return "가진 총";
        if (item.Kind == Kind.Heal && player.DamageReceiver.Health >= player.DamageReceiver.MaximumHealth - .5f) return "체력 가득";
        if (item.Kind == Kind.Shield && player.DamageReceiver.Shield >= item.Amount - .5f) return "실드 있음";
        return "";
    }

    private void Say(string text, Color color)
    {
        if (message == null) return;
        message.text = text ?? "[F] 또는 Esc 닫기";
        message.color = text != null ? color : KitStyle.Soft;
        messageLeft = text != null ? MessageSeconds : 0f;
    }

    private void Build()
    {
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

        int guns = stock.FindAll(item => item.Kind == Kind.Gun).Count, supplies = stock.Count - guns;
        float height = HeaderHeight + (guns > 0 ? SectionHeight : 0f) + (supplies > 0 ? SectionHeight : 0f)
            + stock.Count * (CardHeight + CardGap) + FooterHeight;
        var panel = FeelUI.Rect("Panel", canvas.transform);
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
        KitStyle.Label(titleBox, "상점", 30f, TextAlignmentOptions.Left);
        reveal.Slide(titleBox, new Vector2(300f, 0f), .05f);
        var subBox = KitStyle.TopLeft("Sections", titleMask, new Vector2(0f, 36f), new Vector2(300f, 18f));
        KitStyle.Label(subBox, "총  /  보급", 14f, TextAlignmentOptions.Left, KitStyle.Accent);
        reveal.Slide(subBox, new Vector2(300f, 0f), .1f);

        // The money in a dark box, and the X.
        const float CloseSize = 34f, MoneyWidth = 150f;
        var moneyBox = KitStyle.TopLeft("Money", panel, new Vector2(Width - Inner - CloseSize - 8f - MoneyWidth, 18f), new Vector2(MoneyWidth, CloseSize));
        KitStyle.Fill(moneyBox, KitStyle.Card);
        var moneyText = KitStyle.TopLeft("Value", moneyBox, new Vector2(0f, 0f), new Vector2(MoneyWidth - 10f, CloseSize));
        money = KitStyle.Label(moneyText, "0", 20f, TextAlignmentOptions.Right);
        var holder = FeelUI.Rect("Icon Holder", moneyBox);
        holder.anchorMin = holder.anchorMax = new Vector2(1f, .5f);
        holder.anchoredPosition = new Vector2(-10f, 0f);
        moneyIcon = MoneyIcon.Make(holder, 20f).rectTransform;
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

        float y = HeaderHeight;
        int index = 0;
        for (int section = 0; section < 2; section++)
        {
            bool gunsHere = section == 0;
            if ((gunsHere ? guns : supplies) == 0) continue;
            var header = KitStyle.TopLeft(gunsHere ? "Guns" : "Supplies", panel, new Vector2(Inner, y), new Vector2(Width - Inner * 2f, SectionHeight));
            KitStyle.Label(header, gunsHere ? "총" : "보급", 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
            y += SectionHeight;
            foreach (var item in stock)
            {
                if ((item.Kind == Kind.Gun) != gunsHere) continue;
                rows.Add(BuildCard(panel, item, y, CardsFrom + index++ * CardStagger));
                y += CardHeight + CardGap;
            }
        }
        var sweep = KitStyle.Bar("Sweep", panel, Vector2.zero, new Vector2(Width, 1f), KitStyle.Line).rectTransform;
        reveal.Sweep(sweep, new Vector2(0f, -HeaderHeight), new Vector2(0f, -(height - FooterHeight)), .15f, .5f);
        var footer = KitStyle.TopLeft("Message", panel, new Vector2(Inner, height - FooterHeight), new Vector2(Width - Inner * 2f, FooterHeight));
        message = KitStyle.Label(footer, "", 15f, TextAlignmentOptions.Center, KitStyle.Soft);
        message.fontStyle = FontStyles.Normal;
    }

    // A dark card with its kind's colour down the left, its name and what it does, and its price at the right.
    private Row BuildCard(RectTransform panel, Item item, float top, float at)
    {
        float width = Width - Inner * 2f;
        var card = KitStyle.TopLeft("Card " + item.Name, panel, new Vector2(Inner, top), new Vector2(width, CardHeight));
        var back = KitStyle.Fill(card, KitStyle.Card);
        back.raycastTarget = true;
        KitStyle.Bar("Accent", card, Vector2.zero, new Vector2(KitStyle.AccentWidth, CardHeight), AccentOf(item.Kind));

        var row = new Row { Item = item };
        var name = KitStyle.TopLeft("Name", card, new Vector2(16f, 5f), new Vector2(width * .55f, 24f));
        row.Name = KitStyle.Label(name, item.Name, 19f, TextAlignmentOptions.MidlineLeft);
        var detail = KitStyle.TopLeft("Detail", card, new Vector2(16f, 28f), new Vector2(width * .55f, 18f));
        KitStyle.Label(detail, item.Detail, 12f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        var note = KitStyle.TopLeft("Note", card, new Vector2(width * .5f, 0f), new Vector2(width * .2f, CardHeight));
        row.Note = KitStyle.Label(note, "", 13f, TextAlignmentOptions.Center, KitStyle.Soft);
        var price = KitStyle.TopLeft("Price", card, new Vector2(width * .7f, 0f), new Vector2(width * .3f - 14f, CardHeight));
        row.Price = KitStyle.Label(price, Wallet.Format(item.Price), 20f, TextAlignmentOptions.MidlineRight);
        var icon = MoneyIcon.Make(card, 18f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(-14f - row.Price.GetPreferredValues(row.Price.text).x - 14f, 0f);

        // Its pink lid for the opening and for a buy's pulse; the content shows through.
        var lid = FeelUI.Rect("Flash", card);
        lid.anchorMin = Vector2.zero;
        lid.anchorMax = Vector2.one;
        lid.sizeDelta = Vector2.zero;
        row.Flash = KitStyle.Fill(lid, KitStyle.Flash);
        reveal.Card(card, row.Flash, at);

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Buy(row));
        card.gameObject.AddComponent<KitHover>();
        return row;
    }
}
