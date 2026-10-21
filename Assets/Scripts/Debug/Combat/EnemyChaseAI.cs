
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyChaseAI : MonoBehaviour
{
    [SerializeField] private float updateInterval = 0.15f;

    private NavMeshAgent agent;
    private PlayerMovement player;
    private EnemyHealth enemyHealth;
    private EnemyCombatAI combatAI;
    private float updateTimer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyHealth = GetComponent<EnemyHealth>();
        combatAI = GetComponent<EnemyCombatAI>();
    }

    private void Start()
    {
        player = FindFirstObjectByType<PlayerMovement>();
    }

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (PlayerExecution.Suppresses(this) || (enemyHealth != null && enemyHealth.WavePaused) || GetComponent<MeleeCombatTarget>()?.IsStunned == true)
        { agent.isStopped = true; return; }
        if (enemyHealth != null && !enemyHealth.IsAlive)
        {
            agent.isStopped = true;
            return;
        }

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerMovement>();
            return;
        }

        if (!player.IsInitialized ||
            !player.DamageReceiver.IsAlive ||
            !agent.isOnNavMesh)
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
