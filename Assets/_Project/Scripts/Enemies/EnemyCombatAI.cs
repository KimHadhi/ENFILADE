
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What an attack is (EnemyCombatAI): a swing; a runner's bite at the neck (fatal if it lands); a shambler's grab,
/// which does no harm of its own but holds the player (PlayerHold) and can't be guarded or parried, only dodged; or,
/// at the user's word (lying down was safe from every swing), any zombie kneeling to eat a player lying on the ground:
/// it holds them there and bites at once; or a runner's leap (G&B-2, ZombieRunner), which pins whoever is still where
/// they stood as it began (PlayerHold) and can't be guarded or parried, only dodged or stepped out of. Saw: a breadbag's
/// chainsaw (ZombieBreadbag): the windup is the engine revving, then it cuts whoever is in front for a while. Backstab: a
/// ninja's blow from behind after it steps out of its smoke (ZombieNinja): parried only by someone turned to face it.
/// New moves go on the end: the network sends the number.
/// </summary>
public enum AttackMove : byte { Swing, Bite, Grab, Feed, Pounce, Saw, Backstab }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyCombatAI : MonoBehaviour
{
    [Header("Attack Settings")]
    [SerializeField, Min(1f)] private float attackRange = 2f;
    [Tooltip("How far above its own head it reaches (m), for someone standing on another enemy's head within attackRange across.")]
    [SerializeField, Min(0f)] private float reachAbove = .5f;
    [Tooltip("Where the chase stops, as a share of attackRange, so a stopped enemy is always close enough to swing.")]
    [SerializeField, Range(.3f, 1f)] private float approachShare = .8f;
    [SerializeField, Min(0.2f)] private float attackInterval = 1f;
    [Tooltip("Seconds from the attack cue to the hit. Keep the hit just past the cue's loudest part: that is when players press.")]
    [SerializeField, Min(0.05f)] private float windup = .45f;
    [Tooltip("Each combo part has its own cue. Only parrying the last part opens a perfect counter.")]
    [SerializeField, Min(1)] private int comboHits = 1;
    [SerializeField, Min(0f)] private float damage = 10f;
    [Tooltip("A landed hit kills outright, with no going down first. A dodge or parry still beats it.")]
    [SerializeField] private bool fatalHit;
    [Tooltip("Share of attacks (the first of a combo) that are a bite at the neck instead: a longer windup, a deeper cue, and fatal if it lands. A runner's (ZombieKinds biteShare).")]
    [SerializeField, Range(0f, 1f)] private float biteShare;
    [Tooltip("A bite's windup (s): longer than a swing's, so it can be read and dodged or parried.")]
    [SerializeField, Min(.05f)] private float biteWindup = .75f;
    [Tooltip("Guts & Blackpowder's shambler: it grabs instead of swinging unless the player is moving away from it (ZombieKinds grabs).")]
    [SerializeField] private bool grabs;
    [Tooltip("A grab's windup (s).")]
    [SerializeField, Min(.05f)] private float grabWindup = .4f;
    [Tooltip("Kneeling to eat a player lying on the ground (s): every zombie does it.")]
    [SerializeField, Min(.05f)] private float feedWindup = .6f;
    [Tooltip("The height it aims at a player lying on the ground or down (m above their feet).")]
    [SerializeField, Min(0f)] private float lyingAimHeight = .3f;
    [Tooltip("It grabs, or kneels to eat, only a player whose feet are this near its own in height (m): on the same floor. "
        + "Up on a step or down below one, they are clawed instead.")]
    [SerializeField, Min(0f)] private float holdHeight = .6f;
    [Tooltip("Kneeling to eat, it still takes someone who has crawled this far (m) past its reach since the kneel began, "
        + "while they stay lying. 0 takes them only where they lay then.")]
    [SerializeField, Min(0f)] private float feedReach = .8f;
    [Tooltip("Guts & Blackpowder's runner: it never swings; ZombieRunner charges and leaps (ZombieKinds pounces).")]
    [SerializeField] private bool pounces;
    [Tooltip("A leap's time in the air (s), from its cue to landing.")]
    [SerializeField, Min(.05f)] private float pounceWindup = .35f;
    [Tooltip("A leap catches whoever stays within this of the spot it came down on (m) for ZombieRunner's pounceDwell. ZombieRunner.LeapSpot picks the spot.")]
    [SerializeField, Min(.1f)] private float pounceRadius = 1.1f;
    [Tooltip("A brute's blow throws whoever it lands on this fast away (m/s) and this fast up (ZombieKinds knock, knockUp). 0 throws no one.")]
    [SerializeField] private Vector2 slam;
    // The bite's cue, pitched down from the swing's so it sounds heavier; a grab's a little lower too; a brute's lowest. A
    // chainsaw's cut is low and long, a ninja's blow from behind sharp.
    private const float BitePitch = .72f, GrabPitch = .85f, PouncePitch = 1.2f, SlamPitch = .6f, SawPitch = .7f, BackstabPitch = 1.35f;
    // Moving away from it faster than this (m/s), a player isn't grabbed but clawed.
    private const float AwaySpeed = 1f;
    private AttackMove move;
    private Vector3 lastPlayerPosition;
    private PlayerMovement lastPlayer;

