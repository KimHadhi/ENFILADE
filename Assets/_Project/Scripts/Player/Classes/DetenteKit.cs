using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stage 7, the Détente, the healer (the user's design, 2026-11-11): a syringe with a measure in it. The class key
/// (ClassKit) injects the friend under the crosshair within reach, or, with none there, this player: a dose heals at
/// once and takes its share of the measure. The measure is bought back at a shop (ShopMenu, 주사기 보충) and a Support will
/// top it up a little (Refill, stage 4). With too little left for a dose it can't heal itself at all, and a friend only
/// as Guts &amp; Blackpowder's surgeon does: Interact held beside them for a long while (the user: "아군에게 상호작용을
/// 오랜 시간 동안 해서"). The old memo's two, as the user gave them again: Golden Hour, a quicker pace for a few seconds
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

    private PlayerMovement movement;
    private PlayerDamageReceiver receiver;
    private InputAction interact;
    private WaveManager waves;
    private float syringe = -1f, goldenUntil, goldenTotal = 1f, lastInject = -99f, nextSection;
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
    /// <summary>The friend it is treating by hand now, or bringing back from the dead, and how far along (0 to 1).</summary>
    public PlayerMovement Treating { get; private set; }
    public float TreatProgress { get; private set; }
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
    public bool TakesInteract => Treating != null || Raising != null;

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
        if (!PlayerClass.Is(PlayerClass.Detente))
        {
            if (Local == this) Local = null;
            Treating = Raising = null;
            TreatProgress = RaiseProgress = 0f;
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

        // With the syringe empty, a hurt friend by hand, as a surgeon does.
        var hurt = able && Empty && Raising == null ? Nearest(false) : null;
        if (hurt != Treating) TreatProgress = 0f;
        Treating = hurt;
        if (Treating != null && holding)
        {
            TreatProgress += dt / Mathf.Max(.1f, Number("treatSeconds", 6f));
            if (TreatProgress >= 1f)
            {
                TreatProgress = 0f;
                Heal(Treating, Number("treatHeal", 50f));
                AlertHUD.Self(Texts.Format("class.detente.treated", Name(Treating)));
            }
        }
        else TreatProgress = 0f;
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

    // A friend's health is theirs on their own PC; this player's is here.
    private void Heal(PlayerMovement patient, float amount)
    {
        if (patient == movement) receiver?.Heal(amount);
        else if (patient.TryGetComponent(out PlayerNetworkCombat network)) network.Heal(amount);
        KitSound.Play(KitSound.Cue.PickHeal, .8f);
        Golden();
    }

    private void Resurrect(PlayerMovement friend)
    {
        RaiseProgress = 0f;
        raisesLeft = Mathf.Max(0, raisesLeft - 1);
        if (friend.TryGetComponent(out PlayerNetworkCombat network))
            network.Resurrect(Mathf.Clamp01(Number("resurrectHealth", 50f) / 100f));
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
        return !CampaignRun.Offering && !ShopMenu.IsOpen;
    }

    private static string Name(PlayerMovement someone)
        => someone != null && someone.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : "";

    private static float Number(string name, float fallback) => PlayerClass.Ability(Row, name, fallback);
}
