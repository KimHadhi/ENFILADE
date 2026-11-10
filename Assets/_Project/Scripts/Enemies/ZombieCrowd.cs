using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Stage 4E, a zombie in a crowd, where the enemy really runs (the host, or the PC alone).
/// 511, attack places: around each player lie `slots` places, evenly round, as close as the zombie fights from
/// (EnemyCombatAI.ApproachDistance). A zombie within slotRange of the player it is after takes the free one nearest the
/// side it comes from and walks there, facing them once it stands; with all taken it waits waitGap further out on its
/// side, out of reach, until one comes free. A place inside a wall, off the ground or past an edge isn't used, so in a
/// corner fewer reach them. A place not asked for in a moment is free again (whatever the zombie went on to do).
/// 523, pressed into a wall: a zombie that finds itself pressing into a player steps back out of them itself, along the
/// navmesh, so a crowd never squeezes anyone into a corner or a wall; the player is blocked, never shoved. Not while it
/// holds them (ZombieGrip), is held, or lies down; nor against a rider on its head (EnemyHead).
/// 524, the body: the zombie's capsule and its agent are bodyRadius round, about a body's depth front to back, rather
/// than a metre across (ZombieBody fits it as it dresses), so crowds stand closer and players come right up to them.
/// Numbers: Tables/ZombieCrowd.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieCrowd : MonoBehaviour
{
    public const string TableName = "ZombieCrowd", TableRow = "Default";

    [SerializeField, Min(.1f), Tooltip("The capsule's and the agent's radius (m): about a body's depth front to back.")]
    private float bodyRadius = .28f;
    [SerializeField, Min(1), Tooltip("Places round each player to fight from.")]
    private int slots = 8;
    [SerializeField, Min(0f), Tooltip("Within this of the player it is after (m), a zombie goes for a place; further off it just comes on.")]
    private float slotRange = 8f;
    [SerializeField, Min(0f), Tooltip("All places taken, it waits this much further out than a place (m), on its own side.")]
    private float waitGap = 1.2f;
    [SerializeField, Min(.05f), Tooltip("How often each player's places are looked at again for walls and edges (s).")]
    private float slotCheckEvery = .3f;
    [SerializeField, Min(0f), Tooltip("Stepping back out of a player, it keeps this much air between them (m).")]
    private float yieldGap = .03f;

    // A place not asked for this long is free again (s). How far a place may be from the navmesh and still be one (m),
    // and how far above or below the player.
    private const float SlotHeld = 1f, SlotSnap = .35f, SlotRise = 1f;
    private static readonly Dictionary<PlayerMovement, Ring> rings = new Dictionary<PlayerMovement, Ring>();
    private static int wallMask;

    private sealed class Ring
    {
        public ZombieCrowd[] Taken;
        public float[] Until;
        public bool[] Open;
        public float CheckedAt = float.NegativeInfinity;
    }

    private NavMeshAgent agent;
    private EnemyHealth health;
    private EnemyNetwork network;
    private MeleeCombatTarget melee;
    private ZombieGrip grip;
    private PlayerMovement slotPlayer;
    private int slot = -1;

    /// <summary>The capsule's and the agent's radius (ZombieBody, as it dresses).</summary>
    public float BodyRadius => bodyRadius;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics()
    {
        rings.Clear();
        wallMask = 0;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        melee = GetComponent<MeleeCombatTarget>();
        GameTables.Bind(TableName, TableRow, this);
    }

    private bool Runs => network == null || !network.IsCopy;

    /// <summary>
    /// Where to go to fight this player from (EnemyChaseAI), as close as it fights from (m): its place round them, or,
    /// with all taken, a spot further out on its side to wait at. False when the player is too far for places: it just
    /// comes on.
    /// </summary>
    internal bool Spot(PlayerMovement player, float close, out Vector3 spot, out bool placed)
    {
        spot = default;
        placed = false;
        if (player == null) return false;
        Vector3 center = player.transform.position;
        Vector3 bearing = Vector3.ProjectOnPlane(transform.position - center, Vector3.up);
        if (bearing.magnitude > slotRange) { Leave(); return false; }
        bearing = bearing.sqrMagnitude > 1e-4f ? bearing.normalized : Vector3.ProjectOnPlane(-player.transform.forward, Vector3.up).normalized;
        var ring = RingOf(player, close);
        if (slotPlayer != player) Leave();
        if (slot >= 0 && (!ring.Open[slot] || (ring.Taken[slot] != this && Holds(ring, slot)))) Leave();
        if (slot < 0) Claim(ring, player, bearing);
        if (slot >= 0)
        {
            ring.Taken[slot] = this;
            ring.Until[slot] = Time.time + SlotHeld;
            placed = true;
            spot = OnGround(center + Direction(ring, slot) * close, center);
            return true;
        }
        spot = OnGround(center + bearing * (close + waitGap), center);
        return true;
    }

    // The free place nearest the side it comes from.
    private void Claim(Ring ring, PlayerMovement player, Vector3 bearing)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < ring.Taken.Length; i++)
        {
            if (!ring.Open[i] || Holds(ring, i)) continue;
            float off = Vector3.Angle(bearing, Direction(ring, i));
            if (off < best) { best = off; slot = i; }
        }
        if (slot >= 0) slotPlayer = player;
    }

    // Someone still holds this place.
    private static bool Holds(Ring ring, int index)
    {
        var holder = ring.Taken[index];
        return holder != null && holder.isActiveAndEnabled && Time.time < ring.Until[index];
    }

    private void Leave()
    {
        if (slot >= 0 && slotPlayer != null && rings.TryGetValue(slotPlayer, out var ring) && slot < ring.Taken.Length
            && ring.Taken[slot] == this)
        {
            ring.Taken[slot] = null;
            ring.Until[slot] = 0f;
        }
        slot = -1;
        slotPlayer = null;
    }

    private Ring RingOf(PlayerMovement player, float close)
    {
        int count = Mathf.Max(1, slots);
        if (!rings.TryGetValue(player, out var ring) || ring.Taken.Length != count)
        {
            ring = new Ring { Taken = new ZombieCrowd[count], Until = new float[count], Open = new bool[count] };
            rings[player] = ring;
        }
        if (Time.time - ring.CheckedAt >= slotCheckEvery)
        {
            ring.CheckedAt = Time.time;
            Vector3 center = player.transform.position;
            Vector3 chest = center + Vector3.up;
            if (wallMask == 0)
            {
                int enemy = LayerMask.NameToLayer("Enemy");
                wallMask = ~((enemy >= 0 ? 1 << enemy : 0) | 1 << 31);
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 point = center + Direction(ring, i) * close;
                ring.Open[i] = NavMesh.SamplePosition(point, out var hit, SlotSnap, NavMesh.AllAreas)
                    && Mathf.Abs(hit.position.y - center.y) <= SlotRise
                    && !Physics.Linecast(chest, hit.position + Vector3.up, wallMask, QueryTriggerInteraction.Ignore);
            }
        }
        return ring;
    }

    private static Vector3 Direction(Ring ring, int index)
    {
        float angle = index * Mathf.PI * 2f / ring.Taken.Length;
        return new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
    }

    // On the navmesh near there, or the player's own spot if there is none.
    private static Vector3 OnGround(Vector3 point, Vector3 fallback)
        => NavMesh.SamplePosition(point, out var hit, 1f, NavMesh.AllAreas) ? hit.position : fallback;

    // 523: pressing into a player, it steps back out of them itself.
    private void LateUpdate()
    {
        if (!Runs || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || agent.isOnOffMeshLink) return;
        if (health != null && !health.IsAlive) return;
        if (grip == null) grip = GetComponent<ZombieGrip>();
        if ((grip != null && grip.IsHolding) || (melee != null && (melee.IsHeld || melee.IsLying))) return;
        float mine = agent.radius;
        Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
        float top = feet.y + agent.height;
        foreach (var player in PlayerRoster.All)
        {
            if (player == null || !player.IsInitialized || player.IsDead) continue;
            if (!player.TryGetComponent(out CharacterController walker) || !walker.enabled) continue;
            Vector3 theirs = player.transform.position;
            // Only body against body: not a rider on its head, nor someone far above or below.
            if (theirs.y > top - .3f || theirs.y + walker.height < feet.y || EnemyHead.Carries(this, player)) continue;
            Vector3 apart = Vector3.ProjectOnPlane(transform.position - theirs, Vector3.up);
            float least = mine + walker.radius * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z)) + yieldGap;
            float distance = apart.magnitude;
            if (distance >= least) continue;
            Vector3 away = distance > 1e-3f ? apart / distance : Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
            agent.Move(away * (least - distance));
        }
    }

    private void OnDisable() => Leave();
}
