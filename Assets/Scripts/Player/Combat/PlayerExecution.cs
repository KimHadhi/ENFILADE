using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>Single-player deathblow transaction. Animation can subscribe to Started/Impact/Ended.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-160)]
public sealed class PlayerExecution : MonoBehaviour
{
    [SerializeField, Min(.2f)] private float reach = 2.1f;
    [SerializeField, Range(5f, 75f)] private float aimAngle = 38f;
    [SerializeField, Min(.1f)] private float duration = .8f;
    [SerializeField, Min(0f)] private float impactTime = .35f;
    [SerializeField, Min(0f)] private float clearance = 2.6f;
    [SerializeField, Min(0f)] private float retreatSpeed = 2.8f;
    private PlayerMovement player;
    private Transform aim;
    private InputAction interact;
    private MeleeCombatTarget victim;
    private float elapsed;
    private bool impacted;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    public bool IsExecuting { get; private set; }
    public MeleeCombatTarget Candidate { get; private set; }
    public MeleeCombatTarget Victim => victim;
    public static PlayerExecution ActiveExecution { get; private set; }
    public event Action<MeleeCombatTarget> Started;
    public event Action<MeleeCombatTarget> Impact;
    public event Action Ended;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearActive() => ActiveExecution = null;
    public static bool Suppresses(Component enemy) => ActiveExecution != null && ActiveExecution.IsExecuting
        && enemy != null && enemy.gameObject.scene == ActiveExecution.gameObject.scene;

    private void OnEnable()
    {
        player = GetComponent<PlayerMovement>();
        var camera = GetComponentInChildren<Camera>(); aim = camera != null ? camera.transform : transform;
        if (Application.isPlaying && GetComponent<ExecutionHUD>() == null) gameObject.AddComponent<ExecutionHUD>();
    }
    private void Start()
    {
        var input = GetComponent<PlayerInput>();
        interact = (input != null ? input.actions : InputSystem.actions)?.FindAction("Player/Interact", false);
    }
    private bool CanOffer => isActiveAndEnabled && player != null && player.IsInitialized && player.InputAvailable
        && !player.CombatInputLocked && !player.IsParkour && !player.IsSliding && !player.IsDiving
        && player.CurrentState != PlayerMovement.MovementState.StepDodge
        && player.CurrentState != PlayerMovement.MovementState.Tackle
        && player.CurrentState != PlayerMovement.MovementState.LandingRoll
        && (ActiveExecution == null || !ActiveExecution.IsExecuting);
    private void Update()
    {
        if (IsExecuting)
        {
            // Pause retains the transaction, but opening chat, death, or focus loss cancels it.
            if (Time.timeScale <= 0f) return;
            if (!player.InputAvailable || interact == null || !interact.enabled) { Cancel(); return; }
            Tick(Time.deltaTime); return;
        }
        Candidate = interact != null && interact.enabled ? FindCandidate() : null;
        if (Candidate != null && interact.WasPressedThisFrame()) TryBegin(Candidate);
    }
    public MeleeCombatTarget FindCandidate()
    {
        if (!CanOffer || aim == null) return null;
        MeleeCombatTarget best = null; float bestScore = float.NegativeInfinity;
        foreach (var target in MeleeCombatTarget.Active)
        {
            if (!Eligible(target, out Vector3 point)) continue;
            Vector3 direction = point - aim.position;
            float score = Vector3.Dot(aim.forward, direction.normalized) * 4f - direction.magnitude * .1f;
            if (score > bestScore) { best = target; bestScore = score; }
        }
        return best;
    }
    private bool Eligible(MeleeCombatTarget target, out Vector3 point)
    {
        point = default;
        if (target == null || !target.CanExecute || target.gameObject.scene != gameObject.scene || target.transform.IsChildOf(transform)) return false;
        var body = target.GetComponentInChildren<Collider>();
        if (body == null || !body.enabled || body.isTrigger) return false;
        point = body.bounds.center;
        if (Vector3.Distance(aim.position, body.ClosestPoint(aim.position)) > reach) return false;
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
    public bool TryBegin(MeleeCombatTarget target)
    {
        if (!CanOffer || !Eligible(target, out _) || !target.TryReserveExecution(this)) return false;
        victim = target; elapsed = 0f; impacted = false; IsExecuting = true; ActiveExecution = this; Candidate = null;
        player.MeleeCombat?.CancelAction();
        player.HorizontalVelocity = Vector3.zero;
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
        elapsed += dt;
        if (!impacted && elapsed >= Mathf.Min(impactTime, duration))
        {
            impacted = true;
            var vitals = victim.GetComponent<ICombatVitals>();
            var receiver = victim.GetComponent<IDamageReceiver>();
            if (vitals == null || receiver == null) { Cancel(); return; }
            // A death callback may disable the enemy synchronously. Release its reservation first.
            victim.ReleaseExecution(this);
            receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), vitals.Health, DamageKind.Execution,
                victim.transform.position, aim.forward, gameObject));
            Impact?.Invoke(victim);
        }
        if (elapsed >= duration) Cancel();
    }
    public void Cancel()
    {
        bool wasExecuting = IsExecuting; IsExecuting = false; Candidate = null;
        if (ActiveExecution == this) ActiveExecution = null;
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
    private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
}
