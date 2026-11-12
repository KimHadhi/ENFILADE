using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A zombie going at a Support's barricade (the user, 2026-11-12: "바리케이드를 부술 줄 모르는데 얘들이. 그리고 바리케이드에
/// 어그로 끌리게 했으면 좋겠어. 그래야 바리케이드 부술 거 아니야."). The planks carve the walk mesh (Barricade), so a zombie
/// whose way they closed stood at the end of its path or walked round them, and they wore down only when a swing at a
/// player behind them happened to land on them or a crowd pressed on them.
/// On the host, an awake zombie on its feet within pullRadius of the planks weighs them against its quarry every
/// thinkEvery. It goes for them when they close its way (no walk to its quarry, or one longer than roundShare times the
/// straight line and roundExtra more), or when they draw it harder than its quarry: planks d away draw it as a player
/// d / pullShare away would, weaker than a player in the open (the Support's taunt). It walks to its own side where it
/// meets them, stands square to them and swings its own swing (EnemyCombatAI.TickBreach: windup, cue and voice on every
/// PC, nobody's to answer). Each blow lands through the barricade's own damage path (Barricade.ReceiveHit: the planks
/// come off from the top, it bursts at 0, every PC shows it). Its kind's row says how hard: a brute smashes through, a
/// breadbag's saw cuts; an exploder doesn't swing but lights its fuse at them and its burst wears them (ZombieExploder).
/// It leaves them for a player it can walk to within playerFirst or much nearer than they draw it, when no blow of its
/// has landed for giveUpSeconds (it then lets those planks be for restSeconds), and when they fall. Nothing a zombie does
/// to them costs the Support anything. Numbers: Tables/ZombieBreach (Default, and a row per kind for blow).
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieBreach : MonoBehaviour
{
    public const string TableName = "ZombieBreach", TableRow = "Default";
    // Planks on another floor (m up or down) are none of its business; how near either end of them it may stand (m).
    private const float FloorGap = 1.6f, EndRoom = .25f;
    // How high on the planks its blow lands (m), and how far out from them it stands beyond its own radius (m).
    private const float BlowHeight = .75f, StandOff = .2f;
    // On another PC: a swing that ends with its middle this near the planks (m) lands on them there.
    private const float HeardGap = 1.3f;

    [SerializeField, Min(0f), Tooltip("Within this of a barricade's planks (m) an awake zombie weighs them against its quarry.")]
    private float pullRadius = 7f;
    [SerializeField, Range(.05f, 1f), Tooltip("How hard the planks draw it: planks d away draw it as a player d / this away would (.5: planks 3 m off = a player 6 m off).")]
    private float pullShare = .5f;
    [SerializeField, Min(0f), Tooltip("A player it can walk to within this (m) always comes first: it leaves the planks for them.")]
    private float playerFirst = 3f;
    [SerializeField, Min(1f), Tooltip("Its way to its quarry counts as closed when the walk is longer than this many times the straight line, plus roundExtra.")]
    private float roundShare = 2f;
    [SerializeField, Min(0f), Tooltip("Added to that (m).")]
    private float roundExtra = 6f;
    [SerializeField, Min(.1f), Tooltip("It swings once the space between its body and the planks is this small (m).")]
    private float reach = .5f;
    [SerializeField, Min(.5f), Tooltip("No blow of its landed on them for this long (s): it gives up on those planks (no room at them, or stuck).")]
    private float giveUpSeconds = 8f;
    [SerializeField, Min(0f), Tooltip("Planks it gave up on are left alone this long (s).")]
    private float restSeconds = 8f;
    [SerializeField, Min(.1f), Tooltip("How often it weighs the planks against its quarry (s).")]
    private float thinkEvery = .4f;
    [SerializeField, Min(0f), Tooltip("What one swing of its takes off the planks, before Support.barricadeBlow (its kind's row). 0: it doesn't swing at them.")]
    private float blow = 10f;

    private static readonly Vector3[] corners = new Vector3[64];

    private EnemyHealth health;
    private EnemyNetwork network;
    private NavMeshAgent agent;
    private ZombieMind mind;
    private MeleeCombatTarget melee;
    private EnemyTargeting targeting;
    private EnemyCombatAI combat;
    private ZombieGrip grip;
    private ZombieRunner runner;
    private ZombieCharger charger;
    private ZombieElite elite;
    private ZombieType type;
    private NavMeshPath path;
    private Barricade target, restFrom;
    private Vector3 spot, face;
    private float nextThink, nextLook, nextPath, lastBlow, restUntil;
    private bool inReach;
    private string boundKind;

    /// <summary>The planks it is going at, or null.</summary>
    public Barricade Target => target;
    /// <summary>It walks itself, to the planks and standing at them: EnemyChaseAI leaves it be.</summary>
    public bool Driving => target != null;
    /// <summary>At the planks: EnemyCombatAI swings at them (Face).</summary>
    public bool Swinging => target != null && inReach;
    /// <summary>Where on the planks its blow lands.</summary>
    public Vector3 Face => face;
    /// <summary>What one swing of its takes off the planks (its kind's); 0 for one that doesn't swing at them (an exploder).</summary>
    public float Blow => blow;

    // The host's (or a lone PC's) zombie is the real one; a copy only shows what it does.
    private bool Runs => network == null || !network.IsCopy;
    private float Radius => agent.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
    private NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        agent = GetComponent<NavMeshAgent>();
        mind = GetComponent<ZombieMind>();
        melee = GetComponent<MeleeCombatTarget>();
        targeting = GetComponent<EnemyTargeting>();
        combat = GetComponent<EnemyCombatAI>();
        GameTables.Bind(TableName, TableRow, this);
        nextThink = Random.Range(0f, thinkEvery);
    }

    private void Update()
    {
        if (!Runs) return;
        // Nothing to weigh till its next look, unless it is at some planks now; nothing at all while none stand.
        if (target == null && (Time.time < nextThink || Barricade.Standing.Count == 0)) return;
        if (!Free() || (target != null && !Stands(target)))
        {
            Drop();
            nextThink = Time.time + thinkEvery;
            return;
        }
        if (Time.time >= nextThink)
        {
            nextThink = Time.time + thinkEvery * Random.Range(.85f, 1.15f);
            Weigh();
        }
        if (target != null) Approach();
    }

    /// <summary>The host: its swing at the planks lands now (EnemyCombatAI.TickBreach).</summary>
    internal void Hit()
    {
        if (target == null || blow <= 0f) return;
        Vector3 from = transform.position + Vector3.up * 1.2f;
        Vector3 way = Vector3.ProjectOnPlane(face - from, Vector3.up);
        way = way.sqrMagnitude > 1e-4f ? way.normalized : transform.forward;
        target.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), blow, DamageKind.Attack, face, way, gameObject, false, 0f, true,
            origin: from));
        lastBlow = Time.time;
    }

    /// <summary>
    /// On another PC, a copy's plain swing ended that wasn't this PC's to judge: if it stands right at some planks, the
    /// blow lands on them, so this PC hears it there (the host's blow wears them; their health follows).
    /// </summary>
    internal static void HeardOnCopy(Transform zombie)
    {
        if (zombie == null) return;
        foreach (var each in Barricade.Standing)
        {
            if (each == null || each.Type != Barricade.Kind.Planks || Gap(each, zombie.position, out Vector3 local) > HeardGap
                || Mathf.Abs(local.x) > Barricade.Width * .5f + .3f) continue;
            var mine = PlayerRoster.Local;
            float distance = mine != null ? Vector3.Distance(mine.transform.position, each.transform.position) : 0f;
            KitSound.Play(KitSound.Cue.Splinter, .5f * Mathf.Clamp01(1f - distance / 35f));
            return;
        }
    }

    /// <summary>
    /// Every PC, as a plain swing ends (EnemyCombatAI): a brute's blow at planks right before it smashes into them with a
    /// burst from Piloto's Big Pointblank, toned down to a punchy chip-and-shock (Tables/PackFx bruteSmash; the user's go
    /// at moment-vfx's Realistic Gunslinger map, "ㅇㅇ 제안대로 써"). Its wear is the host's, as ever.
    /// </summary>
    internal static void Smash(Transform zombie)
    {
        if (zombie == null || !zombie.TryGetComponent(out ZombieType type) || type.Kind != ZombieKind.Brute) return;
        foreach (var each in Barricade.Standing)
        {
            if (each == null || each.Type != Barricade.Kind.Planks || Gap(each, zombie.position, out Vector3 local) > HeardGap
                || Mathf.Abs(local.x) > Barricade.Width * .5f + .3f) continue;
            float side = local.z >= 0f ? 1f : -1f;
            var planks = each.transform;
            Vector3 point = planks.TransformPoint(new Vector3(Mathf.Clamp(local.x, -Barricade.Width * .5f, Barricade.Width * .5f),
                BlowHeight, side * Barricade.Depth * .5f));
            // Into the planks, the way its blow goes.
            PackFx.Play("bruteSmash", point, Quaternion.LookRotation(planks.forward * -side));
            return;
        }
    }

    /// <summary>Whether some standing planks lie within <paramref name="range"/> of <paramref name="point"/> (m, along the ground).</summary>
    internal static bool PlanksNear(Vector3 point, float range)
    {
        foreach (var each in Barricade.Standing)
            if (each != null && each.Type == Barricade.Kind.Planks && Mathf.Abs(each.transform.position.y - point.y) <= FloorGap
                && Gap(each, point, out _) <= range) return true;
        return false;
    }

    /// <summary>How far <paramref name="point"/> is from barricade <paramref name="barricade"/> (m, along the ground), and where it is in its own space.</summary>
    internal static float Gap(Barricade barricade, Vector3 point, out Vector3 local)
    {
        local = barricade.transform.InverseTransformPoint(point);
        float along = Mathf.Max(0f, Mathf.Abs(local.x) - Barricade.Width * .5f);
        float through = Mathf.Max(0f, Mathf.Abs(local.z) - Barricade.Depth * .5f);
        return Mathf.Sqrt(along * along + through * through);
    }

    // Awake, on its feet and its own master: not held, stunned or down, holding nobody, not moving itself (a runner's
    // leap, a charger's run, an elite's move), and not in the middle of a swing at a player (finished first: it may land
    // on the planks anyway).
    private bool Free()
    {
        if (health == null || !health.IsAlive || health.WavePaused) return false;
        if (mind != null && !mind.Ready) return false;
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
        if (melee != null && (melee.IsStunned || melee.IsLying || melee.IsHeld)) return false;
        // Parts its kind may add after it was made (ZombieType, EnemyHealth), looked for now and then.
        if (Time.time >= nextLook)
        {
            nextLook = Time.time + 1f;
            if (grip == null) TryGetComponent(out grip);
            if (runner == null) TryGetComponent(out runner);
            if (charger == null) TryGetComponent(out charger);
            if (elite == null) TryGetComponent(out elite);
        }
        if (grip != null && grip.IsHolding) return false;
        if ((runner != null && runner.Driving) || (charger != null && charger.Driving) || (elite != null && elite.Driving)) return false;
        return target != null || combat == null || !combat.IsPreparingAttack;
    }

    // The nearest planks within pullRadius (not ones it gave up on lately), and whether to go for them or keep at them.
    private void Weigh()
    {
        if (Barricade.Standing.Count == 0) { Drop(); return; }
        Barricade best = null;
        float nearest = float.MaxValue;
        Vector3 feet = transform.position - Vector3.up * agent.baseOffset;
        foreach (var each in Barricade.Standing)
        {
            if (each == null || each.Type != Barricade.Kind.Planks || (each == restFrom && Time.time < restUntil)) continue;
            if (Mathf.Abs(each.transform.position.y - feet.y) > FloorGap) continue;
            float gap = Gap(each, feet, out _);
            if (gap > pullRadius || gap >= nearest) continue;
            best = each;
            nearest = gap;
        }
        if (best == null) { Drop(); return; }
        BindKind();
        var quarry = targeting != null ? targeting.Player : null;
        bool keep = best == target ? !Leaves(quarry, nearest) : Goes(quarry, nearest);
        if (keep) Take(best);
        else Drop();
    }

    // Planks this far off are worth going for now: they close its way to its quarry, or draw it harder than its quarry.
    private bool Goes(PlayerMovement quarry, float gap)
    {
        if (quarry == null) return true;
        if (!Route(quarry, out float walk, out float straight)) return true;
        if (straight <= playerFirst) return false;
        if (walk > straight * roundShare + roundExtra) return true;
        return gap / Mathf.Max(.05f, pullShare) < walk;
    }

    // At planks this far off: it leaves them for a quarry it can walk to that is right there or much nearer than they draw.
    private bool Leaves(PlayerMovement quarry, float gap)
    {
        if (quarry == null || !Route(quarry, out float walk, out float straight)) return false;
        if (straight <= playerFirst) return true;
        if (walk > straight * roundShare + roundExtra) return false;
        return walk < gap / Mathf.Max(.05f, pullShare) * .5f;
    }

    // Whether it can walk to its quarry, how long that walk is and how far they are in a straight line (m).
    private bool Route(PlayerMovement quarry, out float walk, out float straight)
    {
        Vector3 from = agent.nextPosition, to = quarry.transform.position;
        straight = Vector3.Distance(from, to);
        walk = float.MaxValue;
        if (!NavMesh.SamplePosition(to, out NavMeshHit end, 2f, Filter)) return false;
        path ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(from, end.position, Filter, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        int count = path.GetCornersNonAlloc(corners);
        walk = 0f;
        for (int i = 1; i < count; i++) walk += Vector3.Distance(corners[i - 1], corners[i]);
        return true;
    }

    private void Take(Barricade barricade)
    {
        if (barricade != target)
        {
            target = barricade;
            inReach = false;
            nextPath = 0f;
            // The clock to give up runs from when it sets off for them.
            lastBlow = Time.time;
        }
        Aim();
    }

    // Where it stands at the planks and where its blow lands: on its own side, where it meets them along their length.
    private void Aim()
    {
        var planks = target.transform;
        Vector3 local = planks.InverseTransformPoint(transform.position);
        float side = local.z >= 0f ? 1f : -1f;
        float along = Mathf.Clamp(local.x, -Barricade.Width * .5f + EndRoom, Barricade.Width * .5f - EndRoom);
        Vector3 wanted = planks.TransformPoint(new Vector3(along, 0f, side * (Barricade.Depth * .5f + Radius + StandOff)));
        spot = NavMesh.SamplePosition(wanted, out NavMeshHit hit, 1f, Filter) ? hit.position : wanted;
        face = planks.TransformPoint(new Vector3(along, BlowHeight, side * Barricade.Depth * .5f));
    }

    // Up to the planks; there, square to them, still (EnemyCombatAI swings). It gives up if no blow lands for a while.
    private void Approach()
    {
        float gap = Gap(target, transform.position - Vector3.up * agent.baseOffset, out Vector3 local);
        inReach = Mathf.Abs(local.x) <= Barricade.Width * .5f + .2f && gap <= Radius + reach;
        if (inReach)
        {
            if (!agent.isStopped) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            Vector3 toward = Vector3.ProjectOnPlane(face - transform.position, Vector3.up);
            if (toward.sqrMagnitude > 1e-3f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), agent.angularSpeed * Time.deltaTime);
        }
        else
        {
            agent.isStopped = false;
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + .3f;
                agent.stoppingDistance = .05f;
                agent.SetDestination(spot);
            }
        }
        if (Time.time - lastBlow > giveUpSeconds)
        {
            restFrom = target;
            restUntil = Time.time + restSeconds;
            Drop();
        }
    }

    private void Drop()
    {
        if (target == null) return;
        target = null;
        inReach = false;
        // A swing at the planks under way stops with them (the chase takes it on from here: EnemyChaseAI).
        if (combat != null && combat.IsPreparingAttack) combat.CancelAttack();
    }

    private static bool Stands(Barricade barricade)
    {
        foreach (var each in Barricade.Standing) if (each == barricade) return true;
        return false;
    }

    // Its kind's row over the Default one (a brute's blow), once its kind is known (ZombieType names it after it is made).
    private void BindKind()
    {
        if (type == null) TryGetComponent(out type);
        string kind = type != null ? type.Kind.ToString() : null;
        if (kind == null || kind == boundKind) return;
        boundKind = kind;
        GameTables.Bind(TableName, kind, this);
    }
}
