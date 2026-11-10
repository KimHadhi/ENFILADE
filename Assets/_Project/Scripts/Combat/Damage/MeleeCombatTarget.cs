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
    public bool IsKnockedDown => knockdownRemaining > 0f || knockdownPresentation?.IsActive == true || shownDown;
    /// <summary>
    /// On the ground, not yet getting up: what other PCs are told. A zombie getting up is still knocked down (it can't
    /// fight yet), but every PC plays its getting up at once (ZombieBody).
    /// </summary>
    public bool IsLying => knockdownRemaining > 0f || knockdownPresentation?.IsLying == true || shownDown;
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
    public bool IsBroken => IsStaggered;
    public bool IsStunned => IsKnockedDown || IsStaggered || stunRemaining > 0f || shoveRemaining > 0f || grabber != null || executor != null;
    public bool CanExecute => IsAvailable && IsStaggered && !IsHeld && !shownHeld && GetComponent<IDamageReceiver>() != null;
    public bool IsAvailable => isActiveAndEnabled && (GetComponent<ICombatVitals>() == null || GetComponent<ICombatVitals>().Health > 0f) && (GetComponent<EnemyHealth>() == null ||
        (GetComponent<EnemyHealth>().IsAlive && !GetComponent<EnemyHealth>().WavePaused));
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
    public static IEnumerable<MeleeCombatTarget> Active => active;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearRegistry()
    {
        active.Clear();
        AnyStaggered = null;
    }
    private float stunRemaining, recoveryRemaining, shoveRemaining, shoveDuration = ShoveDuration, bargeKnockdown;
    private Vector3 shoveVelocity;
    private const float ShoveDuration = .32f, KnockdownSoundDelay = .22f;
    // How long a guard break holds the host's enemy while it closes in, and how long a copy trusts what this PC just did
    // to it over the host's reports, which were sent before the host heard (s).
    private const float GrabHold = .4f, PredictionHold = .3f;
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
        // Runtime attachment also covers existing scenes/prefabs without requiring asset migration.
        if (Application.isPlaying && GetComponent<ICombatVitals>() != null && GetComponent<EnemyVitalsHUD>() == null)
            gameObject.AddComponent<EnemyVitalsHUD>();
        if (Application.isPlaying && GetComponent<ExecutableHighlight>() == null) gameObject.AddComponent<ExecutableHighlight>();
    }
    private void Update() => Tick(Time.deltaTime * (network != null ? network.Pace : 1f));
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
                // A barge throw ends on the ground.
                if (bargeKnockdown > 0f) KnockDown(shoveVelocity, bargeKnockdown);
            }
        }
        stunRemaining = Mathf.Max(0f, stunRemaining - dt);
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
        if (IsCopy) { network.SendPosture(amount, stun, interrupt); predictedUntil = Time.time + PredictionHold; }
        // One that stands firm takes a share of it (the host was told the whole, and shares it out itself).
        if (Steadfast) amount *= SteadfastPosture;
        Posture = Mathf.Max(0f, Posture - Mathf.Max(0f, amount));
        recoveryRemaining = recoveryDelay;
        if (Posture <= 0f)
        {
            IsStaggered = true; StaggerAge = 0f; recoveryRemaining = staggerRecoveryDelay;
            PlayStaggerSound(); Staggered?.Invoke(); AnyStaggered?.Invoke(this);
        }
        if (interrupt || IsStaggered) InterruptHere(stun);
    }
    public void Interrupt(float duration)
    {
        if (IsAvailable && IsCopy) network.SendInterrupt(duration);
        InterruptHere(duration);
    }
    private void InterruptHere(float duration)
    {
        if (!IsAvailable) return;
        if (Steadfast) duration = Mathf.Min(duration, SteadfastStun);
        stunRemaining = Mathf.Max(stunRemaining, duration);
        StopAgent();
        GetComponent<EnemyCombatAI>()?.CancelAttack();
        Interrupted?.Invoke();
    }
    public void KnockDown(Vector3 direction, float duration)
    {
        if (!IsAvailable || executor != null || Steadfast) return;
        if (IsCopy) { network.SendKnockDown(direction, duration); return; }
        FallDirection = direction;
        // Repeated knockdowns only extend the timer; the body hits the ground once, a moment after it starts to fall.
        if (!IsKnockedDown) Invoke(nameof(PlayKnockdownSound), KnockdownSoundDelay);
        knockdownRemaining = Mathf.Max(knockdownRemaining, duration);
        shoveRemaining = bargeKnockdown = 0f; shoveChain = null;
        Interrupt(duration);
        if (knockdownPresentation == null) knockdownPresentation = new EnemyKnockdownPresentation(transform);
        knockdownPresentation.Fall(direction);
    }
    /// <summary>
    /// A dash push: thrown along the ground at this velocity, slowing to a stop over throwTime and shoving whoever it
    /// runs into like a shove does, then knocked down for knockdownTime. Without a navmesh agent it falls at once.
    /// </summary>
    public void Barge(Vector3 velocity, float throwTime, float knockdownTime)
    {
        if (!IsAvailable || executor != null || Steadfast) return;
        if (IsCopy) { network.SendBarge(velocity, throwTime, knockdownTime); return; }
        if (!StartShove(velocity, new HashSet<MeleeCombatTarget> { this }, Mathf.Max(.05f, throwTime)))
        { KnockDown(velocity, knockdownTime); return; }
        bargeKnockdown = Mathf.Max(.1f, knockdownTime);
        Interrupt(shoveDuration);
    }
    public bool TryReserve(PlayerMeleeCombat owner)
    {
        if (!IsAvailable || owner == null || grabber != null || executor != null || Steadfast) return false;
        knockdownPresentation?.Reset(); knockdownRemaining = 0f;
        grabber = owner; bargeKnockdown = 0f; StopAgent(); GetComponent<EnemyCombatAI>()?.CancelAttack(); Interrupted?.Invoke();
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
    /// Whether this executor may execute it now: broken (CanExecute), or a zombie unaware of anyone with them behind its
    /// back, a stealth kill (ZombieMind.CanBeTakenDownFrom).
    /// </summary>
    public bool CanBeExecutedBy(Component executor) => CanExecute || CanBeTakenDownBy(executor);

    /// <summary>The stealth kill alone: an unaware zombie with this executor behind it.</summary>
    public bool CanBeTakenDownBy(Component executor)
        => executor != null && IsAvailable && !IsHeld && !shownHeld && !IsKnockedDown && GetComponent<IDamageReceiver>() != null
            && TryGetComponent(out ZombieMind mind) && mind.CanBeTakenDownFrom(executor.transform.position);

    public bool TryReserveExecution(PlayerExecution owner)
    {
        if (owner == null || !CanBeExecutedBy(owner)) return false;
        knockdownPresentation?.Reset(); knockdownRemaining = 0f;
        executor = owner; shoveRemaining = bargeKnockdown = 0f; StopAgent();
        GetComponent<EnemyCombatAI>()?.CancelAttack(); Interrupted?.Invoke();
        // The execution starts at once; the host says no if someone else got there first.
        if (IsCopy) network.AskExecution();
        return true;
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
            if (down) Invoke(nameof(PlayKnockdownSound), KnockdownSoundDelay);
            // A zombie's body falls on a doll that follows the host's (ZombieBody); only a plain capsule is tipped over.
            if (!TryGetComponent(out ZombieBody zombie) || !zombie.Wears) EnemyParryRecoil.For(this)?.Lie(fall, down);
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
        if (IsCopy) { network.SendShove(impulse, point); return; }
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
