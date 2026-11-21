using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A Ghost's own wall sight and its cards with code behind them (Docs/Features/ClassCards.md, the Ghost; the user's card
/// feedback of 2026-11-14: "고스트가 원래 컨셉이 '리콘' 쪽"), at work for this PC's own player (ClassKit adds it). Each is
/// judged here, where the player's sights, shots and takedowns are. What the other PCs must see goes through GhostNet, what
/// zombies must do through the zombies' own door (ZombieEffects) or with the hit itself (EnemyHealth.SilentWhen), and a
/// miss changes nothing (the user's rule: a hit is a plus, a miss is never a minus).
/// <list type="bullet">
/// <item>Wall sight, the class's own: every zombie within Ghost.wallSight shows through walls as a soft white silhouette, on
/// this PC only (GhostSight).</item>
/// <item>정찰 (ghost_recon): F pings a zombie seen through a wall too, so the whole team sees it as any ping (PlayerPing asks
/// GhostSight.AimedThroughWall), and the wall sight reaches farther (the card's value, 12/18/24 m). It is always in a
/// Ghost's first hand (PlayerPerks.Draw; the user: "고스트일 때 무조건 처음 카드 선택 목록 중에 뜨게").</item>
/// <item>조용한 표적 (ghost_quiet): a second down the sights on a zombie that knows nothing puts a small white mark over it
/// for everyone (GhostMarks). This Ghost's headshot on its own mark makes no stir (no zombie by it notices the death:
/// EnemyHealth.SilentWhen) and counts as an assassination: its money (Stealth.csv Score.takedown) and a Head Hunter stack
/// more than the headshot's own.</item>
/// <item>암살 연쇄 (ghost_chain): in a takedown, F on another zombie that knows nothing, within reach and in plain sight,
/// takes the Ghost behind it in a gust everyone sees (GhostFx) and straight into its takedown, a few in a row (the user:
/// "한 명 암살 중에 다른 애 보고 암살 키 누르면 걔 쪽으로 바람 이펙트와 함께 텔레포트하면서 바로 암살하고"). An execution
/// holds the view still, so while a chain is left the Ghost's view turns in its takedown all the same (PlayerLook asks
/// LooksDuringTakedown), to find the next one.
/// The chain has a time of its own (ChainMomentOf): the next may be named from the takedown's start, the Ghost goes as
/// its blow lands, and for Ghost.chainWindow seconds after that blow the chain stays open, though the takedown has let
/// the Ghost go long before. It used to be open "while the takedown holds the player" and no longer: nobody chose that
/// length, and when takedowns were made quick (2026-11-18: the blow at 0.3 s, let go 0.1 s later, where the player had
/// been held 1.2 to 1.5 s) the card lost most of its time with them. It runs from the blow, not from the takedown's
/// start, because the blow is the first moment the Ghost can go at all: what is left to choose is how long after the
/// kill it may still move on, and counted so neither a longer dash in, a slower kneeling takedown nor the Ghost's own
/// quicker knife takes anything from it.</item>
/// <item>시선 읽기 (ghost_cones): a crouched Ghost sees more zombies' sight cones, from farther off (StealthView asks).</item>
/// <item>화이트 타이거 (ghost_whitetiger, its final card until those went, 2026-11-14; 1.5/2/2.5 m): every headshot hurts
/// the enemies round the one it hit as much as it did, a small white claw mark on each (CardFx Claws, for everyone).</item>
/// <item>The ultimate (X once every card is taken): for some seconds no zombie sees the Ghost, its sounds and its touch still
/// counting (ZombieEffects.Unseen), and every zombie near it shows through walls to the whole team (GhostSight).</item>
/// </list>
/// Numbers: Tables/Perks (each card's value) and Tables/ClassAbilities Ghost.*.
/// </summary>
[DisallowMultipleComponent]
public sealed class GhostCards : MonoBehaviour
{
    public const string ReconCard = "ghost_recon", QuietCard = "ghost_quiet", ChainCard = "ghost_chain", ConesCard = "ghost_cones",
        QuickAimCard = "ghost_quickaim", QuickKnifeCard = "ghost_quickknife";
    private const string Row = PlayerClass.GhostRow;
    // Where a chain may land round a zombie, from straight behind its back (degrees), tried in turn.
    private static readonly float[] Sides = { 0f, 30f, -30f, 55f, -55f };
    // The quiet rule asked of every hit this PC judges (EnemyHealth.SilentWhen), made once so it is given only once.
    private static readonly Func<EnemyHealth, DamageHit, bool> quietRule = Quiet;

