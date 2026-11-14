using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The class cards that need more than a number (Docs/Features/ClassCards.md; the numbers alone are PlayerPerks.AbilityBonus),
/// at work for this PC's own player (ClassKit adds it). What they do is judged here, where the player's own hits are;
/// how they look goes to every PC (CardNet, CardFx).
/// <list type="bullet">
/// <item>Momentum (기세, everyone's; the user's "때릴 때마다 갱신되는 강화 효과"): every hit this player lands on an
/// enemy keeps it going a few seconds more (PlayerPerks.Struck). A miss takes nothing; it only lets it run out.</item>
/// <item>Ghost: its cards are GhostCards' (Classes/Ghost), White Tiger (화이트 타이거) among them. Here only
/// the class HUD's bar shows its ultimate under way (PhantomShare).</item>
/// <item>Point, Gunslinger (건슬링어): the gun just drawn hits harder a moment (ClassKit.DamageFactor); its time shows
/// on the class HUD's bar.</item>
/// <item>Assault, Danger Close (데인저 클로즈; the user, 2026-11-14: "진짜 폭격 스킬처럼"): the class key held aims a mark on
/// the ground, let go calls a barrage round it, shells landing one after another on the enemies there, each blast
/// throwing them down, never hurting a player (ClassCards.DangerClose, ClassCards.Barrage); a tap is still the CHARGE.
/// Broken Arrow (브로큰 애로우, the user's name: the radio call for every gun in reach when a unit is being overrun) drops
/// the same barrage on the Assault's own spot when its health falls under a share or it is grabbed, whatever holds it
/// shelled first; once in a while. Its ultimate is an A-10's gun run down the line it aims at (ClassCards.GunRun).</item>
/// <item>Assault, the phantom cavalry (유령 기병대, the 러프 라이더스 card; the user's "돌진할 때 일직선으로 적들 쓸고
/// 나가는"): a CHARGE sends five riders galloping out along where they look, and whatever stands in their line is hit and thrown
/// aside, knocked down (a brute only reels).</item>
/// <item>Knight, 무너뜨리기 (the old 비껴 흘리기 renamed by the user, 2026-11-14): a parried elite or brute reels and loses
/// more posture (PlayerClass.ParryPosture), a white arc running off the parry. A pack zombie breaks on a Knight's parry
/// without it (Knight.parryBreaks).</item>
/// <item>Knight, 비껴 흘리기 (the user's name, checked against the dictionary: Docs/Features/ClassCards.md; now on the snappier
/// card): a counter that lands keeps the parry window longer a few seconds (PlayerClass.ParryWindowFactor).</item>
/// <item>Knight, 원호 반격: a blow the cover turned strengthens the Knight's counters and fists a few seconds, and the covered
/// friend's guns on their own PC (KnightCover).</item>
/// <item>Knight, 되치기, no card since 2026-11-14 (Dying Light's grab counter; the user: "잡혔을 때 빠져 나오는 기능은 카드가
/// 아니라 기본으로"): held by a grab or a kneel to eat, V throws every holder off at once, its posture broken for the
/// execution, with its pink throw for everyone and REVERSAL on this screen. A runner's pin stays the kick's.</item>
/// <item>Knight, 그림자 분신 (the Knight's ultimate, once its final card too, gone 2026-11-14 with the rest of them; the
/// user's Ryoshu of Limbus Company, then 2026-11-14: "걍 때릴 때 데미지 두 배, 패링할 때 체간 두 배"): X brings out a
/// black, see-through double that does what the Knight does a beat late (BodyDouble); every punch, slash and counter strikes again a beat later, a violet
/// crescent cutting where it lands, and a parry takes twice the posture.</item>
/// </list>
/// The HUD's bar (ClassHUD) shows the card state under way: BarShare, BarWord, BarColor.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ClassCards : MonoBehaviour
{
    private const string AssaultRow = "Assault", KnightRow = PlayerClass.KnightRow;
    private static readonly Color Violet = new Color(.62f, .32f, 1f);

    // A shadow double's blow waiting to land: on whom, how hard, where and which way.
    private struct Echo
    {
        public float Due, Amount, Posture;
        public EnemyHealth Target;
        public Vector3 Point, Direction;
    }

    /// <summary>This PC's own player's cards at work.</summary>
    public static ClassCards Local { get; private set; }

    private PlayerMovement movement;
    private ClassKit kit;
    // Broken Arrow: when it may be called again; whether health stood above its line last frame and whether they were
    // held. An Assault's barrages' shells on their way down, landing when and where (ClassCards.Barrage).
    private float brokenAgain = -99f;
    private bool wasHigh = true, wasHeld;
    private readonly List<(float due, Vector3 spot)> falling = new List<(float due, Vector3 spot)>();
    private readonly List<EnemyHealth> near = new List<EnemyHealth>();
    // The phantom cavalry riding: from where, which way, since when; and whom it has hit.
    private bool riding;
    private Vector3 rideFrom, rideWay;
    private float rideSince;
    private readonly HashSet<EnemyHealth> ridden = new HashSet<EnemyHealth>();
    // Knight: the health whose parries, dodges and holds are watched (the rest of the Knight's state is ClassCards.Knight's).
    private PlayerDamageReceiver receiver;
    // The shadow double: until when and how long; when its coming out was last shown; its blows waiting to land.
    private float shadowUntil = -99f, shadowTotal = 1f, shadowShown = -99f;
    private readonly List<Echo> echoes = new List<Echo>();

    /// <summary>A Ghost's ultimate under way, no zombie seeing it: how much of it is left, 0 to 1 (GhostCards).</summary>
    public float PhantomShare => PlayerClass.Is(PlayerClass.Ghost) && GhostCards.Local != null ? GhostCards.Local.PhantomShare : 0f;

    /// <summary>
    /// A Knight's shadow double is out: its punches, slashes and counters strike twice, and its parries take twice the
    /// posture (PlayerClass.ParryPosture).
    /// </summary>
    public bool ShadowOut => shadowUntil > Time.time && PlayerClass.Is(PlayerClass.Knight);
    public float ShadowShare => ShadowOut ? Mathf.Clamp01((shadowUntil - Time.time) / shadowTotal) : 0f;

    /// <summary>The card state the class HUD's bar shows now, 0 to 1 (0: none), its word and its colour.</summary>
    public float BarShare
    {
        get
        {
            if (KnightBarShare > 0f) return KnightBarShare;
            if (PhantomShare > 0f) return PhantomShare;
            return kit != null ? kit.GunslingerShare : 0f;
        }
    }

    public string BarWord => KnightBarShare > 0f ? KnightBarWord
        : PhantomShare > 0f ? Texts.Get("class.hud.phantom") : Texts.Get("class.hud.gunslinger");
    public Color BarColor => KnightBarShare > 0f ? KnightBarColor : PhantomShare > 0f ? KitStyle.Ice : KitStyle.Accent;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        if (receiver != null) receiver.HitResolved -= OnResolved;
        receiver = null;
        if (Local == this)
        {
            Local = null;
            CardNet.Stop();
        }
    }

    // This player's health, once it is there (it may come after this).
    private void Wire()
    {
        if (receiver == null && movement.DamageReceiver != null)
        {
            receiver = movement.DamageReceiver;
            receiver.HitResolved += OnResolved;
        }
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        if (kit == null) kit = GetComponent<ClassKit>();
        // In a room, hear the others' card effects.
        var network = GameSession.Network;
        if (network != null && network.IsListening) CardNet.Listen();
        Wire();
        BrokenArrow();
        DangerClose();
        Barrage();
        Strafe();
        Ride();
        KnightTick();
    }

    // Every hit of this player's on an enemy: Momentum goes on. (A Ghost's headshot cards, the White Tiger among them, are
    // GhostCards'.)
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || report.Target == null || !PlayerPerks.IsMine(report.Hit.Source)) return;
        if (report.Target.GetComponentInParent<PlayerMovement>() != null) return;
        PlayerPerks.Struck();
        // A Knight's own punch, slash or counter: the shadow's echo, the flow's moment (ClassCards.Knight).
        if (report.Target is EnemyHealth struck) KnightStruck(struck, report.Hit);
    }
}
