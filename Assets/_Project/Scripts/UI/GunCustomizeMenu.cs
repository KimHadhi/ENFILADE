using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The held gun's customize menu on [C], laid out as ArcCW's (user, 2026-11-09: "arccw처럼 … 그게 더 직관적이고"): the gun
/// comes out before the eyes turned to show its side (WeaponController.Customizing, FPSBody); its slots stand in a column
/// down the left (sight, muzzle, grip, underbarrel), each with the part on it now; a click on a slot opens its parts beside
/// it, a part under the pointer says what it gives and costs (green +, red −), and the stats down the right show the gun's
/// numbers with what that part would make of them. A click on a part puts it on at once, with the sight swap's click, and
/// friends see it (the stance carries it). Quiet on purpose: the menus' colours (KitStyle) and nothing that draws or
/// flashes. While open the cursor is free and the player's move and combat input wait; C, Esc or the X closes it, and so
/// does going down, dying or the gun leaving the hands. Nothing changes mid-reload or while the gun comes out
/// (WeaponController.CustomizeRefusal).
/// </summary>
[DisallowMultipleComponent]
public sealed class GunCustomizeMenu : MonoBehaviour
{
    private enum Slot { Sight, Muzzle, Grip, Underbarrel }

    private sealed class Part
    {
        public int Index;
        public string Name;
        public readonly List<string> Pros = new List<string>(), Cons = new List<string>();
        public Image Back, Mark;
        public TextMeshProUGUI Label;
        public bool Hovered;
    }

    private sealed class SlotBox
    {
        public Slot Slot;
        public string Title;
        public readonly List<Part> Parts = new List<Part>();
        public Image Back, Accent;
        public TextMeshProUGUI Current;
        public RectTransform Column;
    }

    private struct Stats
    {
        public float Body, Head, Rpm, Recoil, AimTime, Noise;
        public int Pellets, Magazine;
    }

    // Layout (px at 1920x1080): slots down the left, a slot's parts beside it, the stats down the right.
    private const float Margin = 56f, Top = 150f, SlotWidth = 300f, SlotHeight = 66f, SlotGap = 10f;
    private const float PartWidth = 330f, PartHeight = 46f, PartGap = 6f, ColumnGap = 16f, StatsWidth = 380f, StatRow = 40f;
    private const float FadeSeconds = .15f, MessageSeconds = 2.2f;

    private static GunCustomizeMenu instance;
    private static int closedFrame = -1;

    /// <summary>The menu is open on this PC (closing included).</summary>
    public static bool IsOpen => instance != null && instance.open;
    /// <summary>The menu has the keyboard (open, or closed by a key this very frame): Esc mustn't also open the settings.</summary>
    public static bool TakesKeyboard => IsOpen || Time.frameCount == closedFrame;

