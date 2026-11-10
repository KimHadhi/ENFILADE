using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What kind of zombie it is (ZombieType). Shambler is 0, so an older report reads as one. Brute: the boss that comes
/// at a song's highlight (friend feedback C2), never picked at random.
/// </summary>
public enum ZombieKind : byte { Shambler, Runner, Brute }

/// <summary>
/// Multiplayer plan stage 4D-1, made Guts & Blackpowder's at the user's word: what kind of zombie it is. A shambler
/// walks nearly as fast as a player and, close in, claws, or grabs and holds anyone not getting away (PlayerHold); a
/// runner (G&B-2) never swings: it charges, leaps and pins (ZombieRunner), and dies sooner (healthShare). The host (or
/// the PC alone) picks one by the shares in
/// Tables/ZombieKinds as the zombie appears, or /spawn n runner|shambler does; the other PCs are told (EnemyNetwork),
/// since their copy times its own swing. A zombie that has lost a leg crawls with the table's Crawler numbers instead
/// (ZombieBody.Crawl.cs). The numbers set its agent (speed, acceleration, turning, and height crawling), its swing
/// (damage, windup, interval, aim height), a runner's bite at the neck (share of attacks, windup; fatal if it lands) and
/// its chase speed (ZombieMind).
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieType : MonoBehaviour
{
    public const string TableName = "ZombieKinds";

    /// <summary>One row of Tables/ZombieKinds.</summary>
    [Serializable]
    public sealed class Numbers
    {
        [Tooltip("How often this kind appears, against the other kinds' shares.")]
        public float share;
        [Tooltip("Chase speed (m/s).")]
        public float speed = 3f;
        public float acceleration = 8f;
        [Tooltip("Turning (degrees a second).")]
        public float angularSpeed = 120f;
        [Tooltip("Damage of one blow.")]
        public float damage = 10f;
        [Tooltip("Seconds from the attack cue to the hit.")]
        public float windup = .45f;
        [Tooltip("Seconds between attacks.")]
        public float attackInterval = 1f;
        [Tooltip("Height it swings at, above a player's feet (m).")]
        public float aimHeight = 1f;
        [Tooltip("Its height on the ground (m): crawling only; 0 keeps the standing height.")]
        public float height;
        [Tooltip("Share of its opening attacks that are a bite at the neck: fatal if it lands (stage 4D-2a).")]
        public float biteShare;
        [Tooltip("A bite's windup (s).")]
        public float biteWindup = .75f;
        [Tooltip("It grabs instead of clawing unless the player is moving away from it (Guts & Blackpowder's shambler).")]
        public bool grabs;
        [Tooltip("A grab's windup (s).")]
        public float grabWindup = .4f;
        [Tooltip("How close it attacks from (m); 0 keeps the enemy's own.")]
        public float attackRange;
        [Tooltip("Where its chase stops, as a share of attackRange; 0 keeps the enemy's own.")]
        public float approachShare;
        [Tooltip("It never swings: it charges and leaps to pin (Guts & Blackpowder's runner, ZombieRunner).")]
        public bool pounces;
        [Tooltip("A leap's time in the air (s).")]
        public float pounceWindup = .35f;
        [Tooltip("A leap catches whoever is still within this of where they stood as it began (m).")]
        public float pounceRadius = 1.1f;
        [Tooltip("Its health, as a share of the enemy row's; 0 keeps what it has.")]
        public float healthShare;
        [Tooltip("Its body's size against the pack's own (1 as it comes; a brute stands bigger).")]
        public float scale = 1f;
        [Tooltip("A blow that lands throws the player this fast away from it (m/s); 0 throws no one.")]
        public float knock;
        [Tooltip("And this fast up (m/s), so they fly and land on their back.")]
        public float knockUp;
        [Tooltip("Knockdowns, guard breaks and bullet flinches don't move it, a parry stuns it only briefly, a shove moves it less and its posture breaks far more slowly (MeleeCombatTarget.Steadfast).")]
        public bool steadfast;
        [Tooltip("An execution takes this share of its whole health; 0 or 1 takes all it has left (the pack's).")]
        public float executionShare;
    }

    // Guts & Blackpowder's shambler, its 14 studs a second against a player's walking 16 put to our walk (4.9 m/s).
    private readonly Numbers shambler = new Numbers
    {
        share = .65f, speed = 4.3f, acceleration = 8f, angularSpeed = 160f, damage = 10f, windup = .3f, attackInterval = 1f,
        grabs = true, grabWindup = .4f, attackRange = 1.5f, approachShare = .7f, healthShare = 1f
    };
    // Its 24 studs a second, and 30 health against the shambler's 100... put to ours.
    private readonly Numbers runner = new Numbers
    {
        share = .35f, speed = 7.4f, acceleration = 14f, angularSpeed = 300f, damage = 10f, windup = .45f, attackInterval = 1f,
        pounces = true, pounceWindup = .35f, pounceRadius = 1.1f, healthShare = .4f
    };
    private readonly Numbers crawler = new Numbers
        { speed = .7f, acceleration = 3f, angularSpeed = 90f, damage = 8f, windup = .6f, attackInterval = 1.6f, aimHeight = .35f, height = .8f };
    // C2's boss: half again as big, keeps up with a walking player, a slow readable swing that throws whoever it lands
    // on, and health for a whole team's fire.
    private readonly Numbers brute = new Numbers
    {
        speed = 5.2f, acceleration = 10f, angularSpeed = 140f, damage = 30f, windup = .65f, attackInterval = 1.8f, aimHeight = 1.1f,
        attackRange = 2.6f, approachShare = .7f, healthShare = 30f, scale = 1.45f, knock = 9f, knockUp = 4.5f, steadfast = true,
        executionShare = .35f
    };

    private NavMeshAgent agent;
    private EnemyCombatAI combat;
    private ZombieMind mind;
    private EnemyNetwork network;
    private EnemyHealth health;
    private MeleeCombatTarget melee;
    private ZombieBody body;

    public ZombieKind Kind { get; private set; }
    /// <summary>Its body's size against the pack's own (ZombieBody).</summary>
    public float Scale => Mathf.Clamp((Kind == ZombieKind.Brute ? brute : Current).scale, .5f, 3f);
    private Numbers Current => Crawls ? crawler : Kind == ZombieKind.Runner ? runner : Kind == ZombieKind.Brute ? brute : shambler;
    /// <summary>Lost a leg: it crawls with the Crawler numbers, whatever its kind.</summary>
    public bool Crawls { get; private set; }
    /// <summary>Its height crawling (m), for its capsule (ZombieBody.Crawl.cs).</summary>
    public float CrawlHeight => crawler.height;

    // Added by EnemyHealth after the enemy's own table row, so these numbers go on top of it.
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        combat = GetComponent<EnemyCombatAI>();
        mind = GetComponent<ZombieMind>();
        network = GetComponent<EnemyNetwork>();
        health = GetComponent<EnemyHealth>();
        melee = GetComponent<MeleeCombatTarget>();
        body = GetComponent<ZombieBody>();
        // A runner's charge and leap; it does nothing for other kinds.
        if (GetComponent<ZombieRunner>() == null) gameObject.AddComponent<ZombieRunner>();
        // Its place in a crowd, its body's size, and stepping back out of players (stage 4E).
        if (GetComponent<ZombieCrowd>() == null) gameObject.AddComponent<ZombieCrowd>();
        // Down ledges and across gaps on the walk mesh's links (friend feedback C5).
        if (GetComponent<ZombieDrop>() == null) gameObject.AddComponent<ZombieDrop>();
        GameTables.BindNested(TableName, nameof(ZombieKind.Shambler), this, shambler);
        GameTables.BindNested(TableName, nameof(ZombieKind.Runner), this, runner);
        GameTables.BindNested(TableName, "Crawler", this, crawler);
        GameTables.BindNested(TableName, nameof(ZombieKind.Brute), this, brute);
        GameTables.Reloaded += Reapply;
        float total = Mathf.Max(0f, shambler.share) + Mathf.Max(0f, runner.share);
        Kind = total > 0f && UnityEngine.Random.value * total < Mathf.Max(0f, runner.share) ? ZombieKind.Runner : ZombieKind.Shambler;
        Apply();
    }

    private void OnDestroy() => GameTables.Reloaded -= Reapply;

    // A live table edit puts the enemy's own row back first; its kind goes on top again.
    private void Reapply() => Apply();

    /// <summary>The host or the PC alone: makes it this kind (/spawn n runner) and tells the others.</summary>
    public void Set(ZombieKind kind)
    {
        Kind = kind;
        Apply();
        if (network != null) network.ShareKind(kind);
    }

    /// <summary>Another PC: the kind the host's zombie is.</summary>
    internal void Show(ZombieKind kind)
    {
        Kind = kind;
        Apply();
    }

    /// <summary>It lost a leg: from now on it crawls (every PC alike).</summary>
    internal void Crawl()
    {
        if (Crawls) return;
        Crawls = true;
        Apply();
    }

    private void Apply()
    {
        var numbers = Current;
        if (agent != null)
        {
            agent.speed = numbers.speed;
            agent.acceleration = numbers.acceleration;
            agent.angularSpeed = numbers.angularSpeed;
            if (numbers.height > 0f) agent.height = numbers.height;
        }
        if (combat != null)
        {
            combat.SetSwing(numbers.damage, numbers.windup, numbers.attackInterval, numbers.aimHeight);
            combat.SetBite(numbers.biteShare, numbers.biteWindup);
            combat.SetGrab(numbers.grabs, numbers.grabWindup);
            combat.SetReach(numbers.attackRange, numbers.approachShare);
            combat.SetPounce(numbers.pounces, numbers.pounceWindup, numbers.pounceRadius);
            combat.SetSlam(numbers.knock, numbers.knockUp);
        }
        if (health != null && numbers.healthShare > 0f) health.SetHealthShare(numbers.healthShare);
        if (mind != null) mind.SetChaseSpeed(numbers.speed);
        // A brute that loses a leg crawls but stays a brute: firm and big.
        var kept = Kind == ZombieKind.Brute ? brute : numbers;
        if (melee != null)
        {
            melee.Steadfast = kept.steadfast;
            melee.ExecutionShare = kept.executionShare > 0f ? Mathf.Min(1f, kept.executionShare) : 1f;
        }
        if (body == null) body = GetComponent<ZombieBody>();
        if (body != null) body.Regrow();
    }
}
