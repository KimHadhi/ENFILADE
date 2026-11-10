using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Stage G&amp;B-2, Guts &amp; Blackpowder's runner, at the user's word: a runner doesn't swing. Seeing its quarry within
/// chargeRange it charges, faster than anyone can run but turning badly, so a sidestep late in the charge makes it run
/// past and skid to a stop. Within pounceRange, its quarry ahead, it leaps (EnemyCombatAI's Pounce): it flies to where
/// they stood as the leap began, and whoever is still there is knocked flat and pinned (PlayerHold), its teeth at their
/// neck until a teammate kills it. A leap that lands on nothing leaves it standing there a moment. Up to
/// PlayerHold.MostPinning runners pin one player; the others don't leap at a full pin. This runs where the enemy really
/// runs (the host, or the PC alone); other PCs see it move as the host moves it, and time the leap themselves.
/// Numbers: Tables/ZombieRunner.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieRunner : MonoBehaviour
{
    public const string TableName = "ZombieRunner", TableRow = "Default";

    [SerializeField, Min(0f), Tooltip("It charges from this far (m), seeing its quarry.")]
    private float chargeRange = 9f;
    [SerializeField, Min(0f), Tooltip("Charging speed (m/s).")]
    private float chargeSpeed = 9.2f;
    [SerializeField, Min(0f), Tooltip("Charging, how fast it turns (degrees a second): slowly, so a sidestep makes it run past.")]
    private float chargeTurn = 100f;
    [SerializeField, Min(.1f), Tooltip("The longest one charge lasts (s).")]
    private float chargeLongest = 3f;
    [SerializeField, Min(0f), Tooltip("It leaps from this close (m).")]
    private float pounceRange = 3f;
    [SerializeField, Range(0f, 180f), Tooltip("It leaps only with its quarry within this angle of the way it runs (degrees either side).")]
    private float pounceAngle = 35f;
    [SerializeField, Range(0f, 180f), Tooltip("Its quarry this far off the way it runs (degrees): it ran past them, and skids.")]
    private float passedAngle = 100f;
    [SerializeField, Min(.05f), Tooltip("Running past, how long it skids to a stop (s).")]
    private float skidSeconds = .6f;
    [SerializeField, Min(0f), Tooltip("A leap that caught nobody: how long it stays where it landed (s).")]
    private float missSeconds = 1f;
    [SerializeField, Min(0f), Tooltip("From the end of one charge or leap to the next (s).")]
    private float cooldown = 1.2f;
    [SerializeField, Min(0f), Tooltip("Knocked down or stunned, once back on its feet it waits at least this long before it charges or leaps again (s): knocking it down buys that time (friend feedback C7).")]
    private float riseDaze = 1.5f;
    [SerializeField, Min(0f), Tooltip("Further above or below its quarry than this (m), it doesn't charge or leap.")]
    private float chargeHeight = 1.5f;
    [SerializeField, Min(0f), Tooltip("How high the leap goes, shown (m).")]
    private float leapHeight = .45f;
    [Header("Glowing eyes (ZombieBody.Eyes.cs)")]
    [SerializeField, Tooltip("Without eye bones: the eyes this far ahead of the head bone (m).")]
    private float eyeForward = .1f;
    [SerializeField, Tooltip("Without eye bones: the eyes this far above the head bone (m).")]
    private float eyeUp = .085f;
    [SerializeField, Min(0f), Tooltip("Without eye bones: this far apart (m).")]
    private float eyeApart = .064f;
    [SerializeField, Min(.01f), Tooltip("Each eye's glow, halo and all, across (m).")]
    private float eyeSize = .16f;
    [SerializeField, Min(0f), Tooltip("How bright the eyes burn (HDR; bloom makes it glow).")]
    private float eyeBrightness = 6f;
    [SerializeField, Min(0f), Tooltip("How long the eyes' afterimage lasts behind them (s).")]
    private float eyeTrailSeconds = .2f;
    [SerializeField, Min(0f), Tooltip("How wide the afterimage starts (m).")]
    private float eyeTrailWidth = .045f;

    private enum Phase { Chase, Charge, Leap, Skid, Rest }

    // How often it looks for a clear line to its quarry while charging (s).
    private const float SightEvery = .1f;

    private NavMeshAgent agent;
    private EnemyHealth health;
    private EnemyCombatAI combat;
    private EnemyTargeting targeting;
    private EnemyNetwork network;
    private MeleeCombatTarget melee;
    private ZombieMind mind;
    private ZombieGrip grip;
    private ZombieType type;
    private Phase phase;
    private Vector3 heading, leapFrom, leapTo;
    private float chargeTime, skidLeft, restLeft, cooldownLeft, sightLeft;
    private bool sees;
    private int sightMask;

    /// <summary>It moves itself (charging, leaping, skidding or standing where it landed): its chase waits.</summary>
    public bool Driving => phase != Phase.Chase;
    /// <summary>How high its leap goes (m), for ZombieBody to show on every PC.</summary>
    public float LeapHeight => leapHeight;
    // Its glowing eyes, for ZombieBody.Eyes.cs.
    public float EyeForward => eyeForward;
    public float EyeUp => eyeUp;
    public float EyeApart => eyeApart;
    public float EyeSize => eyeSize;
    public float EyeBrightness => eyeBrightness;
    public float EyeTrailSeconds => eyeTrailSeconds;
    public float EyeTrailWidth => eyeTrailWidth;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<EnemyHealth>();
        combat = GetComponent<EnemyCombatAI>();
        targeting = GetComponent<EnemyTargeting>();
        network = GetComponent<EnemyNetwork>();
        melee = GetComponent<MeleeCombatTarget>();
        mind = GetComponent<ZombieMind>();
        grip = GetComponent<ZombieGrip>();
        type = GetComponent<ZombieType>();
        int enemy = LayerMask.NameToLayer("Enemy");
        sightMask = ~((enemy >= 0 ? 1 << enemy : 0) | 1 << 31);
        GameTables.Bind(TableName, TableRow, this);
    }

    private bool Runs => network == null || !network.IsCopy;

    private void Update()
    {
        if (!Runs) return;
        float dt = Time.deltaTime * (network != null ? network.Pace : 1f);
        if (dt <= 0f) return;
        if (!CanAct())
        {
            Stop();
            // Down or reeling, it gets up dazed: no charge or leap the moment it stands.
            if (melee != null && (melee.IsStunned || melee.IsLying)) cooldownLeft = Mathf.Max(cooldownLeft, riseDaze);
            return;
        }
        if (targeting == null) targeting = GetComponent<EnemyTargeting>();
        var player = targeting != null ? targeting.Player : null;
        switch (phase)
        {
            case Phase.Chase: Chase(player, dt); break;
            case Phase.Charge: Charge(player, dt); break;
            case Phase.Leap: Leap(dt); break;
            case Phase.Skid: Skid(player, dt); break;
            case Phase.Rest: Rest(dt); break;
        }
    }

    // A runner on its feet, awake, free and not holding anyone (a pin is ZombieGrip's).
    private bool CanAct()
    {
        if (type == null || type.Kind != ZombieKind.Runner || type.Crawls) return false;
        if (health == null || !health.IsAlive || health.WavePaused) return false;
        if (mind != null && !mind.Ready) return false;
        if (grip == null) grip = GetComponent<ZombieGrip>();
        if (grip != null && grip.IsHolding) return false;
        if (melee != null && (melee.IsStunned || melee.IsLying)) return false;
        if (PlayerExecution.Suppresses(this)) return false;
        return agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
    }

    // Back to its chase (EnemyChaseAI), the next charge a while off.
    private void Stop()
    {
        if (phase == Phase.Chase) return;
        phase = Phase.Chase;
        cooldownLeft = cooldown;
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.velocity = Vector3.zero;
    }

    private void Chase(PlayerMovement player, float dt)
    {
        cooldownLeft -= dt;
        if (player == null || combat == null || combat.IsPreparingAttack) return;
        Vector3 to = Flat(player.transform.position - transform.position);
        float distance = to.magnitude;
        if (distance < .01f || Mathf.Abs(player.transform.position.y - Feet.y) > chargeHeight) return;
        // A player already pinned by as many as can pin them is left to them.
        if (ZombieGrip.CountPinning(player) >= PlayerHold.MostPinning) return;
        if (distance <= pounceRange)
        {
            // Close, it faces them, which an agent stopped short of them doesn't, and leaps once it may.
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to),
                Mathf.Max(90f, agent.angularSpeed) * dt);
            if (cooldownLeft <= 0f && Vector3.Angle(Flat(transform.forward), to) <= pounceAngle && Sees(player)) BeginLeap(player);
            return;
        }
        if (cooldownLeft > 0f) return;
        if (distance <= chargeRange && !player.IsPinned && Sees(player))
        {
            phase = Phase.Charge;
            chargeTime = 0f;
            sightLeft = SightEvery;
            sees = true;
            heading = to / distance;
            Halt();
        }
    }

    private void Charge(PlayerMovement player, float dt)
    {
        chargeTime += dt;
        if (player == null) { BeginSkid(); return; }
        Vector3 to = Flat(player.transform.position - transform.position);
        float distance = to.magnitude;
        float off = distance > .01f ? Vector3.Angle(heading, to) : 0f;
        if ((sightLeft -= dt) <= 0f) { sightLeft = SightEvery; sees = Sees(player); }
        if (distance <= pounceRange && off <= pounceAngle && sees && ZombieGrip.CountPinning(player) < PlayerHold.MostPinning)
        {
            BeginLeap(player);
            return;
        }
        // Ran past them, ran too long, lost them, or they climbed out of reach: it skids to a stop.
        if (off >= passedAngle || chargeTime >= chargeLongest || !sees
            || Mathf.Abs(player.transform.position.y - Feet.y) > chargeHeight)
        {
            BeginSkid();
            return;
        }
        if (distance > .01f) heading = Vector3.RotateTowards(heading, to / distance, chargeTurn * Mathf.Deg2Rad * dt, 0f);
        Run(heading * chargeSpeed * dt);
    }

    private void BeginLeap(PlayerMovement player)
    {
        combat.Pounce(player);
        if (!combat.IsPouncing) return;
        phase = Phase.Leap;
        leapFrom = transform.position;
        Vector3 spot = player.transform.position;
        leapTo = new Vector3(spot.x, leapFrom.y, spot.z);
        Vector3 way = Flat(leapTo - leapFrom);
        if (way.sqrMagnitude > 1e-4f) heading = way.normalized;
        transform.rotation = Quaternion.LookRotation(heading);
        Halt();
    }

    // In the air from where it leapt to where they stood, over the leap's windup (EnemyCombatAI).
    private void Leap(float dt)
    {
        if (!combat.IsPouncing)
        {
            // It landed. A pin, if it caught them, comes from their PC (PlayerHold, ZombieGrip) and takes over.
            phase = Phase.Rest;
            restLeft = missSeconds;
            cooldownLeft = cooldown;
            return;
        }
        float share = Mathf.Clamp01(combat.WindupElapsed / Mathf.Max(.05f, combat.Windup));
        Vector3 want = Vector3.Lerp(leapFrom, leapTo, share);
        Vector3 step = want - transform.position;
        step.y = 0f;
        Move(step);
    }

    private void BeginSkid()
    {
        phase = Phase.Skid;
        skidLeft = skidSeconds;
    }

    private void Skid(PlayerMovement player, float dt)
    {
        skidLeft -= dt;
        if (skidLeft <= 0f)
        {
            phase = Phase.Rest;
            restLeft = 0f;
            cooldownLeft = cooldown;
            return;
        }
        if (player != null)
        {
            Vector3 to = Flat(player.transform.position - transform.position);
            if (to.sqrMagnitude > 1e-4f)
                heading = Vector3.RotateTowards(heading, to.normalized, chargeTurn * .5f * Mathf.Deg2Rad * dt, 0f);
        }
        float share = skidLeft / skidSeconds;
        Run(heading * chargeSpeed * share * share * dt);
    }

    private void Rest(float dt)
    {
        if ((restLeft -= dt) > 0f) return;
        phase = Phase.Chase;
    }

    // Moving itself, the agent stands still and only follows (no steering, no turning of its own).
    private void Halt()
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    private void Run(Vector3 step)
    {
        if (heading.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(heading);
        Move(step);
    }

    private void Move(Vector3 step)
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.Move(step);
        else transform.position += step;
    }

    // Nothing but enemies and players between its eyes and their chest.
    private bool Sees(PlayerMovement player)
    {
        Vector3 eye = Feet + Vector3.up * 1.5f, chest = player.transform.position + Vector3.up;
        return !Physics.Linecast(eye, chest, sightMask, QueryTriggerInteraction.Ignore);
    }

    private Vector3 Feet => agent != null ? transform.position - Vector3.up * agent.baseOffset : transform.position;

    private static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);

    private void OnDisable() => phase = Phase.Chase;
}
