using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Stage C-16, the second of the elites (user, 2026-11-10: "a charger and an exploder for now"): the charger. Bigger
/// than the pack and much harder to kill, a red light burning at its head. Up close it swings and throws whoever it
/// lands on, as a brute does (its row in ZombieKinds). Further off, seeing someone within chargeRange, it stops, turns
/// to them and bellows for windup seconds (its rouse on every PC, ZombieMind.Rouse, the light flaring), then runs at
/// where they were in a line it can barely bend. Whoever it runs into is hurt and thrown (a Slam: PlayerHold throws
/// them by its row's knock) and it keeps going; a step to the side as it comes lets it pass. It runs until it has gone
/// chargeLongest, has passed its quarry, or meets a wall, which stuns it for wallStun: the time to put rounds into it.
/// A shove or a knockdown ends a charge too. The host runs it; every PC judges its own player being run into, on the
/// host by the charge itself and elsewhere by the copy moving that fast head first (shoved or thrown it moves fast
/// too, but backward). On every zombie, as ZombieRunner is; it does nothing for other kinds.
/// Numbers: Tables/ZombieCharger.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieCharger : MonoBehaviour
{
    public const string TableName = "ZombieCharger", TableRow = "Default";
    private const int SampleRate = 44100;
    private static AudioClip bellow;

    [Header("Charge")]
    [SerializeField, Min(0f), Tooltip("It charges at someone it sees within this (m).")]
    private float chargeRange = 16f;
    [SerializeField, Min(0f), Tooltip("And no nearer than this (m): closer, it swings.")]
    private float closest = 4.5f;
    [SerializeField, Min(.1f), Tooltip("How long it bellows before it runs (s): the warning.")]
    private float windup = 1f;
    [SerializeField, Min(.1f), Tooltip("How fast it runs (m/s).")]
    private float chargeSpeed = 11f;
    [SerializeField, Min(0f), Tooltip("How fast it can bend its line toward its quarry (degrees a second): little, so a step aside lets it pass.")]
    private float chargeTurn = 22f;
    [SerializeField, Min(.1f), Tooltip("The longest it runs (s).")]
    private float chargeLongest = 2.4f;
    [SerializeField, Min(0f), Tooltip("Its quarry this far off its line (degrees), it has run past: it stops.")]
    private float passedAngle = 100f;
    [SerializeField, Min(0f), Tooltip("How long it slides to a stop (s).")]
    private float skidSeconds = .5f;
    [SerializeField, Min(0f), Tooltip("From the end of one charge to the next (s).")]
    private float cooldown = 6f;
    [SerializeField, Min(0f), Tooltip("Run into a wall, it is stunned this long (s).")]
    private float wallStun = 2.5f;
    [SerializeField, Min(0f), Tooltip("Further above or below its quarry than this (m), it doesn't charge.")]
    private float chargeHeight = 1.5f;
    [Header("Running into someone")]
    [SerializeField, Min(0f), Tooltip("A player within this of the point just ahead of it is run into (m).")]
    private float ramReach = 1.15f;
    [SerializeField, Min(0f), Tooltip("What that does to them. The throw is its row's knock and knockUp (ZombieKinds).")]
    private float ramDamage = 30f;
    [SerializeField, Min(0f), Tooltip("On another PC, a charger moving at least this fast is charging (m/s): between its walk and its charge.")]
    private float ramSpeed = 7.5f;
    [Header("The light at its head")]
    [SerializeField, Min(.05f), Tooltip("How big the light is (m).")]
    private float glowSize = .5f;
    [SerializeField] private Color glowColor = new Color(1f, .12f, .08f);

    private enum Phase { Chase, Windup, Charge, Skid }

    private NavMeshAgent agent;
    private EnemyHealth health;
    private EnemyNetwork network;
    private EnemyTargeting targeting;
    private MeleeCombatTarget melee;
    private ZombieMind mind;
    private ZombieGrip grip;
    private ZombieType type;
    private EliteLight marker;
    private PlayerMovement quarry;
    private Phase phase;
    private Vector3 heading, lastPlace, seenVelocity;
    private float left, chargeTime, cooldownLeft, ramAgain, flare;
    private bool wasRousing, placed;
    private int sightMask;

    /// <summary>It moves itself (winding up, charging or sliding to a stop): its chase waits (EnemyChaseAI).</summary>
    public bool Driving => phase != Phase.Chase;
    /// <summary>Running at its quarry as this PC sees it (the host's phase, or a copy's speed and heading): RAILGUN reads it (SpecialKills).</summary>
    public bool IsCharging => Runs ? phase == Phase.Charge : seenVelocity.magnitude >= ramSpeed && Vector3.Dot(seenVelocity.normalized, ZombieElite.Flat(transform.forward).normalized) > .7f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => bellow = null;

    private bool IsCharger => type != null && type.Kind == ZombieKind.Charger && !type.Crawls;
    private bool Runs => network == null || !network.IsCopy;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        targeting = GetComponent<EnemyTargeting>();
        melee = GetComponent<MeleeCombatTarget>();
        mind = GetComponent<ZombieMind>();
        type = GetComponent<ZombieType>();
        int enemy = LayerMask.NameToLayer("Enemy");
        sightMask = ~((enemy >= 0 ? 1 << enemy : 0) | 1 << 31);
        GameTables.Bind(TableName, TableRow, this);
    }

    private void OnDestroy() => marker?.Remove();

    private void OnDisable()
    {
        phase = Phase.Chase;
        marker?.Hide();
    }

    // ---- The host: when it charges, and the charge itself ----

    private void Update()
    {
        if (!Runs || !IsCharger) { phase = Phase.Chase; return; }
        float dt = Time.deltaTime * (network != null ? network.Pace : 1f);
        if (dt <= 0f) return;
        if (!CanAct())
        {
            // Shoved, knocked down, held or dead: whatever it was doing is over, and the next charge waits.
            if (phase != Phase.Chase) Stop();
            return;
        }
        if (targeting == null) targeting = GetComponent<EnemyTargeting>();
        switch (phase)
        {
            case Phase.Chase: Chase(targeting != null ? targeting.Player : null, dt); break;
            case Phase.Windup: Windup(dt); break;
            case Phase.Charge: Charge(dt); break;
            case Phase.Skid: Skid(dt); break;
        }
    }

    // On its feet, alive, free. Its mind is rousing while it winds up, so that alone doesn't stop it.
    private bool CanAct()
    {
        if (health == null || !health.IsAlive || health.WavePaused) return false;
        // Not while its calm pose still shows (ZombieMind.GettingUp): it gets up first.
        if (mind != null && (!mind.IsAwake || mind.GettingUp)) return false;
        if (grip == null) grip = GetComponent<ZombieGrip>();
        if (grip != null && grip.IsHolding) return false;
        if (melee != null && (melee.IsStunned || melee.IsLying)) return false;
        if (PlayerExecution.Suppresses(this)) return false;
        return agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
    }

    // Back to its chase (EnemyChaseAI sets the agent going again), the next charge a while off.
    private void Stop()
    {
        phase = Phase.Chase;
        cooldownLeft = cooldown;
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.velocity = Vector3.zero;
    }

    private ZombieBreach breach;

    private void Chase(PlayerMovement player, float dt)
    {
        cooldownLeft -= dt;
        // At a Support's barricade it breaks through first, rather than charge someone across it (ZombieBreach).
        if (breach == null) TryGetComponent(out breach);
        if (breach != null && breach.Driving) return;
        if (cooldownLeft > 0f || player == null || (mind != null && !mind.Ready)) return;
        Vector3 to = ZombieElite.Flat(player.transform.position - transform.position);
        float distance = to.magnitude;
        if (distance < closest || distance > chargeRange || Mathf.Abs(player.transform.position.y - Feet.y) > chargeHeight || !Sees(player)) return;
        phase = Phase.Windup;
        quarry = player;
        left = windup;
        heading = to / distance;
        Halt();
        // It stands, turns to them and plays its rouse on every PC, which is also what tells the others it is winding up.
        if (mind != null) mind.Rouse(player, windup);
    }

    private void Windup(float dt)
    {
        if (quarry != null)
        {
            Vector3 to = ZombieElite.Flat(quarry.transform.position - transform.position);
            if (to.sqrMagnitude > 1e-4f) heading = to.normalized;
        }
        if ((left -= dt) > 0f) return;
        // It goes at where they are now, and from here can barely turn.
        phase = Phase.Charge;
        chargeTime = 0f;
        Halt();
    }

    private void Charge(float dt)
    {
        // Its mind, done rousing, lets the agent go again: it stays this one's to move.
        if (!agent.isStopped) Halt();
        chargeTime += dt;
        if (quarry != null && !quarry.IsDead)
        {
            Vector3 to = ZombieElite.Flat(quarry.transform.position - transform.position);
            if (to.sqrMagnitude > 1e-4f)
            {
                if (Vector3.Angle(heading, to) >= passedAngle) { BeginSkid(); return; }
                heading = Vector3.RotateTowards(heading, to.normalized, chargeTurn * Mathf.Deg2Rad * dt, 0f);
            }
        }
        if (chargeTime >= chargeLongest) { BeginSkid(); return; }
        float step = chargeSpeed * dt;
        // A wall ahead: it runs into it and is stunned. The walk mesh ending ahead (a roof's edge): it only stops.
        if (Physics.SphereCast(Feet + Vector3.up * 1f, .3f, heading, out RaycastHit wall, step + .7f, sightMask, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(wall.normal.y) < .5f)
        {
            Stop();
            if (melee != null) melee.Interrupt(wallStun);
            HitFlashRenderer.For(this).OrNull()?.WhiteFlash(.12f);
            return;
        }
        if (NavMesh.Raycast(transform.position, transform.position + heading * (step + .5f), out _, NavMesh.AllAreas)) { BeginSkid(); return; }
        Run(heading * step);
    }

    private void BeginSkid()
    {
        phase = Phase.Skid;
        left = skidSeconds;
    }

    private void Skid(float dt)
    {
        if (!agent.isStopped) Halt();
        if ((left -= dt) <= 0f || skidSeconds <= 0f) { Stop(); return; }
        float share = left / skidSeconds;
        Vector3 step = heading * (chargeSpeed * share * share * dt);
        if (!NavMesh.Raycast(transform.position, transform.position + step + heading * .3f, out _, NavMesh.AllAreas)) Run(step);
    }

    // Moving itself, the agent stands still and only follows.
    private void Halt()
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    private void Run(Vector3 step)
    {
        if (heading.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(heading);
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.Move(step);
        else transform.position += step;
    }

    private bool Sees(PlayerMovement player)
        => !Physics.Linecast(Feet + Vector3.up * 1.6f, player.transform.position + Vector3.up, sightMask, QueryTriggerInteraction.Ignore);

    private Vector3 Feet => agent != null ? transform.position - Vector3.up * agent.baseOffset : transform.position;

    // ---- Every PC: its light, its bellow, and this PC's own player run into ----

    private void LateUpdate()
    {
        bool lit = IsCharger && health != null && health.IsAlive;
        if (!lit)
        {
            marker?.Hide();
            wasRousing = placed = false;
            return;
        }
        float dt = Time.deltaTime;
        // How it is seen to move, eased: a copy's steps come in bursts.
        if (placed && dt > 0f)
            seenVelocity = Vector3.Lerp(seenVelocity, ZombieElite.Flat(transform.position - lastPlace) / dt, 1f - Mathf.Exp(-12f * dt));
        lastPlace = transform.position;
        placed = true;

        bool rousing = mind != null && mind.Mood == ZombieMood.Rousing;
        if (rousing && !wasRousing)
        {
            GameAudio.PlayAt(Bellow(), transform.position + Vector3.up * 1.6f, AudioChannel.Combat, 1f, 5f, 70f);
            HitFlashRenderer.For(this).OrNull()?.WhiteFlash(.15f);
        }
        wasRousing = rousing;
        // On another PC: fast, and the way it faces. Shoved or thrown it moves fast too, but not head first.
        bool charging = Runs ? phase == Phase.Charge
            : seenVelocity.magnitude >= ramSpeed && Vector3.Dot(seenVelocity.normalized, ZombieElite.Flat(transform.forward).normalized) > .7f;
        if (charging) Ram();

        // The light: steady as it walks, flaring while it bellows and runs.
        flare = Mathf.MoveTowards(flare, rousing || charging ? 1f : 0f, dt * 6f);
        if (marker == null) marker = new EliteLight(this, HumanBodyBones.Head, HumanBodyBones.Neck);
        Color color = Color.Lerp(glowColor, Color.white, flare * .5f);
        color.a = Mathf.Lerp(.7f, 1f, flare);
        marker.Show(color, glowSize * (1f + flare * .9f + (rousing ? .25f * Mathf.Sin(Time.time * 40f) : 0f)));
    }

    // This PC's own player, in its way: hurt, and thrown by the hit's own kind (a Slam, PlayerHold).
    private void Ram()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.IsDowned || Time.time < ramAgain) return;
        Vector3 ahead = transform.forward, to = mine.transform.position - (transform.position + ahead * .6f);
        if (Mathf.Abs(to.y) > 1.7f) return;
        to.y = 0f;
        if (to.sqrMagnitude > ramReach * ramReach) return;
        // Once a charge, and not again as they fly off along its line.
        ramAgain = Time.time + 1.5f;
        mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), ramDamage, DamageKind.Attack, mine.transform.position + Vector3.up,
            ahead, gameObject, method: DamageMethod.Slam, origin: transform.position + Vector3.up));
    }

    // A deep bellow, made here once: a rough low voice that climbs and falls, breath over it.
    private static AudioClip Bellow()
    {
        if (bellow != null) return bellow;
        var data = new float[(int)(SampleRate * 1.1f)];
        var random = new System.Random(5150);
        float turn = 0f, held = 0f, top = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate, along = t / 1.1f;
            float pitch = Mathf.Lerp(70f, 130f, Mathf.Sin(along * Mathf.PI)) * (1f + .04f * Mathf.Sin(2f * Mathf.PI * 23f * t));
            turn += 2f * Mathf.PI * pitch / SampleRate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .2f;
            // A saw's edge on a low tone is a throat; the noise is the breath in it.
            float voice = Mathf.Sin(turn) + .5f * Mathf.Sin(turn * 2f) + .33f * Mathf.Sin(turn * 3f) + .2f * Mathf.Sin(turn * 5f);
            float shape = Mathf.Min(1f, t / .08f) * Mathf.Min(1f, (1.1f - t) / .3f);
            data[i] = (voice * .7f + held * 1.4f) * shape;
            top = Mathf.Max(top, Mathf.Abs(data[i]));
        }
        for (int i = 0; i < data.Length; i++) data[i] = data[i] / Mathf.Max(.001f, top) * .9f;
        bellow = AudioClip.Create("Charger Bellow", data.Length, 1, SampleRate, false);
        bellow.SetData(data, 0);
        bellow.hideFlags = HideFlags.HideAndDontSave;
        return bellow;
    }
}
