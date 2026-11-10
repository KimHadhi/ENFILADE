using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Friend feedback C5 (a ledge the zombies never came down from was a safe spot): the walk mesh has links down every
/// ledge up to a drop height and across small gaps (GreyboxBuilder bakes them), and this carries a zombie over one: off
/// the edge in an arc and down under gravity, or a hop across, facing the way it goes, then on along its path. The body
/// shows its leap pose meanwhile (ZombieBody.Leap.cs). Runs where the zombie's agent runs (the host, or alone); copies
/// follow its position. Killed or knocked down on the way, it is put where the link ends at once.
/// Stage C-23, the other way (user, 2026-11-10: zombies climb walls and ledges, so standing on something is no
/// safety): the walk mesh has no links up, so a zombie whose way to its quarry stops short, with them standing on
/// something between ClimbLeast and ClimbMost above it and within ClimbNear, goes to the foot of it, climbs straight
/// up its face and pulls itself over onto the walk mesh up there. A runner climbs twice as fast. It shows the same
/// pose as a drop for now (the pack has no climbing clips). Knocked down or killed on the way up, it is back at the foot.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieDrop : MonoBehaviour
{
    // The game's gravity (m/s²), the arc over a drop's edge and over a hop (m), and the shortest crossing (s).
    private const float Gravity = 20f, DropArc = .3f, HopArc = .5f, Shortest = .3f;
    // Climbing: how high it can and will climb (m), how near it must be to where its quarry stands, flat (m), how fast it
    // goes to the foot of the face and up it (m/s; a runner twice that up), how long getting over the edge takes (s),
    // and how often it looks for a climb (s).
    private const float ClimbLeast = .9f, ClimbMost = 4.5f, ClimbNear = 2.6f, FootSpeed = 3f, ClimbSpeed = 2.2f, OverSeconds = .35f, LookEvery = .6f;

    private NavMeshAgent agent;
    private EnemyNetwork network;
    private EnemyHealth health;
    private MeleeCombatTarget target;
    private EnemyTargeting targeting;
    private ZombieMind mind;
    private ZombieType type;
    private Vector3 from, to, foot, lip;
    private float time, length, arc, footTime, riseTime, nextLook;
    private bool falls, climbing;
    private int solidMask;

    /// <summary>In the air between a link's two ends.</summary>
    public bool Dropping { get; private set; }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        network = GetComponent<EnemyNetwork>();
        health = GetComponent<EnemyHealth>();
        target = GetComponent<MeleeCombatTarget>();
        targeting = GetComponent<EnemyTargeting>();
        mind = GetComponent<ZombieMind>();
        int enemy = LayerMask.NameToLayer("Enemy");
        solidMask = ~((enemy >= 0 ? 1 << enemy : 0) | 1 << 31);
        if (agent != null) agent.autoTraverseOffMeshLink = false;
    }

    private void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || (network != null && network.IsCopy))
        {
            Dropping = false;
            // Its agent went from under a climb (it died, it was taken away): the agent has its body back.
            if (climbing)
            {
                climbing = false;
                if (agent != null) { agent.updatePosition = true; agent.updateRotation = true; }
            }
            return;
        }
        if (climbing) { Climb(); return; }
        if (!Dropping)
        {
            if (!agent.isOnOffMeshLink) { LookForClimb(); return; }
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

    // ---- Up a face (C-23) ----

    // Now and then: awake, on its feet, its way to its quarry stopping short, and them standing on something it can climb.
    private void LookForClimb()
    {
        if (Time.time < nextLook) return;
        nextLook = Time.time + LookEvery + Random.value * .4f;
        if ((health != null && !health.IsAlive) || (mind != null && !mind.Ready) || (target != null && target.IsStunned)) return;
        if (agent.pathPending || agent.pathStatus == NavMeshPathStatus.PathComplete) return;
        if (targeting == null) targeting = GetComponent<EnemyTargeting>();
        var quarry = targeting != null ? targeting.Player : null;
        if (quarry == null) return;
        if (type == null) type = GetComponent<ZombieType>();
        if (type != null && type.Crawls) return;
        Vector3 lift = Vector3.up * agent.baseOffset, feet = transform.position - lift, them = quarry.transform.position;
        Vector3 flat = Vector3.ProjectOnPlane(them - feet, Vector3.up);
        float rise = them.y - feet.y, across = flat.magnitude;
        if (rise < ClimbLeast || rise > ClimbMost || across > ClimbNear) return;
        // Where it comes up: the walk mesh by where they stand, at their height. No walk mesh up there, no climb.
        if (!NavMesh.SamplePosition(them, out NavMeshHit top, 1.5f, NavMesh.AllAreas) || top.position.y - feet.y < ClimbLeast) return;
        Vector3 way = across > .05f ? flat / across : transform.forward;
        // The foot of the face: just short of whatever it would walk into on the way; with nothing there (an overhang),
        // under the edge it comes up over.
        Vector3 over = Vector3.ProjectOnPlane(top.position - feet, Vector3.up);
        float reach = over.magnitude;
        if (reach > .05f) way = over / reach;
        float toFace = Physics.Raycast(feet + Vector3.up * .5f, way, out RaycastHit face, reach + .5f, solidMask, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(0f, face.distance - agent.radius) : Mathf.Max(0f, reach - agent.radius - .2f);
        from = transform.position;
        foot = from + way * toFace;
        lip = new Vector3(foot.x, top.position.y + agent.baseOffset + .12f, foot.z);
        to = top.position + lift;
        // The way up and over has to be clear of the world: straight up from the foot to the top's height, then across
        // onto the top. Without it a zombie under a platform rose through the slab and one at a wall came up through it
        // (user, 2026-11-11 playtest: "올라오는 게 벽을 뚫고 올라오는 거 같은데").
        if (!ClimbIsClear(foot - lift, lip - lift, to - lift)) return;
        footTime = toFace / FootSpeed;
        riseTime = Mathf.Max(.2f, (lip.y - foot.y) / (ClimbSpeed * (type != null && type.Kind == ZombieKind.Runner ? 2f : 1f)));
        time = 0f;
        // The agent stays where it stood, on the walk mesh; the body is carried up, then the agent is put where it comes up.
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.updatePosition = false;
        agent.updateRotation = false;
        if (way.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(way);
        climbing = Dropping = true;
    }

    // A sphere the body's width rises from the foot to the top's height, then a body-high capsule crosses onto the top.
    private bool ClimbIsClear(Vector3 foot, Vector3 lip, Vector3 top)
    {
        float radius = Mathf.Clamp(agent.radius * .85f, .15f, .4f);
        Vector3 start = foot + Vector3.up * (radius + .15f), high = new Vector3(foot.x, lip.y + radius + .05f, foot.z);
        float rise = high.y - start.y;
        if (rise > 0f && Physics.SphereCast(start, radius, Vector3.up, out _, rise, solidMask, QueryTriggerInteraction.Ignore)) return false;
        Vector3 across = Vector3.ProjectOnPlane(top - foot, Vector3.up);
        float distance = across.magnitude;
        if (distance < .05f) return true;
        Vector3 low = high, upper = high + Vector3.up * Mathf.Max(0f, 1.2f - 2f * radius);
        return !Physics.CapsuleCast(low, upper, radius, across / distance, out _, distance, solidMask, QueryTriggerInteraction.Ignore);
    }

    private void Climb()
    {
        if ((health != null && !health.IsAlive) || (target != null && target.IsKnockedDown)) { EndClimb(from); return; }
        time += Time.deltaTime * (network != null ? network.Pace : 1f);
        float t = time;
        if (t < footTime) transform.position = Vector3.Lerp(from, foot, footTime > 0f ? t / footTime : 1f);
        else if ((t -= footTime) < riseTime) transform.position = Vector3.Lerp(foot, lip, t / riseTime);
        else if ((t -= riseTime) < OverSeconds) transform.position = Vector3.Lerp(lip, to, t / OverSeconds);
        else EndClimb(to);
    }

    // On the walk mesh again, where it came up or back where it started, and the agent moves it once more.
    private void EndClimb(Vector3 at)
    {
        climbing = Dropping = false;
        transform.position = at;
        if (!agent.isActiveAndEnabled) return;
        agent.Warp(at - Vector3.up * agent.baseOffset);
        agent.updatePosition = true;
        agent.updateRotation = true;
        if (agent.isOnNavMesh) agent.isStopped = false;
    }

    private void OnDisable()
    {
        if (!climbing) return;
        climbing = Dropping = false;
        if (agent != null) { agent.updatePosition = true; agent.updateRotation = true; }
    }
}
