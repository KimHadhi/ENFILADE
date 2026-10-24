
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent, RequireComponent(typeof(MeleeCombatTarget))]
public sealed class EnemyHealth : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName
{
    [SerializeField, Min(1f)]
    private float maximumHealth = 100f;

    [Tooltip("체력 바 위에 표시할 이름. 비워두면 오브젝트 이름((Clone)/(Console) 제외)을 사용한다.")]
    [SerializeField] private string displayName = "";

    public string DisplayName => displayName;
    public float Health { get; private set; }
    public float MaximumHealth => Mathf.Max(1f, maximumHealth);
    public bool IsAlive => Health > 0f;
    public bool WavePaused { get; private set; }
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private NavMeshAgent pausedAgent;
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

    private void Awake()
    {
        Health = maximumHealth;
        if (GetComponent<MeleeCombatTarget>() == null) gameObject.AddComponent<MeleeCombatTarget>();
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
        Health = Mathf.Max(0f, Health - hit.Amount);
        UltimateCharge.AwardEnemyHit(hit);
        // Report before Die(): popups bind to this transform and keep its last position after Destroy.
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth);

        Debug.Log(
            $"[{name}] 피해: {hit.Amount}, 남은 체력: {Health}/{maximumHealth}",
            this
        );

        if (!IsAlive)
        {
            Die();
        }

        return DamageResult.Damaged;
    }

    private void Die()
    {
        Debug.Log($"[{name}] 사망!", this);

        // 웨이브 관리자에게 처치 사실을 알린다.
        Died?.Invoke(this);

        Destroy(gameObject);
    }
}