    /// <summary>This PC's own player's.</summary>
    public static GhostCards Local { get; private set; }

    private PlayerMovement movement;
    private ClassKit kit;
    private WeaponController weapon;
    private PlayerExecution execution;
    // 조용한 표적: the zombie held in the sights now, and how long it has been.
    private EnemyHealth sighted;
    private float sightedFor;
    // 암살 연쇄: a run of chains is open (a takedown of a zombie that knew nothing began it); when the takedown in hand
    // began, and when its blow landed (below 0: not yet); how many more may follow; the zombie F asked for, waiting for
    // that blow (or for the body to be able); and whether this PC is moving the Ghost on itself. Whose a press of F is
    // (the chain's, the takedown's that began it, a pickup's) is not kept here: the interact key's one owner says
    // (InteractKey, row ghost.chain).
    private bool run, chaining;
    private float startedAt, struckAt = -1f;
    private int chainsLeft;
    private EnemyHealth queued;
    // The ultimate: until when, and how long it lasts.
    private float phantomUntil = -99f, phantomTotal = 1f;
    // White Tiger: the enemies round a headshot, gathered first, as a kill takes one out of the list it was found in.
    private readonly List<EnemyHealth> near = new List<EnemyHealth>();
    private readonly RaycastHit[] sightHits = new RaycastHit[16];
    // The chain's prompt under the crosshair.
    private GameObject promptHost;
    private CanvasGroup promptGroup;
    private TextMeshProUGUI prompt;

    // ---- What the others ask ----

    /// <summary>How far this PC's own Ghost sees through walls now (m): the class's own, farther with 정찰; 0 for any other class.</summary>
    public static float SightReach
    {
        get
        {
            if (!PlayerClass.Is(PlayerClass.Ghost)) return 0f;
            float reach = Mathf.Max(0f, PlayerClass.Ability(Row, "wallSight", 12f));
            return PlayerPerks.Has(ReconCard) ? Mathf.Max(reach, PlayerPerks.Total(ReconCard)) : reach;
        }
    }

    /// <summary>정찰: this PC's own Ghost's F pings a zombie it sees through a wall as well (GhostSight.AimedThroughWall).</summary>
    public static bool ReconPings(PlayerMovement player)
        => player != null && player.IsLocal && PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(ReconCard);

    /// <summary>시선 읽기: the most sight cones this PC's crouched Ghost sees (StealthView), from the view's own <paramref name="most"/>.</summary>
    public static int ConeCount(int most) => ReadsCones ? most + Mathf.Max(0, Mathf.RoundToInt(PlayerPerks.Total2(ConesCard))) : most;

    /// <summary>시선 읽기: how far off a zombie's sight cone still shows to this PC's crouched Ghost (m), from the view's own.</summary>
    public static float ConeReach(float reach) => ReadsCones ? Mathf.Max(reach, PlayerPerks.Total(ConesCard)) : reach;

    private static bool ReadsCones => PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(ConesCard);

    /// <summary>빠른 조준: <paramref name="who"/> is this PC's own Ghost holding the card (WeaponController.Snap: its first shot once the sights are up flies true).</summary>
    public static bool QuickAims(PlayerMovement who)
        => who != null && who.IsLocal && PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(QuickAimCard);

    /// <summary>빠른 조준: how much faster <paramref name="who"/> brings the sights up (WeaponController.AdsTime divides by it), 1 for anyone else.</summary>
    public static float AimSpeed(PlayerMovement who) => QuickAims(who) ? 1f + Mathf.Max(0f, PlayerPerks.Total(QuickAimCard)) / 100f : 1f;

    /// <summary>
    /// 빠른 칼: how fast this PC's own Ghost's takedown of <paramref name="victim"/>, a zombie that knows nothing, plays (a
    /// factor on the paired clips' clock: ExecutionPairPlayer, which sends it with the pair so every PC plays it as fast).
    /// 1 for anything else: an execution of a broken zombie, another class, another player.
    /// </summary>
    public static float TakedownPace(PlayerMovement who, MeleeCombatTarget victim)
    {
        if (who == null || !who.IsLocal || !PlayerClass.Is(PlayerClass.Ghost) || !PlayerPerks.Has(QuickKnifeCard)) return 1f;
        if (victim == null || !victim.TryGetComponent(out ZombieMind mind) || !mind.Unaware) return 1f;
        return 1f + Mathf.Max(0f, PlayerPerks.Total(QuickKnifeCard)) / 100f;
    }

