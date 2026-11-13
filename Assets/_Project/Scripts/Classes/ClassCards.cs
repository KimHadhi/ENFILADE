using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The class cards that need more than a number (Docs/Features/ClassCards.md; the numbers alone are PlayerPerks.AbilityBonus),
/// at work for this PC's own player (ClassKit adds it). What they do is judged here, where the player's own hits are;
/// how they look goes to every PC (CardNet, CardFx).
/// <list type="bullet">
/// <item>Momentum (기세, everyone's; the user's "때릴 때마다 갱신되는 강화 효과"): every hit this player lands on an
/// enemy keeps it going a few seconds more (PlayerPerks.Struck). A miss takes nothing; it only lets it run out.</item>
/// <item>Ghost, White Tiger (화이트 타이거, its final card; the user named it for the snipers called White Death and
/// White Feather): a headshot kill with the Head Hunter full rakes white claws across the head, staggers the enemies
/// round it, and for some seconds no hurt takes the stacks (ClassKit asks TigerGuards).</item>
/// <item>Point, Gunslinger (건슬링어): the gun just drawn hits harder a moment (ClassKit.DamageFactor); its time shows
/// on the class HUD's bar.</item>
/// <item>Assault, Broken Arrow (브로큰 애로우, the user's name: the radio call for every gun in reach when a unit is being
/// overrun): falling under a share of health or being grabbed calls a barrage down after a moment, shells landing one
/// after another on the enemies nearest first (the ones holding them before all), never hurting a player; once in a
/// while. Danger Close (데인저 클로즈) is a plain factor on near enemies (PlayerClass.GunFactor).</item>
/// <item>Assault, the phantom cavalry (유령 기병대, its final card; the user's "돌진할 때 일직선으로 적들 쓸고 나가는"):
/// a CHARGE sends five riders galloping out along where they look, and whatever stands in their line is hit and thrown
/// aside, knocked down (a brute only reels).</item>
/// <item>Knight, 비껴 흘리기 (the user's name, checked against the dictionary: Docs/Features/ClassCards.md): a parried blow slides
/// off sideways, the zombie stumbles and loses more posture, a white arc running off the parry.</item>
/// <item>Knight, 되치기 (Dying Light's grab counter, at the user's word): grabbed, a ring closes on a mark round the
/// crosshair; the shove key as it meets it throws the zombie, its posture broken for the execution, with REVERSAL slammed
/// across the screen. Early or late is no worse than without the card: the shove only tears it off as ever.</item>
/// <item>Knight, 폭렬 건틀렛 (the user's gauntlet that bursts a flash and a shotgun): a charged punch or a counter that
/// lands sets off the gauntlet, by turns a flash that stuns what is in front and a shot that blows it back.</item>
/// <item>Knight, 그림자 분신 (its final card; the user's Ryoshu of Limbus Company): a perfect parry or dodge brings out a
/// shadow that strikes every punch and counter again a beat later, a violet crescent cutting where it lands.</item>
/// </list>
/// The HUD's bar (ClassHUD) shows the card state under way: BarShare, BarWord, BarColor.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ClassCards : MonoBehaviour
{
    private const string GhostRow = PlayerClass.GhostRow, AssaultRow = "Assault", KnightRow = PlayerClass.KnightRow;
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
    // White Tiger: until when the stacks are safe and how long that lasts; when its claws may strike again; a headshot
    // kill waiting for its shot's stack to be counted (ClassKit counts it after the hit), and where.
    private float tigerUntil = -99f, tigerTotal = 1f, tigerAgain = -99f;
    private int tigerFrame = -1;
    private Vector3 tigerAt, tigerFrom;
    // Broken Arrow: when it may be called again; whether health stood above its line last frame and whether they were
    // held; the shells still to call and when the next one is; the shells on their way down, landing when and where.
    private float brokenAgain = -99f, shellAt;
    private bool wasHigh = true, wasHeld;
    private int shellsLeft;
    private readonly List<(float due, Vector3 spot)> falling = new List<(float due, Vector3 spot)>();
    private readonly List<EnemyHealth> near = new List<EnemyHealth>();
    // The phantom cavalry riding: from where, which way, since when; and whom it has hit.
    private bool riding;
    private Vector3 rideFrom, rideWay;
    private float rideSince;
    private readonly HashSet<EnemyHealth> ridden = new HashSet<EnemyHealth>();
    // Knight: the health whose parries, dodges and grabs are watched, and the fists whose punches are.
    private PlayerDamageReceiver receiver;
    private PlayerMeleeCombat fists;
    // The gauntlet: the last punch's charge and when it went; the blow the last burst went off for; which burst is next.
    private float punchCharge, punchAt = -99f;
    private long burstFor = -1;
    private bool flashNext = true;
    // The shadow double: until when and how long; when its coming out was last shown; its blows waiting to land.
    private float shadowUntil = -99f, shadowTotal = 1f, shadowShown = -99f;
    private readonly List<Echo> echoes = new List<Echo>();
    // The reversal: when a grab took hold (below 0: none waiting), whose; whether its one press is spent; the ring.
    private float grabbedAt = -1f;
    private EnemyHealth grabber;
    private bool reversalSpent;
    private GameObject ringHost;
    private RawImage ringClosing, ringMark;

    /// <summary>A Ghost's White Tiger holds: hurt takes no Head Hunter stack (ClassKit).</summary>
    public bool TigerGuards => tigerUntil > Time.time && PlayerClass.Is(PlayerClass.Ghost);
    public float TigerShare => TigerGuards ? Mathf.Clamp01((tigerUntil - Time.time) / tigerTotal) : 0f;

    /// <summary>A Knight's shadow double is out: its punches and counters strike twice.</summary>
    public bool ShadowOut => shadowUntil > Time.time && PlayerClass.Is(PlayerClass.Knight);
    public float ShadowShare => ShadowOut ? Mathf.Clamp01((shadowUntil - Time.time) / shadowTotal) : 0f;

    /// <summary>The card state the class HUD's bar shows now, 0 to 1 (0: none), its word and its colour.</summary>
    public float BarShare
    {
        get
        {
            if (ShadowShare > 0f) return ShadowShare;
            if (TigerShare > 0f) return TigerShare;
            return kit != null ? kit.GunslingerShare : 0f;
        }
    }

    public string BarWord => ShadowShare > 0f ? Texts.Get("class.hud.shadow")
        : TigerShare > 0f ? Texts.Get("class.hud.tiger") : Texts.Get("class.hud.gunslinger");
    public Color BarColor => ShadowShare > 0f ? Violet : TigerShare > 0f ? KitStyle.Ice : KitStyle.Accent;

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
        if (fists != null) fists.Punched -= OnPunched;
        receiver = null;
        fists = null;
        if (ringHost != null) ringHost.SetActive(false);
        if (Local == this)
        {
            Local = null;
            CardNet.Stop();
        }
    }

    private void OnDestroy()
    {
        if (ringHost != null) Destroy(ringHost);
    }

    // This player's health and fists, once they are there (both may come after this).
    private void Wire()
    {
        if (receiver == null && movement.DamageReceiver != null)
        {
            receiver = movement.DamageReceiver;
            receiver.HitResolved += OnResolved;
        }
        if (fists == null && movement.MeleeCombat != null)
        {
            fists = movement.MeleeCombat;
            fists.Punched += OnPunched;
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
        Ride();
        Reversal();
        Echoes();
    }

    // After the frame's shots are counted (ClassKit adds a headshot's stack once the shot is traced).
    private void LateUpdate()
    {
        if (tigerFrame < 0) return;
        tigerFrame = -1;
        if (kit != null && kit.Hunter >= kit.HunterMax) Tiger();
    }

    // Every hit of this player's on an enemy: Momentum goes on; a Ghost's headshot kill may wake the White Tiger.
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || report.Target == null || !PlayerPerks.IsMine(report.Hit.Source)) return;
        if (report.Target.GetComponentInParent<PlayerMovement>() != null) return;
        PlayerPerks.Struck();
        // A Knight's own punch or counter (the cards' own blows can't be guarded, so they never count here).
        var hit = report.Hit;
        if (hit.Kind == DamageKind.Attack && hit.Method == DamageMethod.Other && (hit.IsCounter || hit.CanBeGuarded)
            && report.Target is EnemyHealth struck && PlayerClass.Is(PlayerClass.Knight))
        {
            if (ShadowOut) Shadowed(struck, hit);
            if (PlayerPerks.Has(PlayerPerks.KnightGauntlet) && hit.Id != burstFor
                && (hit.IsCounter || (Time.time - punchAt < .5f && punchCharge >= PlayerClass.Ability(KnightRow, "gauntletCharge", .6f))))
            {
                burstFor = hit.Id;
                Gauntlet(hit);
            }
        }
        if (report.IsKillingBlow && report.Hit.Method == DamageMethod.Bullet && report.Hit.IsCritical
            && PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(PlayerPerks.GhostTiger))
        {
            tigerFrame = Time.frameCount;
            tigerAt = report.Hit.Point;
            tigerFrom = report.Hit.Origin ?? transform.position + Vector3.up * 1.5f;
        }
    }

    // ---- Ghost: White Tiger ----

    // The stacks are safe a while; the claws, and the stagger round the kill, at most once in a while.
    private void Tiger()
    {
        tigerTotal = Mathf.Max(.1f, PlayerPerks.Total(PlayerPerks.GhostTiger));
        tigerUntil = Time.time + tigerTotal;
        if (Time.time < tigerAgain) return;
        tigerAgain = Time.time + Mathf.Max(0f, PlayerClass.Ability(GhostRow, "tigerRest", 1.5f));
        CardNet.Show(CardFx.Kind.Claws, tigerAt, tigerAt - tigerFrom, 1f);
        KitSound.Play(KitSound.Cue.Hunter, 1f);
        float reach = Mathf.Max(0f, PlayerClass.Ability(GhostRow, "tigerReach", 6f)), seconds = PlayerClass.Ability(GhostRow, "tigerStagger", 1.2f);
        if (reach <= 0f || seconds <= 0f) return;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || (enemy.transform.position - tigerAt).sqrMagnitude > reach * reach) continue;
            if (enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable) target.Interrupt(seconds);
        }
    }
}
