using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
/// </summary>
[DisallowMultipleComponent]
public sealed class DetenteKit : MonoBehaviour
{
    private const string Row = "Detente";
    // How often the wave and the rest stop are looked at for a new Resurrection (s).
    private const float SectionEvery = 1f;

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

    /// <summary>A dose went in (its look for the others: PlayerNetworkCombat).</summary>
    public event Action Injected;
    /// <summary>A treatment by hand began for so long, or was called off early (the bandage, for the others).</summary>
    public event Action<float> TreatBegan;
    public event Action TreatStopped;

    private PlayerMovement movement;
    private PlayerDamageReceiver receiver;
    private InputAction interact;
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
    private float TreatSeconds => Mathf.Max(.5f, Number("treatSeconds", 4f));
    /// <summary>This PC's Détente has its hands in a treatment: no shot, swing or turn of the view (PlayerMovement).</summary>
    public static bool HoldsHands => Local != null && Local.Treating != null;
    /// <summary>A Détente's offer waits for this PC's player's F (F does nothing else meanwhile: PlayerPing).</summary>
    public static bool Answering => own != null && own.offeredBy != null && Time.time < own.offeredUntil;
    public PlayerMovement Raising { get; private set; }
    public float RaiseProgress { get; private set; }
    /// <summary>Resurrections left this wave (or until the next rest stop).</summary>
    public int RaisesLeft => raisesLeft;
    /// <summary>How much of Golden Hour is left, 0 to 1 (0: none), and the pace it gives now.</summary>
    public float GoldenShare => goldenUntil > Time.time ? Mathf.Clamp01((goldenUntil - Time.time) / goldenTotal) : 0f;
    public float SpeedFactor => goldenUntil > Time.time ? 1f + Number("goldenSpeed", 8f) / 100f : 1f;
    /// <summary>Seconds since the last dose went in (the HUD's flash).</summary>
    public float SinceInject => Time.time - lastInject;
    /// <summary>Whether Interact is taken here now (a treatment or a resurrection on offer): the ping keeps off it.</summary>
    public bool TakesInteract => Candidate != null || Treating != null || Raising != null;

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
        if (!PlayerClass.Is(PlayerClass.Detente))
        {
            if (Local == this) Local = null;
            if (Treating != null) Stop(false);
            Candidate = Raising = null;
            RaiseProgress = 0f;
            return;
        }
        Local = this;
        if (receiver == null) receiver = movement.DamageReceiver;
        if (syringe < 0f) syringe = Mathf.Clamp(Number("syringeStart", 100f), 0f, SyringeMax);
        Sections();
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        bool able = interact != null && movement.InputAvailable && !movement.IsDead && !movement.IsDowned && !movement.IsHeld && HandsFree();
        bool holding = able && interact.IsPressed();
        float dt = Time.deltaTime;