    /// <summary>
    /// 암살 연쇄: this PC's own Ghost may turn its view in the takedown under way, to find the next zombie (PlayerLook asks:
    /// an execution holds the view still otherwise). Only while a chain is left; the body keeps the pair's way meanwhile.
    /// </summary>
    public static bool LooksDuringTakedown(PlayerMovement who)
    {
        // The chain's own time says so (Moment), not how long the takedown lasts; it only lifts the hold a takedown puts
        // on the view (once let go, the view is the player's anyway), never another's (a broken guard).
        var mine = Local;
        return mine != null && who != null && mine.movement == who && mine.Moment != ChainMoment.Closed
            && who.Execution != null && who.Execution.IsExecuting;
    }

    /// <summary>Where a run of chains stands.</summary>
    public enum ChainMoment : byte
    {
        /// <summary>No chain may be asked for: no run, none left in it, or the time after the last blow has run out.</summary>
        Closed,
        /// <summary>The takedown in hand has not struck yet: the next may be named now, and the Ghost goes as the blow lands.</summary>
        Waiting,
        /// <summary>The blow has landed and its time runs: a zombie named now is gone to at once.</summary>
        Open,
    }

    /// <summary>
    /// The chain's rule by itself, a pure function of the run and the clock (checked without the game:
    /// Tools/Checks/ghost_chain_check.py runs the same lines over the tables' own numbers). <paramref name="run"/>: a
    /// takedown of a zombie that knew nothing opened a run and the card is held; <paramref name="left"/>: chains still
    /// to take in it; <paramref name="struckAt"/>: when the blow of the takedown in hand landed, below 0 while it has
    /// not; <paramref name="window"/>: Ghost.chainWindow, the seconds after a blow the chain stays open.
    /// </summary>
    public static ChainMoment ChainMomentOf(bool run, int left, float struckAt, float now, float window)
    {
        if (!run || left <= 0) return ChainMoment.Closed;
        if (struckAt < 0f) return ChainMoment.Waiting;
        return now - struckAt <= Mathf.Max(0f, window) ? ChainMoment.Open : ChainMoment.Closed;
    }

    /// <summary>How long after a takedown's blow the chain stays open (s): Tables/ClassAbilities Ghost.chainWindow.</summary>
    public static float ChainWindow => Mathf.Max(0f, PlayerClass.Ability(Row, "chainWindow", 1.1f));

    private ChainMoment Moment => ChainMomentOf(run && PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(ChainCard), chainsLeft, struckAt,
        Time.time, ChainWindow);

    /// <summary>
    /// 암살 연쇄: F naming the next zombie, as the interact key's one owner knows it (InteractKey). One press is one act:
    /// a plain takedown on offer comes before it, a pickup and a ping after it, by its row's place in
    /// Tables/Player/InteractKey.csv (until 2026-11-18 the pickup and the ping each asked this card whether F was the
    /// chain's, and nothing else did).
    /// </summary>
    public const string ChainOffer = "ghost.chain";

    /// <summary>How much of the ultimate under way is left, 0 to 1 (0: none).</summary>
    public float PhantomShare => phantomUntil > Time.time ? Mathf.Clamp01((phantomUntil - Time.time) / phantomTotal) : 0f;
    /// <summary>Seconds of it left, and of how many (its slot among the timed effects: TimedEffects).</summary>
    public float PhantomLeft => phantomUntil - Time.time;
    public float PhantomTotal => phantomTotal;

