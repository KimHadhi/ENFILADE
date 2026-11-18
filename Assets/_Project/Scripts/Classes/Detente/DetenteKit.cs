using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 7, the Détente, the healer (the user's design, 2026-11-11): a syringe with a measure in it. The class key
/// (ClassKit) injects the friend under the crosshair within reach, or, with none there, this player: a dose heals at
/// once and takes its share of the measure. The measure is bought back at a shop (ShopMenu, 주사기 보충) and a Support will
/// top it up a little (Refill, stage 4). The syringe shows in hand as the dose goes in (DetenteLooks; the user, 2026-11-12:
/// "주사기 모델링이 없는데 휠 클릭하면 주사기 들게 할 수 있나?"). With too little left for a dose it can't heal itself at all,
/// and a friend only as Guts &amp; Blackpowder's surgeon does (the user, 2026-11-12: "G&amp;B 스타일로 가까이 다가가서 치료
/// offer하면 아군이 그거 보고 accept 눌렀을 때 모션 길게 나오면서 모션 타이밍에 치료 되고 이제 모션 끝나고 서서히 나머지 체력
/// 치료되는 느낌으로"): F beside a hurt friend offers it, their F takes it, and then a long treatment, the bandage winding,
/// its hands busy all the while (no shot, no turn of the view: PlayerMovement.CombatInputLocked): part way through a share
/// of health goes in, and once it is done the rest of theirs comes back slowly on their own PC. A friend who called for
/// healing (Callouts) is marked for a Détente and needs no answer: the treatment starts at its F ("그런 아군은 accept 누를
/// 필요 없이 … 바로 치료 모션으로"). The old memo's two, as the user gave them again: Golden Hour, a quicker pace for a few seconds
/// after any heal, renewed by each; and Resurrection, once a wave (in a chapter, once between rest stops): Interact held
/// beside a dead friend brings them back where they fell. A friend's health is theirs on their own PC, so heals and
/// resurrections go there (PlayerNetworkCombat). This PC's own player only. Numbers: Tables/ClassAbilities, row Detente.
/// The card feedback of 2026-11-14 (Docs/Features/ClassCards.md): the treatment by hand takes 10 s and the 손에 밴 붕대
/// card cuts it, so this Détente's PC sends its own seconds with the treatment (the patient's alert counts them); Golden
/// Hour is shorter and quicker (it is this Détente's own pace, judged here, so nothing of it is sent); 공생 gives this
/// Détente a share of the health it gives a friend; 아드레날린 주사 sends a friend it doses a few seconds of pace and gun
/// damage, which their own PC gives them (Boosted, read by ClassKit); and the ultimate, Second Life, gets every downed
/// friend up and fills everyone's health where they are, under a disinfectant cloud at each that blinds the zombies in it
/// (SecondLife, DetenteGas). The Détente's PC decides each and sends its numbers (memory actor-decides).
/// </summary>
[DisallowMultipleComponent]
public sealed class DetenteKit : MonoBehaviour
{
    private const string Row = "Detente";
    // The cards with code behind them here (Perks.csv): 공생 (a share of the health given comes back) and 아드레날린 주사.
    private const string SymbioCard = "detente_symbio", AdrenCard = "detente_adren";
    // How often the wave and the rest stop are looked at for a new Resurrection (s); how near two of the team may stand
    // and share one cloud of the ultimate's gas (m): two clouds on one spot only thicken the mist and double the hiss.
    private const float SectionEvery = 1f, ShareCloud = 2f;
    // The ultimate's cloud spots, gathered afresh each time it goes off.
    private static readonly List<Vector3> spots = new List<Vector3>();

    // The class's mint (ClassColours.csv detente, the disinfectant's), for the health bar's blink when its care fills it.
    private static Color Mint
    {
        get
        {
            var colour = KillFlairs.ClassColourOf(PlayerClass.Detente);
            if (colour.a <= 0f) return new Color(.49f, .94f, .69f);
            colour.a = 1f;
            return colour;
        }
    }

    /// <summary>This PC's own player's, while they play a Détente.</summary>
    public static DetenteKit Local { get; private set; }
    // This PC's own player's, whatever they play: a Détente's offer to them, and a treatment's rest, are theirs.
    private static DetenteKit own;
    // Friends' calls for healing heard on this PC, and until when each holds.
    private static readonly Dictionary<PlayerMovement, float> calls = new Dictionary<PlayerMovement, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Local = own = null;
        calls.Clear();
    }

    /// <summary>A dose went in, into this player (its look for the others: PlayerNetworkCombat, which says who so a friend's dose is aimed at them there too).</summary>
    public event Action<PlayerMovement> Injected;
    /// <summary>A treatment by hand began for so long, or was called off early (the bandage, for the others).</summary>
    public event Action<float, PlayerMovement> TreatBegan;
    public event Action TreatStopped;

    private PlayerMovement movement;
    private PlayerDamageReceiver receiver;
    private WaveManager waves;
    private float syringe = -1f, goldenUntil, goldenTotal = 1f, lastInject = -99f, nextSection;
    // Treatment by hand, as the Détente: the friend offered it and until when, when the one under way began and whether
    // its share went in. As the patient: the Détente whose offer waits for F and until when, and the rest of a treatment
    // coming back (how fast, until when).
    private PlayerMovement offeredTo, offeredBy;
    private float offerUntil, offeredUntil, treatStart, regenRate, regenUntil;
    private bool treatHealed;
    private int raisesLeft = 1, lastWave = -1;
    private Vector3 lastCheckpoint;
    private bool hadCheckpoint;
    // As anyone (any class): a Détente friend's adrenaline shot in this player, until when, how long it was, and what it
    // gives (pace and gun damage, %), as that Détente's PC sent them.
    private float boostUntil, boostTotal = 1f, boostSpeed, boostDamage;
    // The run to a friend out of the arm's reach, for a dose or a treatment (InjectApproach, the body's surge): what it
    // was for is given when the arm reaches them (Approaching).
    private InjectApproach approach;

    /// <summary>The syringe's measure now, and the most it holds.</summary>
    public float Syringe => Mathf.Max(0f, syringe);
    public float SyringeMax => Mathf.Max(1f, Number("syringeMax", 100f));
    /// <summary>What one dose takes from it, and how many doses are left in it.</summary>
    public float Dose => Mathf.Max(1f, Number("injectCost", 25f));
    public int Doses => Mathf.FloorToInt(Syringe / Dose + .001f);
    public int MostDoses => Mathf.Max(1, Mathf.FloorToInt(SyringeMax / Dose + .001f));
    /// <summary>Too little for a dose: no healing itself, and friends only by the long treatment.</summary>
    public bool Empty => Syringe < Dose;
    /// <summary>The hurt friend in reach F would offer a treatment now (the syringe empty), or null.</summary>
    public PlayerMovement Candidate { get; private set; }
    /// <summary>The friend offered a treatment, still waiting for their answer, or null.</summary>
    public PlayerMovement Offering => offeredTo != null && Time.time < offerUntil ? offeredTo : null;
    /// <summary>Whether this Détente's offer to <paramref name="friend"/> takes at once (they called for healing).</summary>
    public static bool Asked(PlayerMovement friend) => friend != null && calls.TryGetValue(friend, out float until) && Time.time < until;
    /// <summary>The friend it is treating by hand now, or bringing back from the dead, and how far along (0 to 1).</summary>
    public PlayerMovement Treating { get; private set; }
    public float TreatProgress => Treating != null ? Mathf.Clamp01((Time.time - treatStart) / TreatSeconds) : 0f;
    // The 손에 밴 붕대 card takes off this PC's player's own seconds (PlayerClass.Ability adds it in); the patient's PC is
    // told this number with the treatment rather than reading its own player's.
    private float TreatSeconds => Mathf.Max(.5f, Number("treatSeconds", 10f));
    /// <summary>This PC's Détente has its hands in a treatment: no shot, swing or turn of the view (PlayerMovement).</summary>
    public static bool HoldsHands => Local != null && Local.Treating != null;
    /// <summary>A Détente's offer waits for this PC's player's F (F does nothing else meanwhile: PlayerPing).</summary>
    public static bool Answering => own != null && own.offeredBy != null && Time.time < own.offeredUntil;
    public PlayerMovement Raising { get; private set; }
    public float RaiseProgress { get; private set; }
    /// <summary>Resurrections left this wave (or until the next rest stop).</summary>
    public int RaisesLeft => raisesLeft;
    /// <summary>
    /// The factor on this player's pace from here (ClassKit.SpeedFactor): a Détente's own Golden Hour, and a Détente
    /// friend's adrenaline shot in any class.
    /// </summary>
    public float SpeedFactor
    {
        get
        {
            float factor = goldenUntil > Time.time && PlayerClass.Is(PlayerClass.Detente) ? 1f + Number("goldenSpeed", 12f) / 100f : 1f;
            return BoostShare > 0f ? factor * (1f + boostSpeed / 100f) : factor;
        }
    }
    /// <summary>How much of a friend's adrenaline shot in this player is left, 0 to 1 (0: none), and its gun damage (%).</summary>
    public float BoostShare => boostUntil > Time.time ? Mathf.Clamp01((boostUntil - Time.time) / boostTotal) : 0f;
    public float BoostDamage => BoostShare > 0f ? boostDamage : 0f;
    /// <summary>The factor a friend's adrenaline shot puts on this player's guns now (ClassKit.DamageFactor): 1 without one.</summary>
    public float GunFactor => 1f + BoostDamage / 100f;
    /// <summary>This PC's own player's, whatever class they play (a friend's shot, a treatment's rest): the HUD reads it.</summary>
    public static DetenteKit Mine => own;
    /// <summary>Seconds since the last dose went in (the HUD's flash).</summary>
    public float SinceInject => Time.time - lastInject;
    /// <summary>
    /// What F does around a Détente, as the interact key's one owner knows each (InteractKey; their places among
    /// everything else F does are the rows of these names in Tables/Player/InteractKey.csv): any player's answer to a
    /// treatment offered them, and a Détente's own two, a dead friend brought back for as long as F is held and a
    /// treatment by hand offered to a hurt one. The ping used to ask this class whether F was taken; nobody does now.
    /// </summary>
    public const string AnswerOffer = "detente.answer", RaiseOffer = "detente.raise", TreatOffer = "detente.treat";
    // The answer's line is up at the foot of the screen (Patient): taken down when the key's owner gives F to another.
    private bool answerShown;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    private void OnDisable()
    {
        if (Local == this) Local = null;
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        // Any class: a Détente's offer waiting for this player's F, and a treatment's rest coming back.
        Patient();
        // A Détente friend's adrenaline shot in this player, whatever class they play: its slot among the timed effects
        // (TimedEffects), said each frame it runs, by the clock its factors go by.
        TimedEffects.Report("detente.boost", boostUntil - Time.time, boostTotal, 0, boostDamage);
        if (!PlayerClass.Is(PlayerClass.Detente))
        {
            if (Local == this) Local = null;
            if (Treating != null) Stop(false);
            approach?.Stop();
            Candidate = Raising = null;
            RaiseProgress = 0f;
            return;
        }
        Local = this;
        // A Détente's own timed states, each a slot of its own: Golden Hour, and the work its hands are in (a treatment's
        // or a Resurrection's seconds still to go).
        TimedEffects.Report("detente.golden", goldenUntil - Time.time, goldenTotal);
        if (Treating != null) TimedEffects.Report("detente.treat", TreatSeconds - (Time.time - treatStart), TreatSeconds);
        if (Raising != null)
        {
            float raise = Mathf.Max(.1f, Number("resurrectSeconds", 5f));
            TimedEffects.Report("detente.raise", (1f - RaiseProgress) * raise, raise);
        }
        if (receiver == null) receiver = movement.DamageReceiver;
        if (syringe < 0f) syringe = Mathf.Clamp(Number("syringeStart", 100f), 0f, SyringeMax);
        Sections();
        // able: what the body can do (reasons of the game). Whether F is this Détente's to press or to hold is the
        // interact key's to say (InteractKey): each thing F would do here is said to it by name, and done only on a press
        // or a hold its owner calls this one's. With a screen up the key reads as let go, which starts a held
        // resurrection over by that move's own rule and makes no new offer, but takes nothing else away (a treatment
        // under way never asked the keys: Treatment).
        bool able = movement.CanAct && !movement.IsDowned && !movement.IsHeld && HandsFree();
        float dt = Time.deltaTime;

        // A run to a friend under way: nothing else is offered or begun until it is in reach or over.
        if (approach != null && approach.Running)
        {
            Approaching();
            return;
        }

        // A dead friend first (the key's order has it before a treatment offered): Resurrection, for as long as F is
        // held on it. Raising is the friend F would bring back now: the one in reach while the key's owner says F is
        // the resurrection's (a takedown, a friend only downed, a pickup that is wanted and the mission's prompts come
        // before it, as HandsFree's list had them until 2026-11-18).
        var dead = able && raisesLeft > 0 ? Nearest(true) : null;
        if (dead != null) InteractKey.Offer(RaiseOffer, held: true, target: dead);
        if (dead != null && !InteractKey.Shows(RaiseOffer)) dead = null;
        if (dead != Raising) RaiseProgress = 0f;
        Raising = dead;
        if (Raising != null && InteractKey.Held(RaiseOffer))
        {
            RaiseProgress += dt / Mathf.Max(.1f, Number("resurrectSeconds", 5f));
            if (RaiseProgress >= 1f) Resurrect(Raising);
        }
        else RaiseProgress = 0f;

        // With the syringe empty, a hurt friend by hand, as a surgeon does: F offers, their F takes it (one who called for
        // healing takes it at once), then the long treatment.
        Calls();
        if (Treating != null) { Treatment(); return; }
        if (offeredTo != null && Time.time >= offerUntil) offeredTo = null;
        var hurt = able && Empty ? Nearest(false) : null;
        if (hurt != null) InteractKey.Offer(TreatOffer, target: hurt);
        Candidate = hurt != null && InteractKey.Shows(TreatOffer) ? hurt : null;
        if (Candidate == null || !InteractKey.Pressed(TreatOffer)) return;
        if (Asked(Candidate)) { if (InReach(Candidate, InjectArm.Kind.Bandage, Reach())) Begin(Candidate); return; }
        if (offeredTo == Candidate) return;
        offeredTo = Candidate;
        offerUntil = Time.time + Mathf.Max(1f, Number("offerSeconds", 8f));
        if (Candidate.TryGetComponent(out PlayerNetworkCombat network)) network.OfferTreatment(movement);
        KitSound.Play(KitSound.Cue.PickHeal, .35f);
        AlertHUD.Self(Texts.Format("class.detente.offered", Name(Candidate)));
    }

    // ---- Treatment by hand (the syringe empty) ----

    /// <summary>
    /// The friend took this Détente's offer (their PC said so: PlayerNetworkCombat): the treatment begins, if the offer
    /// still holds and they are still in reach.
    /// </summary>
    internal void Accepted(PlayerMovement patient)
    {
        if (patient == null || patient != offeredTo || Time.time >= offerUntil || Treating != null) return;
        if ((patient.transform.position - transform.position).sqrMagnitude > Reach(1f) * Reach(1f)) return;
        if (InReach(patient, InjectArm.Kind.Bandage, Reach(1f))) Begin(patient);
    }

    // ---- Getting within the arm's reach (the user, 2026-11-18: "닿는 거리로 빠르게 이동하죠, 그 써지 기능처럼요") ----

    // Whether the friend is within the arm's reach as the two stand. If not, the body runs there when the way is clear
    // (InjectApproach: the body's own surge, which passes no wall, door or drop) and what the run was for follows on
    // arrival (Approaching); when there is no way, it says so and nothing is spent.
    private bool InReach(PlayerMovement friend, InjectArm.Kind kind, float within)
    {
        approach ??= new InjectApproach(movement);
        var way = approach.Begin(friend, kind, within);
        if (way == InjectApproach.Way.There) return true;
        if (way == InjectApproach.Way.Run) return false;
        KitSound.Play(KitSound.Cue.Refuse, .4f);
        AlertHUD.Self(Texts.Format(way == InjectApproach.Way.Stance ? "class.detente.way.stance" : "class.detente.way.blocked", Name(friend)));
        return false;
    }

    // The run to a friend, looked after: in reach, the dose goes in or the treatment begins, if it can still be given; a
    // run that did not get there (they walked off, something came between) costs nothing.
    private void Approaching()
    {
        var friend = approach.Friend;
        var kind = approach.Kind;
        var state = approach.Tick(movement.DeltaTime);
        if (state == InjectApproach.State.Running) return;
        if (state != InjectApproach.State.Arrived)
        {
            KitSound.Play(KitSound.Cue.Refuse, .4f);
            AlertHUD.Self(Texts.Format("class.detente.way.missed", Name(friend)));
        }
        else if (kind == InjectArm.Kind.Bandage)
        {
            if (Treating == null) Begin(friend);
        }
        else if (!Empty) Inject(friend);
    }

    private void Begin(PlayerMovement patient)
    {
        offeredTo = null;
        Candidate = null;
        Treating = patient;
        treatStart = Time.time;
        treatHealed = false;
        DetenteLooks.Bandage(movement, TreatSeconds, patient);
        TreatBegan?.Invoke(TreatSeconds, patient);
        // This PC's seconds (its 손에 밴 붕대 card counted) go with it: the patient's PC says them, never its own player's.
        if (patient.TryGetComponent(out PlayerNetworkCombat network)) network.TreatmentBegun(movement, TreatSeconds);
        KitSound.Play(KitSound.Cue.PickHeal, .5f);
    }

    // The treatment under way: off if either goes down or they part; its share part way through; at its end the rest of
    // the friend's health comes back over a few seconds on their own PC.
    private void Treatment()
    {
        var patient = Treating;
        var life = patient != null ? patient.DamageReceiver : null;
        bool off = life == null || !life.IsAlive || life.IsDowned || movement.IsDead || movement.IsDowned || movement.IsHeld
            || (patient.transform.position - transform.position).sqrMagnitude > Reach(1f) * Reach(1f);
        if (off) { Stop(false); return; }
        float progress = TreatProgress;
        if (!treatHealed && progress >= Mathf.Clamp01(Number("treatAt", .6f)))
        {
            treatHealed = true;
            Heal(patient, Number("treatHeal", 50f));
        }
        if (progress < 1f) return;
        // What is left of theirs comes back over time; it fills the gauge as a heal does, by what this PC sees missing,
        // and 공생 gives this Détente its share of it now.
        float rest = Mathf.Max(0f, life.MaximumHealth - life.Health);
        UltimateCharge.AwardHelp(UltimateCharge.Help.Healed, rest);
        Symbiosis(rest);
        if (patient.TryGetComponent(out PlayerNetworkCombat network)) network.HealOverTime(Number("treatRegenSeconds", 8f));
        AlertHUD.Self(Texts.Format("class.detente.treated", Name(patient)));
        calls.Remove(patient);
        DetenteLooks.Unask(patient);
        Stop(true);
    }

    // Over: done (the bandage drops away by itself), or called off (it drops now and the others are told).
    private void Stop(bool done)
    {
        Treating = null;
        if (done) return;
        DetenteLooks.StopBandage(movement);
        TreatStopped?.Invoke();
    }

    // How far a treatment may be offered (and how much further it holds once begun).
    private float Reach(float more = 0f) => Mathf.Max(.5f, Number("treatReach", 2.8f)) + more;

    /// <summary>
    /// A friend called for healing (Callouts request.heal or request.medic, heard on this PC: PlayerNetworkCombat): while
    /// the call holds a Détente here sees them marked and its offer to them takes at once.
    /// </summary>
    public static void Called(PlayerMovement friend, string lineId)
    {
        if (friend == null || friend.IsLocal || (lineId != "request.heal" && lineId != "request.medic")) return;
        float seconds = Mathf.Max(1f, PlayerClass.Ability(Row, "callSeconds", 20f));
        calls[friend] = Time.time + seconds;
        if (PlayerClass.Is(PlayerClass.Detente)) DetenteLooks.Ask(friend, seconds);
    }

    // Calls run out, or are answered once the friend is whole again (healed anyhow) or gone.
    private static readonly List<PlayerMovement> ended = new List<PlayerMovement>();
    private void Calls()
    {
        if (calls.Count == 0) return;
        ended.Clear();
        foreach (var pair in calls)
        {
            var life = pair.Key != null ? pair.Key.DamageReceiver : null;
            if (life == null || Time.time >= pair.Value || !life.IsAlive || life.Health >= life.MaximumHealth - .5f) ended.Add(pair.Key);
        }
        foreach (var friend in ended)
        {
            calls.Remove(friend);
            DetenteLooks.Unask(friend);
        }
    }

    // ---- As the patient (any class) ----

    /// <summary>A Détente offered this PC's player a treatment by hand (PlayerNetworkCombat): F takes it while it holds.</summary>
    internal void Offered(PlayerMovement detente)
    {
        if (detente == null || movement == null || movement.IsDead) return;
        offeredBy = detente;
        offeredUntil = Time.time + Mathf.Max(1f, Number("offerSeconds", 8f));
        KitSound.Play(KitSound.Cue.Alert, .55f);
    }

    /// <summary>
    /// A Détente began treating this PC's player for <paramref name="seconds"/>, as its own PC makes them (its cards
    /// included: PlayerNetworkCombat.TreatBeganRpc): the offer is answered, and the alert says how long to stay.
    /// </summary>
    internal void Begun(PlayerMovement detente, float seconds)
    {
        offeredBy = null;
        DetenteLooks.Prompt(null);
        AlertHUD.Self(Texts.Format("class.detente.treating", Name(detente), Mathf.CeilToInt(Mathf.Max(.5f, seconds))));
    }

    /// <summary>
    /// A Détente friend's adrenaline shot went into this PC's player (PlayerNetworkCombat.AdrenalineRpc, the 아드레날린 주사
    /// card), with its PC's numbers: <paramref name="speed"/> % pace and <paramref name="damage"/> % gun damage for
    /// <paramref name="seconds"/> (ClassKit reads them: SpeedFactor, GunFactor). A shot while one runs starts it again;
    /// only a fresh one says so.
    /// </summary>
    internal void Boosted(float seconds, float speed, float damage, PlayerMovement from)
    {
        if (movement == null || movement.IsDead || seconds <= 0f) return;
        bool fresh = BoostShare <= 0f;
        boostTotal = Mathf.Max(.1f, seconds);
        boostUntil = Time.time + boostTotal;
        boostSpeed = Mathf.Max(0f, speed);
        boostDamage = Mathf.Max(0f, damage);
        if (fresh) AlertHUD.Self(Texts.Format("class.detente.adren.got", Name(from), Mathf.RoundToInt(boostTotal)));
    }

    /// <summary>
    /// A Détente's ultimate reached this PC (PlayerNetworkCombat.SecondLifeRpc, from that Détente's PC): its clouds where
    /// it put them, and this PC's own player up at once if down, or their health full if standing. The dead wait for a
    /// Resurrection, of which the ultimate gave that Détente one more.
    /// </summary>
    internal static void SecondLifeReached(PlayerMovement detente, Vector3[] clouds, float radius, float seconds)
    {
        if (clouds != null) foreach (var spot in clouds) DetenteGas.Burst(spot, radius, seconds);
        var mine = PlayerRoster.Local;
        if (mine != null && mine != detente && mine.TryGetComponent(out DetenteKit kit)) kit.Rallied(detente);
    }

    // This player's own health, which is theirs to change here: up from the ground with all of it, or all of it back.
    private void Rallied(PlayerMovement detente)
    {
        if (receiver == null) receiver = movement.DamageReceiver;
        if (receiver == null || !receiver.IsAlive) return;
        bool down = receiver.IsDowned;
        if (down) receiver.GetUp(receiver.MaximumHealth);
        else receiver.Heal(receiver.MaximumHealth);
        AdaptiveHealthHUD.Blink(Mint, false);
        KitSound.Play(KitSound.Cue.PickHeal, .8f);
        AlertHUD.Self(Texts.Format(down ? "class.detente.ult.up" : "class.detente.ult.got", Name(detente)));
    }

    /// <summary>A treatment's end (PlayerNetworkCombat): the rest of this player's health comes back over <paramref name="seconds"/>.</summary>
    internal void Regen(float seconds)
    {
        if (receiver == null) receiver = movement.DamageReceiver;
        if (receiver == null || !receiver.IsAlive || receiver.IsDowned) return;
        float rest = receiver.MaximumHealth - receiver.Health, time = Mathf.Max(.5f, seconds);
        if (rest <= 0f) return;
        regenRate = rest / time;
        regenUntil = Time.time + time;
    }

    private void Patient()
    {
        own = this;
        if (receiver == null) receiver = movement.DamageReceiver;
        if (regenUntil > Time.time && receiver != null && receiver.IsAlive && !receiver.IsDowned) receiver.Heal(regenRate * Time.deltaTime);
        else regenUntil = 0f;
        bool waiting = offeredBy != null && Time.time < offeredUntil && !movement.IsDead && !movement.IsDowned;
        if (!waiting)
        {
            if (offeredBy != null) { offeredBy = null; DetenteLooks.Prompt(null); }
            answerShown = false;
            return;
        }
        // The answer is one of the things F does (InteractKey): its line shows and its press counts while the key's
        // owner says F is the answer's. Until 2026-11-18 it read the key for itself, so the F that took the offer also
        // took whatever pickup was looked at, and its line stood over a takedown's prompt whose F came first anyway.
        InteractKey.Offer(AnswerOffer, target: offeredBy);
        bool shows = InteractKey.Shows(AnswerOffer);
        if (shows) DetenteLooks.Prompt(Texts.Format("class.detente.offer.prompt", Name(offeredBy), Mathf.CeilToInt(offeredUntil - Time.time)));
        else if (answerShown) DetenteLooks.Prompt(null);
        answerShown = shows;
        if (!InteractKey.Pressed(AnswerOffer)) return;
        var detente = offeredBy;
        offeredBy = null;
        answerShown = false;
        DetenteLooks.Prompt(null);
        if (detente.TryGetComponent(out PlayerNetworkCombat network)) network.AcceptTreatment(movement);
    }

    /// <summary>
    /// The class key (ClassKit): a dose into the friend under the crosshair within reach, or into this player with none
    /// there. Refused with too little in the syringe, too soon after the last, or with nobody to heal.
    /// </summary>
    public void Inject()
    {
        if (receiver == null) receiver = movement.DamageReceiver;
        if (syringe < 0f) syringe = Mathf.Clamp(Number("syringeStart", 100f), 0f, SyringeMax);
        if (Time.time - lastInject < Mathf.Max(0f, Number("injectCooldown", 1f))) { KitSound.Play(KitSound.Cue.Refuse, .4f); return; }
        if (Empty)
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.detente.empty"));
            return;
        }
        var patient = Aimed();
        if (patient == null)
        {
            // Nobody in front: itself, if it needs it.
            if (receiver == null || receiver.Health >= receiver.MaximumHealth - .5f) { KitSound.Play(KitSound.Cue.Refuse, .4f); return; }
            patient = movement;
        }
        // The left hand gives the dose. With that hand on something that keeps it (a grenade held ready, or a reload
        // under way: between those two whichever began first keeps the hand) there is no dose, no run to a friend, and
        // nothing is spent (asked whether a dose should be refused when the hand is not free, the user, 2026-11-18:
        // "네"). Until then the dose went in with no arm to give it.
        if (!HandFor()) { NoHand(); return; }
        // A friend further off than the arm reaches is run to first; the dose goes in on arrival (Approaching).
        if (patient != movement && !InReach(patient, InjectArm.Kind.Friend, Number("injectReach", 3.5f))) return;
        Inject(patient);
    }

    // The dose itself, the needle in reach: its share of the measure, its look, its healing.
    private void Inject(PlayerMovement patient)
    {
        // The hand is taken here, where the needle is in reach: after a run to a friend it may have gone meanwhile (a
        // zombie's grip on the way), and then nothing is spent, as when the run does not get there.
        if (!TakeHand()) { NoHand(); return; }
        syringe = Mathf.Max(0f, syringe - Dose);
        lastInject = Time.time;
        // The syringe in hand as it goes in (DetenteLooks), and in the others' sight of this player.
        DetenteLooks.Syringe(movement, patient);
        Injected?.Invoke(patient);
        Heal(patient, Number("injectHeal", 40f));
        if (patient == movement) return;
        AlertHUD.Self(Texts.Format("class.detente.injected", Name(patient)));
        Adrenaline(patient);
    }

    // 아드레날린 주사 (the card, 2026-11-14): a friend this Détente doses runs faster and shoots harder a few seconds. This
    // PC picks the numbers (its card's level, the table's seconds and pace) and sends them; the friend's own PC gives them
    // (DetenteKit.Boosted there), since their pace and their shots are judged there. A full-health friend may be dosed for it.
    private void Adrenaline(PlayerMovement friend)
    {
        float damage = PlayerPerks.Total(AdrenCard);
        if (damage <= 0f || !friend.TryGetComponent(out PlayerNetworkCombat network)) return;
        network.SendAdrenaline(Mathf.Max(.5f, Number("adrenSeconds", 5f)), Mathf.Max(0f, Number("adrenSpeed", 15f)), damage);
    }

    // 공생 (the card, 2026-11-14): a share of the health this Détente gave a friend comes back to it at once, as far as it
    // is hurt. Only a friend's health counts (a dose into itself would only feed itself), and the bar blinks the mint.
    private void Symbiosis(float given)
    {
        float share = PlayerPerks.Total(SymbioCard) / 100f;
        if (share <= 0f || given <= 0f) return;
        if (receiver == null) receiver = movement.DamageReceiver;
        if (receiver == null || !receiver.IsAlive || receiver.IsDowned || receiver.Health >= receiver.MaximumHealth - .5f) return;
        receiver.Heal(given * share);
        AdaptiveHealthHUD.Blink(Mint, false);
    }

    /// <summary>More in the syringe (a shop's refill, a Support's top-up), up to the most it holds; false when it was full.</summary>
    public bool Refill(float amount)
    {
        if (syringe < 0f) syringe = Mathf.Clamp(Number("syringeStart", 100f), 0f, SyringeMax);
        if (syringe >= SyringeMax - .01f || amount <= 0f) return false;
        syringe = Mathf.Min(SyringeMax, syringe + amount);
        return true;
    }

    /// <summary>A shop sells the refill only to a Détente whose syringe isn't full.</summary>
    public bool Full => Syringe >= SyringeMax - .01f;

    /// <summary>
    /// The ultimate, Second Life (every card taken, a full gauge: ClassCards.Ultimate): the syringe full, one Resurrection
    /// more, and every downed friend up and everyone's health full under a disinfectant cloud each (SecondLife).
    /// </summary>
    internal void Ultimate()
    {
        Refill(SyringeMax);
        raisesLeft++;
        SecondLife();
    }

    // The user, 2026-11-14: "궁극기 사용 시 쓰러진 동료 즉시 일어나면서 전체 동료 위치에서 메딕 소독 가스 3초 동안 분사되게.
    // (왜냐하면 쓰러진 동료가 일어날 때 커버를 받아야 하니까)", and "동료 체력 회복도 좋네". This PC decides it all: this
    // Détente's own health full here; a cloud on every one of the team wherever they are (itself too, so a lone Détente is
    // covered as well, and a dead friend's spot, where the Resurrection it just got will be wanted), two standing close
    // sharing one; each cloud blinds the zombies in it, called once here (ZombieEffects.Blind carries it to the host); and
    // the word to every other PC, which draws the same clouds and gets its own player up or fills their health there,
    // their health being theirs (SecondLifeReached). The gauge it was bought with gets nothing back from it.
    private void SecondLife()
    {
        if (receiver == null) receiver = movement.DamageReceiver;
        if (receiver != null && receiver.IsAlive && !receiver.IsDowned)
        {
            receiver.Heal(receiver.MaximumHealth);
            AdaptiveHealthHUD.Blink(Mint, false);
        }
        float radius = Mathf.Max(.5f, Number("ultGasRadius", 4f)), seconds = Mathf.Max(.5f, Number("ultGasSeconds", 3f));
        float slow = Mathf.Clamp01(Number("ultGasSlow", .5f));
        spots.Clear();
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || (someone != movement && !someone.IsInitialized)) continue;
            // Their getting up counts as this Détente's, as a Resurrection does (the results' 부활 count).
            if (someone != movement && someone.IsDowned) PlayerTally.Raised();
            Vector3 at = someone.transform.position;
            bool shared = false;
            foreach (var spot in spots)
                if ((spot - at).sqrMagnitude < ShareCloud * ShareCloud) { shared = true; break; }
            if (!shared) spots.Add(at);
        }
        if (spots.Count == 0) spots.Add(transform.position);
        foreach (var spot in spots)
        {
            ZombieEffects.Blind(spot, radius, seconds, slow);
            DetenteGas.Burst(spot, radius, seconds);
        }
        KitSound.Play(KitSound.Cue.PickHeal, .8f);
        if (TryGetComponent(out PlayerNetworkCombat network)) network.SendSecondLife(spots.ToArray(), radius, seconds);
    }

    /// <summary>
    /// A class was picked (PlayerClass.Picked, by ClassKit): a fresh start, as a chapter's. The syringe as it starts,
    /// every Resurrection back, no Golden Hour and no friend's adrenaline shot, nobody being treated or raised.
    /// </summary>
    internal void Fresh()
    {
        syringe = -1f;
        raisesLeft = Mathf.Max(0, Mathf.RoundToInt(Number("resurrections", 1f)));
        goldenUntil = 0f;
        // A friend's adrenaline shot goes with every other buff a pick starts afresh (a friend's CHARGE does too).
        boostUntil = 0f;
        lastInject = -99f;
        if (Treating != null) Stop(false);
        offeredTo = null;
        Candidate = Raising = null;
        RaiseProgress = 0f;
    }

    // A friend's health is theirs on their own PC; this player's is here.
    private void Heal(PlayerMovement patient, float amount)
    {
        if (patient == movement) receiver.OrNull()?.Heal(amount);
        else if (patient.TryGetComponent(out PlayerNetworkCombat network))
        {
            // A friend's heal fills the gauge by the health it gives back, as this PC sees their health (their own PC
            // applies it): looking after the team is the healer's fight (UltimateCharge.AwardHelp).
            var life = patient.DamageReceiver;
            float given = life != null ? Mathf.Clamp(life.MaximumHealth - life.Health, 0f, amount) : amount;
            UltimateCharge.AwardHelp(UltimateCharge.Help.Healed, given);
            network.Heal(amount);
            Symbiosis(given);
        }
        KitSound.Play(KitSound.Cue.PickHeal, .8f);
        Golden();
    }

    private void Resurrect(PlayerMovement friend)
    {
        RaiseProgress = 0f;
        raisesLeft = Mathf.Max(0, raisesLeft - 1);
        if (friend.TryGetComponent(out PlayerNetworkCombat network))
            network.Resurrect(Mathf.Clamp01(Number("resurrectHealth", 50f) / 100f));
        UltimateCharge.AwardHelp(UltimateCharge.Help.Revived);
        KitSound.Play(KitSound.Cue.Charge, .8f);
        AlertHUD.Self(Texts.Format("class.detente.raised", Name(friend)));
        Golden();
    }

    // Any heal: Golden Hour, renewed. It is this Détente's own pace (ClassKit.SpeedFactor here), so nothing of it is sent.
    private void Golden()
    {
        goldenTotal = Mathf.Max(.1f, Number("goldenSeconds", 6f));
        goldenUntil = Time.time + goldenTotal;
    }

    // A new wave, or a rest stop reached: Resurrection is there again.
    private void Sections()
    {
        if (Time.time < nextSection) return;
        nextSection = Time.time + SectionEvery;
        int most = Mathf.Max(0, Mathf.RoundToInt(Number("resurrections", 1f)));
        if (waves == null) waves = SceneOne<WaveManager>.Get();
        int wave = waves != null && waves.IsRunning ? waves.CurrentStatus.Wave : -1;
        if (wave != lastWave) { if (lastWave >= 0 || wave >= 0) raisesLeft = most; lastWave = wave; }
        if (CampaignRun.TryCheckpoint(out Vector3 checkpoint))
        {
            if (!hadCheckpoint || (checkpoint - lastCheckpoint).sqrMagnitude > .25f) raisesLeft = most;
            hadCheckpoint = true;
            lastCheckpoint = checkpoint;
        }
    }

    // The nearest friend within reach who is dead (dead) or alive, up and hurt (not dead).
    private PlayerMovement Nearest(bool dead)
    {
        float reach = Number(dead ? "resurrectReach" : "treatReach", 2.2f), best = reach * reach;
        PlayerMovement found = null;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == movement || !other.IsInitialized || other.DamageReceiver == null) continue;
            var life = other.DamageReceiver;
            if (dead ? life.IsAlive : !life.IsAlive || life.IsDowned || life.Health >= life.MaximumHealth - .5f) continue;
            float distance = (other.transform.position - transform.position).sqrMagnitude;
            if (distance > best) continue;
            // A hurt friend the arm cannot get to (a wall, a shut door, a drop between) is offered nothing.
            if (!dead && InjectApproach.Look(movement, other, InjectArm.Kind.Bandage, reach, out _) == InjectApproach.Way.Blocked) continue;
            best = distance;
            found = other;
        }
        return found;
    }

    // The living friend nearest the crosshair within reach and in sight.
    private PlayerMovement Aimed()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Number("injectReach", 3.5f), widest = Number("injectAngle", 14f);
        PlayerMovement found = null;
        float best = widest;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == movement || !other.IsInitialized || other.DamageReceiver == null) continue;
            if (!other.DamageReceiver.IsAlive || other.DamageReceiver.IsDowned) continue;
            Vector3 chest = other.transform.position + Vector3.up * 1.1f, to = chest - eyes.position;
            if (to.sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eyes.forward, to);
            if (angle > best) continue;
            if (Physics.Linecast(eyes.position, chest, out RaycastHit hit, ~(1 << movement.gameObject.layer), QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(other.transform)) continue;
            best = angle;
            found = other;
        }
        return found;
    }

    // ---- The left hand (HandOwner) ----

    // Who asks for the dose's hand: the arm that gives it, which says so again every frame of its motion (InjectArm);
    // on a body with no arm to pose, this.
    private UnityEngine.Object DoseHand
    {
        get
        {
            var arm = InjectArm.Of(movement);
            return arm != null ? arm : this;
        }
    }

    // The left hand could be the dose's now (Tables/Player/HandUses.csv detente.dose). A no goes to Logs/hands.txt with
    // what has the hand: the player pressed a key for it.
    private bool HandFor()
    {
        var hands = HandOwner.Of(movement);
        return hands == null || hands.Could(HandUses.Dose, DoseHand, true);
    }

    private bool TakeHand()
    {
        var hands = HandOwner.Of(movement);
        return hands == null || hands.Take(HandUses.Dose, DoseHand);
    }

    private static void NoHand()
    {
        KitSound.Play(KitSound.Cue.Refuse, .4f);
        AlertHUD.Self(Texts.Get("class.detente.hand"));
    }

    // The hands are this Détente's own: not in a takedown, no shop counter open. What else F might do here is not asked
    // (a takedown on offer, a pickup, a friend to get up, a ladder's top, the mission's prompts were each asked by name
    // until 2026-11-18, the same list SupportKit kept): that they come first is the interact key's order (InteractKey),
    // and a pickup that refuses, a medkit with health full, no longer keeps a friend from being brought back or treated.
    private bool HandsFree()
    {
        var execution = movement.Execution;
        return (execution == null || !execution.IsExecuting) && !ShopMenu.IsOpen;
    }

    private static string Name(PlayerMovement someone)
        => someone != null && someone.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : "";

    private static float Number(string name, float fallback) => PlayerClass.Ability(Row, name, fallback);
}
