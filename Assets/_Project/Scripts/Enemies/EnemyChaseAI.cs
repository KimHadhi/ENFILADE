
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyChaseAI : MonoBehaviour
{
    [SerializeField] private float updateInterval = 0.15f;

    private NavMeshAgent agent;
    private EnemyTargeting targeting;
    private EnemyHealth enemyHealth;
    private EnemyCombatAI combatAI;
    private EnemyNetwork network;
    private float updateTimer, appliedPace = 1f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        network = GetComponent<EnemyNetwork>();
        enemyHealth = GetComponent<EnemyHealth>();
        combatAI = GetComponent<EnemyCombatAI>();
        targeting = GetComponent<EnemyTargeting>();
        if (targeting == null) targeting = gameObject.AddComponent<EnemyTargeting>();
    }

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        float pace = network != null ? network.Pace : 1f;
        if (!Mathf.Approximately(pace, appliedPace)) { agent.speed *= pace / appliedPace; appliedPace = pace; }
        if (PlayerExecution.Suppresses(this) || (enemyHealth != null && enemyHealth.WavePaused) || GetComponent<MeleeCombatTarget>()?.IsStunned == true)
        { agent.isStopped = true; return; }
        if (enemyHealth != null && !enemyHealth.IsAlive)
        {
            agent.isStopped = true;
            return;
        }

        var player = targeting.Player;
        if (player == null || !agent.isOnNavMesh)
        {
            agent.isStopped = true;
            return;
        }

        if (combatAI != null && combatAI.IsPreparingAttack)
        {
            agent.isStopped = true;
            return;
        }

        agent.isStopped = false;
        updateTimer -= Time.deltaTime;

        if (updateTimer <= 0f)
        {
            updateTimer = updateInterval;
            agent.SetDestination(player.transform.position);
        }
    }
}
