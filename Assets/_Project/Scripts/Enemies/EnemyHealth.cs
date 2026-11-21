
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>An enemy's standing. Elite and named kills burn magenta in the kill feed and slow the game briefly (backlog 325).</summary>
public enum EnemyRank { Normal, Elite, Named }

[DisallowMultipleComponent, RequireComponent(typeof(MeleeCombatTarget))]
public sealed class EnemyHealth : MonoBehaviour, IDamageReceiver, ICombatVitals, ICombatDisplayName, ITableBound, ICombatFoe
{
    /// <summary>Data/Resources/Tables/Enemies/Enemies.csv. A row covers EnemyHealth, EnemyCombatAI, MeleeCombatTarget and NavMeshAgent.</summary>
    public const string TableName = "Enemies";

    [SerializeField, Min(1f)]
    private float maximumHealth = 100f;

    [Tooltip("체력 바 위에 표시할 이름. 비워두면 오브젝트 이름((Clone)/(Console) 제외)을 사용한다.")]
    [SerializeField] private string displayName = "";

    [Tooltip("Enemies.csv의 행 id. 비워두면 오브젝트 이름((Clone)/(Console) 제외)을 사용한다.")]
    [SerializeField] private string tableId = "";

    [Tooltip("Normal, Elite 또는 Named. Elite와 Named를 잡으면 킬 피드 줄이 자홍 불꽃으로 타고 게임이 잠깐 느려진다.")]
    [SerializeField] private EnemyRank rank = EnemyRank.Normal;

    public string DisplayName => displayName;

    /// <summary>
    /// Its name over its health bar and in the kill feed: a zombie's kind's words from Texts.csv (ZombieType), where every
    /// zombie used to read its prefab's name, Enemy_Test (user, 2026-11-11 playtest). The bar reads it each frame it shows
    /// (EnemyHealthBarHUD), so a name a copy hears of late needs no word to it.
    /// </summary>
    internal void SetDisplayName(string name)
    {
        name = name ?? "";
        if (name == displayName) return;
        displayName = name;
    }
    public EnemyRank Rank => rank;
    /// <summary>Named or elite: its kill stands out in the feed.</summary>
    public bool IsStandout => rank != EnemyRank.Normal;
    /// <summary>
    /// The host or the PC alone: its rank from now on (a tower champion, a song's brute, /spawn n elite; the table sets it
    /// otherwise), told to the others (EnemyNetwork): what a class does to an elite is judged on each player's own PC.
    /// </summary>
    internal void SetRank(EnemyRank value)
    {
        rank = value;
        if (TryGetComponent(out EnemyNetwork network)) network.ShareRank(value);
    }

    /// <summary>Another PC: the rank the host's enemy has.</summary>
    internal void ShowRank(EnemyRank value) => rank = value;
    public float Health { get; private set; }
    /// <summary>
    /// Out of reach for now: every hit is turned away (a ninja gone into its smoke, ZombieNinja, set on every PC alike so
    /// no copy takes what the host's wouldn't).
    /// </summary>
    public bool Untouchable { get; set; }
    public float MaximumHealth => Mathf.Max(1f, maximumHealth * healthShare);
    // A zombie's kind scales its row's health (ZombieType: a runner dies sooner).
    private float healthShare = 1f;

    /// <summary>Its health becomes this share of its row's, at the same fraction of it (ZombieType, every PC alike).</summary>
    internal void SetHealthShare(float share)
    {
        share = Mathf.Max(.01f, share);
        if (Mathf.Approximately(share, healthShare)) return;
        float fraction = MaximumHealth > 0f ? Health / MaximumHealth : 1f;
        healthShare = share;
        // A copy's health is the host's, already at the new share: it only can't be over the new maximum.
        if (network != null && network.IsCopy) Health = Mathf.Min(Health, MaximumHealth);
        else if (IsAlive) Health = Mathf.Max(.01f, fraction * MaximumHealth);
        appliedMaximum = MaximumHealth;
    }
    public bool IsAlive => Health > 0f;
    /// <summary>A zombie is the real thing: its kills count for the run (ICombatFoe).</summary>
    public bool IsPractice => false;
    public bool WavePaused { get; private set; }
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    private NavMeshAgent pausedAgent;
    private EnemyHitReaction reaction;
    private EnemyNetwork network;
    private ZombieBody body;
    private ZombieMind mind;
    private MeleeCombatTarget melee;
    private float appliedMaximum;
    // On a copy: until when this PC's own count of its health stands over an older report from the host (ShowHealth).
    private float trustUntil;
    private bool restorePosition, restoreRotation, restoreStopped;

