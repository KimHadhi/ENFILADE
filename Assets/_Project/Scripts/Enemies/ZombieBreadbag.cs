using UnityEngine;

/// <summary>
/// The breadbag, an elite from the user's memo of 2026-11-11 (item 25: "The Breadbag: 전기톱 기믹"). A big zombie with a
/// bag over its head and a chainsaw, the engine ticking over wherever it walks so it is heard first. Its only attack is
/// the saw (AttackMove.Saw): it revs the engine up for its windup (the warning: the cue and the engine's scream), then
/// cuts into whatever is in front of it for sawSeconds, pressing forward, every sawTick a cut of sawDamage. A dodge
/// gets out of it; a guard alone doesn't stop a chainsaw (blockShare of each cut grinds through it); a parry (on the rev's
/// last beat or into the cut) throws it back off balance (bounceBreak: open to an execution) and pays parryReward. Every
/// PC cuts only its own player, from the host's word that the windup ran out (EnemyCombatAI), as a charger's ram does;
/// the host calls the cut off on the others when it is stunned. Numbers: Tables/ZombieBreadbag; its kind's row in
/// ZombieKinds.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieBreadbag : ZombieElite
{
    public const string TableName = "ZombieBreadbag", TableRow = "Default";
    private const int Rate = 44100;
    private static AudioClip idleClip, revClip;

    [Header("The cut")]
    [SerializeField, Min(.1f), Tooltip("How long a cut runs on after the rev (s).")]
    private float sawSeconds = 1.1f;
    [SerializeField, Min(.03f), Tooltip("Time between the cuts that land (s).")]
    private float sawTick = .12f;
    [SerializeField, Min(0f), Tooltip("Damage of each cut that lands.")]
    private float sawDamage = 8f;
    [SerializeField, Min(.5f), Tooltip("How far in front the bar reaches (m).")]
    private float sawReach = 2f;
    [SerializeField, Range(10f, 180f), Tooltip("How far to either side of its front the bar cuts (degrees).")]
    private float sawArc = 60f;
    [SerializeField, Min(0f), Tooltip("How fast it presses forward while it cuts (m/s).")]
    private float sawLunge = 1.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Guarded without a parry, this share of each cut grinds through the guard.")]
    private float blockShare = .5f;
    [Header("Parried")]
    [SerializeField, Min(0f), Tooltip("Parried, it is thrown back this fast (m/s, a shove).")]
    private float bounceForce = 7f;
    [SerializeField, Min(0f), Tooltip("And loses this much posture: enough breaks it, open to an execution.")]
    private float bounceBreak = 80f;
    [SerializeField, Min(0f), Tooltip("And reels this long (s).")]
    private float bounceStun = 1.6f;
    [SerializeField, Min(0f), Tooltip("Money for parrying its chainsaw.")]
    private int parryReward = 80;
    [Header("Look and sound")]
    [SerializeField] private Color glowColor = new Color(1f, .55f, .12f);
    [SerializeField, Min(.05f)] private float glowSize = .34f;
    [SerializeField, Range(0f, 1f), Tooltip("The engine ticking over.")]
    private float idleVolume = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("The engine revved, as it winds up and cuts.")]
    private float revVolume = .95f;

    private bool cutting;
    private float cutLeft, tickLeft;
    private PlayerMovement quarry;
    private AudioSource idle, rev;

    protected override ZombieKind Kind => ZombieKind.Breadbag;
    /// <summary>Cutting, it presses on by itself (the chase waits).</summary>
    public override bool Driving => Mine && cutting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => idleClip = revClip = null;

    protected override void Awake()
    {
        base.Awake();
        GameTables.Bind(TableName, TableRow, this);
    }

    // The saw is all it has.
    internal override AttackMove? Choose(PlayerMovement target, bool level) => AttackMove.Saw;

    internal override void Struck(AttackMove move)
    {
        if (move != AttackMove.Saw || !Mine) return;
        cutting = true;
        cutLeft = sawSeconds;
        tickLeft = 0f;
        quarry = targeting != null ? targeting.Player : null;
    }

    internal override void Cancelled()
    {
        if (!cutting) return;
        cutting = false;
        // The host stops it on every PC: a copy times its own cut from the windup and would cut on.
        if (Runs && network != null) network.AnnounceCancel();
    }

    private void Update()
    {
        if (!cutting) return;
        float dt = Time.deltaTime * (network != null ? network.Pace : 1f);
        if (!Mine || health == null || !health.IsAlive || (melee != null && (melee.IsStunned || melee.IsLying))) { Cancelled(); return; }
        if ((cutLeft -= dt) <= 0f) { cutting = false; return; }
        // The host presses it forward, turning a little toward whom it cuts.
        if (Runs && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            Halt();
            Vector3 ahead = Flat(transform.forward).normalized;
            if (quarry != null && !quarry.IsDead)
            {
                Vector3 to = Flat(quarry.transform.position - transform.position);
                if (to.sqrMagnitude > .01f) ahead = Vector3.RotateTowards(ahead, to.normalized, 60f * Mathf.Deg2Rad * dt, 0f);
            }
            if (ahead.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(ahead);
            agent.Move(ahead * sawLunge * dt);
        }
        if ((tickLeft -= dt) > 0f) return;
        tickLeft += sawTick;
        Cut(PlayerRoster.Local);
    }

    // One cut of the bar at this PC's own player, if they stand in front of it within reach with nothing between.
    private void Cut(PlayerMovement mine)
    {
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.DamageReceiver == null) return;
        Vector3 chest = mine.transform.position + Vector3.up * 1.1f, from = Feet + Vector3.up * 1.1f;
        Vector3 to = chest - from;
        if (Mathf.Abs(to.y) > 1.6f) return;
        Vector3 flat = Flat(to);
        if (flat.magnitude > sawReach || Vector3.Angle(Flat(transform.forward), flat) > sawArc) return;
        if (Physics.Linecast(from, chest, WorldMask, QueryTriggerInteraction.Ignore)) return;
        Vector3 direction = flat.sqrMagnitude > 1e-4f ? flat.normalized : Flat(transform.forward).normalized;
        var hit = new DamageHit(DamageHit.NewAttackId(), sawDamage, DamageKind.Attack, chest, direction, gameObject, true,
            origin: from);
        var result = mine.DamageReceiver.ReceiveHit(hit);
        if (result == DamageResult.Parried) Bounce(mine, direction);
        else if (result == DamageResult.Blocked && blockShare > 0f)
            // A chainsaw grinds through a guard: a share of the cut gets past it.
            mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), sawDamage * blockShare, DamageKind.Attack, chest,
                direction, gameObject, false, origin: from));
    }

    // Parried: thrown back off balance, the cut over, and the parry paid for (the user's rule: a hit is a plus).
    private void Bounce(PlayerMovement by, Vector3 direction)
    {
        cutting = false;
        if (melee != null && melee.IsAvailable)
        {
            melee.ReceiveShove(direction * bounceForce, transform.position + Vector3.up);
            melee.ApplyPostureDamage(bounceBreak, bounceStun);
        }
        if (by.IsLocal && parryReward > 0) CombatFeedHUD.Award(Texts.Get("feed.score.saw_parry"), parryReward);
    }

    // ---- Every PC: its light, the engine ----

    private void LateUpdate()
    {
        bool shown = ShowElite(glowColor, glowSize, HumanBodyBones.Head, HumanBodyBones.Neck);
        float dt = Time.deltaTime;
        if (!shown)
        {
            if (idle != null) idle.volume = 0f;
            if (rev != null) rev.volume = 0f;
            return;
        }
        if (idle == null) idle = Loop(Or(EliteLooks.Instance != null ? EliteLooks.Instance.chainsawIdle : null, Idle()), 2f, 34f);
        if (rev == null) rev = Loop(Or(EliteLooks.Instance != null ? EliteLooks.Instance.chainsawRev : null, Rev()), 3f, 45f);
        // Revving through the windup (rising), screaming through the cut, ticking over otherwise.
        bool winding = combat != null && combat.IsPreparingAttack && combat.Move == AttackMove.Saw;
        float rise = winding ? Mathf.Clamp01(combat.WindupElapsed / Mathf.Max(.05f, combat.Windup)) : cutting ? 1f : 0f;
        float revShare = winding || cutting ? 1f : 0f;
        if (idle != null) idle.volume = Mathf.MoveTowards(idle.volume, idleVolume * (1f - revShare), dt * 4f);
        if (rev != null)
        {
            rev.volume = Mathf.MoveTowards(rev.volume, revVolume * revShare, dt * 8f);
            rev.pitch = Mathf.Lerp(.85f, 1.15f, rise);
        }
    }

    protected override void BuildHand(Transform holder)
    {
        // A chainsaw: a red-orange engine at the grip, a handle over it, and the bar with its dark chain along +Z.
        Box(holder, new Vector3(0f, .02f, .04f), new Vector3(.13f, .17f, .28f), new Color(.85f, .28f, .08f), default, .35f);
        Box(holder, new Vector3(0f, .13f, .02f), new Vector3(.03f, .05f, .18f), new Color(.08f, .08f, .08f));
        Box(holder, new Vector3(0f, .01f, .46f), new Vector3(.022f, .085f, .58f), new Color(.7f, .72f, .75f), default, .8f);
        Box(holder, new Vector3(0f, .01f, .46f), new Vector3(.03f, .1f, .56f), new Color(.12f, .12f, .12f), default, .5f);
    }

    protected override void BuildHead(Transform holder)
    {
        // The bag over its head: crumpled brown paper, two holes torn for the eyes.
        var paper = new Color(.62f, .5f, .34f);
        Box(holder, new Vector3(0f, .07f, .01f), new Vector3(.27f, .34f, .27f), paper, default, .05f);
        Box(holder, new Vector3(0f, .245f, .01f), new Vector3(.2f, .03f, .2f), paper * .9f, new Vector3(0f, 12f, 0f), .05f);
        Box(holder, new Vector3(.055f, .1f, .14f), new Vector3(.045f, .03f, .012f), Color.black);
        Box(holder, new Vector3(-.055f, .1f, .14f), new Vector3(.045f, .03f, .012f), Color.black);
    }

    // ---- The engine, made here when EliteLooks has none: a two-stroke's pulses over a rasp, looped seamlessly ----

    private static AudioClip Idle() => idleClip != null ? idleClip : idleClip = Engine("Chainsaw Idle", 36, 1f, .35f, 501);
    private static AudioClip Rev() => revClip != null ? revClip : revClip = Engine("Chainsaw Rev", 124, 1f, .8f, 733);

    // Firing pulses at `firing` a second (a whole number over the loop, so it loops without a seam), each a sharp knock
    // that rings out with a little difference from the last, a raspy buzz at twice the rate, and the chain's hiss.
    private static AudioClip Engine(string name, int firing, float length, float rasp, int seed)
    {
        int total = Mathf.RoundToInt(Rate * length), pulses = Mathf.RoundToInt(firing * length);
        var random = new System.Random(seed);
        var strength = new float[pulses];
        for (int i = 0; i < pulses; i++) strength[i] = .75f + (float)random.NextDouble() * .25f;
        var data = new float[total];
        float hiss = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)Rate;
            float cycles = t * firing;
            int pulse = (int)cycles % pulses;
            float within = cycles - Mathf.Floor(cycles);
            float knock = Mathf.Exp(-within * 7f) * strength[pulse] - .14f;
            float buzz = 2f * (cycles * 2f - Mathf.Floor(cycles * 2f)) - 1f;
            hiss += ((float)random.NextDouble() * 2f - 1f - hiss) * .4f;
            data[i] = knock * .8f + buzz * rasp * .35f + hiss * rasp * .3f;
        }
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f) for (int i = 0; i < total; i++) data[i] *= .8f / top;
        var clip = AudioClip.Create(name, total, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
