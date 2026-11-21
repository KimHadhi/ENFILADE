using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum EnemyBodySize { Small, Medium, Large }

/// <summary>Shared posture/stun contract for enemies and practice targets. AI must respect IsStunned.</summary>
[DisallowMultipleComponent]
public sealed class MeleeCombatTarget : MonoBehaviour, IShoveReceiver, ITableBound
{
    [Header("Slide Reaction")]
    [SerializeField] private EnemyBodySize bodySize = EnemyBodySize.Small;
    public EnemyBodySize BodySize => bodySize;
    public bool IsKnockedDown => knockdownRemaining > 0f || knockdownPresentation?.IsActive == true || shownDown || FellHere;
    /// <summary>
    /// On the ground, not yet getting up: what other PCs are told. A zombie getting up is still knocked down (it can't
    /// fight yet), but every PC plays its getting up at once (ZombieBody).
    /// </summary>
    public bool IsLying => knockdownRemaining > 0f || knockdownPresentation?.IsLying == true || shownDown || FellHere;
    // On a copy: this PC's own player knocked it down a moment ago and it fell here at once (KnockDownHere); the host's
    // word that it lies is still on its way, and until it must have come this PC's own fall stands.
    private bool FellHere => Time.time < fellHereUntil;
    private float fellHereUntil;
    /// <summary>On a copy: the host says it lies knocked down (apart from what this PC's own player just did to it).</summary>
    internal bool ShownDown => shownDown;
    /// <summary>Someone is holding it: a guard break or an execution under way.</summary>
    public bool IsHeld => grabber != null || executor != null;
    /// <summary>The way it was last knocked down.</summary>
    public Vector3 FallDirection { get; private set; }
    private float knockdownRemaining;
    private EnemyKnockdownPresentation knockdownPresentation;
    [SerializeField, Min(1f)] private float maximumPosture = 100f;
    [SerializeField, Min(0f)] private float postureRecovery = 15f;
    [SerializeField, Min(0f)] private float recoveryDelay = 3f;
    [SerializeField, Min(0.1f)] private float staggerRecoverySeconds = 4f;
    [SerializeField, Min(0f)] private float staggerRecoveryDelay = .35f;
    public float Posture { get; private set; }
    public float MaximumPosture => Mathf.Max(1f, maximumPosture);
    public bool IsStaggered { get; private set; }
    public bool IsStunned => IsKnockedDown || IsStaggered || stunRemaining > 0f || shoveRemaining > 0f || grabber != null || executor != null;
    public bool CanExecute => IsAvailable && IsStaggered && !IsHeld && !shownHeld && receiver != null;
    public bool IsAvailable => isActiveAndEnabled && Parts() && (vitals == null || vitals.Health > 0f)
        && (health == null || (health.IsAlive && !health.WavePaused));
    // The parts beside it that say whether it is still there to be fought, found once. IsAvailable looked all three up
    // each time it was asked (five lookups, two of them by interface), and it is asked several times a frame for every
    // enemy on every PC: its own frame, its bar, its highlight, each player's look for something to execute. Until both
    // its health and its receiver are there (a part may be put on after this one), they are looked for once a frame.
    private ICombatVitals vitals;
    private IDamageReceiver receiver;
    private EnemyHealth health;
    private bool partsFound;
    private int partsFrame = -1;
    private bool Parts()
    {
        if (partsFound || partsFrame == Time.frameCount) return true;
        partsFrame = Time.frameCount;
        vitals = GetComponent<ICombatVitals>();
        receiver = GetComponent<IDamageReceiver>();
        health = GetComponent<EnemyHealth>();
        partsFound = vitals != null && receiver != null;
        return true;
    }
    public event Action Interrupted;
    public event Action Staggered;
    /// <summary>Any target's posture broke, for presentation that watches every enemy (the break hit marker).</summary>
    public static event Action<MeleeCombatTarget> AnyStaggered;
    public float StaggerAge { get; private set; }
    /// <summary>
    /// A brute (friend feedback C2, ZombieType): barges, knockdowns, guard breaks and the player's holds don't move it,
    /// nor do bullet flinches (EnemyHitReaction); a stun (a parried blow) lasts at most SteadfastStun. Shoves and
    /// executions do work on it, at the user's word (2026-11-10: "밀치기와 처형은 먹히게 했으면"), though less than on
    /// the pack: a shove moves it SteadfastShove as far, its posture takes SteadfastPosture of what is put into it, and
    /// an execution takes ExecutionShare of its health rather than all of it (EnemyHealth).
    /// </summary>
    internal bool Steadfast { get; set; }
    /// <summary>The share of its whole health an execution takes: 1 for all it has left (ZombieType, ZombieKinds' executionShare).</summary>
    internal float ExecutionShare { get; set; } = 1f;
    private const float SteadfastStun = .5f, SteadfastShove = .4f, SteadfastPosture = .25f;
    private static readonly HashSet<MeleeCombatTarget> active = new HashSet<MeleeCombatTarget>();
    /// <summary>Every target in play. A foreach over it makes no garbage (Targets).</summary>
    public static Targets Active => new Targets(active);

