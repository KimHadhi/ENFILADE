
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What an attack is (EnemyCombatAI): a swing; a runner's bite at the neck (fatal if it lands); a shambler's grab,
/// which does no harm of its own but holds the player (PlayerHold) and can't be guarded or parried, only dodged; or,
/// at the user's word (lying down was safe from every swing), any zombie kneeling to eat a player lying on the ground:
/// it holds them there and bites at once; or a runner's leap (G&B-2, ZombieRunner), which pins whoever is still where
/// they stood as it began (PlayerHold) and can't be guarded or parried, only dodged or stepped out of.
/// </summary>
public enum AttackMove : byte { Swing, Bite, Grab, Feed, Pounce }

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
    [Tooltip("Guts & Blackpowder's runner: it never swings; ZombieRunner charges and leaps (ZombieKinds pounces).")]
    [SerializeField] private bool pounces;
    [Tooltip("A leap's time in the air (s), from its cue to landing.")]
    [SerializeField, Min(.05f)] private float pounceWindup = .35f;
    [Tooltip("A leap catches whoever is still within this of where they stood as it began (m).")]
    [SerializeField, Min(.1f)] private float pounceRadius = 1.1f;
    // The bite's cue, pitched down from the swing's so it sounds heavier; a grab's a little lower too.
    private const float BitePitch = .72f, GrabPitch = .85f, PouncePitch = 1.2f;
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
        : move == AttackMove.Feed ? feedWindup : move == AttackMove.Pounce ? pounceWindup : windup;
    /// <summary>The windup under way is a bite at the neck: fatal if it lands (ZombieBody shows the lunge).</summary>
    public bool IsBiting => preparing && move == AttackMove.Bite;
    /// <summary>The windup under way is a grab (ZombieBody shows the reach).</summary>
    public bool IsGrabbing => preparing && move == AttackMove.Grab;
    /// <summary>The windup under way is kneeling to eat a player lying down (ZombieBody shows it).</summary>
    public bool IsFeeding => preparing && move == AttackMove.Feed;
    /// <summary>A runner is in the air, leaping (ZombieBody shows it, ZombieRunner carries it).</summary>
    public bool IsPouncing => preparing && move == AttackMove.Pounce;

    /// <summary>A runner's leap (ZombieType): whether it leaps instead of swinging, its time in the air and its reach.</summary>
    internal void SetPounce(bool pounces, float windup, float radius)
    {
        this.pounces = pounces;
        pounceWindup = Mathf.Max(.05f, windup);
        pounceRadius = Mathf.Max(.1f, radius);
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

        // A runner doesn't swing: ZombieRunner starts its leap (Pounce), and here it lands.
        if (pounces)
        {
            if (!preparing) { timer = 0f; return; }
            if (player == null) { CancelAttack(); return; }
            timer += dt;
            if (timer >= Windup) FireAttack();
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
        move = Lying(player) ? AttackMove.Feed
            : grabs && !MovingAway() ? AttackMove.Grab
            : comboIndex == 0 && biteShare > 0f && Random.value < biteShare ? AttackMove.Bite : AttackMove.Swing;
        lockedAim = player.transform.position + Vector3.up * AimHeightFor(player);
        UpdateIndicator(true);
        CombatCueAudio.Play(gameObject, CombatCue.EnemyAttackStart, CuePitch);
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

        bool finisher = move == AttackMove.Pounce || comboIndex >= Mathf.Max(1, comboHits) - 1;
        // Aimed at someone on another PC, the swing lands there. A parry comes back as a stun, which ends the combo.
        var result = player.IsNetworkDriven ? DamageResult.Ignored : Strike();
        if (!finisher && result != DamageResult.Parried && meleeTarget?.IsStunned != true)
        { comboIndex++; BeginWindup(); }
    }

    /// <summary>
    /// A runner leaps at this player now (ZombieRunner), to land where they stand: the windup is its time in the air.
    /// </summary>
    internal void Pounce(PlayerMovement target)
    {
        if (preparing || target == null) return;
        player = target;
        comboIndex = 0;
        preparing = true; timer = 0f; move = AttackMove.Pounce;
        lockedFeet = target.transform.position;
        lockedAim = lockedFeet + Vector3.up * AimHeightFor(target);
        UpdateIndicator(true);
        CombatCueAudio.Play(gameObject, CombatCue.EnemyAttackStart, CuePitch);
        if (network != null) network.AnnounceWindup(target, 0, move);
    }

    // The leap lands where they stood as it began: whoever is still there is knocked flat and pinned (PlayerHold). Not
    // guarded or parried; a dodge's dodging frames get through it (the receiver), as a sidestep out of the spot does.
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
        if (new Vector2(off.x, off.z).magnitude > pounceRadius || Mathf.Abs(off.y) > 1.5f) return DamageResult.Ignored;
        return victim.DamageReceiver.ReceiveHit(hit);
    }

    private DamageResult Strike()
    {
        if (move == AttackMove.Pounce) return Land();
        Vector3 origin = transform.position;
        Vector3 direction = (lockedAim - origin).normalized;

        if (direction.sqrMagnitude < 0.0001f)
            return DamageResult.Ignored;

        // 적 자신의 충돌체 안에서 광선이 시작되는 것을 줄인다.
        origin += direction * rayStartOffset;
        // Up on a neighbour's head the aim point is further than attackRange; the arm reaches up to it, no further.
        float reach = Mathf.Max(attackRange + rayStartOffset,
            Mathf.Min(Vector3.Distance(origin, lockedAim) + rayStartOffset, attackRange + reachAbove + rayStartOffset));

        // A grab or a feed does no harm of its own (PlayerHold holds them and bites), but a hit needs some to land.
        bool holds = move == AttackMove.Grab || move == AttackMove.Feed;
        DamageHit hit = new DamageHit(
            DamageHit.NewAttackId(),
            holds ? .01f : damage,
            DamageKind.Attack,
            lockedAim,
            direction,
            gameObject, !holds, 0f, comboIndex >= Mathf.Max(1, comboHits) - 1,
            method: move == AttackMove.Grab ? DamageMethod.Grab : move == AttackMove.Feed ? DamageMethod.Feed : DamageMethod.Other,
            isFatal: fatalHit || move == AttackMove.Bite
        );

        return AttackTrace.Raycast(
            hit,
            origin,
            direction,
            reach,
            hitLayers
        );
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
            lockedFeet = mine.transform.position;
            lockedAim = lockedFeet + Vector3.up * AimHeightFor(mine);
        }
        UpdateIndicator(true);
        CombatCueAudio.Play(gameObject, CombatCue.EnemyAttackStart, CuePitch);
    }

    private float CuePitch => move == AttackMove.Bite ? BitePitch : move == AttackMove.Grab || move == AttackMove.Feed ? GrabPitch
        : move == AttackMove.Pounce ? PouncePitch : 1f;

    // Someone lying on the ground, or pinned there by a runner, not down: they are eaten (being down has its own
    // bleeding out, PlayerLife).
    private static bool Lying(PlayerMovement target) => target != null && (target.IsLyingDown || target.IsPinned) && !target.IsDowned;

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
        preparing = false;
        timer = 0f;
        UpdateIndicator(false);
        // The host can't see a rider land in time; this PC can, and doesn't swing at its own rider either.
        if (aimedHere && !EnemyHead.Carries(this, PlayerRoster.Local)) Strike();
    }

    public void CancelAttack()
    {
        if (preparing && network != null) network.AnnounceCancel();
        preparing = false;
        move = AttackMove.Swing;
        timer = 0f;
        comboIndex = 0;
        GetComponent<CombatCueAudio>()?.Stop();
        UpdateIndicator(false);
    }

    private void UpdateIndicator(bool isPreparing)
    {
        if (indicator == null)
            return;

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
