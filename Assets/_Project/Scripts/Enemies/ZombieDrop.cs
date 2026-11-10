using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Friend feedback C5 (a ledge the zombies never came down from was a safe spot): the walk mesh has links down every
/// ledge up to a drop height and across small gaps (GreyboxBuilder bakes them), and this carries a zombie over one: off
/// the edge in an arc and down under gravity, or a hop across, facing the way it goes, then on along its path. The body
/// shows its leap pose meanwhile (ZombieBody.Leap.cs). Runs where the zombie's agent runs (the host, or alone); copies
/// follow its position. Killed or knocked down on the way, it is put where the link ends at once.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieDrop : MonoBehaviour
{
    // The game's gravity (m/s²), the arc over a drop's edge and over a hop (m), and the shortest crossing (s).
    private const float Gravity = 20f, DropArc = .3f, HopArc = .5f, Shortest = .3f;

    private NavMeshAgent agent;
    private EnemyNetwork network;
    private EnemyHealth health;
    private MeleeCombatTarget target;
    private Vector3 from, to;
    private float time, length, arc;
    private bool falls;

    /// <summary>In the air between a link's two ends.</summary>
    public bool Dropping { get; private set; }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        network = GetComponent<EnemyNetwork>();
        health = GetComponent<EnemyHealth>();
        target = GetComponent<MeleeCombatTarget>();
        if (agent != null) agent.autoTraverseOffMeshLink = false;
    }

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || (network != null && network.IsCopy))
        {
            Dropping = false;
            return;
        }
        if (!Dropping)
        {
            if (!agent.isOnOffMeshLink) return;
            Begin(agent.currentOffMeshLinkData);
        }
        if ((health != null && !health.IsAlive) || (target != null && target.IsKnockedDown)) { Land(); return; }
        time +=Time.deltaTime * (network != null ? network.Pace : 1f);
        float share = Mathf.Clamp01(time / length);
        Vector3 at = Vector3.Lerp(from, to, share);
        at.y = Mathf.Lerp(from.y, to.y, Fall(share)) + 4f * arc * share * (1f - share);
        transform.position = at;
        Vector3 way = Vector3.ProjectOnPlane(to - from, Vector3.up);
        if (way.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(way), agent.angularSpeed * Time.deltaTime);
        if (share >= 1f) Land();
    }

    private void Begin(OffMeshLinkData link)
    {
        Vector3 lift = Vector3.up * agent.baseOffset;
        from = transform.position;
        to = link.endPos + lift;
        float drop = from.y - to.y, across = Vector3.ProjectOnPlane(to - from, Vector3.up).magnitude;
        falls = drop > .5f;
        arc = falls ? DropArc : HopArc;
        // Off a ledge it takes as long as falling that far; across a gap, as long as running it.
        length = Mathf.Max(Shortest, falls ? Mathf.Sqrt(2f * drop / Gravity) + .15f : across / Mathf.Max(1f, agent.speed) + .15f);
        time = 0f;
        Dropping = true;
    }

    // Down a drop the fall speeds up (slow off the edge, fast at the bottom); a hop goes evenly.
    private float Fall(float share) => falls ? share * share : share;

    private void Land()
    {
        Dropping = false;
        if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        transform.position = to;
        agent.CompleteOffMeshLink();
    }
}