    /// <summary>
    /// The targets, read only. Each player walks them every frame (what to execute, twice; whose head they stand on), and
    /// a foreach over an IEnumerable boxes the set's enumerator, one piece of garbage per walk. Its own GetEnumerator
    /// hands foreach the set's struct enumerator instead; it is still an IEnumerable for anything that wants one.
    /// </summary>
    public readonly struct Targets : IEnumerable<MeleeCombatTarget>
    {
        private readonly HashSet<MeleeCombatTarget> set;
        internal Targets(HashSet<MeleeCombatTarget> set) => this.set = set;
        public HashSet<MeleeCombatTarget>.Enumerator GetEnumerator() => set.GetEnumerator();
        IEnumerator<MeleeCombatTarget> IEnumerable<MeleeCombatTarget>.GetEnumerator() => set.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => set.GetEnumerator();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearRegistry()
    {
        active.Clear();
        AnyStaggered = null;
    }
    private float stunRemaining, recoveryRemaining, shoveRemaining, shoveDuration = ShoveDuration, bargeKnockdown;
    private Vector3 shoveVelocity;
    private const float ShoveDuration = .32f, KnockdownSoundDelay = .22f;
    // How long a guard break holds the host's enemy while it closes in (s). How long a copy trusts what this PC just did
    // to it over the host's reports, which were sent before the host heard, is EnemyNetwork.PredictionHold.
    private const float GrabHold = .4f;
    [Header("Shove Chain")]
    [SerializeField, Range(0f, 1f)] private float chainTransfer = .8f;
    [SerializeField, Min(.1f)] private float minimumChainSpeed = 1.2f;
    [SerializeField, Min(0f)] private float chainStun = .55f;
    private HashSet<MeleeCombatTarget> shoveChain;
    private PlayerMeleeCombat grabber;
    private PlayerExecution executor;
    private AudioSource bodyAudio;
    private NavMeshAgent agent;
    private EnemyNetwork network;
    private bool stoppedAgent, previousStopped, shownHeld, shownDown;

    private float appliedMaximumPosture, predictedUntil;
    // On a copy these also reach the host: the copy reacts at once, the host's enemy is the one that really stops.
    private bool IsCopy => network != null && network.IsCopy;

    private void Awake() { Posture = appliedMaximumPosture = MaximumPosture; agent = GetComponent<NavMeshAgent>(); network = GetComponent<EnemyNetwork>(); }

    // Enemies.csv may change the maximum before or after Awake; keep the current posture fraction either way.
    void ITableBound.OnTableApplied()
    {
        if (appliedMaximumPosture > 0f) Posture = Posture / appliedMaximumPosture * MaximumPosture;
        appliedMaximumPosture = MaximumPosture;
    }
    private void OnEnable() { active.Add(this); EnsureHUD(); }
    private void Start() => EnsureHUD();
    private void EnsureHUD()
    {
        // Runtime attachment also covers existing scenes/prefabs without requiring asset migration. Its health bar is the
        // one screen overlay's (EnemyHealthBarHUD), which keeps every target's bar from here on.
        if (Application.isPlaying && GetComponent<ICombatVitals>() != null) EnemyHealthBarHUD.Track(this);
        if (Application.isPlaying && GetComponent<ExecutableHighlight>() == null) gameObject.AddComponent<ExecutableHighlight>();
    }
    private void Update()
    {
        long began = ZombieCost.Begin();
        Tick(Time.deltaTime * (network != null ? network.Pace : 1f));
        ZombieCost.End(ZombieCost.Part.Stance, began);
    }
    public void Tick(float dt)
    {
        if (dt <= 0f || !IsAvailable) return;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        // A hold whose executor has moved on (an end message lost, a copy let go) is dropped, or this enemy would stay
        // frozen and no one could ever execute it. Only here: the executor's own PC already said whatever it was going to.
        if (executor != null && (!executor.isActiveAndEnabled || !executor.IsExecuting || executor.Victim != this))
        {
            executor = null;
            if (!IsStunned && !PlayerExecution.Suppresses(this)) RestoreAgent();
        }
        var suppressor = PlayerExecution.SuppressorOf(this);
        bool suppressed = suppressor != null;
        if (suppressed)
        {
            shoveRemaining = bargeKnockdown = 0f;
            StopAgent();
            if (!IsStunned) suppressor.Retreat(agent, dt);
        }
        if (executor != null) return;
        knockdownRemaining = Mathf.Max(0f, knockdownRemaining - dt);
        knockdownPresentation?.Tick(dt, knockdownRemaining > 0f);
        if (shoveRemaining > 0f)
        {
            float slice = Mathf.Min(dt, shoveRemaining);
            float strength = (shoveRemaining + shoveRemaining - slice) / (2f * shoveDuration);
            shoveRemaining -= slice;
            MoveShove(shoveVelocity * (slice * strength), shoveVelocity.magnitude * strength);
            if (shoveRemaining <= 0f)
            {
                shoveChain = null;
                // A barge throw ends on the ground (on a doll its thrower's PC runs, if a guest threw it: BargeHere).
                if (bargeKnockdown > 0f) KnockDown(shoveVelocity, bargeKnockdown, bargeRunner);
            }
        }
        stunRemaining = Mathf.Max(0f, stunRemaining - dt);
        // A copy this PC's own player threw with a barge: its slide here is over, and it falls (BargeHere).
        if (bargeFallLeft > 0f && (bargeFallLeft -= dt) <= 0f) FallAfterBarge();
        // A copy's posture comes from the host, recovery included.
        if (IsCopy) { if (IsStaggered) StaggerAge += dt; return; }
        float recoveryDt = Mathf.Max(0f, dt - recoveryRemaining);
        recoveryRemaining = Mathf.Max(0f, recoveryRemaining - dt);
        if (IsStaggered)
        {
            StaggerAge += dt;
            Posture = Mathf.MoveTowards(Posture, MaximumPosture, MaximumPosture / Mathf.Max(.1f, staggerRecoverySeconds) * recoveryDt);
            if (Posture >= MaximumPosture) IsStaggered = false;
        }
        else if (!IsStunned) Posture = Mathf.MoveTowards(Posture, MaximumPosture, postureRecovery * recoveryDt);
        if (!IsStunned && !suppressed) RestoreAgent();
    }
    /// <summary>interrupt = false wears posture down without cancelling the current attack (bullets); a break still interrupts.</summary>
    public void ApplyPostureDamage(float amount, float stun = 0f, bool interrupt = true)
    {
        // Further hits cannot restart the recovery clock or repeatedly trigger the stagger cue.
        if (!IsAvailable || IsStaggered) return;
        // One that stands firm takes a share of it (the host is told the whole, and shares it out itself).
        float whole = amount;
        if (Steadfast) amount *= SteadfastPosture;
        Posture = Mathf.Max(0f, Posture - Mathf.Max(0f, amount));
        recoveryRemaining = recoveryDelay;
        if (Posture <= 0f)
        {
            IsStaggered = true; StaggerAge = 0f; recoveryRemaining = staggerRecoveryDelay;
            PlayStaggerSound(); Staggered?.Invoke(); AnyStaggered?.Invoke(this);
        }
        // On a copy this blow is this PC's to judge: the host is told the blow and whether it broke the posture here,
        // and breaks it there too if so (ApplySentPosture). What this PC saw stands until the host's word of it is back.
        if (IsCopy) { network.SendPosture(whole, stun, interrupt, IsStaggered); predictedUntil = Time.time + network.PredictionHold; }
        if (interrupt || IsStaggered) InterruptHere(stun);
    }
    /// <summary>
    /// The host: posture damage another PC's player dealt its copy, as their PC judged it. Broke: on their screen this
    /// blow broke its posture, so it breaks here whatever the host's own count has left. The host's count hears of each
    /// blow late and recovers on its own clock, so it could be a few points short of breaking; their break then showed
    /// for a moment and was taken back.
    /// </summary>
    internal void ApplySentPosture(float amount, float stun, bool interrupt, bool broke)
    {
        if (broke && IsAvailable && !IsStaggered) Posture = 0f;
        ApplyPostureDamage(amount, stun, interrupt);
    }
    public void Interrupt(float duration) => Interrupt(duration, GameSession.Network != null && GameSession.Network.IsListening
        ? GameSession.Network.LocalClientId : 0UL);

    /// <summary>
    /// It flinches and its attack is cut short (a headshot's stun, damage adding up, a grab). By: the client whose player did
    /// it. A copy sends it to the host; the host passes it on to every other copy, which used to show only an attack cut
    /// short and no flinch (SyncAudit.md, item 7), all but the copy of the PC that did it, which showed it at once.
    /// </summary>
    internal void Interrupt(float duration, ulong by)
    {
        if (IsAvailable && IsCopy) network.SendInterrupt(duration);
        else if (IsAvailable && network != null && network.IsSpawned) network.ShareInterrupt(duration, by);
        InterruptHere(duration);
    }

    /// <summary>On a copy: the host says it flinched (Interrupt). Shown here; nothing goes back.</summary>
    internal void ShowInterrupt(float duration) => InterruptHere(duration);
    private void InterruptHere(float duration)
    {
        if (!IsAvailable) return;
        if (Steadfast) duration = Mathf.Min(duration, SteadfastStun);
        stunRemaining = Mathf.Max(stunRemaining, duration);
        StopAgent();
        GetComponent<EnemyCombatAI>().OrNull()?.CancelAttack();
        Interrupted?.Invoke();
    }
    public void KnockDown(Vector3 direction, float duration) => KnockDown(direction, duration, ZombieBody.NoRunner);
    /// <summary>
    /// Runner: on the host, the client whose player knocked it down on their own PC, which runs a zombie's fall from
    /// the blow on (ZombieBody.FallHere); the host's zombie follows that. ZombieBody.NoRunner: this PC's own knockdown.
    /// </summary>
    internal void KnockDown(Vector3 direction, float duration, ulong runner)
    {
        if (!IsAvailable || executor != null || Steadfast) return;
        if (IsCopy) { KnockDownHere(direction, duration); return; }
        FallDirection = direction;
        // Repeated knockdowns only extend the timer; the body hits the ground once, a moment after it starts to fall.
        if (!IsKnockedDown) Invoke(nameof(PlayKnockdownSound), KnockdownSoundDelay);
        knockdownRemaining = Mathf.Max(knockdownRemaining, duration);
        shoveRemaining = bargeKnockdown = 0f; shoveChain = null;
        Interrupt(duration);
        if (knockdownPresentation == null) knockdownPresentation = new EnemyKnockdownPresentation(transform);
        knockdownPresentation.Fall(direction, runner);
    }
    // On a copy, this PC's own player knocked it down. A zombie falls here at once, on a doll this PC runs, and the host
    // is told so and follows that fall (the user's rule of 2026-11-14: what a player does shows on their own screen at
    // once and the others get the result; it used to wait a round trip for the host's doll). Whatever it was doing stops
    // here too, as it will on the host: a swing it was winding up at this PC's player is off. Anything that can't fall
    // here (a plain capsule, one already down or held on the host's word) goes to the host as before.
    private void KnockDownHere(Vector3 direction, float duration)
    {
        bool runs = false;
        if (EnemyNetwork.KnockdownAtOnce && !IsKnockedDown && !shownHeld && TryGetComponent(out ZombieBody zombie) && zombie.Wears)
        {
            InterruptHere(duration);
            runs = zombie.FallHere(direction);
        }
        if (runs)
        {
            FallDirection = direction;
            fellHereUntil = Time.time + network.PredictionHold;
            Invoke(nameof(PlayKnockdownSound), KnockdownSoundDelay);
        }
        network.SendKnockDown(direction, duration, runs);
    }
    /// <summary>
    /// A dash push: thrown along the ground at this velocity, slowing to a stop over throwTime and shoving whoever it
    /// runs into like a shove does, then knocked down for knockdownTime. Without a navmesh agent it falls at once.
    /// </summary>
    public void Barge(Vector3 velocity, float throwTime, float knockdownTime) => Barge(velocity, throwTime, knockdownTime, ZombieBody.NoRunner);
    /// <summary>
    /// Runner: on the host, the client whose player threw it on their own PC, which runs a zombie's fall at the end of
    /// the slide (BargeHere); the host's zombie follows that fall. ZombieBody.NoRunner: this PC's own barge.
    /// </summary>
    internal void Barge(Vector3 velocity, float throwTime, float knockdownTime, ulong runner)
    {
        if (!IsAvailable || executor != null || Steadfast) return;
        if (IsCopy) { BargeHere(velocity, throwTime, knockdownTime); return; }
        if (!StartShove(velocity, new HashSet<MeleeCombatTarget> { this }, Mathf.Max(.05f, throwTime)))
        { KnockDown(velocity, knockdownTime, runner); return; }
        bargeKnockdown = Mathf.Max(.1f, knockdownTime);
        bargeRunner = runner;
        Interrupt(shoveDuration);
    }
    // The host: whose PC runs the fall a barge under way ends in (a client id, or ZombieBody.NoRunner for this PC).
    private ulong bargeRunner = ZombieBody.NoRunner;
    // On a copy this PC's own player threw: how long until its slide ends here (s), which way it then falls, for how long.
    private float bargeFallLeft, bargeFallFor;
    private Vector3 bargeFall;

    // On a copy, this PC's own player threw it with a barge. As the host's zombie will, it stops what it was doing and
    // slides here at once (EnemyNetwork.SlideHere), and where the slide ends it falls on a doll this PC runs
    // (FallAfterBarge); the host is told so with the barge, and its zombie follows that fall at the end of its own slide.
    // It used to stand until the host's slide and fall came back a round trip later. Anything that can't fall here goes
    // to the host as before.
    private void BargeHere(Vector3 velocity, float throwTime, float knockdownTime)
    {
        bool here = EnemyNetwork.SlideAtOnce && EnemyNetwork.KnockdownAtOnce && !IsKnockedDown && !shownHeld
            && TryGetComponent(out ZombieBody zombie) && zombie.Wears;
        if (here)
        {
            float slide = Mathf.Max(.05f, throwTime);
            InterruptHere(slide);
            network.SlideHere(velocity, slide);
            bargeFall = velocity;
            bargeFallLeft = slide;
            bargeFallFor = Mathf.Max(.1f, knockdownTime);
        }
        network.SendBarge(velocity, throwTime, knockdownTime, here);
    }

    // The barge's slide is over on this PC: it falls here. Should it not be able to after all (it went down or was
    // taken meanwhile), the host is told to run the fall itself, or its zombie would wait for a doll that never comes.
    private void FallAfterBarge()
    {
        bool runs = false;
        if (IsAvailable && !IsKnockedDown && !shownHeld && executor == null && TryGetComponent(out ZombieBody zombie))
        {
            InterruptHere(bargeFallFor);
            runs = zombie.FallHere(bargeFall);
        }
        if (!runs) { network.SendKnockDown(bargeFall, bargeFallFor, false); return; }
        FallDirection = bargeFall;
        fellHereUntil = Time.time + network.PredictionHold;
        Invoke(nameof(PlayKnockdownSound), KnockdownSoundDelay);
    }
    public bool TryReserve(PlayerMeleeCombat owner)
    {
        if (!IsAvailable || owner == null || grabber != null || executor != null || Steadfast) return false;
        knockdownPresentation?.Reset(); knockdownRemaining = 0f;
        grabber = owner; bargeKnockdown = 0f; StopAgent(); GetComponent<EnemyCombatAI>().OrNull()?.CancelAttack(); Interrupted?.Invoke();
        if (IsCopy) network.SendInterrupt(GrabHold);
        return true;
    }
    public void Release(PlayerMeleeCombat owner)
    {
        if (grabber != owner) return;
        grabber = null;
        if (!IsStunned && !PlayerExecution.Suppresses(this)) RestoreAgent();
    }
    /// <summary>
    /// Whether this executor may execute it or take it down from where they stand, whatever the distance
    /// (ExecutionRuleFor): broken, or unaware of anyone, and some rule offers it from that side.
    /// </summary>
    public bool CanBeExecutedBy(Component executor) => ExecutionRuleFor(executor) != null;

    /// <summary>It knows of nobody and nothing has hold of it: a takedown's zombie, should a rule offer it from where the player stands.</summary>
    public bool CanBeTakenDown => IsAvailable && !IsHeld && !shownHeld && !IsKnockedDown && receiver != null && Mind != null && Mind.Unaware;

    /// <summary>
    /// The rule (Tables/ExecutionRules.csv) by which this executor may execute it or take it down now, or null: what it
    /// is (broken, unaware of anyone), the posture its body is in and where the executor stands round it pick the rule,
    /// which names the pair to play. <paramref name="distance"/>: from the executor's eyes to its body (m), against the
    /// rule's own reach; below 0 asks about the place alone. <paramref name="lunge"/>: with the attack button and the
    /// melee slot out, which reaches the rule's lungeReach. Asked on the executor's own PC, which decides
    /// (PlayerExecution); nobody judges it again.
    /// </summary>
    public ExecutionRule ExecutionRuleFor(Component executor, float distance = -1f, bool lunge = false)
    {
        if (executor == null) return null;
        bool broken = CanExecute, unaware = CanBeTakenDown;
        if (!broken && !unaware) return null;
        string posture = Mind != null ? Mind.BodyPosture : ExecutionRules.Stand;
        float bearing = ExecutionRules.Bearing(transform.forward, executor.transform.position - transform.position);
        return ExecutionRules.Pick(posture, unaware, broken, bearing, distance, lunge);
    }

    // Its mind, found once it is there: every player asks every enemy this twice a frame (PlayerExecution.FindCandidate),
    // and it was a component lookup each time. Something without one (a practice target) looks again once a frame at most.
    private ZombieMind mind;
    private int mindFrame = -1;
    private ZombieMind Mind
    {
        get
        {
            if (mind == null && mindFrame != Time.frameCount)
            {
                mindFrame = Time.frameCount;
                TryGetComponent(out mind);
            }
            return mind;
        }
    }

    public bool TryReserveExecution(PlayerExecution owner)
    {
        if (owner == null || !CanBeExecutedBy(owner)) return false;
        HoldFor(owner);
        // The execution starts at once; the host says no only if someone else got there first.
        if (IsCopy) network.AskExecution();
        return true;
    }
    /// <summary>
    /// The host: another PC's player began an execution or a takedown on their own copy of this enemy, where their PC
    /// judged it could be done. It is held for them as asked and no rule is run again here: the host's own posture, mood
    /// and positions are a moment off theirs, and it used to cancel executions their screen had already started (the
    /// user's rule of 2026-11-14: the acting player's PC decides, the host applies and never judges again). Refused only
    /// when it is gone here already, or when someone else holds it: a true tie, and the first to ask has it.
    /// </summary>
    internal bool HoldForExecution(PlayerExecution owner)
    {
        if (owner == null || !IsAvailable) return false;
        if (executor == owner) return true;
        if (executor != null || grabber != null) return false;
        HoldFor(owner);
        return true;
    }
    // Held for this executor from now: whatever it was doing stops, and it stands if it lay.
    private void HoldFor(PlayerExecution owner)
    {
        knockdownPresentation?.Reset(); knockdownRemaining = 0f;
        executor = owner; shoveRemaining = bargeKnockdown = 0f; StopAgent();
        GetComponent<EnemyCombatAI>().OrNull()?.CancelAttack(); Interrupted?.Invoke();
    }
    public void ReleaseExecution(PlayerExecution owner)
    {
        if (executor != owner) return;
        executor = null;
        if (IsCopy) network.EndExecution();
        if (!IsStunned && !PlayerExecution.Suppresses(this)) RestoreAgent();
    }
    /// <summary>On a copy: someone else got to this execution first.</summary>
    internal void DenyExecution()
    {
        if (executor != null) executor.Cancel();
    }
    /// <summary>
    /// On a copy: how the host's enemy stands. What this PC just did to it stays for a moment, because a report sent
    /// before the host heard about it would undo it.
    /// </summary>
    internal void ShowCondition(float posture, bool staggered, bool held, bool down, Vector3 fall)
    {
        shownHeld = held;
        if (down != shownDown)
        {
            shownDown = down;
            // Not when this PC's own player just knocked it down: its fall was heard here as it began (KnockDownHere).
            if (down && !FellHere) Invoke(nameof(PlayKnockdownSound), KnockdownSoundDelay);
            // A zombie's body falls on a doll that follows the host's (ZombieBody); only a plain capsule is tipped over.
            if (!TryGetComponent(out ZombieBody zombie) || !zombie.Wears) EnemyParryRecoil.For(this).OrNull()?.Lie(fall, down);
        }
        if (Time.time < predictedUntil) return;
        Posture = posture;
        if (staggered == IsStaggered) return;
        IsStaggered = staggered;
        if (!staggered) return;
        StaggerAge = 0f;
        PlayStaggerSound();
        Staggered?.Invoke();
        AnyStaggered?.Invoke(this);
    }
    private void PlayStaggerSound()
    {
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null) PlayBodySound(settings.staggerSound, settings.staggerSoundVolume);
    }
    private void PlayKnockdownSound()
    {
        var settings = CombatFeedbackSettings.Instance;
        var clips = settings != null ? settings.knockdownSounds : null;
        if (clips == null || clips.Length == 0) return;
        PlayBodySound(clips[UnityEngine.Random.Range(0, clips.Length)], settings.knockdownVolume);
    }
    private void PlayBodySound(AudioClip clip, float volume)
    {
        if (!Application.isPlaying || clip == null) return;
        if (bodyAudio == null)
        {
            bodyAudio = gameObject.AddComponent<AudioSource>(); bodyAudio.playOnAwake = false;
            bodyAudio.spatialBlend = .85f; bodyAudio.minDistance = 2f; bodyAudio.maxDistance = 22f;
            bodyAudio.rolloffMode = AudioRolloffMode.Linear;
            GameAudio.Bind(bodyAudio, AudioChannel.Combat);
        }
        bodyAudio.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
    public void ResetPosture()
    {
        knockdownRemaining = 0f; knockdownPresentation?.Reset();
        Posture = MaximumPosture; IsStaggered = false; StaggerAge = 0f;
        stunRemaining = recoveryRemaining = shoveRemaining = bargeKnockdown = 0f;
        shoveChain = null;
        if (executor != null) executor.Cancel();
        grabber = null; RestoreAgent();
    }
    public void ReceiveShove(Vector3 impulse, Vector3 point)
    {
        if (IsCopy)
        {
            network.SendShove(impulse, point);
            // This PC's own player shoved it: it slides here at once as the host's zombie will (the same speed and time),
            // ahead of the host's word of where it went (EnemyNetwork.SlideHere). One lying, held or gone doesn't slide
            // on the host either.
            if (IsAvailable && !IsKnockedDown && !shownHeld && executor == null)
                network.SlideHere(Steadfast ? impulse * SteadfastShove : impulse, ShoveDuration);
            return;
        }
        ReceiveShove(impulse, point, new HashSet<MeleeCombatTarget> { this });
    }
    private void ReceiveShove(Vector3 impulse, Vector3 point, HashSet<MeleeCombatTarget> chain)
    {
        if (!IsAvailable || executor != null || StartShove(Steadfast ? impulse * SteadfastShove : impulse, chain, ShoveDuration)) return;
        var body = GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
    }
    // Slides the agent along the navmesh at this velocity, slowing to a stop over duration; false without an agent.
    private bool StartShove(Vector3 velocity, HashSet<MeleeCombatTarget> chain, float duration)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
        shoveVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        shoveRemaining = shoveDuration = duration; shoveChain = chain; StopAgent();
        return true;
    }
    private void MoveShove(Vector3 displacement, float speed)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || displacement.sqrMagnitude < .000001f) return;
        float distance = displacement.magnitude; Vector3 direction = displacement / distance;
        if (NavMesh.Raycast(agent.nextPosition, agent.nextPosition + displacement, out var edge, agent.areaMask))
            distance = Mathf.Min(distance, Mathf.Max(0f, Vector3.Distance(agent.nextPosition, edge.position) - .025f));
        float radius = Mathf.Max(.1f, agent.radius);
        Vector3 feet = agent.transform.position - Vector3.up * agent.baseOffset;
        Vector3 bottom = feet + Vector3.up * (radius + .05f);
        Vector3 top = feet + Vector3.up * Mathf.Max(radius + .05f, agent.height - radius);
        var hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance + .025f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
            if (!hit.transform.IsChildOf(transform)) distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .025f));
        // Only actual contacts at the nearest obstruction can propagate a shove, never actors behind cover.
        foreach (var hit in hits)
            if (hit.distance <= distance + .026f)
                TransferShove(hit.collider, direction, speed);
        // Casts can miss colliders already touching/overlapping the capsule in a packed crowd.
        foreach (var collider in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
            TransferShove(collider, direction, speed);
        agent.Move(direction * distance);
    }
    private void TransferShove(Collider collider, Vector3 direction, float speed)
    {
        var other = collider.GetComponentInParent<MeleeCombatTarget>();
        float transferredSpeed = speed * chainTransfer;
        if (other == null || other == this || !other.IsAvailable || other.grabber != null || other.executor != null ||
            PlayerExecution.Suppresses(other) || transferredSpeed < minimumChainSpeed || shoveChain == null || shoveChain.Contains(other)) return;
        Vector3 toward = Vector3.ProjectOnPlane(other.transform.position - transform.position, Vector3.up);
        if (Vector3.Dot(toward, direction) <= .001f) return;
        // Prevent overlap fallback from relaying force across a thin wall.
        Vector3 center = collider.bounds.center;
        Vector3 source = new Vector3(transform.position.x, center.y, transform.position.z);
        foreach (var obstruction in Physics.RaycastAll(source, center - source, Vector3.Distance(source, center), ~0, QueryTriggerInteraction.Ignore))
            if (!obstruction.transform.IsChildOf(transform) && !obstruction.transform.IsChildOf(other.transform)) return;
        shoveChain.Add(other);
        // Existing stronger shoves keep their momentum and chain ownership.
        if (other.shoveRemaining > 0f && other.shoveVelocity.magnitude * other.shoveRemaining / other.shoveDuration >= transferredSpeed) return;
        other.ReceiveShove(direction * transferredSpeed, center, shoveChain);
        other.Interrupt(Mathf.Max(ShoveDuration, chainStun));
    }
    private void StopAgent()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (!stoppedAgent) { previousStopped = agent.isStopped; stoppedAgent = true; }
        agent.isStopped = true; agent.velocity = Vector3.zero;
    }
    private void RestoreAgent()
    {
        if (!stoppedAgent) return;
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = previousStopped;
        stoppedAgent = false;
    }
    private void OnDisable()
    { active.Remove(this); CancelInvoke(nameof(PlayKnockdownSound)); if (executor != null) executor.Cancel(); executor = null; grabber = null; shoveChain = null; stunRemaining = shoveRemaining = knockdownRemaining = bargeKnockdown = 0f; knockdownPresentation?.Reset(); RestoreAgent(); }
}
