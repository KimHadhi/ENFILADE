using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Stage 4D-1: a zombie that loses a leg crawls (the user's gore pick: ZombieGore cuts the leg and raises Crippled on
/// every PC). It goes down on its doll as in a knockdown (the host knocks it down along its facing), then instead of
/// standing it pushes up into a crawl (ZombieLooks.toCrawl, lined up with the doll like a getting-up clip), and from then
/// on it crawls: crawl idle and crawl forward by its speed under the legs, a low swing at the legs (crawlAttack) and its
/// own flinch. Its capsule shrinks, and its speed, swing and aim come from the Crawler row (ZombieType). With this in
/// place a living zombie may lose a leg (ZombieGore.LegsCanCrawl).
/// </summary>
public sealed partial class ZombieBody
{
    // The knockdown a lost leg brings, at least (s): then it lies until still, as any knockdown.
    private const float CrawlFall = .6f;

    private EnemyHealth enemyHealth;
    private ZombieType type;
    private AnimationMixerPlayable crawls;
    private AnimationClipPlayable crawlIdle, crawlForward, crawlAttack, crawlFlinch, riseCrawl;
    private float crawlIdleLength, crawlForwardLength, crawlAttackLength, crawlFlinchLength, riseCrawlLength;
    private float crawlClipSpeed, crawlAttackHit, crawlShare, crawlIdleTime, crawlPhase;
    private (Vector2 hips, float head) riseCrawlShape;
    private bool crawling, watchingCrawl;

    /// <summary>It lost a leg and crawls.</summary>
    public bool Crawling => crawling;

    // Getting up: the crawl's push-up for a crawler, else the clip for how it lies.
    private AnimationClipPlayable RisePlayable() => crawling ? riseCrawl : riseFaceDown ? riseDown : riseUp;
    private float RiseLength => crawling ? riseCrawlLength : riseFaceDown ? riseDownLength : riseUpLength;

    // The crawl under the legs (top 8), its swing (9), its flinch (10) and its push-up (11).
    private void BuildCrawl(ZombieLooks looks, AnimationClip rest)
    {
        crawls = AnimationMixerPlayable.Create(graph, 2);
        crawlIdle = Clip(Or(looks.crawlIdle, rest), crawls, 0, out crawlIdleLength);
        crawlForward = Clip(Or(looks.crawl, Or(looks.crawlIdle, rest)), crawls, 1, out crawlForwardLength);
        graph.Connect(crawls, 0, top, 8);
        crawls.SetInputWeight(0, 1f);
        crawlAttack = Clip(Or(looks.crawlAttack, Or(looks.attack, rest)), top, 9, out crawlAttackLength);
        crawlFlinch = Clip(Or(looks.crawlFlinch, Or(looks.flinch, rest)), top, 10, out crawlFlinchLength);
        riseCrawl = Clip(Or(looks.toCrawl, Or(looks.crawlIdle, rest)), top, 11, out riseCrawlLength);
        crawlClipSpeed = RootSpeed(looks.crawl, .5f);
        crawlAttackHit = HitTime(looks.crawlAttack, crawlAttackLength);
    }

    private void WatchCrawl()
    {
        enemyHealth = GetComponent<EnemyHealth>();
        type = GetComponent<ZombieType>();
        ZombieGore.LegsCanCrawl = true;
        if (!watchingCrawl) ZombieGore.Crippled += OnCrippled;
        watchingCrawl = true;
        // A leg or an arm already lost (a copy made late).
        if (ZombieGore.Losses(enemyHealth, out int legs, out _) && legs > 0) StartCrawl(false);
        FindLostArms();
    }

    private void StopWatchingCrawl()
    {
        if (watchingCrawl) ZombieGore.Crippled -= OnCrippled;
        watchingCrawl = false;
    }

    private void OnCrippled(EnemyHealth enemy, BodyPart part)
    {
        if (enemy != enemyHealth || enemyHealth == null || !enemyHealth.IsAlive) return;
        if (BodyParts.IsLeg(part)) StartCrawl(true);
        else if (BodyParts.IsArm(part)) OnArmLost(part);
    }

    private void StartCrawl(bool fall)
    {
        if (crawling || Model == null) return;
        crawling = true;
        crawlShare = 1f;
        striking = flinching = cueing = false;
        attackWeight = flinchWeight = staggerWeight = cueWeight = 0f;
        if (combat != null) combat.CancelAttack();
        if (type != null) type.Crawl();
        // Low to the ground: its capsule shrinks to its crawling height, its feet where they were.
        var capsule = covered.Count > 0 ? covered[0] as CapsuleCollider : null;
        if (capsule != null && type != null && type.CrawlHeight > 0f)
        {
            capsule.height = Mathf.Max(type.CrawlHeight, capsule.radius * 2f);
            capsule.center = new Vector3(capsule.center.x, feetHeight + capsule.height * .5f, capsule.center.z);
        }
        // Losing the leg brings it down; the host says so, and its copies follow the fall.
        // A brute (steadfast, friend feedback C2) can't be knocked down, but a lost leg still brings it down.
        if (fall && !down && target != null && (network == null || !network.IsCopy))
        {
            bool firm = target.Steadfast;
            target.Steadfast = false;
            target.KnockDown(transform.forward, CrawlFall);
            target.Steadfast = firm;
        }
    }

    /// <summary>From Update, after Move: crawl idle and forward by its speed, the arms keeping the clip's pace.</summary>
    private void CrawlTick(float dt)
    {
        if (!crawling) return;
        float pace = Mathf.Max(.05f, crawlClipSpeed);
        float forward = Mathf.SmoothStep(0f, 1f, speed / pace);
        crawls.SetInputWeight(0, 1f - forward);
        crawls.SetInputWeight(1, forward);
        float rate = Mathf.Clamp(speed / pace, .5f, MaxRate);
        crawlPhase = Mathf.Repeat(crawlPhase + dt * rate / crawlForwardLength, 1f);
        crawlForward.SetTime(crawlPhase * crawlForwardLength);
        crawlIdleTime = Mathf.Repeat(crawlIdleTime + dt, crawlIdleLength);
        crawlIdle.SetTime(crawlIdleTime);
        crawlAttack.SetTime(SwingTime(crawlAttackHit));
        crawlFlinch.SetTime(Mathf.Min(flinchTime, crawlFlinchLength));
    }
}