    private readonly List<SlotBox> slots = new List<SlotBox>();
    private readonly List<(TextMeshProUGUI value, System.Func<Stats, string> read, System.Func<Stats, Stats, int> better)> statRows
        = new List<(TextMeshProUGUI, System.Func<Stats, string>, System.Func<Stats, Stats, int>)>();
    private PlayerMovement player;
    private WeaponController weapon;
    private FPSGun gun;
    private GameObject canvas;
    private RectTransform root;
    private CanvasGroup fade;
    private SlotBox openSlot;
    private TextMeshProUGUI detailTitle, detailBody, message;
    private RectTransform detail;
    private float messageLeft, alpha;
    private bool open, closing, playerMapWas, combatMapWas;
    // The frame it opened on: the C that opened it still reads as pressed in this frame's Update, and once the menu existed
    // it closed it again at once (user, 2026-11-09: "C 눌러도 메뉴 나왔다 바로 사라지고").
    private int openedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        closedFrame = -1;
    }

    /// <summary>Opens the menu for this PC's <paramref name="who"/> on the gun they hold, if it is one of ours.</summary>
    public static void Open(PlayerMovement who, WeaponController weapons)
    {
        if (who == null || weapons == null || IsOpen || ShopMenu.IsOpen) return;
        var held = weapons.HeldGun;
        if (held == null) return;
        if (instance == null) instance = new GameObject("Gun Customize Menu").AddComponent<GunCustomizeMenu>();
        instance.Show(who, weapons, held);
    }

    private void Show(PlayerMovement who, WeaponController weapons, FPSGun held)
    {
        player = who;
        weapon = weapons;
        gun = held;
        if (canvas == null) BuildCanvas();
        canvas.SetActive(true);
        Build();
        open = true;
        closing = false;
        openedFrame = Time.frameCount;
        alpha = 0f;
        fade.alpha = 0f;
        weapon.Customizing = true;
        Say(null, KitStyle.Text);
        OpenSlot(slots.Count > 0 ? slots[0] : null);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        HoldInput(true);
    }

    // The gun goes back to the hands as the menu fades; the cursor and the player's input come back once it's gone.
    private void BeginClose()
    {
        if (!open || closing) return;
        closing = true;
        if (weapon != null) weapon.Customizing = false;
    }

    private void FinishClose()
    {
        if (!open) return;
        open = false;
        closing = false;
        closedFrame = Time.frameCount;
        if (weapon != null) weapon.Customizing = false;
        if (canvas != null) canvas.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        HoldInput(false);
        if (SettingsMenu.IsOpen) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // The player's move and combat input wait while customizing, as for the shop; whatever was on comes back after.
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
        alpha = Mathf.MoveTowards(alpha, closing ? 0f : 1f, Time.unscaledDeltaTime / FadeSeconds);
        fade.alpha = alpha;
        if (closing)
        {
            if (alpha <= 0f) FinishClose();
            return;
        }
        var keyboard = Keyboard.current;
        bool shut = keyboard != null && Time.frameCount != openedFrame
            && (keyboard.escapeKey.wasPressedThisFrame || keyboard.cKey.wasPressedThisFrame);
        if (shut || player == null || !player.IsInitialized || player.IsDead || player.DamageReceiver.IsDowned
            || weapon == null || weapon.HeldGun != gun)
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

    // ---- Choosing ----

    private void OpenSlot(SlotBox box)
    {
        openSlot = box;
        foreach (var each in slots) each.Column.gameObject.SetActive(each == box);
        Refresh();
    }

    private void Choose(SlotBox box, Part part)
    {
        if (!open || closing || weapon == null) return;
        string why = weapon.CustomizeRefusal;
        bool done = why == null && (box.Slot == Slot.Sight ? weapon.ChooseSight(part.Index)
            : box.Slot == Slot.Muzzle ? weapon.ChooseMuzzle(part.Index)
            : box.Slot == Slot.Grip ? weapon.ChooseGrip(part.Index) : weapon.ChooseUnderbarrel(part.Index));
        if (why != null) Say(why, KitStyle.Red);
        else if (done) Say(part.Name + " 장착", KitStyle.Accent);
        Refresh();
    }

    private int Current(Slot slot) => slot == Slot.Sight ? gun.SightIndex : slot == Slot.Muzzle ? gun.MuzzleIndex
        : slot == Slot.Grip ? gun.GripIndex : gun.UnderbarrelIndex;

    // ---- The gun's numbers ----

    // The held gun's stats with what it wears now, or with one slot's part swapped for another.
    private Stats StatsWith(SlotBox swapSlot = null, int swapIndex = -1)
    {
        var data = gun.Data;
        var parts = gun.Attachments;
        int muzzle = gun.MuzzleIndex, grip = gun.GripIndex;
        if (swapSlot != null && swapSlot.Slot == Slot.Muzzle) muzzle = swapIndex;
        if (swapSlot != null && swapSlot.Slot == Slot.Grip) grip = swapIndex;
        var muzzleKind = parts != null && muzzle >= 0 && muzzle < parts.muzzle.Length ? parts.muzzle[muzzle].kind : GunAttachments.Kind.None;
        var gripKind = parts != null && grip >= 0 && grip < parts.grips.Length ? parts.grips[grip].kind : GunAttachments.Kind.None;
        if (data == null) return default;
        return new Stats
        {
            Body = data.Damage,
            Head = data.Damage * data.HeadshotMultiplier,
            Pellets = data.Pellets,
            Rpm = 60f / Mathf.Max(.001f, data.ShotInterval),
            Recoil = data.RecoilVertical * (gripKind == GunAttachments.Kind.VerticalGrip ? data.GripRecoil : 1f),
            AimTime = data.AdsTransitionTime * (gripKind == GunAttachments.Kind.AngledGrip ? data.GripAimTime : 1f),
            Noise = data.NoiseRadius * (muzzleKind == GunAttachments.Kind.Suppressor ? data.SuppressedNoise : 1f),
            Magazine = data.MagazineSize
        };
    }

    // ---- What each part is called and does ----

    private static string SightName(in GunSights.Sight sight)
    {
        if (sight.scope != null) return "배율 조준경 ×" + sight.scope.magnification.ToString("0.#");
        string part = sight.part != null ? sight.part.name : "";
        if (sight.part == null || sight.ironSight || part.Contains("Iron")) return "가늠쇠";
        if (part.Contains("Holograph")) return "홀로그램";
        if (part.Contains("RedDot")) return part.Contains("Canted") ? "레드닷 (기울임)" : "레드닷";
        return Clean(part);
    }

    private static string PartName(GunAttachments.Choice choice) => choice.kind switch
    {
        GunAttachments.Kind.None => "없음",
        GunAttachments.Kind.Suppressor => "소음기",
        GunAttachments.Kind.Flashlight => "플래시라이트",
        // Two vertical grips can be on offer (the TR-15's own and the Zetco), told apart by their maker.
        GunAttachments.Kind.VerticalGrip => choice.part != null && choice.part.name.Contains("Zetco") ? "수직 손잡이 (Zetco)"
            : choice.part != null && choice.part.name.Contains("TR15") ? "수직 손잡이 (TR-15)" : "수직 손잡이",
        GunAttachments.Kind.AngledGrip => "앵글 손잡이",
        _ => choice.part != null ? Clean(choice.part.name) : "부품",
    };

    // What a part gives (+) and costs (−), as ArcCW lists them.
    private void Describe(Part part, GunAttachments.Kind kind, GunScope scope)
    {
        var data = gun.Data;
        if (scope != null) part.Pros.Add("조준하면 렌즈 안이 ×" + scope.magnification.ToString("0.#") + " 확대");
        switch (kind)
        {
            case GunAttachments.Kind.Suppressor:
                float noise = data != null ? data.NoiseRadius : 40f, share = data != null ? data.SuppressedNoise : .3f;
                part.Pros.Add("좀비가 듣는 거리 " + noise.ToString("0") + "m → " + (noise * share).ToString("0") + "m");
                part.Pros.Add("총소리가 작아짐");
                part.Pros.Add("총구 불꽃과 섬광이 없어짐");
                break;
            case GunAttachments.Kind.VerticalGrip:
                part.Pros.Add("위로 튀는 반동 " + Mathf.RoundToInt((1f - (data != null ? data.GripRecoil : .8f)) * 100f) + "% 감소");
                part.Pros.Add("왼손이 손잡이를 쥠");
                break;
            case GunAttachments.Kind.AngledGrip:
                part.Pros.Add("조준이 " + Mathf.RoundToInt((1f - (data != null ? data.GripAimTime : .8f)) * 100f) + "% 빨라짐");
                part.Pros.Add("왼손이 손잡이에 닿음");
                break;
            case GunAttachments.Kind.Flashlight:
                part.Pros.Add("[L]로 켜고 끄는 라이트");
                break;
        }
    }

    // A pack part's own name without its prefix and underscores (A_Phantom_TAC → Phantom TAC).
    private static string Clean(string name)
    {
        if (name.StartsWith("A_")) name = name.Substring(2);
        return name.Replace('_', ' ');
    }

    // ---- Screen ----

    private void Refresh()
    {
        if (gun == null) return;
        foreach (var box in slots)
        {
            bool isOpen = box == openSlot;
            box.Accent.color = isOpen ? KitStyle.Accent : KitStyle.Faint;
            box.Back.color = isOpen ? KitStyle.With(KitStyle.Card, 1f) : KitStyle.Card;
            int now = Current(box.Slot);
            var worn = box.Parts.Find(part => part.Index == now);
            box.Current.text = worn != null ? worn.Name : "없음";
            foreach (var part in box.Parts)
            {
                bool on = part.Index == now;
                part.Mark.enabled = on;
                part.Label.color = on ? KitStyle.Accent : KitStyle.Text;
                part.Back.color = part.Hovered ? KitStyle.With(KitStyle.Accent, .16f) : KitStyle.Card;
            }
        }

        // The detail: the part under the pointer, else the open slot's part on now.
        Part shown = null;
        if (openSlot != null)
        {
            shown = openSlot.Parts.Find(part => part.Hovered);
            int now = Current(openSlot.Slot);
            if (shown == null) shown = openSlot.Parts.Find(part => part.Index == now);
        }
        detail.gameObject.SetActive(shown != null);
        if (shown != null)
        {
            detailTitle.text = shown.Name;
            var lines = new System.Text.StringBuilder();
            foreach (string pro in shown.Pros) lines.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(KitStyle.Green)).Append(">+ ").Append(pro).Append("</color>\n");
            foreach (string con in shown.Cons) lines.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(KitStyle.Red)).Append(">− ").Append(con).Append("</color>\n");
            detailBody.text = lines.Length > 0 ? lines.ToString().TrimEnd('\n') : "능력치는 그대로예요";
            // Under the open slot's last part.
            float top = -openSlot.Column.anchoredPosition.y + openSlot.Parts.Count * (PartHeight + PartGap) + 8f;
            detail.anchoredPosition = new Vector2(Margin + SlotWidth + ColumnGap, -top);
        }

        // The stats: what the gun has now, and what the part under the pointer would make of it.
        var nowStats = StatsWith();
        var hovered = openSlot != null ? openSlot.Parts.Find(part => part.Hovered) : null;
        bool comparing = hovered != null && hovered.Index != Current(openSlot.Slot);
        var then = comparing ? StatsWith(openSlot, hovered.Index) : nowStats;
        foreach (var (value, read, better) in statRows)
        {
            string a = read(nowStats), b = read(then);
            if (!comparing || a == b) { value.text = a; value.color = KitStyle.Text; continue; }
            int sign = better(nowStats, then);
            value.text = a + "  →  " + b;
            value.color = sign > 0 ? KitStyle.Green : sign < 0 ? KitStyle.Red : KitStyle.Text;
        }
    }

    private void Say(string text, Color color)
    {
        if (message == null) return;
        message.text = text ?? (slots.Count > 0 ? "[C] · Esc 닫기   슬롯을 눌러 부품을 고르세요" : "바꿀 수 있는 부품이 없어요   [C] 닫기");
        message.color = text != null ? color : KitStyle.Soft;
        messageLeft = text != null ? MessageSeconds : 0f;
    }

    private void BuildCanvas()
    {
        canvas = new GameObject("Gun Customize", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.hideFlags = HideFlags.DontSave;
        canvas.transform.SetParent(transform, false);
        var layer = canvas.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = 95;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
    }

    // Made again for each gun: its slots and parts are its own.
    private void Build()
    {
        if (root != null) Destroy(root.gameObject);
        slots.Clear();
        statRows.Clear();
        openSlot = null;
        root = KitStyle.Stretch("Menu", canvas.transform);
        fade = root.gameObject.AddComponent<CanvasGroup>();

        // The gun's name over the slots.
        var name = KitStyle.TopLeft("Gun", root, new Vector2(Margin, 56f), new Vector2(SlotWidth + PartWidth, 44f));
        KitStyle.Label(name, gun.Data != null ? gun.Data.DisplayName : gun.name, 34f, TextAlignmentOptions.Left);
        var caption = KitStyle.TopLeft("Caption", root, new Vector2(Margin, 100f), new Vector2(SlotWidth, 20f));
        KitStyle.Label(caption, "커스터마이징", 14f, TextAlignmentOptions.Left, KitStyle.Accent);

        // The slots, each with its parts.
        var sights = gun.Sights;
        if (sights != null && sights.Count > 1)
        {
            var box = new SlotBox { Slot = Slot.Sight, Title = "조준기" };
            for (int i = 0; i < sights.sights.Length; i++)
            {
                var part = new Part { Index = i, Name = SightName(sights.sights[i]) };
                Describe(part, GunAttachments.Kind.None, sights.sights[i].scope);
                box.Parts.Add(part);
            }
            slots.Add(box);
        }
        var parts = gun.Attachments;
        if (parts != null)
        {
            AddSlot(Slot.Muzzle, "총구", parts.muzzle);
            AddSlot(Slot.Grip, "손잡이", parts.grips);
            AddSlot(Slot.Underbarrel, "하단 레일", parts.underbarrel);
        }
        for (int i = 0; i < slots.Count; i++) BuildSlot(slots[i], Top + i * (SlotHeight + SlotGap));

        // What the part under the pointer gives and costs, under the open slot's parts.
        detail = KitStyle.TopLeft("Detail", root, new Vector2(Margin + SlotWidth + ColumnGap, Top), new Vector2(PartWidth, 150f));
        KitStyle.Fill(detail, KitStyle.Panel);
        var detailName = KitStyle.TopLeft("Name", detail, new Vector2(16f, 10f), new Vector2(PartWidth - 32f, 26f));
        detailTitle = KitStyle.Label(detailName, "", 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        var detailLines = KitStyle.TopLeft("Lines", detail, new Vector2(16f, 40f), new Vector2(PartWidth - 32f, 100f));
        detailBody = KitStyle.Label(detailLines, "", 14f, TextAlignmentOptions.TopLeft);
        detailBody.fontStyle = FontStyles.Normal;
        detailBody.richText = true;

        BuildStats();

        // The way out, and what was refused.
        var footer = FeelUI.Rect("Message", root);
        footer.anchorMin = footer.anchorMax = footer.pivot = new Vector2(0f, 0f);
        footer.anchoredPosition = new Vector2(Margin, 40f);
        footer.sizeDelta = new Vector2(900f, 30f);
        message = KitStyle.Label(footer, "", 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        message.fontStyle = FontStyles.Normal;
        var close = FeelUI.Rect("Close", root);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-Margin, -56f);
        close.sizeDelta = new Vector2(40f, 40f);
        var closeBack = KitStyle.Fill(close, KitStyle.Card);
        closeBack.raycastTarget = true;
        KitStyle.Label(close, "X", 18f, TextAlignmentOptions.Center);
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBack;
        closeButton.onClick.AddListener(BeginClose);

        void AddSlot(Slot slot, string title, GunAttachments.Choice[] choices)
        {
            if (choices == null || choices.Length <= 1) return;
            var box = new SlotBox { Slot = slot, Title = title };
            for (int i = 0; i < choices.Length; i++)
            {
                var part = new Part { Index = i, Name = PartName(choices[i]) };
                Describe(part, choices[i].kind, null);
                box.Parts.Add(part);
            }
            slots.Add(box);
        }
    }

    // A slot's box down the left (its name small, the part on it now large) and its parts' column beside it.
    private void BuildSlot(SlotBox box, float top)
    {
        var rect = KitStyle.TopLeft("Slot " + box.Title, root, new Vector2(Margin, top), new Vector2(SlotWidth, SlotHeight));
        box.Back = KitStyle.Fill(rect, KitStyle.Card);
        box.Back.raycastTarget = true;
        box.Accent = KitStyle.Bar("Accent", rect, Vector2.zero, new Vector2(KitStyle.AccentWidth, SlotHeight), KitStyle.Faint);
        var title = KitStyle.TopLeft("Title", rect, new Vector2(18f, 8f), new Vector2(SlotWidth - 30f, 18f));
        KitStyle.Label(title, box.Title, 13f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
        var current = KitStyle.TopLeft("Current", rect, new Vector2(18f, 28f), new Vector2(SlotWidth - 30f, 30f));
        box.Current = KitStyle.Label(current, "", 20f, TextAlignmentOptions.MidlineLeft);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = box.Back;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => OpenSlot(box));

        box.Column = KitStyle.TopLeft("Parts " + box.Title, root, new Vector2(Margin + SlotWidth + ColumnGap, top), new Vector2(PartWidth, 10f));
        for (int i = 0; i < box.Parts.Count; i++) BuildPart(box, box.Parts[i], i * (PartHeight + PartGap));
        box.Column.gameObject.SetActive(false);
    }

    private void BuildPart(SlotBox box, Part part, float top)
    {
        var rect = KitStyle.TopLeft("Part " + part.Name, box.Column, new Vector2(0f, top), new Vector2(PartWidth, PartHeight));
        part.Back = KitStyle.Fill(rect, KitStyle.Card);
        part.Back.raycastTarget = true;
        var label = KitStyle.TopLeft("Name", rect, new Vector2(16f, 0f), new Vector2(PartWidth - 60f, PartHeight));
        part.Label = KitStyle.Label(label, part.Name, 17f, TextAlignmentOptions.MidlineLeft);
        // A small square at the right for the part on now.
        part.Mark = KitStyle.Bar("On", rect, new Vector2(PartWidth - 26f, PartHeight * .5f - 5f), new Vector2(10f, 10f), KitStyle.Accent);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = part.Back;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Choose(box, part));
        var trigger = rect.gameObject.AddComponent<EventTrigger>();
        Listen(trigger, EventTriggerType.PointerEnter, () => { part.Hovered = true; Refresh(); });
        Listen(trigger, EventTriggerType.PointerExit, () => { part.Hovered = false; Refresh(); });
    }

    private static void Listen(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    // The stats down the right, each row the gun's number now (and "→ then" in green or red for a part under the pointer).
    private void BuildStats()
    {
        var panel = FeelUI.Rect("Stats", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-Margin, -Top);
        panel.sizeDelta = new Vector2(StatsWidth, 56f + 6 * StatRow);
        KitStyle.Fill(panel, KitStyle.Panel);
        var title = KitStyle.TopLeft("Title", panel, new Vector2(18f, 12f), new Vector2(StatsWidth - 36f, 24f));
        KitStyle.Label(title, "능력치", 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        int row = 0;
        Add("피해 (몸 / 머리)", s => (s.Pellets > 1 ? s.Body.ToString("0") + "×" + s.Pellets : s.Body.ToString("0")) + " / " + s.Head.ToString("0"), (a, b) => 0);
        Add("연사 속도", s => s.Rpm.ToString("0") + " RPM", (a, b) => 0);
        Add("위로 튀는 반동", s => s.Recoil.ToString("0.00") + "°", (a, b) => b.Recoil < a.Recoil ? 1 : b.Recoil > a.Recoil ? -1 : 0);
        Add("조준 시간", s => s.AimTime.ToString("0.00") + "초", (a, b) => b.AimTime < a.AimTime ? 1 : b.AimTime > a.AimTime ? -1 : 0);
        Add("좀비가 듣는 거리", s => s.Noise.ToString("0") + "m", (a, b) => b.Noise < a.Noise ? 1 : b.Noise > a.Noise ? -1 : 0);
        Add("탄창", s => s.Magazine.ToString(), (a, b) => 0);

        void Add(string label, System.Func<Stats, string> read, System.Func<Stats, Stats, int> better)
        {
            float y = 46f + row++ * StatRow;
            var name = KitStyle.TopLeft("Label " + label, panel, new Vector2(18f, y), new Vector2(150f, StatRow));
            KitStyle.Label(name, label, 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft).fontStyle = FontStyles.Normal;
            var value = KitStyle.TopLeft("Value " + label, panel, new Vector2(170f, y), new Vector2(StatsWidth - 188f, StatRow));
            statRows.Add((KitStyle.Label(value, "", 17f, TextAlignmentOptions.MidlineRight), read, better));
        }
    }
}