    [Header("Aim Settings")]
    [SerializeField] private float aimHeight = 1f;
    [SerializeField] private float rayStartOffset = 0.4f;
    [SerializeField] private LayerMask hitLayers = ~0;

    private NavMeshAgent agent;
    private EnemyHealth enemyHealth;
    private MeleeCombatTarget meleeTarget;
    private EnemyTargeting targeting;
    private EnemyNetwork network;
    private ZombieMind mind;
    private PlayerMovement player;
    private ZombieGrip grip;
    private Vector3 playerVelocity;
    private Renderer indicator;
    private MaterialPropertyBlock color;

    private float timer;
    private bool preparing, aimedHere;
    private Vector3 lockedAim, lockedFeet;
    private int comboIndex;

    public bool IsPreparingAttack => preparing;
    public float ApproachDistance => attackRange * approachShare;
    /// <summary>Seconds from the attack cue to the hit (ZombieBody lands its swing clip's strike there).</summary>
    public float Windup => move == AttackMove.Bite ? biteWindup : move == AttackMove.Grab ? grabWindup
        : move == AttackMove.Feed ? feedWindup : move == AttackMove.Pounce ? pounceWindup
        : move == AttackMove.Backstab && Elite != null && Elite.WindupOf(move) > 0f ? Elite.WindupOf(move) : windup;
    /// <summary>The move under way (or last made).</summary>
    public AttackMove Move => move;
    /// <summary>
    /// A memo elite's own part (ZombieElite sets it as it is put on): it picks its kind's attack, may hold the swing back
    /// while it moves itself, and hears how its blows went.
    /// </summary>
    internal ZombieElite Elite { get; set; }
    /// <summary>The windup under way is a bite at the neck: fatal if it lands (ZombieBody shows the lunge).</summary>
    public bool IsBiting => preparing && move == AttackMove.Bite;
    /// <summary>The windup under way is a grab (ZombieBody shows the reach).</summary>
    public bool IsGrabbing => preparing && move == AttackMove.Grab;
    /// <summary>The windup under way is kneeling to eat a player lying down (ZombieBody shows it).</summary>
    public bool IsFeeding => preparing && move == AttackMove.Feed;
    /// <summary>A runner is in the air, leaping (ZombieBody shows it, ZombieRunner carries it).</summary>
    public bool IsPouncing => preparing && move == AttackMove.Pounce;
    /// <summary>The leap has come down and is waiting to see whether its quarry stays in reach (PounceOver).</summary>
    public bool PounceLanded => IsPouncing && timer >= Windup;

    /// <summary>A runner's leap (ZombieType): whether it leaps instead of swinging, its time in the air and its reach.</summary>
    internal void SetPounce(bool pounces, float windup, float radius)
    {
        this.pounces = pounces;
        pounceWindup = Mathf.Max(.05f, windup);
        pounceRadius = Mathf.Max(.1f, radius);
    }

    /// <summary>A brute's blow (ZombieType): how fast it throws the player away and up (m/s); 0 throws no one.</summary>
    internal void SetSlam(float away, float up) => slam = new Vector2(Mathf.Max(0f, away), Mathf.Max(0f, up));

    /// <summary>How this one's blow throws a player it lands on, from where it stands (zero when it throws no one).</summary>
    public Vector3 SlamVelocity(Vector3 victim)
    {
        if (slam.x <= 0f && slam.y <= 0f) return Vector3.zero;
        Vector3 away = Vector3.ProjectOnPlane(victim - transform.position, Vector3.up);
        away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;
        return away * slam.x + Vector3.up * slam.y;
    }

    /// <summary>A shambler's grab (ZombieType): whether it grabs, and the grab's windup.</summary>
    internal void SetGrab(bool grabs, float windup)
    {
        this.grabs = grabs;
        grabWindup = Mathf.Max(.05f, windup);
    }

