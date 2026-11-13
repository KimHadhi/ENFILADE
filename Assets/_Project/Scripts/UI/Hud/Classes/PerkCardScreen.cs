using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The perk card pick (stage 6C, the user's rules on 2026-11-09). When the ultimate gauge is full a "능력 준비 [X]"
/// prompt glows at the bottom. X opens the show:
/// <list type="bullet">
/// <item>the screen darkens, a pink slash band tears across it and "능력 선택" slams in;</item>
/// <item>three big cards are dealt face down from below one after another, each landing with a ring burst and a jolt,
/// and flip over: the perk's kind, icon, name, level pips, what it already has ("보유 1단계 → 2단계") and what the next
/// level does, with its key (1, 2, 3) under it; a shine runs across them now and then;</item>
/// <item>the perks already held line up at the bottom as badges (PerkBadge).</item>
/// </list>
/// The card under the pointer lifts and glows pink. A key or a click picks one: it flashes white, shards burst out,
/// the others fall away spinning, and the picked card shrinks into its badge in the tray, which pops with its new level;
/// the gauge is spent and the perk goes up a level (PlayerPerks). X or Esc puts the cards away and keeps the gauge.
/// Alone the game stops while they are up; in a room it keeps running and only the combat keys wait (so 1, 2 and 3 pick
/// instead of switching guns). Also applies the health perk to this player's body. Added to this PC's own player only.
/// Since the class cards (2026-11-11, Docs/Features/ClassCards.md) the three are drawn from everyone's cards and this player's
/// class's own (PlayerPerks.Dealable); each card says under its name, small and slanted, why it works (the user's rule
/// that an effect comes with a believable reason), and a class's final card (궁극) wears a pink frame from the start.
/// Once every card this player can be dealt is taken, the prompt reads "궁극 준비" and X spends the full gauge on the
/// class's ultimate at once (ClassCards.Ultimate) instead of opening the cards. The hub keeps the same rule with practice
/// cards (2026-11-12, the user: "궁극기가 좀 이상한 게. 카드를 뽑아야 하는 거 아니야?"): its range's kills charge the gauge,
/// X deals cards, the ultimate comes after all of them, and a pink line under the title says they go when the hub is left
/// or a class is picked (PlayerPerks.IsPractising).
/// </summary>
[DisallowMultipleComponent]
public sealed class PerkCardScreen : MonoBehaviour
{
    private const int Choices = 3, MaxPips = 5, Order = 92, ShardCount = 18;
    private const float CardWidth = 380f, CardHeight = 540f, Spread = 440f, Border = 3f, TrayY = 52f, BadgeHeight = 56f;
    private const float DealFirst = .12f, DealSeconds = .2f, DealStagger = .09f, FlipSeconds = .14f, DealFrom = -800f, HoverSeconds = .08f;
    private const float PickPunch = .14f, PickFly = .3f, PickSeconds = .62f, CloseSeconds = .16f, ShineEvery = 2.6f, ShineSeconds = .5f;
    private static readonly float[] Tilts = { 3f, -2f, 1.5f };
    private static readonly Key[] DigitKeys = { Key.Digit1, Key.Digit2, Key.Digit3 }, PadKeys = { Key.Numpad1, Key.Numpad2, Key.Numpad3 };

    private sealed class Card
    {
        public RectTransform Root, Lift, Body, Front, Back, Shine;
        public Image Frame, Plate, Lid, KeyBack, TagBack, Owned;
        public RawImage Icon, Glow;
        public TMP_Text Tag, Name, Step, Why, Text, Key;
        public Image[] Pips;
        public PlayerPerks.Perk Perk;
        public float Hover;
        public bool Landed;
        public Vector2 FlyFrom;
    }

    private sealed class Spark
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 Velocity;
        public float Spin, Age, Life;
    }

    private sealed class Burst
    {
        public RectTransform Rect;
        public RawImage Image;
        public Color Color;
        public float Age, Life, From, To;
    }

    private static PerkCardScreen instance;
    private static int closedFrame = -1;

    /// <summary>The cards are up (other menus and Esc leave the keyboard to them).</summary>
    public static bool IsOpen => instance != null && instance.open;
    public static bool TakesKeyboard => IsOpen || Time.frameCount == closedFrame;

    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private SinglePlayerChatManager chat;
    private GameObject host;
    private RectTransform layer, stage, fx, prompt, band, title, tray, landing;
    private CanvasGroup layerGroup, promptGroup, titleGroup, landingGroup;
    private Image veil, promptBar, bandImage;
    private TMP_Text costLabel, emptyLabel, promptLabel, promptKey, practiceLabel;
    // What the ready prompt says now: the cards, or, every card taken, the class's ultimate.
    private bool promptCards = true;
    private readonly List<Card> cards = new List<Card>();
    private readonly List<Spark> sparks = new List<Spark>();
    private readonly List<Burst> bursts = new List<Burst>();
    private readonly System.Random random = new System.Random(6026);
    private bool open, closing, paused, playerMapWas, combatMapWas, holdingPlayerMap, holdingCombatMap, popped;
    private int picked = -1, dealt;
    private float openedAt, endedAt, shake, baseHealth = -1f, shownExtra;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        closedFrame = -1;
    }

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        receiver = GetComponent<PlayerDamageReceiver>();
    }

    private void Start()
    {
        if (player == null || !player.IsLocal)
        {
            enabled = false;
            return;
        }
        instance = this;
        Build();
        PlayerPerks.Changed += OnPerksChanged;
        ApplyHealth(true);
    }

    private void OnDestroy()
    {
        PlayerPerks.Changed -= OnPerksChanged;
        if (open) Release();
        if (host != null) Destroy(host);
        if (instance == this) instance = null;
    }

    // ---- The health perk ----

    private void OnPerksChanged() => ApplyHealth(false);

    // The body's own top health plus the perk's; a new level heals by what it added. A fresh body starts full.
    private void ApplyHealth(bool fresh)
    {
        if (receiver == null) return;
        if (baseHealth < 0f) baseHealth = receiver.MaximumHealth;
        float extra = PlayerPerks.ExtraHealth;
        if (!fresh && Mathf.Approximately(extra, shownExtra)) return;
        float gained = extra - shownExtra;
        shownExtra = extra;
        receiver.SetMaximumHealth(baseHealth + extra, fresh && extra > 0f);
        if (!fresh && gained > 0f) receiver.Heal(gained);
    }

    // ---- Building ----

    private void Build()
    {
        host = new GameObject("Perk Cards", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        FeelUI.SetupOverlay(host, Order);
        var canvas = (RectTransform)host.transform;
        BuildPrompt(canvas);

        layer = KitStyle.Stretch("Cards", canvas);
        layerGroup = layer.gameObject.AddComponent<CanvasGroup>();
        veil = KitStyle.Fill(KitStyle.Stretch("Veil", layer), KitStyle.With(KitStyle.Night, 0f));
        // Everything that shakes on a landing lives on the stage.
        stage = KitStyle.Stretch("Stage", layer);

        band = FeelUI.Rect("Slash", stage);
        band.pivot = new Vector2(0f, .5f);
        band.anchoredPosition = new Vector2(-1500f, 40f);
        band.sizeDelta = new Vector2(3000f, 300f);
        band.localRotation = Quaternion.Euler(0f, 0f, 10f);
        bandImage = KitStyle.Fill(band, KitStyle.Accent);

        title = KitStyle.TopLeft("Title", stage, new Vector2(72f, 64f), new Vector2(700f, 76f));
        title.pivot = new Vector2(0f, .5f);
        title.anchoredPosition = new Vector2(72f, -102f);
        title.localRotation = Quaternion.Euler(0f, 0f, 3f);
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();
        KitStyle.Flat(title, Texts.Get("perk.title"), 60f, TextAlignmentOptions.MidlineLeft);
        var cost = KitStyle.TopLeft("Cost", title, new Vector2(6f, 80f), new Vector2(220f, 32f));
        cost.localRotation = Quaternion.Euler(0f, 0f, -6f);
        KitStyle.Fill(cost, KitStyle.Accent);
        costLabel = KitStyle.Flat(cost, "", 17f, TextAlignmentOptions.Center, KitStyle.Ink);
        // In the hub: these are practice cards, gone again when it is left (shown only then, Open).
        practiceLabel = KitStyle.Flat(KitStyle.TopLeft("Practice", title, new Vector2(10f, 124f), new Vector2(640f, 28f)),
            Texts.Get("perk.practice"), 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        practiceLabel.gameObject.SetActive(false);

        var hint = FeelUI.Rect("Hint", stage);
        hint.anchorMin = hint.anchorMax = hint.pivot = Vector2.one;
        hint.anchoredPosition = new Vector2(-72f, -70f);
        hint.sizeDelta = new Vector2(420f, 64f);
        var hintLabel = KitStyle.Flat(hint, Texts.Get("perk.hint"), 19f, TextAlignmentOptions.TopRight, KitStyle.Soft);
        hintLabel.fontStyle = FontStyles.Normal;
        hintLabel.lineSpacing = 10f;

        for (int i = 0; i < Choices; i++) cards.Add(BuildCard(i));

        // The perks held, as badges along the bottom.
        var trayCaption = FeelUI.Rect("Tray Caption", stage);
        trayCaption.anchorMin = trayCaption.anchorMax = trayCaption.pivot = new Vector2(.5f, 0f);
        trayCaption.anchoredPosition = new Vector2(0f, TrayY + BadgeHeight + 8f);
        trayCaption.sizeDelta = new Vector2(400f, 24f);
        KitStyle.Flat(trayCaption, Texts.Get("perk.tray"), 15f, TextAlignmentOptions.Center, KitStyle.Ice).characterSpacing = 4f;
        tray = FeelUI.Rect("Tray", stage);
        tray.anchorMin = tray.anchorMax = new Vector2(.5f, 0f);
        tray.anchoredPosition = new Vector2(0f, TrayY + BadgeHeight * .5f);
        var empty = FeelUI.Rect("None Yet", stage);
        empty.anchorMin = empty.anchorMax = new Vector2(.5f, 0f);
        empty.anchoredPosition = new Vector2(0f, TrayY + BadgeHeight * .5f);
        empty.sizeDelta = new Vector2(600f, BadgeHeight);
        emptyLabel = KitStyle.Flat(empty, Texts.Get("perk.tray.empty"), 17f, TextAlignmentOptions.Center, KitStyle.Faint);
        emptyLabel.fontStyle = FontStyles.Normal;

        fx = KitStyle.Stretch("Effects", stage);
        layer.gameObject.SetActive(false);
    }

    // "능력 준비 [X]" at the bottom, shown while the gauge is full and the cards are away.
    private void BuildPrompt(RectTransform canvas)
    {
        prompt = FeelUI.Rect("Ready", canvas);
        prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, 172f);
        prompt.sizeDelta = new Vector2(250f, 48f);
        promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        promptGroup.blocksRaycasts = promptGroup.interactable = false;
        KitSkew.On(KitStyle.Fill(prompt, KitStyle.With(KitStyle.Night, .82f)), 14f);
        promptBar = KitStyle.Bar("Bar", prompt, Vector2.zero, new Vector2(6f, 48f), KitStyle.Accent);
        KitSkew.On(promptBar, 14f);
        promptLabel = KitStyle.Flat(KitStyle.TopLeft("Text", prompt, new Vector2(24f, 0f), new Vector2(160f, 48f)), Texts.Get("perk.ready"), 21f,
            TextAlignmentOptions.MidlineLeft);
        var key = KitStyle.TopLeft("Key", prompt, new Vector2(250f - 52f, 8f), new Vector2(32f, 32f));
        KitStyle.Fill(key, KitStyle.Ice);
        // The key the player put on Player/PerkCards (X by default), read again each time the prompt shows.
        promptKey = KitStyle.Flat(key, KeyBindings.Name("Player/PerkCards"), 18f, TextAlignmentOptions.Center, KitStyle.Ink);
        promptKey.enableAutoSizing = true;
        promptKey.fontSizeMin = 9f;
        promptKey.fontSizeMax = 18f;
        prompt.gameObject.SetActive(false);
    }

    private Card BuildCard(int index)
    {
        var root = FeelUI.Rect("Card " + (index + 1), stage);
        root.anchoredPosition = new Vector2((index - (Choices - 1) * .5f) * Spread, 30f);
        root.sizeDelta = new Vector2(CardWidth, CardHeight);
        root.localRotation = Quaternion.Euler(0f, 0f, Tilts[index % Tilts.Length]);
        KitStyle.Fill(root, Color.clear).raycastTarget = true;
        var card = new Card { Root = root };

        // A pink glow behind it while it is pointed at.
        var glow = FeelUI.Rect("Glow", root);
        glow.sizeDelta = new Vector2(CardWidth * 1.7f, CardHeight * 1.45f);
        card.Glow = glow.gameObject.AddComponent<RawImage>();
        card.Glow.texture = KitShapes.Glow;
        card.Glow.color = Color.clear;
        card.Glow.raycastTarget = false;

        card.Lift = KitStyle.Stretch("Lift", root);
        card.Body = KitStyle.Stretch("Body", card.Lift);
        card.Front = KitStyle.Stretch("Front", card.Body);
        card.Frame = KitStyle.Fill(KitStyle.Stretch("Frame", card.Front), KitStyle.Ice);
        var plate = KitStyle.Stretch("Plate", card.Front);
        plate.sizeDelta = new Vector2(-Border * 2f, -Border * 2f);
        card.Plate = KitStyle.Fill(plate, KitStyle.With(KitStyle.Night, .97f));
        plate.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        card.Shine = FeelUI.Rect("Shine", plate);
        card.Shine.sizeDelta = new Vector2(70f, CardHeight * 1.6f);
        card.Shine.localRotation = Quaternion.Euler(0f, 0f, -22f);
        KitStyle.Fill(card.Shine, new Color(1f, 1f, 1f, .13f));

        var tag = KitStyle.TopLeft("Tag", card.Front, new Vector2(24f, 24f), new Vector2(84f, 30f));
        card.TagBack = KitStyle.Fill(tag, KitStyle.With(KitStyle.Ice, .15f));
        card.Tag = KitStyle.Flat(tag, "", 16f, TextAlignmentOptions.Center);
        var owned = KitStyle.TopLeft("Owned", card.Front, new Vector2(CardWidth - 112f, 22f), new Vector2(90f, 32f));
        owned.localRotation = Quaternion.Euler(0f, 0f, -6f);
        card.Owned = KitStyle.Fill(owned, KitStyle.Accent);
        KitStyle.Flat(owned, Texts.Get("perk.owned"), 17f, TextAlignmentOptions.Center, KitStyle.Ink);
        var icon = FeelUI.Rect("Icon", card.Front);
        icon.anchorMin = icon.anchorMax = new Vector2(.5f, 1f);
        icon.anchoredPosition = new Vector2(0f, -150f);
        icon.sizeDelta = new Vector2(128f, 128f);
        card.Icon = icon.gameObject.AddComponent<RawImage>();
        card.Icon.raycastTarget = false;
        card.Name = KitStyle.Flat(KitStyle.TopLeft("Name", card.Front, new Vector2(20f, 226f), new Vector2(CardWidth - 40f, 54f)), "", 38f,
            TextAlignmentOptions.Center);
        var pips = KitStyle.TopLeft("Pips", card.Front, new Vector2(0f, 286f), new Vector2(CardWidth, 6f));
        card.Pips = new Image[MaxPips];
        for (int i = 0; i < MaxPips; i++) card.Pips[i] = KitStyle.Fill(KitStyle.TopLeft("Pip", pips, Vector2.zero, new Vector2(34f, 6f)), KitStyle.Faint);
        card.Step = KitStyle.Flat(KitStyle.TopLeft("Step", card.Front, new Vector2(20f, 298f), new Vector2(CardWidth - 40f, 28f)), "", 18f,
            TextAlignmentOptions.Center, KitStyle.Accent);
        // Why it works (the user's rule: every effect comes with a reason that makes it believable), small, over the effect.
        card.Why = KitStyle.Flat(KitStyle.TopLeft("Why", card.Front, new Vector2(30f, 332f), new Vector2(CardWidth - 60f, 66f)), "", 16f,
            TextAlignmentOptions.Top, KitStyle.With(KitStyle.Soft, .72f));
        card.Why.textWrappingMode = TextWrappingModes.Normal;
        card.Why.fontStyle = FontStyles.Italic;
        card.Text = KitStyle.Flat(KitStyle.TopLeft("Effect", card.Front, new Vector2(30f, 402f), new Vector2(CardWidth - 60f, 100f)), "", 21f,
            TextAlignmentOptions.Top, KitStyle.Soft);
        card.Text.textWrappingMode = TextWrappingModes.Normal;
        card.Text.fontStyle = FontStyles.Normal;
        var key = FeelUI.Rect("Key", card.Front);
        key.anchorMin = key.anchorMax = new Vector2(.5f, 0f);
        key.sizeDelta = new Vector2(60f, 60f);
        card.KeyBack = KitStyle.Fill(key, KitStyle.Ice);
        card.Key = KitStyle.Flat(key, (index + 1).ToString(), 30f, TextAlignmentOptions.Center, KitStyle.Ink);

        // The face-down side: pink, a big question mark, the game's name.
        card.Back = KitStyle.Stretch("Back", card.Body);
        KitStyle.Fill(card.Back, KitStyle.Accent);
        var inner = KitStyle.Stretch("Inner", card.Back);
        inner.sizeDelta = new Vector2(-28f, -28f);
        KitStyle.Fill(inner, KitStyle.With(KitStyle.Ink, .12f));
        KitStyle.Flat(KitStyle.Stretch("Mark", card.Back), "?", 190f, TextAlignmentOptions.Center, KitStyle.Ink);
        var name = KitStyle.TopLeft("Name", card.Back, new Vector2(0f, CardHeight - 70f), new Vector2(CardWidth, 40f));
        KitStyle.Flat(name, "ENFILADE", 20f, TextAlignmentOptions.Center, KitStyle.Ink).characterSpacing = 10f;

        card.Lid = KitStyle.Fill(KitStyle.Stretch("Flash", card.Body), Color.clear);

        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Pick(index));
        return card;
    }

    // ---- Opening, picking, closing ----

    private void Open()
    {
        var drawn = PlayerPerks.Draw(Choices);
        if (drawn.Count == 0) return;
        dealt = drawn.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            bool used = i < drawn.Count;
            card.Root.gameObject.SetActive(used);
            if (!used) continue;
            Fill(card, drawn[i]);
            card.Hover = 0f;
            card.Landed = false;
            card.Lid.color = Color.clear;
            card.Root.anchoredPosition = new Vector2((i - (dealt - 1) * .5f) * Spread, 30f);
            card.Lift.anchoredPosition = Vector2.zero;
            card.Lift.localScale = Vector3.one;
            card.Lift.localRotation = Quaternion.identity;
        }
        costLabel.text = Texts.Format("perk.cost", Mathf.RoundToInt(player.Ultimate.RequiredCharge));
        practiceLabel.gameObject.SetActive(PlayerPerks.IsPractising);
        BuildTray(null);
        ClearEffects();

        open = true;
        closing = popped = false;
        picked = -1;
        shake = 0f;
        openedAt = Time.unscaledTime;
        layer.gameObject.SetActive(true);
        layerGroup.alpha = 1f;
        Hold(true);
        Animate();
    }

    private void Fill(Card card, PlayerPerks.Perk perk)
    {
        card.Perk = perk;
        int level = PlayerPerks.Level(perk.Id);
        card.Tag.text = perk.Tag;
        card.Icon.texture = KitButtons.Icon(perk.Icon);
        card.Icon.enabled = card.Icon.texture != null;
        card.Name.text = perk.Name;
        card.Why.text = perk.Reason;
        card.Owned.gameObject.SetActive(level > 0);
        string next = level + 1 >= perk.Levels ? Texts.Get("perk.step.last") : Texts.Format("perk.step", level + 1);
        card.Step.text = level == 0 ? Texts.Get("perk.step.new") : Texts.Format("perk.step.owned", level, next);
        card.Text.text = perk.Describe(level + 1);
        int shown = Mathf.Min(perk.Levels, MaxPips);
        float width = shown * 34f + (shown - 1) * 8f;
        for (int i = 0; i < MaxPips; i++)
        {
            var pip = card.Pips[i];
            pip.gameObject.SetActive(i < shown);
            pip.rectTransform.anchoredPosition = new Vector2((CardWidth - width) * .5f + i * 42f, 0f);
        }
    }

    // The held perks along the bottom; the one just raised, if any, waits hidden for its card to arrive.
    private void BuildTray(string arriving)
    {
        var made = PerkBadge.Row(tray, PlayerPerks.Held(), BadgeHeight, 12f);
        emptyLabel.gameObject.SetActive(made.Count == 0);
        landing = arriving != null && made.TryGetValue(arriving, out var badge) ? badge : null;
        landingGroup = landing != null ? landing.GetComponent<CanvasGroup>() : null;
        if (landingGroup != null) landingGroup.alpha = 0f;
    }

    private void Pick(int index)
    {
        if (!open || closing || picked >= 0 || index < 0 || index >= dealt) return;
        var card = cards[index];
        if (card.Perk == null || player.Ultimate == null || !player.Ultimate.TryConsume())
        {
            Close();
            return;
        }
        picked = index;
        endedAt = Time.unscaledTime;
        card.Hover = 1f;
        card.FlyFrom = card.Lift.anchoredPosition;
        KitPulse.Play(card.Lid, new Color(1f, 1f, 1f, .85f), .35f);
        PlayerPerks.Raise(card.Perk.Id);
        BuildTray(card.Perk.Id);
        Vector2 middle = Local(card.Root, Vector2.zero);
        Shards(middle);
        Ring(middle, KitStyle.Accent, 240f, 1100f, .4f);
        Ring(middle, Color.white, 160f, 700f, .3f);
        shake = 14f;
    }

    private void Close()
    {
        if (!open || closing || picked >= 0) return;
        closing = true;
        endedAt = Time.unscaledTime;
    }

    // Everything is away: the game runs again.
    private void Finish()
    {
        open = closing = false;
        picked = -1;
        ClearEffects();
        layer.gameObject.SetActive(false);
        Release();
    }

    private void Release()
    {
        Hold(false);
        closedFrame = Time.frameCount;
    }

    // Alone the game stops, the cursor comes out and every key waits; in a room only the combat keys wait.
    private void Hold(bool hold)
    {
        bool alone = !GameSession.IsOnline;
        var actions = InputSystem.actions;
        var moves = actions != null ? actions.FindActionMap("Player", false) : null;
        var combat = actions != null ? actions.FindActionMap("Combat", false) : null;
        if (hold)
        {
            holdingCombatMap = combat != null;
            combatMapWas = combat != null && combat.enabled;
            combat?.Disable();
            holdingPlayerMap = alone && moves != null;
            playerMapWas = holdingPlayerMap && moves.enabled;
            if (holdingPlayerMap) moves.Disable();
            paused = alone && !GamePause.IsPaused;
            if (paused) GamePause.Set(true);
            if (alone)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }
        if (holdingCombatMap && combatMapWas) combat?.Enable();
        if (holdingPlayerMap && playerMapWas) moves?.Enable();
        holdingCombatMap = holdingPlayerMap = false;
        if (paused) GamePause.Set(false);
        paused = false;
        if (!SettingsMenu.IsOpen && !ShopMenu.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---- Every frame ----

    private void Update()
    {
        if (host == null) return;
        var keyboard = Keyboard.current;
        bool able = player.IsInitialized && receiver != null && receiver.IsAlive && !player.IsDowned;
        // Every card taken, the full gauge buys the class's ultimate instead (Docs/Features/ClassCards.md: ClassCards.Ultimate).
        bool cards = PlayerPerks.AnyLeft(), ultimate = !cards && ClassCards.Local != null && ClassCards.Local.HasUltimate;
        bool ready = able && player.Ultimate != null && player.Ultimate.IsReady && (cards || ultimate);
        if (open && !able && picked < 0) Close();

        if (!open)
        {
            ShowPrompt(ready && !Blocked(), cards);
            // Player/PerkCards (X unless the key window says otherwise).
            if (ready && KeyBindings.Pressed("Player/PerkCards") && !Blocked())
            {
                if (cards) Open();
                else if (player.Ultimate.TryConsume())
                {
                    ClassCards.Local.Ultimate();
                    // Its face, for everyone (PlayerFace: only this PC knows the ultimate went off).
                    PlayerFace.Ultimate(player);
                }
            }
            return;
        }
        ShowPrompt(false, true);
        if (keyboard != null && picked < 0 && !closing)
        {
            // The same key closes the cards (read from its keys: alone, the cards hold the player's map).
            if (KeyBindings.Pressed("Player/PerkCards") || keyboard.escapeKey.wasPressedThisFrame) Close();
            else
                for (int i = 0; i < dealt; i++)
                    if (keyboard[DigitKeys[i]].wasPressedThisFrame || keyboard[PadKeys[i]].wasPressedThisFrame) { Pick(i); break; }
        }
        Animate();
    }

    // Another menu, the chat or the console has the keyboard, or the Esc menu is up.
    private bool Blocked()
    {
        if (DevConsole.TakesKeyboard || ShopMenu.TakesKeyboard || GunCustomizeMenu.TakesKeyboard || SettingsMenu.IsOpen || GamePause.IsPaused
            || HubDressing.IsOpen) return true;
        if (chat == null) chat = FindAnyObjectByType<SinglePlayerChatManager>();
        return chat != null && chat.BlocksMissionInputThisFrame;
    }

    private void ShowPrompt(bool show, bool cards)
    {
        if (prompt.gameObject.activeSelf != show)
        {
            prompt.gameObject.SetActive(show);
            if (show) promptKey.text = KeyBindings.Name("Player/PerkCards");
        }
        if (show && cards != promptCards)
        {
            promptCards = cards;
            promptLabel.text = Texts.Get(cards ? "perk.ready" : "perk.ult.ready");
        }
        if (!show) return;
        float time = Time.unscaledTime;
        promptBar.color = KitStyle.With(KitStyle.Accent, .55f + .45f * Mathf.Abs(Mathf.Sin(time * 3f)));
        float bob = 1f + .03f * Mathf.Sin(time * 3f);
        prompt.localScale = new Vector3(bob, bob, 1f);
        promptGroup.alpha = 1f;
    }

    private void Animate()
    {
        float time = Time.unscaledTime, dt = Time.unscaledDeltaTime, since = time - openedAt;
        bool ending = picked >= 0 || closing;
        float end = ending ? time - endedAt : 0f;
        veil.color = KitStyle.With(KitStyle.Night, (GameSession.IsOnline ? .35f : .66f) * Mathf.Clamp01(since / .12f));

        // The slash tears across, then settles faint behind the cards.
        float tear = Mathf.Clamp01(since / .2f);
        band.localScale = new Vector3(KitReveal.EaseOut(tear), 1f - .55f * Mathf.Clamp01((since - .2f) / .3f), 1f);
        bandImage.color = KitStyle.With(KitStyle.Accent, Mathf.Lerp(.9f, .16f, Mathf.Clamp01((since - .15f) / .35f)));
        // The title slams in from big.
        float slam = Mathf.Clamp01((since - .04f) / .18f);
        float titleScale = Mathf.Lerp(1.9f, 1f, KitReveal.EaseOut(slam));
        title.localScale = new Vector3(titleScale, titleScale, 1f);
        titleGroup.alpha = slam;
        if (slam >= 1f && since - .04f - .18f < dt) shake = Mathf.Max(shake, 8f);

        var mouse = Mouse.current;
        bool pointing = Cursor.visible && mouse != null && !ending;
        for (int i = 0; i < dealt; i++) AnimateCard(cards[i], i, since, end, dt, time, pointing, mouse);

        // The picked card's badge pops as the card lands in it.
        if (picked >= 0 && landing != null)
        {
            float arrive = Mathf.Clamp01((end - PickPunch - PickFly) / .16f);
            if (arrive > 0f && !popped)
            {
                popped = true;
                Ring(Local(landing, Vector2.zero), KitStyle.Accent, 60f, 420f, .35f);
                shake = Mathf.Max(shake, 6f);
            }
            if (landingGroup != null) landingGroup.alpha = arrive > 0f ? 1f : 0f;
            float pop = arrive > 0f ? 1f + .35f * (1f - KitReveal.EaseOut(arrive)) : 1f;
            landing.localScale = new Vector3(pop, pop, 1f);
        }

        AnimateEffects(dt);
        shake = Mathf.MoveTowards(shake, 0f, dt * 90f);
        stage.anchoredPosition = shake > 0f ? new Vector2(Jitter(), Jitter()) * shake * .5f : Vector2.zero;

        if (picked >= 0) layerGroup.alpha = 1f - Mathf.Clamp01((end - (PickSeconds - .14f)) / .14f);
        else if (closing) layerGroup.alpha = 1f - Mathf.Clamp01(end / CloseSeconds);
        if ((picked >= 0 && end >= PickSeconds) || (closing && end >= CloseSeconds)) Finish();
    }

    private void AnimateCard(Card card, int index, float since, float end, float dt, float time, bool pointing, Mouse mouse)
    {
        // Dealt face down from below, overshooting into place, then flipped over.
        float start = DealFirst + index * DealStagger;
        float k = Mathf.Clamp01((since - start) / DealSeconds);
        float y = Mathf.LerpUnclamped(DealFrom, 0f, OutBack(k));
        float scale = Mathf.LerpUnclamped(.8f, 1f, OutBack(k));
        if (k >= 1f && !card.Landed)
        {
            card.Landed = true;
            Ring(Local(card.Root, Vector2.zero), KitStyle.Accent, 200f, 760f, .32f);
            shake = Mathf.Max(shake, 9f);
        }
        float flip = Mathf.Clamp01((since - start - DealSeconds) / FlipSeconds);
        bool front = flip >= .5f;
        if (card.Front.gameObject.activeSelf != front) card.Front.gameObject.SetActive(front);
        if (card.Back.gameObject.activeSelf == front) card.Back.gameObject.SetActive(!front);
        float face = front ? (flip - .5f) * 2f : 1f - flip * 2f;
        card.Body.anchoredPosition = new Vector2(0f, y);
        card.Body.localScale = new Vector3(scale * Mathf.Max(.02f, face), scale, 1f);

        // A shine runs across now and then, each card in turn.
        float shine = Mathf.Repeat(time + index * .35f, ShineEvery) / ShineSeconds;
        card.Shine.anchoredPosition = new Vector2(shine < 1f ? Mathf.Lerp(-CardWidth, CardWidth, shine) : CardWidth * 2f, 0f);

        bool over = pointing && flip >= 1f && RectTransformUtility.RectangleContainsScreenPoint(card.Root, mouse.position.ReadValue(), null);
        card.Hover = Mathf.MoveTowards(card.Hover, over || picked == index ? 1f : 0f, dt / HoverSeconds);
        Paint(card, card.Hover);
        card.Glow.color = KitStyle.With(KitStyle.Accent, .5f * card.Hover + (card.Perk != null && card.Perk.Final ? .18f : 0f));

        Vector2 lift = new Vector2(0f, 26f * KitReveal.EaseOut(card.Hover));
        float grow = 1f + .05f * card.Hover;
        float spin = 0f;
        if (picked == index)
        {
            // A punch, then it shrinks into its badge at the bottom.
            float punch = Mathf.Clamp01(end / PickPunch);
            float fly = Mathf.Clamp01((end - PickPunch) / PickFly);
            grow *= 1f + .14f * Mathf.Sin(punch * Mathf.PI);
            if (landing != null && fly > 0f)
            {
                float eased = fly * fly;
                Vector2 target = Local(card.Root, landing, Vector2.zero);
                lift = Vector2.LerpUnclamped(lift, target, eased);
                grow = Mathf.Lerp(grow, BadgeHeight / CardHeight, eased);
                spin = Mathf.Lerp(0f, -Tilts[index % Tilts.Length], eased);
                card.Glow.color = KitStyle.With(KitStyle.Accent, .5f * (1f - fly));
            }
        }
        else if (picked >= 0 || closing)
        {
            float fall = picked >= 0 ? Mathf.Clamp01(end / .4f) : Mathf.Clamp01(end / CloseSeconds);
            lift += new Vector2((index - 1) * 120f * fall, -900f * fall * fall);
            spin = (index % 2 == 0 ? 1f : -1f) * 28f * fall;
        }
        card.Lift.anchoredPosition = lift;
        card.Lift.localScale = new Vector3(grow, grow, 1f);
        card.Lift.localRotation = Quaternion.Euler(0f, 0f, spin);
    }

    // The card under the pointer (or picked) turns pink with dark type. A class's final card (궁극) wears a pink frame
    // from the start, so it reads apart before it is pointed at.
    private static void Paint(Card card, float hover)
    {
        bool final = card.Perk != null && card.Perk.Final;
        card.Frame.color = Color.Lerp(final ? KitStyle.Accent : KitStyle.Ice, KitStyle.Accent, hover);
        card.Plate.color = Color.Lerp(KitStyle.With(KitStyle.Night, .97f), KitStyle.Accent, hover);
        Color ink = Color.Lerp(KitStyle.Text, KitStyle.Ink, hover);
        card.Name.color = card.Tag.color = ink;
        card.Icon.color = ink;
        card.Text.color = Color.Lerp(KitStyle.Soft, KitStyle.With(KitStyle.Ink, .8f), hover);
        card.Why.color = Color.Lerp(KitStyle.With(KitStyle.Soft, .72f), KitStyle.With(KitStyle.Ink, .6f), hover);
        card.Step.color = Color.Lerp(KitStyle.Accent, KitStyle.Ink, hover);
        card.TagBack.color = Color.Lerp(final ? KitStyle.With(KitStyle.Accent, .35f) : KitStyle.With(KitStyle.Ice, .15f),
            KitStyle.With(KitStyle.Ink, .15f), hover);
        card.Owned.color = Color.Lerp(KitStyle.Accent, KitStyle.Ink, hover);
        card.KeyBack.color = Color.Lerp(KitStyle.Ice, KitStyle.Ink, hover);
        card.Key.color = Color.Lerp(KitStyle.Ink, KitStyle.Accent, hover);
        if (card.Perk == null) return;
        int level = PlayerPerks.Level(card.Perk.Id);
        for (int i = 0; i < card.Pips.Length; i++)
        {
            if (!card.Pips[i].gameObject.activeSelf) continue;
            card.Pips[i].color = i < level ? ink
                : i == level ? Color.Lerp(KitStyle.Accent, KitStyle.Ink, hover)
                : Color.Lerp(KitStyle.Faint, KitStyle.With(KitStyle.Ink, .25f), hover);
        }
    }

    // ---- Bursts and shards ----

    private void Ring(Vector2 at, Color color, float from, float to, float life)
    {
        var burst = bursts.FirstOrDefault(b => b.Age >= b.Life);
        if (burst == null)
        {
            var rect = FeelUI.Rect("Ring", fx);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = KitShapes.Ring;
            image.raycastTarget = false;
            burst = new Burst { Rect = rect, Image = image };
            bursts.Add(burst);
        }
        burst.Rect.anchoredPosition = at;
        burst.Color = color;
        burst.From = from;
        burst.To = to;
        burst.Age = 0f;
        burst.Life = life;
        burst.Rect.gameObject.SetActive(true);
    }

    private void Shards(Vector2 at)
    {
        for (int i = 0; i < ShardCount; i++)
        {
            var spark = sparks.FirstOrDefault(s => s.Age >= s.Life);
            if (spark == null)
            {
                var rect = FeelUI.Rect("Shard", fx);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                spark = new Spark { Rect = rect, Image = image };
                sparks.Add(spark);
            }
            float angle = (i / (float)ShardCount + (float)random.NextDouble() * .05f) * Mathf.PI * 2f;
            float speed = 900f + (float)random.NextDouble() * 900f;
            spark.Rect.anchoredPosition = at;
            spark.Rect.sizeDelta = new Vector2(6f + (float)random.NextDouble() * 6f, 20f + (float)random.NextDouble() * 24f);
            spark.Rect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg - 90f);
            spark.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            spark.Spin = ((float)random.NextDouble() - .5f) * 720f;
            spark.Image.color = i % 3 == 0 ? Color.white : KitStyle.Accent;
            spark.Age = 0f;
            spark.Life = .38f + (float)random.NextDouble() * .2f;
            spark.Rect.gameObject.SetActive(true);
        }
    }

    private void AnimateEffects(float dt)
    {
        foreach (var burst in bursts)
        {
            if (burst.Age >= burst.Life) continue;
            burst.Age += dt;
            float k = Mathf.Clamp01(burst.Age / burst.Life);
            float size = Mathf.Lerp(burst.From, burst.To, KitReveal.EaseOut(k));
            burst.Rect.sizeDelta = new Vector2(size, size);
            burst.Image.color = KitStyle.With(burst.Color, .9f * (1f - k));
            if (k >= 1f) burst.Rect.gameObject.SetActive(false);
        }
        foreach (var spark in sparks)
        {
            if (spark.Age >= spark.Life) continue;
            spark.Age += dt;
            float k = Mathf.Clamp01(spark.Age / spark.Life);
            spark.Velocity *= Mathf.Exp(-4f * dt);
            spark.Rect.anchoredPosition += spark.Velocity * dt;
            spark.Rect.localRotation *= Quaternion.Euler(0f, 0f, spark.Spin * dt);
            spark.Rect.localScale = Vector3.one * (1f - k * .6f);
            var color = spark.Image.color;
            color.a = 1f - k * k;
            spark.Image.color = color;
            if (k >= 1f) spark.Rect.gameObject.SetActive(false);
        }
    }

    private void ClearEffects()
    {
        foreach (var burst in bursts) { burst.Age = burst.Life; burst.Rect.gameObject.SetActive(false); }
        foreach (var spark in sparks) { spark.Age = spark.Life; spark.Rect.gameObject.SetActive(false); }
        stage.anchoredPosition = Vector2.zero;
        if (landing != null) landing.localScale = Vector3.one;
    }

    // Where a point of <paramref name="rect"/> (in its own rect space, from its middle) falls in the effects layer.
    private Vector2 Local(RectTransform rect, Vector2 offset)
    {
        Vector3 world = rect.TransformPoint((Vector3)(rect.rect.center + offset));
        return fx.InverseTransformPoint(world);
    }

    // Where the middle of <paramref name="target"/> falls inside <paramref name="space"/> (from its middle).
    private static Vector2 Local(RectTransform space, RectTransform target, Vector2 offset)
    {
        Vector3 world = target.TransformPoint((Vector3)(target.rect.center + offset));
        Vector3 local = space.InverseTransformPoint(world);
        return (Vector2)local - space.rect.center;
    }

    private float Jitter() => (float)random.NextDouble() * 2f - 1f;

    // Past the end a little, then back: the card snaps into place.
    private static float OutBack(float k)
    {
        const float c1 = 1.9f, c3 = c1 + 1f;
        float t = k - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }
}
