
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

    /// <summary>
    /// Its name over its health bar and in the kill feed: a zombie's kind's words from Texts.csv (ZombieType), where every
    /// zombie used to read its prefab's name, Enemy_Test (user, 2026-11-11 playtest).
    /// </summary>
    internal void SetDisplayName(string name)
    {
        name = name ?? "";
        if (name == displayName) return;
        displayName = name;
        if (TryGetComponent(out EnemyVitalsHUD vitals)) vitals.Rename();
    }
    public EnemyRank Rank => rank;
    /// <summary>Named or elite: its kill stands out in the feed.</summary>
    public bool IsStandout => rank != EnemyRank.Normal;
    /// <summary>Console test spawns (/spawn n elite). The table sets it otherwise.</summary>
    internal void SetRank(EnemyRank value) => rank = value;
    public float Health { get; private set; }
    public float MaximumHealth => Mathf.Max(1f, maximumHealth * healthShare);
    // A zombie's kind scales its row's health (ZombieType: a runner dies sooner).
    private float healthShare = 1f;

    /// <summary>Its health becomes this share of its row's, at the same fraction of it (ZombieType, every PC alike).</summary>
    internal void SetHealthShare(float share)
    {
        share = Mathf.Max(.01f, share);
        if (Mathf.Approximately(share, healthShare)) return;
        float fraction = MaximumHealth > 0f ? Health / MaximumHealth : 1f;
        healthShare = share;
        // A copy's health is the host's, already at the new share: it only can't be over the new maximum.
        if (network != null && network.IsCopy) Health = Mathf.Min(Health, MaximumHealth);
        else if (IsAlive) Health = Mathf.Max(.01f, fraction * MaximumHealth);
        appliedMaximum = MaximumHealth;
    }
    public bool IsAlive => Health > 0f;
    public bool WavePaused { get; private set; }
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private NavMeshAgent pausedAgent;
    private EnemyHitReaction reaction;
    private EnemyNetwork network;
    private ZombieBody body;
    private ZombieMind mind;
    private MeleeCombatTarget melee;
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
        // The zombie it wears (Resources/Enemies/ZombieLooks); without that asset it stays a capsule.
        body = GetComponent<ZombieBody>();
        if (body == null) body = gameObject.AddComponent<ZombieBody>();
        // What it knows of the players: a horde zombie unless placed calm (ZombieMind.startCalm or /spawn n calm).
        mind = GetComponent<ZombieMind>();
        if (mind == null) mind = gameObject.AddComponent<ZombieMind>();
        network = GetComponent<EnemyNetwork>();
        GameTables.Bind(TableName, string.IsNullOrWhiteSpace(tableId) ? CombatDamageEvents.BaseName(gameObject) : tableId.Trim(),
            this, GetComponent<EnemyCombatAI>(), GetComponent<MeleeCombatTarget>(), GetComponent<NavMeshAgent>(), reaction);
        // A shambler's hold on a player plays out here (ZombieGrip; the held player's PlayerHold decides it).
        if (GetComponent<ZombieGrip>() == null) gameObject.AddComponent<ZombieGrip>();
        // Its kind (shambler or runner) goes on top of that row (ZombieType, Tables/ZombieKinds).
        if (GetComponent<ZombieType>() == null) gameObject.AddComponent<ZombieType>();
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
        // Knocked down and lying there, it takes more (ZombieKnockdown lyingDamage, friend feedback C7).
        float scale = LyingScale(), amount = hit.Amount * scale;
        if (hit.Kind == DamageKind.Execution)
        {
            // An execution takes whatever is left, which on the host may be more than the copy it was judged on. Of one
            // that stands firm (a brute) it takes a share of its whole health instead: executions work on it, without
            // one of them ending a boss made for a whole team's fire.
            float share = melee != null ? melee.ExecutionShare : 1f;
            // A stealth takedown, on a zombie that never knew they were there, ends it whatever it is (user, 2026-11-11
            // playtest: "돌진 좀비도 암살하면 한 번에 죽어야 하는 거 아님??").
            if (mind != null && mind.Unaware) share = 1f;
            Health = share >= 1f ? 0f : Mathf.Max(0f, Health - MaximumHealth * share);
            scale = hit.Amount > 0f ? (before - Health) / hit.Amount : 1f;
        }
        else Health = Mathf.Max(0f, Health - amount);
        UltimateCharge.AwardEnemyHit(hit);
        // A copy shows the hit now and leaves the rest to the host, whose enemy is the true one.
        bool copy = network != null && network.IsCopy;
        if (copy) network.SendHit(hit);
        else if (network != null) network.AnnounceHit(hit, before, Health);
        // Report before Die(): popups bind to this transform and keep its last position after Destroy. The number shown is
        // what it took, the lying share included: it read as if lying made no difference while only the health knew
        // (user, 2026-11-10).
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth, scale);

        if (!IsAlive)
        {
            Die(hit);
        }
        else if (!copy)
        {
            // It lived through an execution: back on its guard, so the next one has to be earned again.
            if (hit.Kind == DamageKind.Execution && melee != null) melee.ResetPosture();
            if (reaction != null) reaction.OnHit(hit, before - Health, MaximumHealth);
            // Knocked down, the blow pushes the body where its doll runs (here, on the host or alone).
            if (body != null) body.Hit(hit);
            // A blow wakes a calm zombie at once.
            if (mind != null) mind.Struck(hit);
        }

        return DamageResult.Damaged;
    }

    /// <summary>On a copy: the health the host says. A kill this PC already judged stays a kill.</summary>
    internal void ShowHealth(float health) => ShowHealth(health, default, false);

    /// <summary>On a copy: someone else's hit, as the host took it.</summary>
    internal void ShowHit(DamageHit hit, float before, float after)
    {
        // Read before the health is shown: a killing hit ends the lying it was scaled by. An execution's number is what
        // it took, which of a brute is a share.
        float scale = hit.Kind == DamageKind.Execution && hit.Amount > 0f ? Mathf.Max(0f, before - after) / hit.Amount : LyingScale();
        ShowHealth(after, hit, true);
        CombatDamageEvents.Report(this, hit, before, after, MaximumHealth, scale);
    }

    // What a hit's amount is multiplied by here and now: more while it lies knocked down.
    private float LyingScale()
    {
        if (melee == null) melee = GetComponent<MeleeCombatTarget>();
        return body != null && body.Wears && melee != null && melee.IsLying ? body.LyingDamage : 1f;
    }

    // Someone else's kill, seen here: the body falls as their PC runs it (ZombieCorpse), and the enemy waits hidden for
    // the host to remove it. Known: the killing hit came with it, so this PC knows whose PC runs the fall.
    private void ShowHealth(float health, in DamageHit hit, bool known)
    {
        if (!IsAlive) return;
        Health = Mathf.Min(health, MaximumHealth);
        if (IsAlive) return;
        if (body != null) body.Fall(hit, false, known ? ZombieCorpse.RunnerOf(hit, true, out _) : null);
        if (network != null && network.IsCopy) network.HideUntilRemoved();
    }

    // A live table edit keeps the same health fraction instead of healing or killing enemies mid-fight.
    void ITableBound.OnTableApplied()
    {
        if (appliedMaximum > 0f && IsAlive) Health = Mathf.Max(.01f, Health / appliedMaximum * MaximumHealth);
        appliedMaximum = MaximumHealth;
    }

    private void Die(in DamageHit hit)
    {
        // 웨이브 관리자에게 처치 사실을 알린다.
        Died?.Invoke(this);

        // The body falls as its own corpse, run here if this PC's player landed the blow (or the host, for no player).
        if (body != null)
        {
            var runner = ZombieCorpse.RunnerOf(hit, network != null && network.IsSpawned, out bool here);
            body.Fall(hit, here, runner);
        }
        if (network != null && network.IsCopy) network.HideUntilRemoved();
        else Destroy(gameObject);
    }
}