    public void SetWavePaused(bool paused)
    {
        if (WavePaused == paused) return;
        WavePaused = paused;
        if (paused)
        {
            pausedAgent = GetComponent<NavMeshAgent>();
            if (pausedAgent == null || !pausedAgent.isActiveAndEnabled || !pausedAgent.isOnNavMesh) { pausedAgent = null; return; }
            restorePosition = pausedAgent.updatePosition; restoreRotation = pausedAgent.updateRotation; restoreStopped = pausedAgent.isStopped;
            pausedAgent.isStopped = true; pausedAgent.velocity = Vector3.zero;
            pausedAgent.updatePosition = false; pausedAgent.updateRotation = false;
        }
        else if (pausedAgent != null)
        {
            if (pausedAgent.isActiveAndEnabled && pausedAgent.isOnNavMesh)
            { pausedAgent.nextPosition = transform.position; pausedAgent.isStopped = restoreStopped; }
            pausedAgent.updatePosition = restorePosition; pausedAgent.updateRotation = restoreRotation;
            pausedAgent = null;
        }
    }

    public event Action<EnemyHealth> Died;

    private static readonly List<EnemyHealth> living = new List<EnemyHealth>();
    // The rules asked of every hit on the PC that judges it: any that says yes makes it a silent one (SilentWhen).
    private static readonly List<Func<EnemyHealth, DamageHit, bool>> silentRules = new List<Func<EnemyHealth, DamageHit, bool>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => silentRules.Clear();

    /// <summary>
    /// A card's rule for silent kills: asked of each hit as the PC that judges it lands it (this PC's own player's hits
    /// on a copy, or any hit alone), before the hit is applied. True marks the hit silent (DamageHit.Silent), which goes
    /// to the host with it: if it kills, no zombie standing by notices the death (ZombieMind.DeathSeen). A Ghost's
    /// headshot on a zombie it has marked, say. Keep the rule cheap: it runs for every hit.
    /// </summary>
    public static void SilentWhen(Func<EnemyHealth, DamageHit, bool> rule)
    {
        if (rule != null && !silentRules.Contains(rule)) silentRules.Add(rule);
    }

    /// <summary>Takes a rule given to SilentWhen back out.</summary>
    public static void NoLongerSilentWhen(Func<EnemyHealth, DamageHit, bool> rule) => silentRules.Remove(rule);

    private bool AskedSilent(in DamageHit hit)
    {
        for (int i = 0; i < silentRules.Count; i++)
            if (silentRules[i](this, hit)) return true;
        return false;
    }
    /// <summary>Every enemy switched on in the scene, on this PC (the host's or a copy), dead ones included until they go.</summary>
    public static IReadOnlyList<EnemyHealth> Living => living;

    private void OnEnable() => living.Add(this);
    private void OnDisable() => living.Remove(this);

