
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
    private ZombieMind mind;
    private ZombieGrip grip;
    private ZombieRunner runner;
    private ZombieCharger charger;
    private ZombieCrowd crowd;
    // Going to its place in the crowd (ZombieCrowd), it stops this close to it (m).
    private const float SpotStop = .15f;
    private float updateTimer, appliedPace = 1f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        network = GetComponent<EnemyNetwork>();
        enemyHealth = GetComponent<EnemyHealth>();
        combatAI = GetComponent<EnemyCombatAI>();
        mind = GetComponent<ZombieMind>();
        grip = GetComponent<ZombieGrip>();
        targeting = GetComponent<EnemyTargeting>();
        if (targeting == null) targeting = gameObject.AddComponent<EnemyTargeting>();
    }

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        float pace = network != null ? network.Pace : 1f;
        if (!Mathf.Approximately(pace, appliedPace)) { agent.speed *= pace / appliedPace; appliedPace = pace; }
        // Until it is awake and roused, its mind moves it (standing, turning to look, walking over to search).
        if (mind != null && !mind.Ready) return;
        // Holding a player, it stays on them (ZombieGrip, added by EnemyHealth, maybe after Awake).
        if (grip == null) grip = GetComponent<ZombieGrip>();
        if (grip != null && grip.IsHolding) return;
        // A runner charging, leaping or skidding moves itself (ZombieRunner, added by ZombieType, maybe after Awake).
        if (runner == null) runner = GetComponent<ZombieRunner>();
        if (runner != null && runner.Driving) return;
        // So does a charger winding up, charging or sliding to a stop (ZombieCharger).
        if (charger == null) charger = GetComponent<ZombieCharger>();
        if (charger != null && charger.Driving) return;
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
        float close = combatAI != null ? combatAI.ApproachDistance : agent.stoppingDistance;
        // Stage 4E: near enough, it goes to a place of its own round them, or waits further out (ZombieCrowd, added by
        // ZombieType, maybe after Awake).
        if (crowd == null) crowd = GetComponent<ZombieCrowd>();
        Vector3 spot = default;
        bool spotted = crowd != null && crowd.Spot(player, close, out spot, out _);
        agent.stoppingDistance = spotted ? SpotStop : close;
        updateTimer -= Time.deltaTime;

        if (updateTimer <= 0f)
        {
            updateTimer = updateInterval;
            agent.SetDestination(spotted ? spot : player.transform.position);
        }
        // Standing at its place it faces whom it fights: the agent turns it only while it walks.
        if (spotted && !agent.pathPending && agent.remainingDistance <= SpotStop + .2f)
        {
            Vector3 toward = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
            if (toward.sqrMagnitude > 1e-3f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), agent.angularSpeed * Time.deltaTime);
        }
    }
}