    /// <summary>How close it attacks from (m), and where its chase stops as a share of that (ZombieType).</summary>
    internal void SetReach(float range, float approach)
    {
        if (range > 0f) attackRange = Mathf.Max(1f, range);
        if (approach > 0f) approachShare = Mathf.Clamp(approach, .3f, 1f);
    }

    /// <summary>A runner's bite (ZombieType): how many of its attacks are one, and the bite's windup.</summary>
    internal void SetBite(float share, float windup)
    {
        biteShare = Mathf.Clamp01(share);
        biteWindup = Mathf.Max(.05f, windup);
    }

    /// <summary>A zombie's kind, or its crawl, sets its swing on top of the enemy's table row (ZombieType).</summary>
    internal void SetSwing(float damage, float windup, float interval, float aimHeight)
    {
        this.damage = Mathf.Max(0f, damage);
        this.windup = Mathf.Max(.05f, windup);
        attackInterval = Mathf.Max(.2f, interval);
        this.aimHeight = aimHeight;
    }
    /// <summary>How far into the current windup it is (s), while IsPreparingAttack.</summary>
    public float WindupElapsed => preparing ? timer : 0f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyHealth = GetComponent<EnemyHealth>();
        meleeTarget = GetComponent<MeleeCombatTarget>();
        if (meleeTarget == null) meleeTarget = gameObject.AddComponent<MeleeCombatTarget>();
        targeting = GetComponent<EnemyTargeting>();
        if (targeting == null) targeting = gameObject.AddComponent<EnemyTargeting>();
        network = GetComponent<EnemyNetwork>();
        mind = GetComponent<ZombieMind>();
        grip = GetComponent<ZombieGrip>();
        indicator = GetComponent<Renderer>();
        color = new MaterialPropertyBlock();
        timer = attackInterval;
    }

    private void Update() => Tick(Time.deltaTime * (network != null ? network.Pace : 1f));
    public void Tick(float dt)
    {
        if (dt <= 0f) return;
        if (network != null && network.IsCopy) { TickCopy(dt); return; }
        if (PlayerExecution.Suppresses(this) || (meleeTarget != null && meleeTarget.IsStunned)) { CancelAttack(); return; }
        if (enemyHealth == null || !enemyHealth.IsAlive || enemyHealth.WavePaused)
            { CancelAttack(); return; }
        // Not yet awake and roused: it hasn't noticed anyone to swing at (ZombieMind).
        if (mind != null && !mind.Ready) { if (preparing) CancelAttack(); timer = attackInterval; return; }

        // Holding someone, it does nothing else until it lets go (ZombieGrip, added by EnemyHealth, maybe after Awake).
        if (grip == null) grip = GetComponent<ZombieGrip>();
        if (grip != null && grip.IsHolding) { if (preparing) CancelAttack(); timer = 0f; return; }

        // A charger winding up, charging or sliding to a stop swings at no one: running into them is its blow (ZombieCharger).
        if (charger == null) charger = GetComponent<ZombieCharger>();
        if (charger != null && charger.Driving) { if (preparing) CancelAttack(); timer = 0f; return; }
        // Nor a memo elite while it moves itself (a ninja in its smoke, a candyman dragging, a breadbag's cut running on).
        if (Elite != null && Elite.Driving) { if (preparing) CancelAttack(); timer = 0f; return; }

        // A runner doesn't swing: ZombieRunner starts its leap (Pounce), and here it lands.
        if (pounces)
        {
            if (!preparing) { timer = 0f; return; }
            if (player == null) { CancelAttack(); return; }
            timer += dt;
            // Landed, it has to keep them in reach a while before it has them (PounceOver). Someone on another PC is
            // judged there; here it only waits that out.
            if (timer >= Windup && PounceOver(player.IsNetworkDriven ? null : player, dt)) FireAttack();
            return;
        }

        player = targeting.Player;
        if (player == null || !agent.isOnNavMesh)
        {
            CancelAttack();
            return;
        }
        TrackPlayer(dt);

        if (!preparing)
        {
            if (!InReach(player))
            {
                timer = attackInterval;
                UpdateIndicator(false);
                return;
            }

            timer += dt;

            if (timer >= attackInterval)
            {
                comboIndex = 0;
                BeginWindup();
            }

            return;
        }

        // 공격 준비 중에는 조준 위치를 변경하지 않는다.
        timer += dt;

        if (timer < Windup)
            return;

        FireAttack();
    }
    /// <summary>
    /// Feet to feet, since the agent floats baseOffset above the mesh and counting that height kept an enemy stopped at
    /// attackRange from ever being in range. Someone standing on another enemy's head is reached from below while within
    /// attackRange across and no higher than this one's head plus reachAbove; ledges keep the plain rule. Never its own
    /// rider: that one gets thrown off instead (HeadStandShakeOff).
    /// </summary>
    private bool InReach(PlayerMovement target)
    {
        Vector3 delta = target.transform.position - (transform.position - Vector3.up * agent.baseOffset);
        if (delta.magnitude <= attackRange) return !EnemyHead.Carries(this, target);
        return delta.y > 0f && delta.y <= agent.height + reachAbove && new Vector2(delta.x, delta.z).magnitude <= attackRange
            && !EnemyHead.Carries(this, target) && EnemyHead.OnAnyHead(target);
    }
    private void BeginWindup()
    {
        preparing = true; timer = 0f;
        // A shambler grabs whoever isn't getting away from it; only an opening attack may be a bite.
        // Nobody on a ladder or mid-climb is grabbed: held there, they could never climb on while the shambler kept
        // grabbing again (friend feedback C9); they are clawed instead. (A copy's state is mirrored, so this holds for
        // players on other PCs too.) Nor anyone up on a step or down below one (Level): a shambler under a ledge took
        // hold of the player standing on it, hidden by the very floor they stood on (user, 2026-11-11 playtest).
        bool level = Level(player);
        // A memo elite makes its own kind's attack (a breadbag's chainsaw, a candyman's hook) where it has one.
        var chosen = Elite != null ? Elite.Choose(player, level) : null;
        move = chosen.HasValue ? chosen.Value
            : Lying(player) && level ? AttackMove.Feed
            : grabs && level && !MovingAway() && !player.IsParkour ? AttackMove.Grab
            : comboIndex == 0 && biteShare > 0f && Random.value < biteShare ? AttackMove.Bite : AttackMove.Swing;
        lockedAim = player.transform.position + Vector3.up * AimHeightFor(player);
        UpdateIndicator(true);
        Telegraph(player.IsLocal);
        if (network != null) network.AnnounceWindup(player, comboIndex, move);
    }

    private void FireAttack()
    {
        // Someone who got up on its head meanwhile isn't swung at; they're thrown off instead (HeadStandShakeOff).
        if (PlayerExecution.Suppresses(this) || (meleeTarget != null && meleeTarget.IsStunned) || EnemyHead.Carries(this, player))
        { CancelAttack(); return; }
        preparing = false;
        timer = 0f;
        UpdateIndicator(false);
        // A memo elite hears its windup end here (a breadbag's cut starts on it), as every copy does in TickCopy.
        if (Elite != null) Elite.Struck(move);

        bool finisher = move == AttackMove.Pounce || comboIndex >= Mathf.Max(1, comboHits) - 1;
        // Aimed at someone on another PC, the swing lands there. A parry comes back as a stun, which ends the combo.
        var result = player.IsNetworkDriven ? DamageResult.Ignored : Strike();
        if (!finisher && result != DamageResult.Parried && meleeTarget?.IsStunned != true)
        { comboIndex++; BeginWindup(); }
    }

    /// <summary>Where the leap under way comes down (ZombieRunner flies there).</summary>
    internal Vector3 PounceSpot => lockedFeet;

    // How long a landed leap that has caught nobody yet still waits past the dwell before it gives up (s).
    private const float PounceLinger = .25f;
    // How long this leap's quarry has been within its reach since it landed (s).
    private float pounceInside;
    private float PounceDwell
    {
        get
        {
            if (leaper == null) leaper = GetComponent<ZombieRunner>();
            return leaper != null ? leaper.PounceDwell : 0f;
        }
    }

    // Each frame once a leap has landed: it pins only someone who stays within pounceRadius of where it came down for
    // PounceDwell, so the catch keeps its reach but takes time (user, 2026-11-10: "잡는 범위를 줄이기보단 … 잡히려면 그
    // 잡히는 범위에 0.5초 정도 있어야"). Stepping out of it in time clears them, and a shove meanwhile counters it
    // (ShoveCounter: the leap still counts as in its last moment). True once it is over: they have stayed long enough,
    // or it has waited PounceLinger past the dwell for nothing.
    private bool PounceOver(PlayerMovement victim, float dt)
    {
        float dwell = PounceDwell;
        if (dwell <= 0f) return true;
        if (victim != null)
        {
            Vector3 off = victim.transform.position - lockedFeet;
            if (new Vector2(off.x, off.z).magnitude <= pounceRadius && OnItsFloor(victim) && Reached(victim)) pounceInside += dt;
        }
        return pounceInside >= dwell || timer >= Windup + dwell + PounceLinger;
    }

    // Its own feet: the agent floats baseOffset above the mesh (a copy's agent is off but keeps the number).
    private float FeetHeight => transform.position.y - (agent != null ? agent.baseOffset : 0f);

    // On the floor it stands on: the floor under the player within holdHeight of its own feet, so a grab or a kneel can
    // reach them.
    private bool Level(PlayerMovement target) => target != null && Mathf.Abs(FloorUnder(target) - FeetHeight) <= holdHeight;

    private static readonly RaycastHit[] floorHits = new RaycastHit[8];
    private static int floorMask;

    /// <summary>
    /// The height of the floor a player stands on: their feet when this PC moves them and they are on the ground, else
    /// the first thing under their feet that isn't a zombie, a player, a body or a trigger. A hop lifts the feet, not the
    /// floor: going by the feet, jumping in place as a leap came down cleared the height check and the pin with it (the
    /// 2026-11-11 review).
    /// </summary>
    internal static float FloorUnder(PlayerMovement player)
    {
        Vector3 feet = player.transform.position;
        if (!player.IsNetworkDriven && player.IsGrounded) return feet.y;
        if (floorMask == 0) floorMask = ~(LayerMask.GetMask("Enemy") | 1 << 31);
        int count = Physics.RaycastNonAlloc(feet + Vector3.up * .1f, Vector3.down, floorHits, 2.6f, floorMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity, floor = feet.y;
        for (int i = 0; i < count; i++)
        {
            var hit = floorHits[i];
            if (hit.distance >= nearest || hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            nearest = hit.distance;
            floor = hit.point.y;
        }
        return floor;
    }

    // A landed leap catches only someone on the floor it came down on, their feet within ZombieRunner's pounceHeight of
    // its own, and not on a ladder, a ledge or mid-vault: from the foot of a ladder it pinned a player near its top, its
    // check going by where the player had been (user, 2026-11-11 playtest: "사다리 타고 위에 있는데 왜 맨 아래에 있는
    // 러너한테 잡히는 거임??").
    private bool OnItsFloor(PlayerMovement victim)
    {
        if (victim == null || victim.IsParkour) return false;
        if (leaper == null) leaper = GetComponent<ZombieRunner>();
        float most = leaper != null ? leaper.PounceHeight : holdHeight;
        return Mathf.Abs(FloorUnder(victim) - FeetHeight) <= most;
    }

    // How near the runner itself has to come down to whoever it pins (m, flat), past the leap's own reach.
    private const float PinBodyReach = .9f;

    // The runner itself came down on them: near enough, with nothing solid between its chest and theirs. A leap blocked
    // by a wall left the runner behind it while the spot it aimed at, on this side, still pinned whoever stood there,
    // through the wall (user, 2026-11-11 playtest: "벽 너머의 러너가 날 덮쳐서 죽였다고").
    private bool Reached(PlayerMovement victim)
    {
        Vector3 feet = transform.position - Vector3.up * (agent != null ? agent.baseOffset : 0f);
        Vector3 flat = Vector3.ProjectOnPlane(victim.transform.position - feet, Vector3.up);
        if (flat.magnitude > pounceRadius + PinBodyReach) return false;
        if (floorMask == 0) floorMask = ~(LayerMask.GetMask("Enemy") | 1 << 31);
        return !Physics.Linecast(feet + Vector3.up * 1f, victim.transform.position + Vector3.up * 1f, floorMask, QueryTriggerInteraction.Ignore);
    }

    // Where a leap at this player aims as it begins: ahead of them, the way they are moving (ZombieRunner.LeapSpot).
    private Vector3 LeapSpot(PlayerMovement target)
    {
        if (leaper == null) leaper = GetComponent<ZombieRunner>();
        return leaper != null ? leaper.LeapSpot(target, pounceWindup) : target.transform.position;
    }
    private ZombieRunner leaper;
    private ZombieCharger charger;

    /// <summary>
    /// A runner leaps at this player now (ZombieRunner), to land where they are heading: the windup is its time in the air.
    /// </summary>
    internal void Pounce(PlayerMovement target)
    {
        if (preparing || target == null) return;
        player = target;
        comboIndex = 0;
        preparing = true; timer = 0f; move = AttackMove.Pounce;
        pounceInside = 0f;
        lockedFeet = LeapSpot(target);
        lockedAim = lockedFeet + Vector3.up * AimHeightFor(target);
        UpdateIndicator(true);
        Telegraph(target.IsLocal);
        if (network != null) network.AnnounceWindup(target, 0, move);
    }

    // The leap came down on the spot it aimed at as it began, and whoever has stayed within its reach since for the
    // dwell (PounceOver) is knocked flat and pinned (PlayerHold). Not guarded or parried; a dodge's dodging frames get
    // through it (the receiver), as stepping away in time does. A shove just before still counters it from further off
    // than it catches (ZombieRunner.PounceCounterRadius; user, 2026-11-10: the counter stays generous).
    private DamageResult Land()
    {
        var victim = network != null && network.IsCopy ? PlayerRoster.Local : player;
        if (victim == null || victim.DamageReceiver == null) return DamageResult.Ignored;
        Vector3 off = victim.transform.position - lockedFeet;
        Vector3 chest = victim.transform.position + Vector3.up * AimHeightFor(victim);
        Vector3 direction = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 1e-4f) direction = transform.forward;
        direction.Normalize();
        var hit = new DamageHit(DamageHit.NewAttackId(), .01f, DamageKind.Attack, chest, direction, gameObject, false, 0f, true,
            method: DamageMethod.Pounce);
        // A dodge through the leap counts as one through any attack (a perfect dodge).
        StepDodgeTrail.RecognizeAttackSegment(hit, chest - direction * pounceRadius, chest);
        float away = new Vector2(off.x, off.z).magnitude;
        if (!OnItsFloor(victim) || !Reached(victim)) return DamageResult.Ignored;
        if (away > pounceRadius || pounceInside < PounceDwell)
        {
            if (leaper == null) leaper = GetComponent<ZombieRunner>();
            bool counters = leaper != null && away <= leaper.PounceCounterRadius
                && victim.TryGetComponent(out ShoveCounter counter) && counter.WouldCounter(hit);
            if (!counters) return DamageResult.Ignored;
        }
        return victim.DamageReceiver.ReceiveHit(hit);
    }

    private DamageResult Strike()
    {
        if (move == AttackMove.Pounce) return Land();
        // A chainsaw's cut is no single blow: it runs on a while, and every PC cuts its own player (ZombieBreadbag).
        if (move == AttackMove.Saw) return DamageResult.Ignored;
        // The height rule again where the hit is judged (the victim's PC, or the host for its own player): a hop or a step
        // up during the windup takes them out of a grab's or a kneel's reach, and it claws instead (the 2026-11-11 review).
        if (move == AttackMove.Grab || move == AttackMove.Feed)
        {
            var judged = network != null && network.IsCopy ? PlayerRoster.Local : player;
            if (judged != null && !Level(judged)) move = AttackMove.Swing;
        }
        Vector3 origin = transform.position;
        // Kneeling to eat someone on the ground isn't a swing to slip: it takes them where they lie now, if that is still
        // near enough. Aimed only at where they lay as the kneel began, a crawl of a metre took them out from under it and
        // the feed kept missing (user, 2026-11-10: "잘 못 잡음 … 판정이 약함"). Up and away from it, they are clear as before.
        float stretch = 0f;
        if (move == AttackMove.Feed && feedReach > 0f)
        {
            var eaten = network != null && network.IsCopy ? PlayerRoster.Local : player;
            if (eaten != null && Lying(eaten))
            {
                Vector3 lies = eaten.transform.position + Vector3.up * AimHeightFor(eaten);
                Vector3 flat = Vector3.ProjectOnPlane(lies - origin, Vector3.up);
                if (flat.magnitude <= attackRange + feedReach) { lockedAim = lies; stretch = feedReach; }
            }
        }
        Vector3 direction = (lockedAim - origin).normalized;

        if (direction.sqrMagnitude < 0.0001f)
            return DamageResult.Ignored;

        // 적 자신의 충돌체 안에서 광선이 시작되는 것을 줄인다.
        origin += direction * rayStartOffset;
        // Up on a neighbour's head the aim point is further than attackRange; the arm reaches up to it, no further.
        float reach = Mathf.Max(attackRange + rayStartOffset,
            Mathf.Min(Vector3.Distance(origin, lockedAim) + rayStartOffset, attackRange + reachAbove + rayStartOffset + stretch));

        // A grab or a feed does no harm of its own (PlayerHold holds them and bites), but a hit needs some to land.
        bool holds = move == AttackMove.Grab || move == AttackMove.Feed;
        // Whom it lands on is judged on this PC: the host's own player, or this PC's for a copy. A memo elite may say it
        // can't be guarded from where they stand (a ninja behind someone who hasn't turned to it).
        var victim = network != null && network.IsCopy ? PlayerRoster.Local : player;
        bool guardable = !holds && (Elite == null || Elite.GuardableBy(victim, move));
        DamageHit hit = new DamageHit(
            DamageHit.NewAttackId(),
            holds ? .01f : damage,
            DamageKind.Attack,
            lockedAim,
            direction,
            gameObject, guardable, 0f, comboIndex >= Mathf.Max(1, comboHits) - 1,
            method: move == AttackMove.Grab ? DamageMethod.Grab : move == AttackMove.Feed ? DamageMethod.Feed
                : slam.x > 0f || slam.y > 0f ? DamageMethod.Slam : DamageMethod.Other,
            isFatal: fatalHit || move == AttackMove.Bite
        );

        var result = AttackTrace.Raycast(
            hit,
            origin,
            direction,
            reach,
            hitLayers
        );
        // Turned aside, the elite hears of it on the PC that turned it (the reward and its stagger start there).
        if (Elite != null && victim != null)
        {
            if (result == DamageResult.Parried) Elite.Parried(victim);
            else if (result == DamageResult.Blocked) Elite.Blocked(victim);
        }
        return result;
    }

    /// <summary>
    /// A memo elite starts this attack on this player now, wherever it stands (a ninja stepping out of its smoke behind
    /// them): the windup runs from here as any other's, cue, voice and the word to the other PCs included.
    /// </summary>
    internal void BeginSpecial(PlayerMovement target, AttackMove special)
    {
        if (target == null) return;
        if (preparing) CancelAttack();
        player = target;
        comboIndex = 0;
        preparing = true; timer = 0f; move = special;
        lockedAim = target.transform.position + Vector3.up * AimHeightFor(target);
        UpdateIndicator(true);
        Telegraph(target.IsLocal);
        if (network != null) network.AnnounceWindup(target, 0, move);
    }

    /// <summary>
    /// On a copy: the host's enemy began an attack. Aimed at this PC's player, the windup is timed here, so the cue and
    /// the hit stay as far apart as they are alone and the parry window doesn't move with the connection.
    /// </summary>
    internal void ShowWindup(bool atThisPlayer, int combo, AttackMove shown)
    {
        preparing = true; timer = 0f; comboIndex = combo; move = shown;
        var mine = atThisPlayer ? PlayerRoster.Local : null;
        aimedHere = mine != null;
        if (aimedHere)
        {
            // A leap aims ahead of them as the host's does; this PC judges whether it caught its own player.
            pounceInside = 0f;
            lockedFeet = shown == AttackMove.Pounce ? LeapSpot(mine) : mine.transform.position;
            lockedAim = lockedFeet + Vector3.up * AimHeightFor(mine);
        }
        UpdateIndicator(true);
        Telegraph(aimedHere);
    }

    private float CuePitch => move == AttackMove.Bite ? BitePitch : move == AttackMove.Grab || move == AttackMove.Feed ? GrabPitch
        : move == AttackMove.Pounce ? PouncePitch : move == AttackMove.Saw ? SawPitch : move == AttackMove.Backstab ? BackstabPitch
        : slam.x > 0f || slam.y > 0f ? SlamPitch : 1f;

    // When this zombie last voiced an attack (game time), so it doesn't snarl at every swing (ZombieVoice ownGap).
    private float voicedAt = float.NegativeInfinity;

    /// <summary>
    /// The wind-up is heard as it starts, on every PC: the host's or the lone PC's in BeginWindup and Pounce, a copy's
    /// in ShowWindup, each at the instant its own timer starts, so the parry window doesn't move. The cue whose loud
    /// part marks the blow is louder when the blow is meant for this PC's own player (AtMine), and the zombie's voice
    /// goes with it (the user's memo of 2026-11-10, item 10: a voice and a small signal). Only the opening blow of a
    /// combo is voiced, and never a wind-up that a late message starts on a copy this PC has already seen die. An
    /// exploder's close-up swing does no harm (the burst is its attack): it has no voice, and its cue stays at its
    /// plain volume, so the loud cue always means a blow to answer.
    /// </summary>
    private void Telegraph(bool atMine)
    {
        bool harmless = move == AttackMove.Swing && damage <= 0f;
        CombatCueAudio.Play(gameObject, CombatCue.EnemyAttackStart, CuePitch, gain: ZombieVoice.SignalGain(atMine && !harmless));
        if (comboIndex > 0 || enemyHealth == null || !enemyHealth.IsAlive || harmless) return;
        if (ZombieVoice.Attacks(this, move == AttackMove.Pounce, atMine, Time.time - voicedAt)) voicedAt = Time.time;
    }

    // Someone lying on the ground, pinned there by a runner, or down: they are eaten. Down, the eating kills them fast
    // (PlayerHold downedBiteDamage; friend feedback C8: being down was safe from the zombies).
    private static bool Lying(PlayerMovement target) => target != null && (target.IsLyingDown || target.IsPinned || target.IsDowned);

    // Low at whoever lies on the ground (or is down, or pinned), where a swing at standing height passed over them.
    private float AimHeightFor(PlayerMovement target)
        => target != null && (target.IsLyingDown || target.IsDowned || target.IsPinned) ? Mathf.Min(aimHeight, lyingAimHeight) : aimHeight;

    // How the player it is after moves, from where they are each frame: a player on another PC moves here only as a copy.
    private void TrackPlayer(float dt)
    {
        if (player != lastPlayer)
        {
            lastPlayer = player;
            lastPlayerPosition = player.transform.position;
            playerVelocity = Vector3.zero;
            return;
        }
        Vector3 position = player.transform.position;
        if (dt > 0f) playerVelocity = Vector3.Lerp(playerVelocity, (position - lastPlayerPosition) / dt, .5f);
        lastPlayerPosition = position;
    }

    private bool MovingAway()
    {
        if (player == null) return false;
        Vector3 away = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
        return away.sqrMagnitude > 1e-4f && Vector3.Dot(Vector3.ProjectOnPlane(playerVelocity, Vector3.up), away.normalized) > AwaySpeed;
    }

    private void TickCopy(float dt)
    {
        if (!preparing) return;
        if (enemyHealth == null || !enemyHealth.IsAlive) { CancelAttack(); return; }
        timer += dt;
        if (timer < Windup) return;
        if (move == AttackMove.Pounce)
        {
            // A landed leap waits as the host's does (PounceOver), unless it already holds someone.
            if (grip == null) grip = GetComponent<ZombieGrip>();
            bool holds = grip != null && grip.IsHolding;
            if (!holds && !PounceOver(aimedHere ? PlayerRoster.Local : null, dt)) return;
        }
        preparing = false;
        timer = 0f;
        UpdateIndicator(false);
        if (Elite != null) Elite.Struck(move);
        // The host can't see a rider land in time; this PC can, and doesn't swing at its own rider either.
        if (aimedHere && !EnemyHead.Carries(this, PlayerRoster.Local)) Strike();
    }

    public void CancelAttack()
    {
        if (preparing && network != null) network.AnnounceCancel();
        // A breadbag's cut runs on past its windup: it stops with the rest (ZombieElite.Cancelled).
        if (Elite != null) Elite.Cancelled();
        preparing = false;
        move = AttackMove.Swing;
        timer = 0f;
        pounceInside = 0f;
        comboIndex = 0;
        // Called every frame while it has nobody to fight: the cue's player found once it is there, no lookup a frame.
        if (cueAudio == null) TryGetComponent(out cueAudio);
        if (cueAudio != null) cueAudio.Stop();
        UpdateIndicator(false);
    }
    private CombatCueAudio cueAudio;
    // What the capsule's colour last showed (-1: not yet), so it is only set when that changes.
    private int indicatorShown = -1;

    private void UpdateIndicator(bool isPreparing)
    {
        // A dressed zombie's capsule is hidden (ZombieBody): nothing to colour, and this ran every frame per zombie.
        if (indicator == null || !indicator.enabled || indicatorShown == (isPreparing ? 1 : 0))
            return;
        indicatorShown = isPreparing ? 1 : 0;

        // 평소에는 회색, 공격 준비 중에는 노란색
        Color tint = isPreparing
            ? Color.yellow
            : new Color(0.5f, 0.5f, 0.5f);

        indicator.GetPropertyBlock(color);
        color.SetColor("_BaseColor", tint);
        color.SetColor("_Color", tint);
        indicator.SetPropertyBlock(color);
    }
}
