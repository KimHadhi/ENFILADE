using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One player's deathblow transaction. F executes a broken enemy; with the melee slot out the attack button does too, from
/// further off, dashing in first, as Sekiro's deathblow sits on the attack button. A zombie that hasn't noticed anyone can
/// be executed the same way, unbroken: a stealth kill (ZombieMind, stage 4C-2). Whom, from which side of them and from how
/// near is the rules' to say (Tables/ExecutionRules.csv through MeleeCombatTarget.ExecutionRuleFor): each rule has its own
/// cone and reach, so a zombie's back is taken from further off than its front, and the rule this player's PC found is
/// the one the execution then goes by (Rule: it names the pair, ExecutionPairPlayer). Either way the player closes in to
/// dashStopDistance first, and their class's knife does the deed (ExecutionKnife).
/// Animation can subscribe to Started/Impact/Ended.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Execution)]
public sealed class PlayerExecution : MonoBehaviour
{
    [SerializeField, Range(5f, 75f)] private float aimAngle = 38f;
    [SerializeField, Min(.1f)] private float duration = .8f;
    [SerializeField, Min(0f)] private float impactTime = .35f;
    [SerializeField, Min(0f)] private float clearance = 2.6f;
    [SerializeField, Min(0f)] private float retreatSpeed = 2.8f;
    [Header("The dash in (F, and the attack button's deathblow from a rule's lungeReach)")]
    [SerializeField, Min(1f), Tooltip("Speed of the dash in (m/s).")]
    private float dashSpeed = 14f;
    [SerializeField, Min(.05f), Tooltip("The dash stops closing in after this long (s), and the execution goes on from there.")]
    private float dashTimeout = .35f;
    [SerializeField, Min(.1f), Tooltip("The dash ends this far from the enemy's body (m), close enough to take hold.")]
    private float dashStopDistance = .65f;
    // The host's copy of someone executing on their own PC waits this much past the execution for word that it ended.
    private const float RemoteGrace = 2f;
    [Header("Execution Rewards")]
    [SerializeField, Min(0f)] private float healthRestored = 10f;
    [SerializeField, Range(0f, 1f), Tooltip("Reserve ammunition restored per ranged weapon, as a fraction of its magazine size.")]
    private float ammoRestoredFraction = .2f;
    private PlayerMovement player;
    private Transform aim;
    private PlayerWeaponInput buttons;
    private MeleeCombatTarget victim;
    private ExecutionRule rule;
    private float elapsed, approachTime;
    private bool impacted, approaching;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    /// <summary>
    /// F's execution or takedown, as the interact key's one owner knows it (InteractKey): that it comes before a pickup,
    /// a friend to get up and a ping is its row's place in Tables/Player/InteractKey.csv, not a rule each of them keeps.
    /// </summary>
    public const string OfferId = "takedown";
    public bool IsExecuting { get; private set; }
    /// <summary>The enemy F would execute now.</summary>
    public MeleeCombatTarget Candidate { get; private set; }
    /// <summary>The enemy a left click would execute now (the melee slot out), dashing in if it is beyond reach; null otherwise.</summary>
    public MeleeCombatTarget DeathblowCandidate { get; private set; }
    /// <summary>
    /// The rule the execution under way goes by (Tables/ExecutionRules.csv), as this player's own PC found it when it
    /// began: it names the pair to play (ExecutionPairPlayer). Null with none under way, and on a copy of someone else's
    /// (their PC sends what it plays).
    /// </summary>
    public ExecutionRule Rule => IsExecuting ? rule : null;
    /// <summary>Dashing in to the enemy before the execution proper starts.</summary>
    public bool IsDashing => IsExecuting && approaching;
    public MeleeCombatTarget Victim => victim;
    /// <summary>How long after the execution proper begins (any dash in over) its blow lands (s): the knife's strike meets it (ExecutionKnife).</summary>
    public float ImpactTime => Now.Kill;
    /// <summary>
    /// How long after the execution proper begins the player is let go (s): the execution ends there (Ended), whatever is
    /// left of its pair's clips easing out behind a player who walks on (ExecutionPairPlayer). A rule that keeps the player
    /// to the pair's end makes the two the same moment.
    /// </summary>
    public float Duration => Now.Free;
    /// <summary>How fast the execution under way plays against its clips' own speed (its rule's killIn, a Ghost's 빠른 칼): the class knife's stroke keeps to it (ExecutionKnife).</summary>
    public float Pace => Now.Pace;
    /// <summary>The execution cinematic films the one under way, by its rule (ThirdPersonCamera): a takedown over in a moment is no shot.</summary>
    public bool Filmed => rule == null || rule.Film;
    // When this execution's moments fall (ExecutionTiming, real s from where the execution proper begins). Own: by its
    // rule over the knife's own stab (Player.csv's impactTime and duration), set as it begins. Paired: by its rule over
    // the pair that plays (ExecutionPairPlayer, Tables/Executions.csv), which stands in until the pair lets go; its blow
    // lands on the bone the knife meets and sends the body the pair's way. With neither (a copy before its owner's word)
    // the knife's own stab as the table has it.
    private ExecutionTiming own, paired;
    private bool hasOwn, hasPaired;
    private ExecutionTiming Now => hasPaired ? paired : hasOwn ? own
        : new ExecutionTiming(1f, Mathf.Min(impactTime, duration), duration, duration);
    private Vector3? strikePoint, strikeAlong;
    private static readonly List<PlayerExecution> executing = new List<PlayerExecution>();
    public event Action<MeleeCombatTarget> Started;
    public event Action<MeleeCombatTarget> Impact;
    public event Action Ended;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearActive() => executing.Clear();
    /// <summary>The running execution that holds this enemy back: one by the player it is after, in its scene.</summary>
    public static PlayerExecution SuppressorOf(Component enemy)
    {
        if (executing.Count == 0 || enemy == null) return null;
        // A zombie that hasn't noticed anyone keeps to itself, even beside a stealth kill (ZombieMind).
        if (enemy.TryGetComponent(out ZombieMind mind) && !mind.IsAwake) return null;
        var after = enemy.TryGetComponent(out EnemyTargeting targeting) ? targeting.Player : null;
        foreach (var execution in executing)
            if (enemy.gameObject.scene == execution.gameObject.scene && (after == null || after == execution.player)) return execution;
        return null;
    }
    public static bool Suppresses(Component enemy) => SuppressorOf(enemy) != null;

