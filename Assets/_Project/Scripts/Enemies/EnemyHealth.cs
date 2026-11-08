
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>An enemy's standing. Elite and named kills burn magenta in the kill feed and slow the game briefly (backlog 325).</summary>
public enum EnemyRank { Normal, Elite, Named }

[DisallowMultipleComponent, RequireComponent(typeof(MeleeCombatTarget))]
public sealed class EnemyHealth : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName, ITableBound
{
    /// <summary>Resources/Tables/Enemies.csv. A row covers EnemyHealth, EnemyCombatAI, MeleeCombatTarget and NavMeshAgent.</summary>
    public const string TableName = "Enemies";

    [SerializeField, Min(1f)]
    private float maximumHealth = 100f;

    [Tooltip("체력 바 위에 표시할 이름. 비워두면 오브젝트 이름((Clone)/(Console) 제외)을 사용한다.")]
    [SerializeField] private string displayName = "";

    [Tooltip("Enemies.csv의 행 id. 비워두면 오브젝트 이름((Clone)/(Console) 제외)을 사용한다.")]
    [SerializeField] private string tableId = "";

    [Tooltip("Normal, Elite 또는 Named. Elite와 Named를 잡으면 킬 피드 줄이 자홍 불꽃으로 타고 게임이 잠깐 느려진다.")]
    [SerializeField] private EnemyRank rank = EnemyRank.Normal;

    public string DisplayName => displayName;
    public EnemyRank Rank => rank;
    /// <summary>Named or elite: its kill stands out in the feed.</summary>
    public bool IsStandout => rank != EnemyRank.Normal;
    /// <summary>Console test spawns (/spawn n elite). The table sets it otherwise.</summary>
    internal void SetRank(EnemyRank value) => rank = value;
    public float Health { get; private set; }
    public float MaximumHealth => Mathf.Max(1f, maximumHealth);
    public bool IsAlive => Health > 0f;
    public bool WavePaused { get; private set; }
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private NavMeshAgent pausedAgent;
    private EnemyHitReaction reaction;
    private EnemyNetwork network;
    private float appliedMaximum;
    private bool restorePosition, restoreRotation, restoreStopped;

    public void SetWavePaused(bool paused)
    {
        if (WavePaused == paused) return;
        WavePaused = paused;
        if (paused)
        {
            pausedAgent = GetComponent<NavMeshAgent>();
            if (pausedAgent == null || !pausedAgent.isActiveAndEnabled || !pausedAgent.isOnNavMesh) { pausedAgent = null; return; }
            restorePosition = pausedAgent.updatePosition; restoreRotation = pausedAgent.updateRotation; restoreStopped = pausedAgent.isStopped;
            pausedAgent.isStopped = true; pausedAgent.velocity = Vector3.zero;
            pausedAgent.updatePosition = false; pausedAgent.updateRotation = false;
        }
        else if (pausedAgent != null)
        {
            if (pausedAgent.isActiveAndEnabled && pausedAgent.isOnNavMesh)
            { pausedAgent.nextPosition = transform.position; pausedAgent.isStopped = restoreStopped; }
            pausedAgent.updatePosition = restorePosition; pausedAgent.updateRotation = restoreRotation;
            pausedAgent = null;
        }
    }

    public event Action<EnemyHealth> Died;

    private static readonly List<EnemyHealth> living = new List<EnemyHealth>();
    /// <summary>Every enemy switched on in the scene, on this PC (the host's or a copy), dead ones included until they go.</summary>
    public static IReadOnlyList<EnemyHealth> Living => living;

    private void OnEnable() => living.Add(this);
    private void OnDisable() => living.Remove(this);

    private void Awake()
    {
        if (GetComponent<MeleeCombatTarget>() == null) gameObject.AddComponent<MeleeCombatTarget>();
        // Bullet flinch (push, slow, interrupt) applies to existing prefabs without migration.
        reaction = GetComponent<EnemyHitReaction>();
        if (reaction == null) reaction = gameObject.AddComponent<EnemyHitReaction>();
        network = GetComponent<EnemyNetwork>();
        GameTables.Bind(TableName, string.IsNullOrWhiteSpace(tableId) ? CombatDamageEvents.BaseName(gameObject) : tableId.Trim(),
            this, GetComponent<EnemyCombatAI>(), GetComponent<MeleeCombatTarget>(), GetComponent<NavMeshAgent>(), reaction);
        Health = MaximumHealth;
        appliedMaximum = MaximumHealth;
    }

    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (!isActiveAndEnabled || !IsAlive || WavePaused ||
            hit.Id <= 0 || hit.Amount <= 0f ||
            float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount))
        {
            return DamageResult.Ignored;
        }

        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        float before = Health;
        // An execution takes whatever is left, which on the host may be more than the copy it was judged on.
        Health = hit.Kind == DamageKind.Execution ? 0f : Mathf.Max(0f, Health - hit.Amount);
        UltimateCharge.AwardEnemyHit(hit);
        // A copy shows the hit now and leaves the rest to the host, whose enemy is the true one.
        bool copy = network != null && network.IsCopy;
        if (copy) network.SendHit(hit);
        else if (network != null) network.AnnounceHit(hit, before, Health);
        // Report before Die(): popups bind to this transform and keep its last position after Destroy.
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth);

        if (!IsAlive)
        {
            Die();
        }
        else if (reaction != null && !copy)
        {
            reaction.OnHit(hit, before - Health, MaximumHealth);
        }

        return DamageResult.Damaged;
    }

    /// <summary>On a copy: the health the host says. A kill this PC already judged stays a kill.</summary>
    internal void ShowHealth(float health)
    {
        if (IsAlive) Health = Mathf.Min(health, MaximumHealth);
    }

    /// <summary>On a copy: someone else's hit, as the host took it.</summary>
    internal void ShowHit(DamageHit hit, float before, float after)
    {
        ShowHealth(after);
        CombatDamageEvents.Report(this, hit, before, after, MaximumHealth);
    }

    // A live table edit keeps the same health fraction instead of healing or killing enemies mid-fight.
    void ITableBound.OnTableApplied()
    {
        if (appliedMaximum > 0f && IsAlive) Health = Mathf.Max(.01f, Health / appliedMaximum * MaximumHealth);
        appliedMaximum = MaximumHealth;
    }

    private void Die()
    {
        // 웨이브 관리자에게 처치 사실을 알린다.
        Died?.Invoke(this);

        if (network != null && network.IsCopy) network.HideUntilRemoved();
        else Destroy(gameObject);
    }
}
