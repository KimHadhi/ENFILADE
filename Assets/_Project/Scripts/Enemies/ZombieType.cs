using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What kind of zombie it is (ZombieType). Shambler is 0, so an older report reads as one. Brute: the boss that comes
/// at a song's highlight (friend feedback C2), never picked at random. Exploder: an elite that walks in and bursts
/// (ZombieExploder, stage C-16), and Charger: one that bellows and runs people down (ZombieCharger), each picked now
/// and then by its share. The 2026-11-11 memo's elites (items 23 and 25): Ninja, vanishing in smoke to strike from
/// behind (ZombieNinja); Candyman, hooking a player and dragging them off (ZombieCandyman); Breadbag, with a chainsaw
/// (ZombieBreadbag). Reaper (item 24, as the user shaped it on 2026-11-12): a grim reaper charging its scythe while a ring
/// on the ground widens, beaten by hugging it low (ZombieReaper). New kinds go on the end: the number is what the network
/// sends.
/// </summary>
public enum ZombieKind : byte { Shambler, Runner, Brute, Exploder, Charger, Ninja, Candyman, Breadbag, Reaper }

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
        [Tooltip("Fast, it breaks into a run (the run clips). Off, it hurries in its own walk played faster instead, so only " +
                 "a runner (and the big elites) is ever seen running (user, 2026-11-11: shamblers running read as runners).")]
        public bool runs;
        [Tooltip("The most of this kind about at once when picked by share (0: no limit), so one elite stays a moment.")]
        public int most;
        [Tooltip("Damage one leg (thigh and shin together) takes before it gives way and the zombie falls and crawls, " +
                 "whatever its health (ZombieGore); 0 keeps Gore.csv's rule. The brute's counter: its knees carry all that " +
                 "weight, so a few rounds into one leg bring it down (user, 2026-11-12).")]
        public float legBreak;
        [Tooltip("Its speed while a leg is half gone (ZombieKinds legBreak) and it limps, as a share of its own; 0 or 1: no slowing.")]
        public float limpPace;
        [Tooltip("While it crawls, a hit on its head takes this many times the damage, on top of the gun's headshot " +
                 "(EnemyHealth); 0 or 1: as any head. The brute's: blood rushes into the head it drags along.")]
        public float crawlHead;
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
        pounces = true, pounceWindup = .35f, pounceRadius = 1.1f, healthShare = .4f, runs = true
    };
    private readonly Numbers crawler = new Numbers
        { speed = .7f, acceleration = 3f, angularSpeed = 90f, damage = 8f, windup = .6f, attackInterval = 1.6f, aimHeight = .35f, height = .8f };
    // C2's boss: half again as big, keeps up with a walking player, a slow readable swing that throws whoever it lands
    // on, and health for a whole team's fire.
    private readonly Numbers brute = new Numbers
    {
        speed = 5.2f, acceleration = 10f, angularSpeed = 140f, damage = 30f, windup = .65f, attackInterval = 1.8f, aimHeight = 1.1f,
        attackRange = 2.6f, approachShare = .7f, healthShare = 30f, scale = 1.45f, knock = 9f, knockUp = 4.5f, steadfast = true,
        executionShare = .35f, runs = true
    };

    // C-16's exploder: a little bloated, a little quicker than a shambler, dies sooner, and never claws (its reach is too
    // short to swing from): it walks into its quarry and bursts (ZombieExploder).
    private readonly Numbers exploder = new Numbers
    {
        share = .05f, speed = 4.6f, acceleration = 9f, angularSpeed = 160f, windup = .3f, attackInterval = 1f, attackRange = .2f,
        approachShare = .5f, healthShare = .7f, scale = 1.12f
    };

    // C-16's charger: bigger than the pack and much harder to kill, a slow swing up close that throws whoever it lands on
    // (the throw is also what running into someone does), and its charge (ZombieCharger).
    private readonly Numbers charger = new Numbers
    {
        share = .03f, speed = 4.4f, acceleration = 9f, angularSpeed = 150f, damage = 16f, windup = .55f, attackInterval = 1.6f, aimHeight = 1f,
        attackRange = 2.1f, approachShare = .7f, healthShare = 8f, scale = 1.28f, knock = 10f, knockUp = 5f, runs = true
    };

    // The memo's elites (2026-11-11, items 23 and 25). Each is rare, alone or nearly, and has its own way to be beaten
    // and a reward for doing it (ZombieNinja, ZombieCandyman, ZombieBreadbag). None has a model of its own yet: a pack
    // body, a little bigger or smaller, its light and what it carries tell it apart (EliteLooks holds the slots).
    private readonly Numbers ninja = new Numbers
    {
        share = .02f, speed = 5.2f, acceleration = 14f, angularSpeed = 420f, damage = 22f, windup = .45f, attackInterval = 1.3f,
        aimHeight = 1.2f, attackRange = 1.8f, approachShare = .7f, healthShare = 2.2f, scale = .96f, most = 2
    };
    private readonly Numbers candyman = new Numbers
    {
        share = .015f, speed = 4f, acceleration = 8f, angularSpeed = 170f, damage = 12f, windup = .5f, attackInterval = 1.3f,
        grabs = true, grabWindup = .55f, attackRange = 1.9f, approachShare = .7f, healthShare = 4f, scale = 1.16f, most = 1
    };
    private readonly Numbers breadbag = new Numbers
    {
        share = .015f, speed = 3.4f, acceleration = 8f, angularSpeed = 150f, damage = 0f, windup = .9f, attackInterval = 2.2f,
        aimHeight = 1.1f, attackRange = 2.1f, approachShare = .75f, healthShare = 6f, scale = 1.18f, most = 1
    };
    // Item 24's reaper: tall and slow, it starts its charge from well off (attackRange) and stands there; its own numbers
    // (the charge, the ring, the cut) are ZombieReaper's. Damage 0: the scythe deals its own.
    private readonly Numbers reaper = new Numbers
    {
        share = .012f, speed = 3.6f, acceleration = 7f, angularSpeed = 120f, damage = 0f, windup = 2.4f, attackInterval = 3.2f,
        aimHeight = 1.1f, attackRange = 4.5f, approachShare = .4f, healthShare = 7f, scale = 1.22f, most = 1
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
    private Numbers Current => Crawls ? crawler : Of(Kind);

    private Numbers Of(ZombieKind kind) => kind == ZombieKind.Runner ? runner : kind == ZombieKind.Brute ? brute
        : kind == ZombieKind.Exploder ? exploder : kind == ZombieKind.Charger ? charger : kind == ZombieKind.Ninja ? ninja
        : kind == ZombieKind.Candyman ? candyman : kind == ZombieKind.Breadbag ? breadbag : kind == ZombieKind.Reaper ? reaper : shambler;

    /// <summary>A memo elite (ninja, candyman, breadbag, reaper): its own component plays it out.</summary>
    public bool IsMemoElite => Kind == ZombieKind.Ninja || Kind == ZombieKind.Candyman || Kind == ZombieKind.Breadbag || Kind == ZombieKind.Reaper;
    /// <summary>Lost a leg: it crawls with the Crawler numbers, whatever its kind.</summary>
    public bool Crawls { get; private set; }
    /// <summary>Fast, its body breaks into a run; otherwise it hurries in its walk (ZombieBody.Move, ZombieKinds runs).</summary>
    public bool RunsWhenFast => Current.runs;
    /// <summary>Its height crawling (m), for its capsule (ZombieBody.Crawl.cs).</summary>
    public float CrawlHeight => crawler.height;
    /// <summary>
    /// Damage one of its legs (thigh and shin together) takes before it gives way (ZombieGore), 0 for Gore.csv's rule:
    /// its kind's own number, crawling or not.
    /// </summary>
    public float LegBreak => Mathf.Max(0f, Of(Kind).legBreak);
    /// <summary>What a hit on its head is multiplied by while it crawls (EnemyHealth): its kind's own, at least 1.</summary>
    public float CrawlHead => Mathf.Max(1f, Of(Kind).crawlHead);
    /// <summary>Its head is the weak spot now: it crawls, and its kind's head gives way crawling (the brute).</summary>
    public bool WeakHead => Crawls && CrawlHead > 1f;
    /// <summary>A leg half gone: it limps, slower by its kind's limpPace (ZombieBody.Legs.cs, every PC alike).</summary>
    public bool Limping { get; private set; }
    /// <summary>
    /// Its health on top of its kind's share: the ENFILADE tower's higher floors hold tougher ones (TowerRun, Tables/
    /// TowerFloors). 1 everywhere else. Every PC alike (EnemyNetwork), so a copy's health bar fills to the same maximum.
    /// </summary>
    public float Toughness { get; private set; } = 1f;

    /// <summary>A leg half gone: from now on it limps, slower, until the leg gives way and it crawls (every PC alike).</summary>
    internal void Limp()
    {
        if (Limping) return;
        Limping = true;
        Apply();
    }

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
        // An exploder's fuse and burst; it does nothing for other kinds.
        if (GetComponent<ZombieExploder>() == null) gameObject.AddComponent<ZombieExploder>();
        // A charger's bellow and charge; likewise.
        if (GetComponent<ZombieCharger>() == null) gameObject.AddComponent<ZombieCharger>();
        GameTables.BindNested(TableName, nameof(ZombieKind.Shambler), this, shambler);
        GameTables.BindNested(TableName, nameof(ZombieKind.Runner), this, runner);
        GameTables.BindNested(TableName, "Crawler", this, crawler);
        GameTables.BindNested(TableName, nameof(ZombieKind.Brute), this, brute);
        GameTables.BindNested(TableName, nameof(ZombieKind.Exploder), this, exploder);
        GameTables.BindNested(TableName, nameof(ZombieKind.Charger), this, charger);
        GameTables.BindNested(TableName, nameof(ZombieKind.Ninja), this, ninja);
        GameTables.BindNested(TableName, nameof(ZombieKind.Candyman), this, candyman);
        GameTables.BindNested(TableName, nameof(ZombieKind.Breadbag), this, breadbag);
        GameTables.BindNested(TableName, nameof(ZombieKind.Reaper), this, reaper);
        GameTables.Reloaded += Reapply;
        Kind = Roll();
        Apply();
    }

    // The kinds picked by their shares, in this order; whatever the roll leaves is a shambler.
    private static readonly ZombieKind[] Rolled =
        { ZombieKind.Runner, ZombieKind.Exploder, ZombieKind.Charger, ZombieKind.Ninja, ZombieKind.Candyman, ZombieKind.Breadbag,
          ZombieKind.Reaper };

    // One kind by the shares, leaving out a kind already about as many times as its `most` allows.
    private ZombieKind Roll()
    {
        float total = Mathf.Max(0f, shambler.share);
        foreach (var kind in Rolled) total += Available(kind);
        float roll = UnityEngine.Random.value * total;
        foreach (var kind in Rolled)
        {
            float share = Available(kind);
            if (roll < share) return kind;
            roll -= share;
        }
        return ZombieKind.Shambler;
    }

    private float Available(ZombieKind kind)
    {
        var numbers = Of(kind);
        if (numbers.share <= 0f) return 0f;
        if (numbers.most <= 0) return numbers.share;
        int about = 0;
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy != health && enemy.TryGetComponent(out ZombieType other) && other.Kind == kind) about++;
        return about >= numbers.most ? 0f : numbers.share;
    }

    private void OnDestroy() => GameTables.Reloaded -= Reapply;

    /// <summary>The Texts.csv id of a kind's name: zombie.shambler, zombie.runner and so on.</summary>
    public static string NameId(ZombieKind kind) => "zombie." + kind.ToString().ToLowerInvariant();

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

    /// <summary>The host or the PC alone: its health times <paramref name="toughness"/> from now on (the tower's floors), told to the others.</summary>
    public void Toughen(float toughness)
    {
        Toughness = Mathf.Clamp(toughness, .1f, 20f);
        Apply();
        if (network != null) network.ShareToughness(Toughness);
    }

    /// <summary>Another PC: how tough the host made its zombie.</summary>
    internal void ShowToughness(float toughness)
    {
        float value = Mathf.Clamp(toughness <= 0f ? 1f : toughness, .1f, 20f);
        if (Mathf.Approximately(value, Toughness)) return;
        Toughness = value;
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
        // Limping on a cracked leg it is slower (ZombieKinds limpPace); crawling has its own pace.
        float pace = Limping && !Crawls && numbers.limpPace > 0f ? Mathf.Min(1f, numbers.limpPace) : 1f;
        if (agent != null)
        {
            agent.speed = numbers.speed * pace;
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
        if (health != null && numbers.healthShare > 0f) health.SetHealthShare(numbers.healthShare * Toughness);
        // Its kind's name, over its health bar and in the kill feed (Texts.csv zombie.*), every PC alike.
        if (health != null) health.SetDisplayName(Texts.Get(NameId(Kind)));
        if (mind != null) mind.SetChaseSpeed(numbers.speed * pace);
        // A brute that loses a leg crawls but stays a brute: firm and big.
        var kept = Kind == ZombieKind.Brute ? brute : numbers;
        if (melee != null)
        {
            melee.Steadfast = kept.steadfast;
            melee.ExecutionShare = kept.executionShare > 0f ? Mathf.Min(1f, kept.executionShare) : 1f;
        }
        if (body == null) body = GetComponent<ZombieBody>();
        if (body != null) body.Regrow();
        // A memo elite's own part, put on only when it is one (most zombies never carry them).
        if (Kind == ZombieKind.Ninja && !TryGetComponent(out ZombieNinja _)) gameObject.AddComponent<ZombieNinja>();
        if (Kind == ZombieKind.Candyman && !TryGetComponent(out ZombieCandyman _)) gameObject.AddComponent<ZombieCandyman>();
        if (Kind == ZombieKind.Breadbag && !TryGetComponent(out ZombieBreadbag _)) gameObject.AddComponent<ZombieBreadbag>();
        if (Kind == ZombieKind.Reaper && !TryGetComponent(out ZombieReaper _)) gameObject.AddComponent<ZombieReaper>();
    }
}
