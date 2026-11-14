
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
    // Going to its place in the crowd (ZombieCrowd), it stops this close to it (m). Arrived: within its stopping
    // distance and this much of where it was going (m).
    private const float SpotStop = .15f, ArriveSlack = .2f;
    private float updateTimer, appliedPace = 1f;
    // Where the path it last asked for leads and when it asked (game time), and the last frame it was chasing: taking
    // the chase up again (after a stun, a swing, a search, a leap, planks) it asks afresh, since whatever path the agent
    // holds then may be another part's.
    private Vector3 pathGoal;
    private float pathAt = float.NegativeInfinity;
    private int chasedFrame = -2;
    private MeleeCombatTarget melee;
    // Its place in the crowd as last worked out, and for whom.
    private PlayerMovement spotFor;
    private Vector3 spot;
    private bool spotted;

    /// <summary>A memo elite's own part (ZombieElite sets it as it is put on): it may move the body itself.</summary>
    internal ZombieElite Elite { get; set; }

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
        // It goes at a Support's barricade when one closes its way or draws it (ZombieBreach, on the host).
        if (!TryGetComponent(out breach)) breach = gameObject.AddComponent<ZombieBreach>();
        // Its turn to look at its path falls on a frame of its own: a wave made together took every turn together.
        updateTimer = Random.Range(0f, updateInterval);
    }
    private ZombieBreach breach;

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        long began = ZombieCost.Begin();
        Chase();
        ZombieCost.End(ZombieCost.Part.Chase, began);
    }

    private void Chase()
    {
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
        // So does a charger winding up, charging or sliding to a stop (ZombieCharger), and a memo elite moving itself (a
        // ninja vanishing, a candyman dragging someone off, a breadbag's cut: ZombieElite, which tells this one it is there).
        if (charger == null) charger = GetComponent<ZombieCharger>();
        if (charger != null && charger.Driving) return;
        if (Elite != null && Elite.Driving) return;
        // So does one going at a Support's barricade: up to the planks, then standing at them (ZombieBreach).
        if (breach != null && breach.Driving) return;
        if (melee == null) melee = GetComponent<MeleeCombatTarget>();
        if (PlayerExecution.Suppresses(this) || (enemyHealth != null && enemyHealth.WavePaused) || (melee != null && melee.IsStunned))
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
        updateTimer -= Time.deltaTime;
        bool resumed = chasedFrame != Time.frameCount - 1;
        chasedFrame = Time.frameCount;

        // Its place round them is worked out as often as its path (it was every frame, a walk-mesh query each time, used
        // only here; the memo's item 40), and at once for someone new or as it takes the chase up again.
        if (updateTimer <= 0f || spotFor != player || resumed)
        {
            bool fresh = spotFor != player || resumed;
            // Far from every player its path is looked at less often (ZombieLod).
            updateTimer = ZombieLod.ChaseEvery(transform.position, updateInterval);
            spotFor = player;
            long spotBegan = ZombieCost.Begin();
            spotted = crowd != null && crowd.Spot(player, close, out spot, out _);
            ZombieCost.End(ZombieCost.Part.Spot, spotBegan);
            agent.stoppingDistance = spotted ? SpotStop : close;
            Vector3 goal = spotted ? spot : player.transform.position;
            // A new path only when the one it has no longer serves (NeedsPath): asked for every turn, most came out the
            // same and held up the ones that mattered.
            if (fresh || NeedsPath(goal))
            {
                long pathBegan = ZombieCost.Begin();
                agent.SetDestination(goal);
                ZombieCost.End(ZombieCost.Part.Path, pathBegan);
                ZombieCost.Count(ZombieCost.Tally.Paths);
                pathGoal = goal;
                pathAt = Time.time;
            }
            else ZombieCost.Count(ZombieCost.Tally.PathsKept);
        }
        // Standing at its place it faces whom it fights: the agent turns it only while it walks.
        if (spotted && !agent.pathPending && agent.remainingDistance <= SpotStop + .2f)
        {
            Vector3 toward = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
            if (toward.sqrMagnitude > 1e-3f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), agent.angularSpeed * Time.deltaTime);
        }
    }

    // Whether the path it has no longer serves. Its goal has moved too far from where that path leads: a set share of
    // the way there, so one far off keeps its path through a step of theirs and one beside them follows every move
    // (ZombieLod.PathShift; 0 asks every turn, as it used to). Or the path is old (PathKeep). Or the way found stops
    // short of the goal (they stand where it can't walk) and it is time to try again (PathRetry): that search runs
    // through everything it can reach before it gives up, the dearest there is, and it was run every turn by every
    // zombie under a player on a crate. Or it has no path and isn't there yet.
    private bool NeedsPath(Vector3 goal)
    {
        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        float away = flat.magnitude, shift = ZombieLod.PathShift(away);
        if (shift <= 0f || (goal - pathGoal).sqrMagnitude > shift * shift) return true;
        float age = Time.time - pathAt;
        if (age >= ZombieLod.PathKeep) return true;
        if (agent.pathPending) return false;
        if (agent.hasPath) return agent.pathStatus != NavMeshPathStatus.PathComplete && age >= ZombieLod.PathRetry;
        return away > agent.stoppingDistance + ArriveSlack;
    }
}
