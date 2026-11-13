using System.Collections.Generic;
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
public sealed partial class PerkCardScreen : MonoBehaviour
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
    private bool open, closing, popped;
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
}