    private void OnEnable()
    {
        player = GetComponent<PlayerMovement>();
        var camera = GetComponentInChildren<Camera>(); aim = camera != null ? PlayerView.EyesOf(this) : transform;
        if (Application.isPlaying && player.OrNull()?.IsLocal != false && GetComponent<ExecutionHUD>() == null) gameObject.AddComponent<ExecutionHUD>();
        // Paired executions for what the zombie is doing, on every PC's copy of the player too (ExecutionPairPlayer).
        if (Application.isPlaying) ExecutionPairPlayer.For(gameObject);
        // The class's knife through every execution, on every PC's copy of the player too (ExecutionKnife).
        if (Application.isPlaying) ExecutionKnife.On(gameObject);
    }
    private void Start()
    {
        buttons = GetComponent<PlayerWeaponInput>();
    }
    private bool CanOffer => isActiveAndEnabled && player != null && player.IsInitialized && player.InputAvailable
        && !player.CombatInputLocked && !player.IsHeld && !player.IsParkour && !player.IsSliding && !player.IsDiving && !player.IsDowned
        && player.CurrentState != PlayerMovement.MovementState.StepDodge
        && player.CurrentState != PlayerMovement.MovementState.DashPush
        && !player.IsLandingRecovery && !IsExecuting;
    /// <summary>
    /// Whether F could begin an execution or a takedown now, as far as this player's own state goes (on their feet, hands
    /// and keys their own, none under way). A Ghost's chain asks before it moves the Ghost on (GhostCards.Go), so it never
    /// takes them behind a zombie they could not then take down.
    /// </summary>
    public bool CanBegin => CanOffer;
    /// <summary>
    /// On the host's copy of someone executing on their own PC: the enemy is held for them, and the others back away from
    /// this body as they would from the host's own.
    /// </summary>
    internal void ShowExecuting(MeleeCombatTarget target)
    {
        // An end that never arrived must not leave the last enemy held, or this player listed twice.
        if (victim != null && victim != target) victim.ReleaseExecution(this);
        victim = target; rule = null; IsExecuting = true; elapsed = 0f; impacted = false;
        executing.Remove(this); executing.Add(this);
        foreach (var enemy in MeleeCombatTarget.Active)
            if (enemy != null && Suppresses(enemy)) enemy.GetComponent<EnemyCombatAI>().OrNull()?.CancelAttack();
    }
    private void Update()
    {
        if (player != null && player.IsNetworkDriven)
        {
            // Their PC says when it ends; should that word be lost, the hold lets go on its own.
            if (IsExecuting && (elapsed += Time.deltaTime) > Duration + RemoteGrace) Cancel();
            return;
        }
        if (IsExecuting)
        {
            // An execution under way ends for reasons of the game alone, and Tick knows them all: it is done, its victim
            // is gone before the blow, this player died. Who has the keys is not one of them. This asked
            // player.InputAvailable every frame until 2026-11-18, which was false as soon as the chat or the Esc menu
            // opened, so typing a word or opening the settings threw the execution away mid-stroke (the user: "처형
            // 애니메이션 실행 도중 설정(ESC) 메뉴를 열거나 채팅 입력창을 활성화할 때 처형이 중단·캔슬되는 현상"). A pause only
            // stops its clock.
            if (Time.timeScale <= 0f) return;
            Tick(Time.deltaTime); return;
        }
        // Whether the body's keys are the player's is CanOffer's question (PlayerMovement.InputAvailable); the interact
        // action itself is not held here any more.
        Candidate = FindCandidate(false);
        // F is shared: the takedown says it is on offer, and begins only when the key's one owner gives it the press
        // (InteractKey). It read the key for itself until 2026-11-18, and everything else on F asked Candidate and
        // stepped aside, each by a list of its own.
        if (Candidate != null) InteractKey.Offer(OfferId, target: Candidate);
        if (Candidate != null && InteractKey.Pressed(OfferId)) { TryBegin(Candidate); return; }
        // Before the fists read the same click (PlayerMeleeCombat runs after this): a broken enemy takes the deathblow
        // instead of a punch. A counter, which reads it earlier still, has already claimed the click if it was open.
        var fists = player != null ? player.MeleeCombat : null;
        DeathblowCandidate = fists != null && fists.IsActive ? FindCandidate(true) : null;
        if (DeathblowCandidate != null && buttons != null && buttons.TryReadMeleeInput(out var frame) && frame.AttackPressed)
            TryBegin(DeathblowCandidate, true);
    }
    // lunge: the attack button with the melee slot out, from as far as each rule's lungeReach.
    private MeleeCombatTarget FindCandidate(bool lunge)
    {
        if (!CanOffer || aim == null) return null;
        MeleeCombatTarget best = null; float bestScore = float.NegativeInfinity;
        foreach (var target in MeleeCombatTarget.Active)
        {
            if (Offer(target, lunge, out Vector3 point) == null) continue;
            Vector3 direction = point - aim.position;
            float score = Vector3.Dot(aim.forward, direction.normalized) * 4f - direction.magnitude * .1f;
            if (score > bestScore) { best = target; bestScore = score; }
        }
        return best;
    }
    // The rule that lets this player execute that enemy or take it down from where they stand now, or null: it is broken
    // or knows of nobody, under the crosshair (aimAngle), in plain sight, and some rule of the table offers it in its
    // posture from this side of it within that rule's own reach (MeleeCombatTarget.ExecutionRuleFor), measured from the
    // eyes to the nearest of its body.
    private ExecutionRule Offer(MeleeCombatTarget target, bool lunge, out Vector3 point)
    {
        point = default;
        if (target == null || target.gameObject.scene != gameObject.scene || target.transform.IsChildOf(transform)) return null;
        // The cheap questions first: every player asks every enemy this twice a frame.
        if (!target.CanExecute && !target.CanBeTakenDown) return null;
        var body = target.GetComponentInChildren<Collider>();
        if (body == null || !body.enabled || body.isTrigger) return null;
        point = body.bounds.center;
        Vector3 delta = point - aim.position;
        if (Vector3.Dot(aim.forward, delta.normalized) < Mathf.Cos(aimAngle * Mathf.Deg2Rad)) return null;
        var offered = target.ExecutionRuleFor(this, Vector3.Distance(aim.position, body.ClosestPoint(aim.position)), lunge);
        if (offered == null) return null;
        int count = Physics.RaycastNonAlloc(aim.position, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        var hits = count == sightHits.Length ? Physics.RaycastAll(aim.position, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) : sightHits;
        if (hits != sightHits) count = hits.Length;
        for (int i = 0; i < count; i++)
        {
            Transform hit = hits[i].collider.transform;
            if (!hit.IsChildOf(transform) && !hit.IsChildOf(target.transform)) return null;
        }
        return offered;
    }
    public bool TryBegin(MeleeCombatTarget target) => TryBegin(target, false);
    /// <summary>
    /// Executes target, or takes it down, if a rule offers it from where the player stands (<paramref name="lunge"/>: by
    /// the attack button with the melee slot out, from as far as the rule's lungeReach); the player dashes in to it first.
    /// </summary>
    public bool TryBegin(MeleeCombatTarget target, bool lunge)
    {
        if (!CanOffer || aim == null) return false;
        var offered = Offer(target, lunge, out _);
        if (offered == null || !target.TryReserveExecution(this)) return false;
        rule = offered;
        // By the rule, over the knife's own stab; a pair that plays takes over with its own (Retime) once the dash is in.
        ForgetPair();
        own = ExecutionRules.Timing(rule, Mathf.Min(impactTime, duration), duration);
        hasOwn = true;
        victim = target; elapsed = approachTime = 0f; impacted = false; IsExecuting = true; Candidate = DeathblowCandidate = null;
        // F closes in too (2026-11-13): the class knife is stabbed in by hand (ExecutionKnife), and from F's 2.1 m reach the
        // arm met nothing. The dash stops at dashStopDistance either way.
        approaching = Gap(target, out _) > dashStopDistance;
        executing.Remove(this); executing.Add(this);
        player.PerfectDefense.OrNull()?.Cancel();
        // Pressed during the parry's hit stop: the deathblow goes now, not once time lets go.
        var timeEffects = GetComponent<CombatTimeEffects>();
        var parry = GetComponent<ParryFeedback>();
        if (timeEffects != null && parry != null) timeEffects.Release(parry);
        player.MeleeCombat.OrNull()?.CancelAction();
        player.HorizontalVelocity = Vector3.zero;
        CombatCueAudio.Play(gameObject, CombatCue.Grab);
        GetComponent<WeaponController>().OrNull()?.Runtime?.Interrupt();
        GetComponent<PlayerWeaponInput>().OrNull()?.RequireTriggerRelease();
        // Cancel windups immediately; newly spawned enemies also consult Suppresses in their own update.
        foreach (var enemy in MeleeCombatTarget.Active)
            if (enemy != null && Suppresses(enemy)) enemy.GetComponent<EnemyCombatAI>().OrNull()?.CancelAttack();
        Started?.Invoke(target); return true;
    }
    public void Tick(float dt)
    {
        if (!IsExecuting || dt <= 0f) return;
        if (player == null || !player.isActiveAndEnabled || !player.DamageReceiver.IsAlive) { Cancel(); return; }
        if (!impacted && (victim == null || !victim.IsAvailable)) { Cancel(); return; }
        if (approaching)
        {
            // Into the enemy, along the ground; the capsule's own collisions stop it at anything in the way, and then
            // the timeout lets the execution go on from where it stands.
            approachTime += dt;
            float gap = Gap(victim, out Vector3 toward);
            if (gap > dashStopDistance && approachTime < dashTimeout)
            {
                player.Motor.AddDisplacement(toward * Mathf.Min(gap - dashStopDistance, dashSpeed * dt));
                return;
            }
            approaching = false;
        }
        elapsed += dt;
        if (!impacted && elapsed >= ImpactTime)
        {
            impacted = true;
            var vitals = victim.GetComponent<ICombatVitals>();
            var receiver = victim.GetComponent<IDamageReceiver>();
            if (vitals == null || receiver == null) { Cancel(); return; }
            // A death callback may disable the enemy synchronously. Release its reservation first.
            victim.ReleaseExecution(this);
            var result = receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), vitals.Health, DamageKind.Execution,
                strikePoint ?? victim.transform.position, strikeAlong ?? aim.forward, gameObject));
            if (result == DamageResult.Damaged && vitals.Health <= 0f) GrantRewards();
            Impact?.Invoke(victim);
        }
        // The player is let go: the execution is over for them, and for everything that asks IsExecuting (their
        // hands and view, the enemies held back, their being unhurt), though its pair may still be easing out.
        if (elapsed >= Duration) Cancel();
    }
    private float Gap(MeleeCombatTarget target, out Vector3 toward)
        => BodyGap.Across(player, target != null ? target.GetComponentInChildren<Collider>() : null, out toward);
    private void GrantRewards()
    {
        player.DamageReceiver.Heal(healthRestored + (player.IsLocal ? PlayerPerks.ExecutionHealth : 0f));
        var slots = GetComponent<PlayerWeaponSlots>();
        if (slots != null && slots.isActiveAndEnabled)
        {
            for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) RestoreAmmo(slots.GetRuntime(i));
        }
        else RestoreAmmo(GetComponent<WeaponController>().OrNull()?.Runtime);
    }
    private void RestoreAmmo(WeaponRuntime weapon)
    {
        if (weapon == null || weapon.Data.Category != WeaponCategory.Ranged ||
            float.IsNaN(ammoRestoredFraction) || float.IsInfinity(ammoRestoredFraction)) return;
        // A Schießhund's Desperado in hand is loaded by bounties alone.
        if (TryGetComponent(out PlayerWeaponSlots slots) && slots.KeepsOwnRounds(slots.SelectedSlot)) return;
        weapon.AddReserve(Mathf.CeilToInt(weapon.Data.SupplyRounds * Mathf.Clamp01(ammoRestoredFraction)));
    }
    /// <summary>
    /// A paired execution runs this one (ExecutionPairPlayer): its blow lands <paramref name="kill"/> after the
    /// execution proper began and the player is let go at <paramref name="free"/>, the pair playing <paramref name="speed"/>
    /// times its clips' own, instead of the knife's own stab. On every PC, until ForgetPair: where the execution runs it
    /// times the blow and the end; on a copy that runs none it still times the class knife's stab, which reads
    /// ImpactTime, Duration and Pace (ExecutionKnife), so it strikes and puts the knife away with the pair there too.
    /// </summary>
    internal void Retime(float kill, float free, float speed)
    {
        kill = Mathf.Max(0f, kill);
        free = Mathf.Max(kill, free);
        paired = new ExecutionTiming(Mathf.Max(.1f, speed), kill, free, free);
        hasPaired = true;
    }
    /// <summary>Where the pair's blow meets the victim this frame and which way it sends the body (ExecutionPairPlayer).</summary>
    internal void Strike(Vector3 point, Vector3 along)
    {
        strikePoint = point;
        strikeAlong = along.sqrMagnitude > 1e-6f ? along.normalized : (Vector3?)null;
    }
    /// <summary>The pair's timing and blow no longer stand in (the execution's end, and a pair's end on a copy that runs none).</summary>
    internal void ForgetPair()
    {
        strikePoint = strikeAlong = null;
        hasPaired = false;
    }
    public void Cancel()
    {
        ForgetPair();
        hasOwn = false;
        bool wasExecuting = IsExecuting; IsExecuting = approaching = false; Candidate = DeathblowCandidate = null;
        while (executing.Remove(this)) { }
        if (victim != null) victim.ReleaseExecution(this);
        victim = null;
        if (!wasExecuting) return;
        GetComponent<PlayerWeaponInput>().OrNull()?.RequireTriggerRelease();
        Ended?.Invoke();
    }
    public void Retreat(NavMeshAgent agent, float dt)
    {
        if (!IsExecuting || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || dt <= 0f) return;
        Vector3 away = Vector3.ProjectOnPlane(agent.transform.position - transform.position, Vector3.up);
        float distance = away.magnitude;
        if (distance >= clearance) return;
        away = distance > .01f ? away / distance : -transform.forward;
        float step = Mathf.Min(retreatSpeed * dt, clearance - distance);
        Vector3 destination = agent.nextPosition + away * step;
        if (NavMesh.Raycast(agent.nextPosition, destination, out var edge, agent.areaMask))
            step = Mathf.Min(step, Mathf.Max(0f, Vector3.Distance(agent.nextPosition, edge.position) - .05f));
        // Also respect physical cover that is absent from the baked navigation mesh.
        foreach (var hit in Physics.SphereCastAll(agent.transform.position + Vector3.up * .8f, Mathf.Max(.1f, agent.radius), away,
            step + .05f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(agent.transform)) step = Mathf.Min(step, Mathf.Max(0f, hit.distance - .05f));
        agent.Move(away * step);
        agent.transform.rotation = Quaternion.LookRotation(-away, Vector3.up);
    }
    // Leaving the window is not a reason to end it either (it cancelled here on focus loss until 2026-11-18): alt-tabbing
    // away mid-stroke left the zombie alive and the knife put away.
    private void OnDisable() => Cancel();
}