    // 조용한 표적: this PC's own Ghost's headshot on a zombie it marked makes no stir. Asked of every hit this PC judges as it
    // lands, so the cheap questions go first.
    private static bool Quiet(EnemyHealth enemy, DamageHit hit)
    {
        if (hit.Method != DamageMethod.Bullet || !hit.IsCritical) return false;
        var mine = Local;
        if (mine == null || mine.movement == null || !PlayerClass.Is(PlayerClass.Ghost) || !PlayerPerks.Has(QuietCard)) return false;
        return GhostMarks.IsMarkedBy(enemy, mine.movement) && PlayerPerks.IsMine(hit.Source);
    }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void OnEnable()
    {
        CombatDamageEvents.Damaged += OnDamaged;
        PlayerClass.Picked += OnPicked;
        // The rules are cleared as each play starts, so it is given again whenever this comes on (twice is once). It is
        // never taken back: without this PC's Ghost (Local) it says no.
        EnemyHealth.SilentWhen(quietRule);
    }

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        PlayerClass.Picked -= OnPicked;
        Unhook();
        Forget();
        if (promptHost != null) promptHost.SetActive(false);
        if (Local == this) Local = null;
    }

    private void OnDestroy()
    {
        if (promptHost != null) Destroy(promptHost);
    }

    // A class picked starts the kit afresh (ClassKit): no ultimate, aim or chain left over.
    private void OnPicked(string before, string now) => Forget();

    private void Forget()
    {
        phantomUntil = -99f;
        sighted = null;
        sightedFor = 0f;
        run = chaining = false;
        struckAt = -1f;
        chainsLeft = 0;
        queued = null;
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        Hook();
        bool ghost = PlayerClass.Is(PlayerClass.Ghost);
        GhostSight.Own(ghost && !movement.IsDead ? SightReach : 0f);
        if (!ghost)
        {
            sighted = queued = null;
            Prompt(false);
            return;
        }
        Sights();
        Chain();
    }

    // This PC's own player's gun and executions, once they are there (either may be made after this).
    private void Hook()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (execution != null || movement.Execution == null) return;
        execution = movement.Execution;
        execution.Started += OnTakedownStarted;
        execution.Impact += OnTakedownStruck;
        execution.Ended += OnTakedownEnded;
    }

    private void Unhook()
    {
        if (execution == null) return;
        execution.Started -= OnTakedownStarted;
        execution.Impact -= OnTakedownStruck;
        execution.Ended -= OnTakedownEnded;
        execution = null;
    }

    // ---- 조용한 표적 ----

    // A second down the sights on a zombie that knows nothing marks it for everyone (GhostNet), and the next one held starts
    // a second of its own. Looking away, lowering the sights or its waking only ends the count.
    private void Sights()
    {
        bool aiming = PlayerPerks.Has(QuietCard) && weapon != null && weapon.AimBlend >= .9f && !movement.IsDead && !movement.IsDowned
            && (execution == null || !execution.IsExecuting);
        var zombie = aiming ? Sighted() : null;
        if (zombie != sighted)
        {
            sighted = zombie;
            sightedFor = 0f;
        }
        if (sighted == null) return;
        float need = Mathf.Max(.1f, PlayerClass.Ability(Row, "quietAim", 1f));
        sightedFor += Time.deltaTime;
        GhostMarks.Aiming(sighted, sightedFor / need);
        if (sightedFor < need) return;
        float seconds = Mathf.Max(1f, PlayerClass.Ability(Row, "quietSeconds", 30f));
        GhostNet.Send(new GhostNet.Packet { Step = (byte)GhostNet.Step.Mark, Seconds = seconds, From = sighted.transform.position }, movement, sighted);
        KitSound.Play(KitSound.Cue.Ping, .35f);
        sighted = null;
    }

    // The zombie the crosshair is on, in plain sight, alive, knowing nothing, and not marked by this Ghost yet.
    private EnemyHealth Sighted()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float range = weapon.EquippedData != null ? Mathf.Max(10f, weapon.EquippedData.Range) : 200f;
        if (!AttackTrace.FirstExternalHit(gameObject, eyes.position, eyes.forward, range, ~0, out RaycastHit hit)) return null;
        if (!(CombatFoe.Of(hit.collider) is EnemyHealth zombie) || !zombie.IsAlive) return null;
        if (!zombie.TryGetComponent(out ZombieMind mind) || !mind.Unaware) return null;
        return GhostMarks.IsMarkedBy(zombie, movement) ? null : zombie;
    }

    // ---- 암살 연쇄 ----

    // While the chain's own time is open (Moment): the zombie it would go to is shown (GhostMarks' chevron, the prompt),
    // F names it, and the Ghost goes as soon as the blow in hand has landed, at once if it already has. All of it follows
    // that time, in the takedown and after it has let the Ghost go; none of it follows the takedown's length.
    private void Chain()
    {
        var moment = Moment;
        if (run && (movement.IsDead || moment == ChainMoment.Closed)) Close(movement.IsDead ? "the Ghost died" : chainsLeft <= 0 ? "no chain left" : "its time ran out");
        if (moment == ChainMoment.Closed || movement.IsDead)
        {
            queued = null;
            Prompt(false);
            return;
        }
        // Its time on the HUD, a slot among the timed effects (TimedEffects): whole until the blow, running down after it.
        float window = ChainWindow;
        TimedEffects.Report("ghost.chain", moment == ChainMoment.Open ? window - (Time.time - struckAt) : window, window, chainsLeft, chainsLeft);
        bool executing = execution != null && execution.IsExecuting;
        if (queued != null && !Takeable(queued)) queued = null;
        // What it would go to: the one already asked for, else the one the crosshair is on. Out of a takedown it offers
        // none while the Ghost's body could not begin one (sliding, in a vault, held), and none while a zombie stands
        // where F simply takes it down: that is the takedown's own offer, whose prompt stands where this one would.
        bool can = executing || (execution != null && execution.CanBegin && execution.Candidate == null);
        var next = queued != null ? queued : can ? Next(executing ? execution.Victim : null) : null;
        // F is shared: the chain says each frame that F would name this zombie (InteractKey), and out of a takedown it
        // goes on saying so while one it named waits for the body, so a second press then is still the chain's and takes
        // no pickup at its feet. In a takedown, with one named, F has nothing left to do for the chain and is free.
        if (next != null && (queued == null || !executing)) InteractKey.Offer(ChainOffer, target: next);
        if (next != null) GhostMarks.Pointing(next);
        Prompt(next != null);
        // F names it: a press the key's one owner gives the chain (InteractKey.Pressed). The press that began this
        // takedown was the takedown's and never comes here, and none is given while a screen has the keys. Whose the
        // keys are is asked on this press and nowhere else in the chain: it stops a new asking, and never ends what is
        // under way. This whole method used to give up while the chat or the Esc menu had them, so the prompt, the
        // chevron and the turning view went with a word typed, and a chain already asked for was dropped with them.
        if (queued == null && next != null && InteractKey.Pressed(ChainOffer))
        {
            queued = next;
            Note($"NAMED  {next.name} at +{Time.time - startedAt:0.00}s ({(moment == ChainMoment.Open ? "after the blow: it goes now" : "before the blow: it waits for it")})");
        }
        if (queued != null && moment == ChainMoment.Open) Go(queued);
    }

    // The run is over: nothing more may be asked for until a takedown opens another.
    private void Close(string why)
    {
        if (run) Note($"CLOSED {why} at +{Time.time - startedAt:0.00}s, {chainsLeft} left unused");
        run = false;
        struckAt = -1f;
        chainsLeft = 0;
        queued = null;
    }

    // Checked without playing: every run's moments go to Logs/ghost-chain.txt (PlayRecord), each with its time from the
    // start of the takedown in hand, so a play shows how long the Ghost had and when it pressed.
    private const string RecordName = "ghost-chain", RecordHeading =
        "a Ghost's 암살 연쇄 (GhostCards): RUN a takedown opened one | BLOW its blow landed, the time open after it | NAMED F asked for a zombie "
        + "| WENT the Ghost moved on | WAITS it could not begin there yet | CLOSED why. +s = since the takedown in hand began";

    private static void Note(string line)
    {
        if (PlayRecord.On) PlayRecord.Line(RecordName, RecordHeading, line);
    }

    // The next zombie: knowing nothing, within the card's reach, nearest the crosshair within Ghost.chainAim, in plain sight,
    // with room behind it to land, and not the one in hand.
    private EnemyHealth Next(MeleeCombatTarget victim)
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = PlayerPerks.Total(ChainCard), best = Mathf.Clamp(PlayerClass.Ability(Row, "chainAim", 15f), 1f, 60f);
        EnemyHealth found = null;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || (victim != null && enemy.gameObject == victim.gameObject)) continue;
            if ((GhostSight.Feet(enemy) - transform.position).sqrMagnitude > reach * reach || !Takeable(enemy)) continue;
            Vector3 middle = GhostSight.Middle(enemy);
            float angle = Vector3.Angle(eyes.forward, middle - eyes.position);
            if (angle >= best || !InSight(eyes.position, middle, enemy, victim)) continue;
            if (!enemy.TryGetComponent(out ZombieMind mind) || !Spot(enemy, mind, out _)) continue;
            best = angle;
            found = enemy;
        }
        return found;
    }

    private static bool Takeable(EnemyHealth enemy)
        => enemy != null && enemy.IsAlive && enemy.TryGetComponent(out ZombieMind mind) && mind.Unaware
            && enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable && !target.IsHeld && !target.IsKnockedDown;

    // Behind it at once, facing it, in a gust everyone sees, and into its takedown; the one in hand ends there, its blow
    // landed. Should its spot have filled meanwhile, nothing happens (no minus for it). And the Ghost is never moved to a
    // zombie it could not then take down: while its body can't begin a takedown (its keys taken, a slide, a vault) the
    // one asked for is kept and tried again each frame, for as long as the chain's time lasts.
    private void Go(EnemyHealth next)
    {
        if (!next.TryGetComponent(out MeleeCombatTarget target) || !next.TryGetComponent(out ZombieMind mind) || !Spot(next, mind, out Vector3 spot))
        {
            queued = null;
            return;
        }
        chaining = true;
        if (execution.IsExecuting) execution.Cancel();
        if (!execution.CanBegin)
        {
            chaining = false;
            if (waitedFrame != Time.frameCount - 1) Note($"WAITS  for {next.name}: the body cannot begin a takedown now");
            waitedFrame = Time.frameCount;
            return;
        }
        queued = null;
        Vector3 from = transform.position;
        Step(spot, GhostSight.Middle(next));
        GhostNet.Send(new GhostNet.Packet { Step = (byte)GhostNet.Step.Chain, From = from, To = spot }, movement, null);
        bool began = execution.TryBegin(target);
        chaining = false;
        if (!began)
        {
            Close("its takedown could not begin where it landed");
            return;
        }
        chainsLeft--;
        run = true;
        Note($"WENT   to {next.name}, {chainsLeft} left");
    }

    // The frame a chain last waited for the body (its record says so once, not every frame).
    private int waitedFrame = -2;

    // There at once, looking at it; for the other PCs a jump, never a slide across.
    private void Step(Vector3 spot, Vector3 look)
    {
        movement.MoveTo(spot + Vector3.up * .05f);
        if (TryGetComponent(out PlayerLook view))
        {
            var eyes = PlayerView.EyesOf(movement);
            Vector3 way = look - (eyes != null ? eyes.position : spot + Vector3.up * 1.5f);
            var flat = new Vector3(way.x, 0f, way.z);
            if (flat.sqrMagnitude > 1e-4f) view.FaceYaw(Quaternion.LookRotation(flat).eulerAngles.y);
            view.EasePitch(Mathf.Atan2(-way.y, Mathf.Max(.01f, flat.magnitude)) * Mathf.Rad2Deg, 1f);
        }
        if (TryGetComponent(out NetworkTransform motion) && motion.IsSpawned && motion.CanCommitToTransform)
            motion.Teleport(transform.position, transform.rotation, transform.localScale);
    }

    // Close behind its back, on the floor it stands on, clear, and in plain sight of it: straight behind, else a little to
    // either side, always within the angle a takedown takes (ZombieMind.CanBeTakenDownFrom).
    private bool Spot(EnemyHealth next, ZombieMind mind, out Vector3 spot)
    {
        spot = default;
        Vector3 feet = GhostSight.Feet(next), back = Vector3.ProjectOnPlane(-next.transform.forward, Vector3.up);
        if (back.sqrMagnitude < 1e-4f) return false;
        back.Normalize();
        float off = Breadth(next) + Mathf.Max(.2f, PlayerClass.Ability(Row, "chainGap", .55f));
        var body = GetComponent<CharacterController>();
        float radius = body != null ? body.radius : .3f, height = body != null ? body.height : 1.8f;
        Vector3 middle = GhostSight.Middle(next);
        foreach (float side in Sides)
        {
            Vector3 at = feet + Quaternion.Euler(0f, side, 0f) * back * off;
            if (!Floor(at, feet.y, out at) || !mind.CanBeTakenDownFrom(at) || !Clear(at, radius, height)) continue;
            if (!InSight(at + Vector3.up * height * .85f, middle, next, null)) continue;
            spot = at;
            return true;
        }
        return false;
    }

    // The zombie's own half breadth (m): its capsule's.
    private static float Breadth(EnemyHealth next)
        => next.TryGetComponent(out Collider own) && own.enabled ? Mathf.Max(own.bounds.extents.x, own.bounds.extents.z) : .35f;

    // The floor under that spot, near the height of the zombie's own feet.
    private bool Floor(Vector3 at, float level, out Vector3 floor)
    {
        floor = at;
        if (!Physics.Raycast(new Vector3(at.x, level + 1.2f, at.z), Vector3.down, out RaycastHit hit, 2.6f, Solid, QueryTriggerInteraction.Ignore))
            return false;
        if (Mathf.Abs(hit.point.y - level) > .6f) return false;
        floor = hit.point;
        return true;
    }

    // Room for this player's body there: no wall, prop or other zombie in the way.
    private bool Clear(Vector3 feet, float radius, float height)
    {
        float inner = radius * .95f;
        return !Physics.CheckCapsule(feet + Vector3.up * (inner + .05f), feet + Vector3.up * Mathf.Max(inner + .06f, height - inner), inner, Solid,
            QueryTriggerInteraction.Ignore);
    }

    // Everything solid but the players' own layer.
    private int Solid => Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);

    // Nothing solid between, but this player and the bodies named.
    private bool InSight(Vector3 from, Vector3 to, Component target, Component other)
    {
        Vector3 way = to - from;
        float far = way.magnitude;
        if (far < .05f) return true;
        int count = Physics.RaycastNonAlloc(from, way / far, sightHits, far, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var part = sightHits[i].collider.transform;
            if (part.IsChildOf(transform) || part.IsChildOf(target.transform) || (other != null && part.IsChildOf(other.transform))) continue;
            return false;
        }
        return true;
    }

    // A takedown begun: one on a zombie that knew nothing starts a run of chains (one this PC moved on to keeps its count).
    private void OnTakedownStarted(MeleeCombatTarget victim)
    {
        struckAt = -1f;
        startedAt = Time.time;
        if (chaining) return;
        // A takedown of the Ghost's own (F, not a chain) while a run still stood open begins the count afresh.
        if (run) Close("a takedown of the Ghost's own took its place");
        bool unaware = victim != null && victim.TryGetComponent(out ZombieMind mind) && mind.Unaware;
        chainsLeft = unaware && PlayerPerks.Has(ChainCard) ? Mathf.Max(0, Mathf.RoundToInt(PlayerPerks.Total2(ChainCard))) : 0;
        run = chainsLeft > 0;
        queued = null;
        if (run) Note($"RUN    opened by a takedown of {victim.name}: {chainsLeft} chain(s), open {ChainWindow:0.00}s after each blow");
    }

    // Its blow landed: from here the chain's own time runs (Ghost.chainWindow), whatever the takedown still does.
    private void OnTakedownStruck(MeleeCombatTarget victim)
    {
        struckAt = Time.time;
        if (run) Note($"BLOW   at +{struckAt - startedAt:0.00}s: open until +{struckAt - startedAt + ChainWindow:0.00}s");
    }

    // The takedown let the Ghost go. With its blow landed the run stays open for its own time (Chain closes it when that
    // runs out): this is where the chain used to end, with the takedown. One that ended before its blow killed nothing,
    // and ends the run.
    private void OnTakedownEnded()
    {
        if (chaining || !run) return;
        if (struckAt < 0f) Close("the takedown ended before its blow");
    }

    // ---- The headshot cards ----

    // This player's own headshots: a quiet kill on its own mark counts as an assassination (조용한 표적), and the White Tiger
    // rakes the enemies round the one it hit.
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !(report.Target is ICombatFoe) || !PlayerClass.Is(PlayerClass.Ghost)) return;
        var hit = report.Hit;
        if (hit.Method != DamageMethod.Bullet || !hit.IsCritical || !PlayerPerks.IsMine(hit.Source)) return;
        if (report.IsKillingBlow && hit.Silent && !report.IsPractice && GhostMarks.IsMarkedBy(report.Target, movement)) Assassinated();
        if (PlayerPerks.Has(PlayerPerks.GhostTiger)) Rake(report);
    }

    // A quiet kill on its own mark counts as an assassination: the stealth round's money for one (Stealth.csv
    // Score.takedown, its line under the kill's) and a Head Hunter stack more than the headshot's own (Ghost.quietHunter).
    private void Assassinated()
    {
        int points = StealthScore.Takedown;
        if (points > 0) CombatFeedHUD.Score("feed.score.takedown", points);
        if (kit != null) kit.HuntBonus(Mathf.Max(0, Mathf.RoundToInt(PlayerClass.Ability(Row, "quietHunter", 1f))));
    }

    // 화이트 타이거: the enemies within its reach of the one a headshot hit take as much as it did, each with a small claw
    // mark for everyone. Their hits are this PC's to judge as its shots are, and go on to the host as those do. They are
    // no headshots themselves, so they rake no further.
    private void Rake(CombatDamageReport report)
    {
        float reach = PlayerPerks.Total(PlayerPerks.GhostTiger), amount = report.Hit.Amount;
        if (reach <= 0f || amount <= 0f) return;
        Vector3 at = report.Target.transform.position;
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && enemy.gameObject != report.Target.gameObject && (enemy.transform.position - at).sqrMagnitude <= reach * reach)
                near.Add(enemy);
        foreach (var enemy in near)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 point = GhostSight.Middle(enemy) + Vector3.up * .35f, way = point - report.Hit.Point;
            way = way.sqrMagnitude > 1e-4f ? way.normalized : transform.forward;
            enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, point, way, gameObject,
                method: DamageMethod.Bullet, origin: report.Hit.Origin));
            CardNet.Show(CardFx.Kind.Claws, point, way, 1f);
        }
        near.Clear();
    }

    // ---- The ultimate ----

    /// <summary>
    /// The Ghost's ultimate (X once every card is taken; the user, 2026-11-14: "좀비가 고스트를 보지 못하는 컨셉은 좋네"): for
    /// Ghost.phantomSeconds no zombie sees it, its sounds and its touch still counting (ZombieEffects.Unseen, which carries
    /// itself to the host), and every zombie within Ghost.phantomReach of it shows through walls to the whole team (GhostSight,
    /// through GhostNet). Its notice and its look on the ground are every ultimate's (ClassCards.Ultimate).
    /// </summary>
    public void Phantom()
    {
        if (movement == null) return;
        float seconds = Mathf.Max(1f, PlayerClass.Ability(Row, "phantomSeconds", 10f)), reach = Mathf.Max(0f, PlayerClass.Ability(Row, "phantomReach", 40f));
        ZombieEffects.Unseen(movement, seconds);
        phantomTotal = seconds;
        phantomUntil = Time.time + seconds;
        GhostNet.Send(new GhostNet.Packet { Step = (byte)GhostNet.Step.Phantom, Seconds = seconds, Reach = reach, From = transform.position }, movement, null);
    }

    // ---- The chain's prompt ----

    // Under the crosshair, where a takedown's own prompt stands (it is gone while one runs): the key, and how many may follow.
    private void Prompt(bool on)
    {
        if (!on && promptHost == null) return;
        if (promptHost == null) BuildPrompt();
        if (on)
        {
            string text = Texts.Format("class.ghost.chain.prompt", KeyBindings.Bracketed("Player/Interact"), chainsLeft);
            if (prompt.text != text) prompt.text = text;
        }
        promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 10f);
        bool shown = on || promptGroup.alpha > 0f;
        if (promptHost.activeSelf != shown) promptHost.SetActive(shown);
    }

    private void BuildPrompt()
    {
        promptHost = new GameObject("Ghost Chain Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        promptHost.hideFlags = HideFlags.DontSave;
        promptHost.transform.SetParent(transform, false);
        var canvas = promptHost.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = promptHost.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        promptGroup = promptHost.GetComponent<CanvasGroup>();
        promptGroup.alpha = 0f;
        promptGroup.interactable = promptGroup.blocksRaycasts = false;
        var box = KitStyle.TopLeft("Chain", promptHost.transform, Vector2.zero, new Vector2(520f, 44f));
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f, .5f);
        box.anchoredPosition = new Vector2(0f, -88f);
        prompt = KitStyle.Label(box, "", 26f, TextAlignmentOptions.Center, KitStyle.Ice);
    }
}
