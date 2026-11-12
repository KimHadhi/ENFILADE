using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// One player's deathblow transaction. F executes a broken enemy within reach; with the fists out the attack button
/// does too, from further off, dashing in first, as Sekiro's deathblow sits on the attack button. A zombie that hasn't
/// noticed anyone can be executed the same way from behind, unbroken: a stealth kill (ZombieMind, stage 4C-2). Either way
/// the player closes in to dashStopDistance first, and their class's knife does the deed (ExecutionKnife).
/// Animation can subscribe to Started/Impact/Ended.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Execution)]
public sealed class PlayerExecution : MonoBehaviour
{
    [SerializeField, Min(.2f)] private float reach = 2.1f;
    [SerializeField, Range(5f, 75f)] private float aimAngle = 38f;
    [SerializeField, Min(.1f)] private float duration = .8f;
    [SerializeField, Min(0f)] private float impactTime = .35f;
    [SerializeField, Min(0f)] private float clearance = 2.6f;
    [SerializeField, Min(0f)] private float retreatSpeed = 2.8f;
    [Header("Deathblow on the attack button")]
    [SerializeField, Min(.2f), Tooltip("With the fists out, a left click executes a broken enemy this close (m), dashing in from beyond reach.")]
    private float deathblowReach = 4f;
    [SerializeField, Min(1f), Tooltip("Speed of that dash (m/s).")]
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
    private InputAction interact;
    private PlayerWeaponInput buttons;
    private MeleeCombatTarget victim;
    private float elapsed, approachTime;
    private bool impacted, approaching;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    public bool IsExecuting { get; private set; }
    /// <summary>The enemy F would execute now.</summary>
    public MeleeCombatTarget Candidate { get; private set; }
    /// <summary>The enemy a left click would execute now (fists out), dashing in if it is beyond reach; null otherwise.</summary>
    public MeleeCombatTarget DeathblowCandidate { get; private set; }
    /// <summary>Dashing in to the enemy before the execution proper starts.</summary>
    public bool IsDashing => IsExecuting && approaching;
    public MeleeCombatTarget Victim => victim;
    /// <summary>How long after the execution proper begins (any dash in over) its blow lands (s): the knife's strike meets it (ExecutionKnife).</summary>
    public float ImpactTime => Mathf.Min(impactTime, duration);
    // A paired execution's own timing and blow for this one (ExecutionPairPlayer, Tables/Executions.csv): its kill and
    // length stand in for impactTime and duration until it ends, when the table's come back; its blow lands on the bone
    // the knife meets and sends the body the pair's way.
    private float tableImpact = -1f, tableDuration = -1f;
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
        if (Application.isPlaying && player?.IsLocal != false && GetComponent<ExecutionHUD>() == null) gameObject.AddComponent<ExecutionHUD>();
        // Paired executions for what the zombie is doing, on every PC's copy of the player too (ExecutionPairPlayer).
        if (Application.isPlaying) ExecutionPairPlayer.For(gameObject);
        // The class's knife through every execution, on every PC's copy of the player too (ExecutionKnife).
        if (Application.isPlaying) ExecutionKnife.On(gameObject);
    }
    private void Start()
    {
        var input = GetComponent<PlayerInput>();
        interact = (input != null ? input.actions : InputSystem.actions)?.FindAction("Player/Interact", false);
        buttons = GetComponent<PlayerWeaponInput>();
    }
    private bool CanOffer => isActiveAndEnabled && player != null && player.IsInitialized && player.InputAvailable
        && !player.CombatInputLocked && !player.IsHeld && !player.IsParkour && !player.IsSliding && !player.IsDiving && !player.IsDowned
        && player.CurrentState != PlayerMovement.MovementState.StepDodge
        && player.CurrentState != PlayerMovement.MovementState.DashPush
        && !player.IsLandingRecovery && !IsExecuting;
    /// <summary>
    /// On the host's copy of someone executing on their own PC: the enemy is held for them, and the others back away from
    /// this body as they would from the host's own.
    /// </summary>
    internal void ShowExecuting(MeleeCombatTarget target)
    {
        // An end that never arrived must not leave the last enemy held, or this player listed twice.
        if (victim != null && victim != target) victim.ReleaseExecution(this);
        victim = target; IsExecuting = true; elapsed = 0f; impacted = false;
        executing.Remove(this); executing.Add(this);
        foreach (var enemy in MeleeCombatTarget.Active)
            if (enemy != null && Suppresses(enemy)) enemy.GetComponent<EnemyCombatAI>()?.CancelAttack();
    }
    private void Update()
    {
        if (player != null && player.IsNetworkDriven)
        {
            // Their PC says when it ends; should that word be lost, the hold lets go on its own.
            if (IsExecuting && (elapsed += Time.deltaTime) > duration + RemoteGrace) Cancel();
            return;
        }
        if (IsExecuting)
        {
            // Pause retains the transaction, but opening chat, death, or focus loss cancels it.
            if (Time.timeScale <= 0f) return;
            if (!player.InputAvailable || interact == null || !interact.enabled) { Cancel(); return; }
            Tick(Time.deltaTime); return;
        }
        Candidate = interact != null && interact.enabled ? FindCandidate() : null;
        if (Candidate != null && interact.WasPressedThisFrame()) { TryBegin(Candidate); return; }
        // Before the fists read the same click (PlayerMeleeCombat runs after this): a broken enemy takes the deathblow
        // instead of a punch. A counter, which reads it earlier still, has already claimed the click if it was open.
        var fists = player != null ? player.MeleeCombat : null;
        DeathblowCandidate = fists != null && fists.IsActive ? FindCandidate(deathblowReach) : null;
        if (DeathblowCandidate != null && buttons != null && buttons.TryReadMeleeInput(out var frame) && frame.AttackPressed)
            TryBegin(DeathblowCandidate, deathblowReach);
    }
    public MeleeCombatTarget FindCandidate() => FindCandidate(reach);
    private MeleeCombatTarget FindCandidate(float within)
    {
        if (!CanOffer || aim == null) return null;
        MeleeCombatTarget best = null; float bestScore = float.NegativeInfinity;
        foreach (var target in MeleeCombatTarget.Active)
        {
            if (!Eligible(target, within, out Vector3 point)) continue;
            Vector3 direction = point - aim.position;
            float score = Vector3.Dot(aim.forward, direction.normalized) * 4f - direction.magnitude * .1f;
            if (score > bestScore) { best = target; bestScore = score; }
        }
        return best;
    }
    private bool Eligible(MeleeCombatTarget target, float within, out Vector3 point)
    {
        point = default;
        // Broken, or an unaware zombie with this player behind it: a stealth kill (MeleeCombatTarget.CanBeExecutedBy).
        if (target == null || !target.CanBeExecutedBy(this) || target.gameObject.scene != gameObject.scene || target.transform.IsChildOf(transform)) return false;
        var body = target.GetComponentInChildren<Collider>();
        if (body == null || !body.enabled || body.isTrigger) return false;
        point = body.bounds.center;
        if (Vector3.Distance(aim.position, body.ClosestPoint(aim.position)) > within) return false;
        Vector3 delta = point - aim.position;
        if (Vector3.Dot(aim.forward, delta.normalized) < Mathf.Cos(aimAngle * Mathf.Deg2Rad)) return false;
        int count = Physics.RaycastNonAlloc(aim.position, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        var hits = count == sightHits.Length ? Physics.RaycastAll(aim.position, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) : sightHits;
        if (hits != sightHits) count = hits.Length;
        for (int i = 0; i < count; i++)
        {
            Transform hit = hits[i].collider.transform;
            if (!hit.IsChildOf(transform) && !hit.IsChildOf(target.transform)) return false;
        }
        return true;
    }
    public bool TryBegin(MeleeCombatTarget target) => TryBegin(target, reach);
    /// <summary>Executes target if it is within this distance; beyond reach the player dashes in to it first.</summary>
    public bool TryBegin(MeleeCombatTarget target, float within)
    {
        if (!CanOffer || !Eligible(target, within, out _) || !target.TryReserveExecution(this)) return false;
        victim = target; elapsed = approachTime = 0f; impacted = false; IsExecuting = true; Candidate = DeathblowCandidate = null;
        // F closes in too (2026-11-13): the class knife is stabbed in by hand (ExecutionKnife), and from F's 2.1 m reach the
        // arm met nothing. The dash stops at dashStopDistance either way.
        approaching = Gap(target, out _) > dashStopDistance;
        executing.Remove(this); executing.Add(this);
        player.PerfectDefense?.Cancel();
        // Pressed during the parry's hit stop: the deathblow goes now, not once time lets go.
        var timeEffects = GetComponent<CombatTimeEffects>();
        var parry = GetComponent<ParryFeedback>();
        if (timeEffects != null && parry != null) timeEffects.Release(parry);
        player.MeleeCombat?.CancelAction();
        player.HorizontalVelocity = Vector3.zero;
        CombatCueAudio.Play(gameObject, CombatCue.Grab);
        GetComponent<WeaponController>()?.Runtime?.Interrupt();
        GetComponent<PlayerWeaponInput>()?.RequireTriggerRelease();
        // Cancel windups immediately; newly spawned enemies also consult Suppresses in their own update.
        foreach (var enemy in MeleeCombatTarget.Active)
            if (enemy != null && Suppresses(enemy)) enemy.GetComponent<EnemyCombatAI>()?.CancelAttack();
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
        if (!impacted && elapsed >= Mathf.Min(impactTime, duration))
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
        if (elapsed >= duration) Cancel();
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
        else RestoreAmmo(GetComponent<WeaponController>()?.Runtime);
    }
    private void RestoreAmmo(WeaponRuntime weapon)
    {
        if (weapon == null || weapon.Data.Category != WeaponCategory.Ranged ||
            float.IsNaN(ammoRestoredFraction) || float.IsInfinity(ammoRestoredFraction)) return;
        // A Schießhund's Desperado in hand is loaded by bounties alone.
        if (TryGetComponent(out PlayerWeaponSlots slots) && slots.KeepsOwnRounds(slots.SelectedSlot)) return;
        weapon.AddReserve(Mathf.CeilToInt(weapon.Data.MagazineSize * Mathf.Clamp01(ammoRestoredFraction)));
    }
    /// <summary>
    /// A paired execution runs this one (ExecutionPairPlayer): its blow lands <paramref name="impact"/> after the
    /// execution proper began and it ends at <paramref name="length"/>, instead of the table's impactTime and duration.
    /// </summary>
    internal void Retime(float impact, float length)
    {
        if (!IsExecuting) return;
        if (tableImpact < 0f) { tableImpact = impactTime; tableDuration = duration; }
        impactTime = Mathf.Max(0f, impact);
        duration = Mathf.Max(.1f, length);
    }
    /// <summary>Where the pair's blow meets the victim this frame and which way it sends the body (ExecutionPairPlayer).</summary>
    internal void Strike(Vector3 point, Vector3 along)
    {
        strikePoint = point;
        strikeAlong = along.sqrMagnitude > 1e-6f ? along.normalized : (Vector3?)null;
    }
    private void ForgetPair()
    {
        strikePoint = strikeAlong = null;
        if (tableImpact < 0f) return;
        impactTime = tableImpact;
        duration = tableDuration;
        tableImpact = tableDuration = -1f;
    }
    public void Cancel()
    {
        ForgetPair();
        bool wasExecuting = IsExecuting; IsExecuting = approaching = false; Candidate = DeathblowCandidate = null;
        while (executing.Remove(this)) { }
        if (victim != null) victim.ReleaseExecution(this);
        victim = null;
        if (!wasExecuting) return;
        GetComponent<PlayerWeaponInput>()?.RequireTriggerRelease();
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
    private void OnDisable() => Cancel();
    private void OnApplicationFocus(bool focused) { if (!focused && (player == null || player.IsLocal)) Cancel(); }
}