        // A dead friend first: Resurrection.
        var dead = able && raisesLeft > 0 ? Nearest(true) : null;
        if (dead != Raising) RaiseProgress = 0f;
        Raising = dead;
        if (Raising != null && holding)
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
        Candidate = able && Empty && Raising == null ? Nearest(false) : null;
        if (Candidate == null || !interact.WasPressedThisFrame()) return;
        if (Asked(Candidate)) { Begin(Candidate); return; }
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
        Begin(patient);
    }

    private void Begin(PlayerMovement patient)
    {
        offeredTo = null;
        Candidate = null;
        Treating = patient;
        treatStart = Time.time;
        treatHealed = false;
        DetenteLooks.Bandage(movement, TreatSeconds);
        TreatBegan?.Invoke(TreatSeconds);
        if (patient.TryGetComponent(out PlayerNetworkCombat network)) network.TreatmentBegun(movement);
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
        // What is left of theirs comes back over time; it fills the gauge as a heal does, by what this PC sees missing.
        UltimateCharge.AwardHelp(UltimateCharge.Help.Healed, Mathf.Max(0f, life.MaximumHealth - life.Health));
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

    /// <summary>A Détente began treating this PC's player (PlayerNetworkCombat): the offer is answered.</summary>
    internal void Begun(PlayerMovement detente)
    {
        offeredBy = null;
        DetenteLooks.Prompt(null);
        AlertHUD.Self(Texts.Format("class.detente.treating", Name(detente)));
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
            return;
        }
        if (interact == null && InputSystem.actions != null) interact = InputSystem.actions.FindAction("Player/Interact", false);
        DetenteLooks.Prompt(Texts.Format("class.detente.offer.prompt", Name(offeredBy), Mathf.CeilToInt(offeredUntil - Time.time)));
        if (interact == null || !interact.WasPressedThisFrame() || !movement.InputAvailable) return;
        var detente = offeredBy;
        offeredBy = null;
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
        syringe = Mathf.Max(0f, syringe - Dose);
        lastInject = Time.time;
        // The syringe in hand as it goes in (DetenteLooks), and in the others' sight of this player.
        DetenteLooks.Syringe(movement);
        Injected?.Invoke();
        Heal(patient, Number("injectHeal", 40f));
        if (patient != movement) AlertHUD.Self(Texts.Format("class.detente.injected", Name(patient)));
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

    /// <summary>The ultimate (every card taken, a full gauge: ClassCards.Ultimate): the syringe full and one Resurrection more.</summary>
    internal void Ultimate()
    {
        Refill(SyringeMax);
        raisesLeft++;
    }

    /// <summary>
    /// A class was picked (PlayerClass.Picked, by ClassKit): a fresh start, as a chapter's. The syringe as it starts,
    /// every Resurrection back, no Golden Hour, nobody being treated or raised.
    /// </summary>
    internal void Fresh()
    {
        syringe = -1f;
        raisesLeft = Mathf.Max(0, Mathf.RoundToInt(Number("resurrections", 1f)));
        goldenUntil = 0f;
        lastInject = -99f;
        if (Treating != null) Stop(false);
        offeredTo = null;
        Candidate = Raising = null;
        RaiseProgress = 0f;
    }

    // A friend's health is theirs on their own PC; this player's is here.
    private void Heal(PlayerMovement patient, float amount)
    {
        if (patient == movement) receiver?.Heal(amount);
        else if (patient.TryGetComponent(out PlayerNetworkCombat network))
        {
            // A friend's heal fills the gauge by the health it gives back, as this PC sees their health (their own PC
            // applies it): looking after the team is the healer's fight (UltimateCharge.AwardHelp).
            var life = patient.DamageReceiver;
            float given = life != null ? Mathf.Clamp(life.MaximumHealth - life.Health, 0f, amount) : amount;
            UltimateCharge.AwardHelp(UltimateCharge.Help.Healed, given);
            network.Heal(amount);
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

    // Any heal: Golden Hour, renewed.
    private void Golden()
    {
        goldenTotal = Mathf.Max(.1f, Number("goldenSeconds", 8f));
        goldenUntil = Time.time + goldenTotal;
    }

    // A new wave, or a rest stop reached: Resurrection is there again.
    private void Sections()
    {
        if (Time.time < nextSection) return;
        nextSection = Time.time + SectionEvery;
        int most = Mathf.Max(0, Mathf.RoundToInt(Number("resurrections", 1f)));
        if (waves == null) waves = FindAnyObjectByType<WaveManager>();
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

    // Whatever else Interact would do here goes first (an execution, a pickup, getting a downed friend up, a ladder, a
    // mission's button, a shop).
    private bool HandsFree()
    {
        var execution = movement.Execution;
        if (execution != null && (execution.IsExecuting || execution.Candidate != null)) return false;
        if (FieldPickups.Targeting) return false;
        if (TryGetComponent(out PlayerRevive revive) && revive.Candidate != null) return false;
        if (movement.Abilities != null && movement.Abilities.Fixtures.ClimbDownLadder != null) return false;
        return !CampaignRun.Offering && !TowerRun.Offering && !ShopMenu.IsOpen;
    }

    private static string Name(PlayerMovement someone)
        => someone != null && someone.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : "";

    private static float Number(string name, float fallback) => PlayerClass.Ability(Row, name, fallback);
}
