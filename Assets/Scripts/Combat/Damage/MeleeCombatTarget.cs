using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Shared posture/stun contract for enemies and practice targets. AI must respect IsStunned.</summary>
[DisallowMultipleComponent]
public sealed class MeleeCombatTarget : MonoBehaviour, IShoveReceiver
{
    [SerializeField, Min(1f)] private float maximumPosture = 100f;
    [SerializeField, Min(0f)] private float postureRecovery = 15f;
    [SerializeField, Min(0f)] private float recoveryDelay = 3f;
    [SerializeField, Min(0.1f)] private float staggerRecoverySeconds = 4f;
    [SerializeField, Min(0f)] private float staggerRecoveryDelay = .35f;
    public float Posture { get; private set; }
    public float MaximumPosture => Mathf.Max(1f, maximumPosture);
    public bool IsStaggered { get; private set; }
    public bool IsBroken => IsStaggered;
    public bool IsStunned => IsStaggered || stunRemaining > 0f || grabber != null || executor != null;
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
    private PlayerMeleeCombat grabber;
    private PlayerExecution executor;
    private AudioSource staggerAudio;
    private NavMeshAgent agent;
    private bool stoppedAgent, previousStopped;

    private void Awake() { Posture = MaximumPosture; agent = GetComponent<NavMeshAgent>(); }
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
        if (shoveRemaining > 0f)
        {
            float slice = Mathf.Min(dt, shoveRemaining);
            float strength = (shoveRemaining + shoveRemaining - slice) / (2f * ShoveDuration);
            shoveRemaining -= slice;
            MoveShove(shoveVelocity * (slice * strength));
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
    public void ApplyPostureDamage(float amount, float stun = 0f)
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
        Interrupt(stun);
    }
    public void Interrupt(float duration)
    {
        if (!IsAvailable) return;
        stunRemaining = Mathf.Max(stunRemaining, duration);
        StopAgent();
        GetComponent<EnemyCombatAI>()?.CancelAttack();
        Interrupted?.Invoke();
    }
    public bool TryReserve(PlayerMeleeCombat owner)
    {
        if (!IsAvailable || owner == null || grabber != null || executor != null) return false;
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
        }
        staggerAudio.PlayOneShot(clip, Mathf.Clamp01(settings.staggerSoundVolume));
    }
    public void ResetPosture()
    {
        Posture = MaximumPosture; IsStaggered = false; StaggerAge = 0f;
        stunRemaining = recoveryRemaining = shoveRemaining = 0f;
        if (executor != null) executor.Cancel();
        grabber = null; RestoreAgent();
    }
    public void ReceiveShove(Vector3 impulse, Vector3 point)
    {
        if (!IsAvailable || executor != null) return;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        var body = GetComponent<Rigidbody>();
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        { shoveVelocity = Vector3.ProjectOnPlane(impulse, Vector3.up); shoveRemaining = ShoveDuration; StopAgent(); }
        else if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
    }
    private void MoveShove(Vector3 displacement)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || displacement.sqrMagnitude < .000001f) return;
        float distance = displacement.magnitude; Vector3 direction = displacement / distance;
        if (NavMesh.Raycast(agent.nextPosition, agent.nextPosition + displacement, out var edge, agent.areaMask))
            distance = Mathf.Min(distance, Mathf.Max(0f, Vector3.Distance(agent.nextPosition, edge.position) - .025f));
        float radius = Mathf.Max(.1f, agent.radius);
        Vector3 feet = agent.transform.position - Vector3.up * agent.baseOffset;
        Vector3 bottom = feet + Vector3.up * (radius + .05f);
        Vector3 top = feet + Vector3.up * Mathf.Max(radius + .05f, agent.height - radius);
        foreach (var hit in Physics.CapsuleCastAll(bottom, top, radius, direction, distance + .025f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform)) distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .025f));
        agent.Move(direction * distance);
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
    { active.Remove(this); if (executor != null) executor.Cancel(); executor = null; grabber = null; stunRemaining = shoveRemaining = 0f; RestoreAgent(); }
}
