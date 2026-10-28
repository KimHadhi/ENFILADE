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
    public bool IsKnockedDown => knockdownRemaining > 0f || knockdownPresentation?.IsActive == true;
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
    public bool IsBroken => IsStaggered;
    public bool IsStunned => IsKnockedDown || IsStaggered || stunRemaining > 0f || shoveRemaining > 0f || grabber != null || executor != null;
    public bool CanExecute => IsAvailable && IsStaggered && grabber == null && executor == null && GetComponent<IDamageReceiver>() != null;
    public bool IsAvailable => isActiveAndEnabled && (GetComponent<ICombatVitals>() == null || GetComponent<ICombatVitals>().Health > 0f) && (GetComponent<EnemyHealth>() == null ||
        (GetComponent<EnemyHealth>().IsAlive && !GetComponent<EnemyHealth>().WavePaused));
    public event Action Interrupted;
    public event Action Staggered;
    public float StaggerAge { get; private set; }
    private static readonly HashSet<MeleeCombatTarget> active = new HashSet<MeleeCombatTarget>();
    public static IEnumerable<MeleeCombatTarget> Active => active;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearRegistry() => active.Clear();
    private float stunRemaining, recoveryRemaining, shoveRemaining;
    private Vector3 shoveVelocity;
    private const float ShoveDuration = .32f;
    [Header("Shove Chain")]
    [SerializeField, Range(0f, 1f)] private float chainTransfer = .8f;
    [SerializeField, Min(.1f)] private float minimumChainSpeed = 1.2f;
    [SerializeField, Min(0f)] private float chainStun = .55f;
    private HashSet<MeleeCombatTarget> shoveChain;
    private PlayerMeleeCombat grabber;
    private PlayerExecution executor;
    private AudioSource staggerAudio;
    private NavMeshAgent agent;
    private bool stoppedAgent, previousStopped;

    private float appliedMaximumPosture;

    private void Awake() { Posture = appliedMaximumPosture = MaximumPosture; agent = GetComponent<NavMeshAgent>(); }

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
        // Runtime attachment also covers existing scenes/prefabs without requiring asset migration.
        if (Application.isPlaying && GetComponent<ICombatVitals>() != null && GetComponent<EnemyVitalsHUD>() == null)
            gameObject.AddComponent<EnemyVitalsHUD>();
    }
    private void Update() => Tick(Time.deltaTime);
    public void Tick(float dt)
    {
        if (dt <= 0f || !IsAvailable) return;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        bool suppressed = PlayerExecution.Suppresses(this);
        if (suppressed)
        {
            shoveRemaining = 0f;
            StopAgent();
            if (!IsStunned) PlayerExecution.ActiveExecution.Retreat(agent, dt);
        }
        if (executor != null) return;
        knockdownRemaining = Mathf.Max(0f, knockdownRemaining - dt);
        knockdownPresentation?.Tick(dt, knockdownRemaining > 0f);
        if (shoveRemaining > 0f)
        {
            float slice = Mathf.Min(dt, shoveRemaining);
            float strength = (shoveRemaining + shoveRemaining - slice) / (2f * ShoveDuration);
            shoveRemaining -= slice;
            MoveShove(shoveVelocity * (slice * strength), shoveVelocity.magnitude * strength);
            if (shoveRemaining <= 0f) shoveChain = null;
        }
        stunRemaining = Mathf.Max(0f, stunRemaining - dt);
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
        Posture = Mathf.Max(0f, Posture - Mathf.Max(0f, amount));
        recoveryRemaining = recoveryDelay;
        if (Posture <= 0f)
        {
            IsStaggered = true; StaggerAge = 0f; recoveryRemaining = staggerRecoveryDelay;
            PlayStaggerSound(); Staggered?.Invoke();
        }
        if (interrupt || IsStaggered) Interrupt(stun);
    }
    public void Interrupt(float duration)
    {
        if (!IsAvailable) return;
        stunRemaining = Mathf.Max(stunRemaining, duration);
        StopAgent();
        GetComponent<EnemyCombatAI>()?.CancelAttack();
        Interrupted?.Invoke();
    }
    public void KnockDown(Vector3 direction, float duration)
    {
        if (!IsAvailable || executor != null) return;
        knockdownRemaining = Mathf.Max(knockdownRemaining, duration);
        shoveRemaining = 0f; shoveChain = null;
        Interrupt(duration);
        if (knockdownPresentation == null) knockdownPresentation = new EnemyKnockdownPresentation(transform);
        knockdownPresentation.Fall(direction);
    }
    public bool TryReserve(PlayerMeleeCombat owner)
    {
        if (!IsAvailable || owner == null || grabber != null || executor != null) return false;
        knockdownPresentation?.Reset(); knockdownRemaining = 0f;
        grabber = owner; StopAgent(); GetComponent<EnemyCombatAI>()?.CancelAttack(); Interrupted?.Invoke(); return true;
    }
    public void Release(PlayerMeleeCombat owner)
    {
        if (grabber != owner) return;
        grabber = null;
        if (!IsStunned && !PlayerExecution.Suppresses(this)) RestoreAgent();
    }
    public bool TryReserveExecution(PlayerExecution owner)
    {
        if (owner == null || !CanExecute) return false;
        knockdownPresentation?.Reset(); knockdownRemaining = 0f;
        executor = owner; shoveRemaining = 0f; StopAgent();
        GetComponent<EnemyCombatAI>()?.CancelAttack(); Interrupted?.Invoke(); return true;
    }
    public void ReleaseExecution(PlayerExecution owner)
    {
        if (executor != owner) return;
        executor = null;
        if (!IsStunned && !PlayerExecution.Suppresses(this)) RestoreAgent();
    }
    private void PlayStaggerSound()
    {
        var settings = CombatFeedbackSettings.Instance;
        var clip = settings != null ? settings.staggerSound : null;
        if (!Application.isPlaying || clip == null) return;
        if (staggerAudio == null)
        {
            staggerAudio = gameObject.AddComponent<AudioSource>(); staggerAudio.playOnAwake = false;
            staggerAudio.spatialBlend = .85f; staggerAudio.minDistance = 2f; staggerAudio.maxDistance = 22f;
            staggerAudio.rolloffMode = AudioRolloffMode.Linear;
            GameAudio.Bind(staggerAudio, AudioChannel.Combat);
        }
        staggerAudio.PlayOneShot(clip, Mathf.Clamp01(settings.staggerSoundVolume));
    }
    public void ResetPosture()
    {
        knockdownRemaining = 0f; knockdownPresentation?.Reset();
        Posture = MaximumPosture; IsStaggered = false; StaggerAge = 0f;
        stunRemaining = recoveryRemaining = shoveRemaining = 0f;
        shoveChain = null;
        if (executor != null) executor.Cancel();
        grabber = null; RestoreAgent();
    }
    public void ReceiveShove(Vector3 impulse, Vector3 point)
    {
        ReceiveShove(impulse, point, new HashSet<MeleeCombatTarget> { this });
    }
    private void ReceiveShove(Vector3 impulse, Vector3 point, HashSet<MeleeCombatTarget> chain)
    {
        if (!IsAvailable || executor != null) return;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        var body = GetComponent<Rigidbody>();
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        { shoveVelocity = Vector3.ProjectOnPlane(impulse, Vector3.up); shoveRemaining = ShoveDuration; shoveChain = chain; StopAgent(); }
        else if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
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
        if (other.shoveRemaining > 0f && other.shoveVelocity.magnitude * other.shoveRemaining / ShoveDuration >= transferredSpeed) return;
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
    { active.Remove(this); if (executor != null) executor.Cancel(); executor = null; grabber = null; shoveChain = null; stunRemaining = shoveRemaining = knockdownRemaining = 0f; knockdownPresentation?.Reset(); RestoreAgent(); }
}