    private void Awake()
    {
        melee = GetComponent<MeleeCombatTarget>().OrNull() ?? gameObject.AddComponent<MeleeCombatTarget>();
        // Bullet flinch (push, slow, interrupt) applies to existing prefabs without migration.
        reaction = GetComponent<EnemyHitReaction>();
        if (reaction == null) reaction = gameObject.AddComponent<EnemyHitReaction>();
        // The zombie it wears (Resources/Enemies/ZombieLooks); without that asset it stays a capsule.
        body = GetComponent<ZombieBody>();
        if (body == null) body = gameObject.AddComponent<ZombieBody>();
        // What it knows of the players: a horde zombie unless placed calm (ZombieMind.startCalm or /spawn n calm).
        mind = GetComponent<ZombieMind>();
        if (mind == null) mind = gameObject.AddComponent<ZombieMind>();
        network = GetComponent<EnemyNetwork>();
        GameTables.Bind(TableName, string.IsNullOrWhiteSpace(tableId) ? CombatDamageEvents.BaseName(gameObject) : tableId.Trim(),
            this, GetComponent<EnemyCombatAI>(), melee, GetComponent<NavMeshAgent>(), reaction);
        // A shambler's hold on a player plays out here (ZombieGrip; the held player's PlayerHold decides it).
        if (GetComponent<ZombieGrip>() == null) gameObject.AddComponent<ZombieGrip>();
        // Its kind (shambler or runner) goes on top of that row (ZombieType, Tables/ZombieKinds).
        if (GetComponent<ZombieType>() == null) gameObject.AddComponent<ZombieType>();
        Health = MaximumHealth;
        appliedMaximum = MaximumHealth;
    }

    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (!isActiveAndEnabled || !IsAlive || WavePaused ||
            hit.Id <= 0 || hit.Amount <= 0f ||
            float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount))
        {
            return DamageResult.Ignored;
        }
        if (Untouchable) return DamageResult.Invulnerable;

        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        // A card may make this hit a silent one (SilentWhen), here where it is judged: the mark goes on with it.
        if (!hit.Silent && silentRules.Count > 0 && AskedSilent(hit)) hit = hit.Silenced();
        float before = Health;
        // Knocked down and lying there, it takes more (ZombieKnockdown lyingDamage, friend feedback C7), and a crawling
        // brute's head far more (ZombieKinds crawlHead).
        float scale = TakenScale(hit), amount = hit.Amount * scale;
        if (hit.Kind == DamageKind.Execution)
        {
            // An execution takes whatever is left, which on the host may be more than the copy it was judged on. Of one
            // that stands firm (a brute) it takes a share of its whole health instead: executions work on it, without
            // one of them ending a boss made for a whole team's fire.
            float share = melee != null ? melee.ExecutionShare : 1f;
            // A stealth takedown, on a zombie that never knew they were there, ends it whatever it is (user, 2026-11-11
            // playtest: "돌진 좀비도 암살하면 한 번에 죽어야 하는 거 아님??").
            if (mind != null && mind.Unaware) share = 1f;
            Health = share >= 1f ? 0f : Mathf.Max(0f, Health - MaximumHealth * share);
            scale = hit.Amount > 0f ? (before - Health) / hit.Amount : 1f;
        }
        else Health = Mathf.Max(0f, Health - amount);
        // Called out in a Schießhund's duel, it keeps 1 health against anyone but its hound (HoundDuel.Floor: friends help,
        // the hound finishes), unless the hound holds the Ace.
        Health = Mathf.Max(Health, HoundDuel.Floor(this, hit, before));
        // The gauge fills by the health this hit took (an execution's share of a brute too), not by the hit.
        UltimateCharge.AwardEnemyHit(hit, before - Health);
        // A copy shows the hit now and tells the host what it did here: the health it took and whether it killed. The
        // host takes that as sent (ReceiveSent), so for this hit the count on the shooter's own screen is the one that stands.
        bool copy = network != null && network.IsCopy;
        if (copy)
        {
            network.SendHit(hit, before - Health, !IsAlive);
            trustUntil = Time.time + network.PredictionHold;
        }
        else if (network != null) network.AnnounceHit(hit, before, Health);
        // Report before Die(): popups bind to this transform and keep its last position after Destroy. The number shown is
        // what it took, the lying share included: it read as if lying made no difference while only the health knew
        // (user, 2026-11-10).
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth, scale);

        if (!IsAlive)
        {
            Die(hit);
        }
        else if (!copy)
        {
            // It lived through an execution: back on its guard, so the next one has to be earned again.
            if (hit.Kind == DamageKind.Execution && melee != null) melee.ResetPosture();
            if (reaction != null) reaction.OnHit(hit, before - Health, MaximumHealth);
            // Knocked down, the blow pushes the body where its doll runs (here, on the host or alone).
            if (body != null) body.Hit(hit);
            // A blow wakes a calm zombie at once.
            if (mind != null) mind.Struck(hit);
        }
        // On a copy the hit is still this PC's to judge: what it does to its posture and whether it cuts its swing short
        // show here at once and go to the host as results (MeleeCombatTarget on a copy). They used to be worked out on
        // the host alone, so a headshot's flinch reached the shooter a round trip late. The nudge and the slow are the
        // enemy's movement, which is the host's.
        else
        {
            if (reaction != null) reaction.OnHit(hit, before - Health, MaximumHealth, judged: true, moved: false);
            // Lying on a doll this PC runs (its own player knocked it down), the blow pushes the body here at once.
            if (body != null) body.Hit(hit);
        }

        return DamageResult.Damaged;
    }

    /// <summary>
    /// The host: a hit another PC's player landed on their copy, as their own PC judged it (the user's rule of
    /// 2026-11-14: the acting player's PC decides and sends only the result; the host applies it and never judges it
    /// again). It takes what it took there and kills if it killed there. Nothing is worked out again here: not the lying
    /// share, not an execution's part, not whether it could be touched at all. Each of those could differ on the host by
    /// the moment the hit took to arrive (it had begun to get up, noticed someone, a ninja had stepped into its smoke),
    /// and the difference used to leave the shooter with a kill their screen showed and the host had not made: a zombie
    /// hidden on their PC and alive on everyone else's. Only a zombie already dead here takes nothing.
    /// </summary>
    internal void ReceiveSent(DamageHit hit, float taken, bool killed)
    {
        if (!isActiveAndEnabled || !IsAlive || hit.Id <= 0 || hit.Amount <= 0f || float.IsNaN(taken) || float.IsInfinity(taken)) return;
        float before = Health;
        Health = killed ? 0f : Mathf.Max(0f, Health - Mathf.Max(0f, taken));
        if (network != null) network.AnnounceHit(hit, before, Health);
        // The number is theirs to show; here the report carries the world's side of it (blood, sound, gore, drops).
        CombatDamageEvents.Report(this, hit, before, Health, MaximumHealth, (before - Health) / hit.Amount);
        if (!IsAlive) { Die(hit); return; }
        // It lived through an execution: back on its guard, so the next one has to be earned again.
        if (hit.Kind == DamageKind.Execution && melee != null) melee.ResetPosture();
        // Their PC judged what the hit did to its posture and its swing, and sends those apart; where it is nudged and
        // slowed is its movement, which is here.
        if (reaction != null) reaction.OnHit(hit, before - Health, MaximumHealth, judged: false, moved: true);
        if (body != null) body.Hit(hit);
        if (mind != null) mind.Struck(hit);
    }

    /// <summary>
    /// On the attacker's own PC: the host says this hit of theirs killed it, though it had health left on this PC (a
    /// friend's hit this PC had not heard of yet). The kill is theirs, so their PC shows it as its own after all: the
    /// kill's sound and marks, its count, and the body falling here, where its fall is run (ZombieCorpse). It used to
    /// arrive as a bare health of 0: no kill on their screen, and a body that stood stiff for a second with no PC running it.
    /// </summary>
    internal void ConfirmKill(DamageHit hit)
    {
        if (!isActiveAndEnabled || !IsAlive) return;
        float before = Health;
        Health = 0f;
        CombatDamageEvents.Report(this, hit, before, 0f, MaximumHealth, hit.Amount > 0f ? before / hit.Amount : 1f);
        Die(hit);
    }

    /// <summary>On a copy: the health the host says. A kill this PC already judged stays a kill.</summary>
    internal void ShowHealth(float health) => ShowHealth(health, default, false);

    /// <summary>On a copy: someone else's hit, as the host took it.</summary>
    internal void ShowHit(DamageHit hit, float before, float after)
    {
        // Read before the health is shown: a killing hit ends the lying it was scaled by. An execution's number is what
        // it took, which of a brute is a share.
        float scale = hit.Kind == DamageKind.Execution && hit.Amount > 0f ? Mathf.Max(0f, before - after) / hit.Amount : TakenScale(hit);
        ShowHealth(after, hit, true);
        CombatDamageEvents.Report(this, hit, before, after, MaximumHealth, scale);
    }

    // What a hit's amount is multiplied by here and now: more while it lies knocked down.
    private float LyingScale()
    {
        return body != null && body.Wears && melee != null && melee.IsLying ? body.LyingDamage : 1f;
    }

    // A crawling brute's head (ZombieKinds crawlHead) takes its kind's number in place of the lying share when that is
    // more: the leg shot out, the head is the way to finish it (user, 2026-11-12: "누운 거구의 머리를 노려서 빠르게
    // 녹이는"). Not on top of lying, so the fall itself doesn't end a boss in a burst. The host and the shooter's PC both
    // know it crawls (ZombieGore cuts the leg on every PC from the same hits), so both take it alike.
    private float TakenScale(in DamageHit hit)
    {
        float lying = LyingScale();
        if (kind == null) TryGetComponent(out kind);
        bool head = hit.Part == BodyPart.Head || (hit.IsCritical && hit.Method == DamageMethod.Bullet);
        return head && kind != null && kind.WeakHead ? Mathf.Max(lying, kind.CrawlHead) : lying;
    }
    private ZombieType kind;

    // Someone else's kill, seen here: the body falls as their PC runs it (ZombieCorpse), and the enemy waits hidden for
    // the host to remove it. Known: the killing hit came with it, so this PC knows whose PC runs the fall.
    private void ShowHealth(float health, in DamageHit hit, bool known)
    {
        if (!IsAlive)
        {
            // Dead here already by this PC's own blow, and the host's count reached zero on someone else's: theirs is the
            // kill, and the corpse this PC runs goes over to their fall (SyncAudit.md, item 3).
            if (known && health <= 0f && network != null && network.IsSpawned)
                ZombieCorpse.HandTo(network.NetworkObjectId, ZombieCorpse.RunnerOf(hit, true, out _));
            return;
        }
        // While this PC's own hits may still be on their way to the host (trustUntil), a report can only lower the
        // health: one the host sent before it heard of them would give back what they took, and the shot that should
        // have killed then left it standing on the shooter's screen until the host's count came round. The host's count
        // only ever goes down by hits, this PC's among them, so the lower of the two is never below the truth.
        Health = Mathf.Min(Time.time < trustUntil ? Mathf.Min(Health, health) : health, MaximumHealth);
        if (IsAlive) return;
        if (body != null) body.Fall(hit, false, known ? ZombieCorpse.RunnerOf(hit, true, out _) : null);
        if (network != null && network.IsCopy) network.HideUntilRemoved();
    }

    // A live table edit keeps the same health fraction instead of healing or killing enemies mid-fight.
    void ITableBound.OnTableApplied()
    {
        if (appliedMaximum > 0f && IsAlive) Health = Mathf.Max(.01f, Health / appliedMaximum * MaximumHealth);
        appliedMaximum = MaximumHealth;
    }

    private void Die(in DamageHit hit)
    {
        // 웨이브 관리자에게 처치 사실을 알린다.
        Died?.Invoke(this);
        // Where zombies think (the host, or alone): those standing by notice the death, unless the kill was silent.
        if (network == null || !network.IsCopy) ZombieMind.DeathSeen(this, hit);

        // The body falls as its own corpse, run here if this PC's player landed the blow (or the host, for no player).
        if (body != null)
        {
            var runner = ZombieCorpse.RunnerOf(hit, network != null && network.IsSpawned, out bool here);
            body.Fall(hit, here, runner);
        }
        if (network != null && network.IsCopy) network.HideUntilRemoved();
        else Destroy(gameObject);
    }
}
