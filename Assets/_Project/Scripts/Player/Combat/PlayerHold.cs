using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage G&B-1, Guts &amp; Blackpowder's wrestle, at the user's word: a shambler's grab that lands (EnemyCombatAI's Grab,
/// not guarded or parried, only dodged) holds the player. Held, they can't step anywhere or use a gun (PlayerMovement,
/// WeaponController), but a shove or the fists can tear the shambler off: any blow to it, theirs or a teammate's, or a
/// stun, frees them from that one (CombatDamageEvents). A hold lasts holdSeconds. Two or more holding, or one that has
/// grabbed them more than regrabsToBite times, bite (biteDamage a second each); eatenAt holding at once kills outright.
/// Their own PC decides it all (the hit is judged there) and tells the host which zombies hold them (ZombieGrip,
/// PlayerNetworkCombat). Lying on the ground the zombie that reaches them kneels to eat them instead (EnemyCombatAI
/// Feed): it holds them there the same way and bites from the start, alone or not.
/// Stage G&amp;B-2, the runner's pin: a leap that lands (EnemyCombatAI's Pounce) knocks them flat on their back facing it
/// and pins them there, its teeth at their neck. Nothing they do frees them, and no blow but a killing one takes it off
/// (G&amp;B: a teammate must kill it; a teammate knocking it down does too), unless FightsPinned (a melee class perk to
/// come, off until then) lets them fight it themselves. The bites hurt a little (pinBiteDamage); what kills is the pin itself, at the end of pinSeconds, sooner
/// the lower their health (pinLowHealthShare at none) and sooner for each runner that joins it (up to maxJoiners, each
/// joinSpeedup faster). With shield left, though, the shove key bursts the whole shield and throws every runner on them
/// off (TryBurstShield; the user's playtest of 2026-11-11): one shield, one escape. A Knight kicks them off the same way
/// without the shield, once every BreakFreeEvery seconds (the user, 2026-11-11: "나이트는 혼자서 러너한테 덮쳐져도 빠져
/// 나올 수 있게"); the shield is only spent while the kick is not ready. What all of it looks and sounds like
/// on their own screen is HoldFeedback's; whatever holds anyone stands out through walls on every PC (HoldReveal).
/// Numbers: Tables/Player.csv, row Hold.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHold : MonoBehaviour
{
    [SerializeField, Min(.1f), Tooltip("How long one shambler's hold lasts (s).")]
    private float holdSeconds = 5f;
    [SerializeField, Min(0f), Tooltip("Damage a second from each biting shambler.")]
    private float biteDamage = 10f;
    [SerializeField, Min(.05f), Tooltip("How often the bites land (s).")]
    private float biteEvery = .5f;
    [SerializeField, Min(1), Tooltip("This many holding at once bite.")]
    private int biteFrom = 2;
    [SerializeField, Min(1), Tooltip("A single shambler that has grabbed more than this many times bites alone.")]
    private int regrabsToBite = 3;
    [SerializeField, Min(2), Tooltip("This many holding at once eat the player: dead at once.")]
    private int eatenAt = 4;
    [SerializeField, Tooltip("A jolt to the view as a grab lands (degrees; pitch;roll).")]
    private Vector2 grabJolt = new Vector2(4f, 5f);
    [SerializeField, Min(0f), Tooltip("Down on the ground, each zombie eating them takes this much a second from what holds them on the ground (friend feedback C8).")]
    private float downedBiteDamage = 25f;
    [Header("Runner's pin (G&B-2)")]
    [SerializeField, Min(.1f), Tooltip("Pinned at full health, the runner's bite kills after this long (s).")]
    private float pinSeconds = 5f;
    [SerializeField, Range(.05f, 1f), Tooltip("At no health left the pin kills in this share of pinSeconds; in between, in proportion.")]
    private float pinLowHealthShare = .5f;
    [SerializeField, Min(0), Tooltip("This many more runners can join a pin.")]
    private int maxJoiners = 2;
    [SerializeField, Min(0f), Tooltip("Each runner that joins makes the kill this much faster (.5: half again as fast).")]
    private float joinSpeedup = .5f;
    [SerializeField, Min(0f), Tooltip("Damage of each runner's bite while it pins (every biteEvery): the hurt is shown, the pin kills.")]
    private float pinBiteDamage = 2f;
    [SerializeField, Min(0f), Tooltip("Knocked flat by the leap: how fast they skid back along the ground (m/s).")]
    private float pinKnock = 2.5f;
    [SerializeField, Tooltip("A jolt to the view as a runner pins them (degrees; pitch;roll).")]
    private Vector2 pinJolt = new Vector2(10f, 8f);
    [SerializeField, Tooltip("A jolt to the view as a brute's blow throws them (degrees; pitch;roll).")]
    private Vector2 slamJolt = new Vector2(16f, 10f);
    [Header("Shield burst (2026-11-11 playtest)")]
    [SerializeField, Tooltip("Pinned with any shield left, the shove key bursts the whole shield and throws the runners off.")]
    private bool shieldBursts = true;
    [SerializeField, Min(0f), Tooltip("The pin has to have held this long before the key bursts the shield (s), so a shove pressed "
        + "for the counter as the leap landed doesn't spend it by accident.")]
    private float shieldBurstFrom = .25f;
    [SerializeField, Tooltip("The shield bursts by itself this long into a pin (s), the key or not. Negative: only the key.")]
    private float shieldBurstAuto = -1f;
    [SerializeField, Min(0f), Tooltip("Thrown off by the burst, each runner lies this long (s).")]
    private float shieldBurstKnockdown = 2.5f;
    [SerializeField, Tooltip("A jolt to the view as the shield bursts (degrees; pitch;roll).")]
    private Vector2 shieldBurstJolt = new Vector2(7f, 5f);
    [SerializeField, Min(0f), Tooltip("After the burst, nothing takes hold of them for this long (s): the rest of getting up off "
        + "the back, so a runner that was waiting for the pin to free up can't pin them again before they stand.")]
    private float shieldBurstGrace = 2.7f;

    /// <summary>
    /// Pinned by a runner, how far into getting up off the back (the share of Ability.backLandingTime) they stay lying
    /// (PlayerMovement, PlayerBody): the get-up plays on from there once the pin ends.
    /// </summary>
    public const float PinnedDownShare = .12f;

    private struct Holder
    {
        public EnemyHealth Enemy;
        public ZombieGrip Grip;
        public MeleeCombatTarget Melee;
        public float Until;
        public bool Feeding, Pinning;
    }

    private static PlayerHold local;
    private readonly List<Holder> holders = new List<Holder>();
    private readonly Dictionary<EnemyHealth, int> grabs = new Dictionary<EnemyHealth, int>();
    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private PlayerNetworkCombat network;
    private HoldFeedback feedback;
    private float biteLeft, pinProgress, pinnedAt, burstGraceUntil = float.NegativeInfinity, breakFreeAt = float.NegativeInfinity;

    /// <summary>Some zombie holds them.</summary>
    public bool IsHeld => holders.Count > 0;
    /// <summary>How many hold them.</summary>
    public int Holders => holders.Count;
    /// <summary>A runner pins them to the ground.</summary>
    public bool IsPinned => Pinners > 0;
    /// <summary>How far the pin is to killing them (0-1).</summary>
    public float PinProgress => IsPinned ? Mathf.Clamp01(pinProgress) : 0f;
    /// <summary>
    /// The hook for a melee class perk (classes come later): pinned, they can still punch and shove, and kill the runner
    /// themselves. Off by default, as in Guts &amp; Blackpowder.
    /// </summary>
    public bool FightsPinned { get; set; }
    /// <summary>
    /// A Knight's (ClassKit sets it from Tables/ClassAbilities, Knight.breakFree): pinned, the shove key kicks every runner
    /// on them off as the shield burst does, without spending the shield, once every this many seconds. Negative: never.
    /// </summary>
    public float BreakFreeEvery { get; set; } = -1f;
    /// <summary>Pinned and a Knight whose kick is ready: the shove key would throw the runners off for free (HoldFeedback says so).</summary>
    public bool CanBreakFree => BreakFreeEvery >= 0f && IsPinned && receiver != null && receiver.IsAlive && Time.time >= breakFreeAt;
    /// <summary>The most runners that pin one player (the host's runners don't leap at a full pin, ZombieRunner).</summary>
    public static int MostPinning => 1 + (local != null ? local.maxJoiners : 2);
    /// <summary>Pinned with shield left: the shove key would burst it and throw the runners off (HoldFeedback says so).</summary>
    public bool CanBurstShield => shieldBursts && IsPinned && receiver != null && receiver.Shield > 0f && receiver.IsAlive;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        receiver = GetComponent<PlayerDamageReceiver>();
        network = GetComponent<PlayerNetworkCombat>();
    }

    private void OnEnable()
    {
        local = this;
        if (receiver != null) receiver.HitResolved += OnHit;
        CombatDamageEvents.Damaged += OnEnemyDamaged;
    }

    private void OnDisable()
    {
        if (local == this) local = null;
        if (receiver != null) receiver.HitResolved -= OnHit;
        CombatDamageEvents.Damaged -= OnEnemyDamaged;
        LetAllGo();
    }

    /// <summary>A zombie kneels over them eating them where they lie (a Feed hold, not a runner's pin).</summary>
    public bool IsEaten
    {
        get
        {
            foreach (var holder in holders) if (holder.Feeding && !holder.Pinning) return true;
            return false;
        }
    }

    // What being held looks and sounds like on their own screen, made on first need.
    private HoldFeedback Feedback
    {
        get
        {
            if (feedback == null && !TryGetComponent(out feedback))
            {
                feedback = gameObject.AddComponent<HoldFeedback>();
                GameTables.Bind(PlayerMovement.TableName, "HoldFeedback", feedback);
            }
            return feedback;
        }
    }

    private int Pinners
    {
        get
        {
            int count = 0;
            foreach (var holder in holders) if (holder.Pinning) count++;
            return count;
        }
    }

    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hit.Source == null) return;
        // A brute's blow (friend feedback C2) throws them: they fly and land on their back.
        if (hit.Method == DamageMethod.Slam)
        {
            var thrower = hit.Source.GetComponentInParent<EnemyCombatAI>();
            if (thrower != null && receiver.IsAlive && !receiver.IsDowned)
            {
                LetAllGo();
                player.KnockBack(thrower.SlamVelocity(transform.position));
                Jolt(slamJolt);
            }
            return;
        }
        if (hit.Method != DamageMethod.Grab && hit.Method != DamageMethod.Feed && hit.Method != DamageMethod.Pounce) return;
        // Down, only eating takes hold (C8): a zombie kneels over them and eats.
        if (!receiver.IsAlive || (receiver.IsDowned && hit.Method != DamageMethod.Feed)) return;
        var enemy = hit.Source.GetComponentInParent<EnemyHealth>();
        if (hit.Method == DamageMethod.Pounce) Pin(enemy);
        else Grab(enemy, hit.Method == DamageMethod.Feed);
    }

    private void Grab(EnemyHealth enemy, bool feeding)
    {
        if (enemy == null || !enemy.IsAlive) return;
        // Just burst out with the shield: nothing takes hold until they are up.
        if (Time.time < burstGraceUntil) return;
        // On a ladder, a ledge or mid-vault nothing takes hold: held there, they could never climb on (C9).
        if (!feeding && player.IsParkour) return;
        grabs[enemy] = grabs.TryGetValue(enemy, out int times) ? times + 1 : 1;
        int at = IndexOf(enemy);
        if (at >= 0)
        {
            var again = holders[at];
            if (again.Pinning) return;
            again.Until = Time.time + holdSeconds;
            again.Feeding |= feeding;
            holders[at] = again;
            return;
        }
        var grip = enemy.GetComponent<ZombieGrip>();
        var kind = feeding ? HoldKind.Feed : HoldKind.Grab;
        holders.Add(new Holder
        {
            Enemy = enemy, Grip = grip, Melee = enemy.GetComponent<MeleeCombatTarget>(), Until = Time.time + holdSeconds, Feeding = feeding
        });
        if (grip != null) grip.Hold(player, kind);
        Share(enemy, true, kind);
        // A feed bites from the start.
        if (feeding) biteLeft = 0f;
        Jolt(grabJolt);
        Feedback.Began(false);
        if (holders.Count >= eatenAt) Eaten(enemy);
    }

    // A runner's leap landed: flat on their back, facing it, pinned. Others join, up to maxJoiners.
    private void Pin(EnemyHealth enemy)
    {
        if (enemy == null || !enemy.IsAlive || IndexOf(enemy) >= 0) return;
        // On a ladder, a ledge or mid-vault no leap takes hold, as no grab does (EnemyCombatAI also lets it miss there);
        // nor in the moments after a shield burst.
        if (player.IsParkour || Time.time < burstGraceUntil) return;
        int pinners = Pinners;
        if (pinners >= 1 + maxJoiners) return;
        if (pinners == 0)
        {
            // Shamblers standing holding them lose their grip as they go down; any eating them stays on them.
            for (int i = holders.Count - 1; i >= 0; i--) if (!holders[i].Feeding) Release(i);
            pinProgress = 0f;
            pinnedAt = Time.time;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - enemy.transform.position, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            player.KnockBack(away * pinKnock);
        }
        var grip = enemy.GetComponent<ZombieGrip>();
        holders.Add(new Holder
        {
            Enemy = enemy, Grip = grip, Melee = enemy.GetComponent<MeleeCombatTarget>(), Until = float.PositiveInfinity,
            Feeding = true, Pinning = true
        });
        if (grip != null) grip.Hold(player, HoldKind.Pin);
        Share(enemy, true, HoldKind.Pin);
        biteLeft = 0f;
        Jolt(pinJolt);
        Feedback.Began(true);
        if (holders.Count >= eatenAt) Eaten(enemy);
    }

    private void Update()
    {
        if (holders.Count == 0) return;
        if (!receiver.IsAlive) { LetAllGo(); return; }
        // Gone down: a standing grab and a runner's pin let go (a pin would hold them in its own pose, not lying down);
        // whatever kneels eating them stays on them and eats faster.
        if (receiver.IsDowned)
        {
            for (int i = holders.Count - 1; i >= 0; i--)
                if (holders[i].Enemy == null || !holders[i].Enemy.IsAlive || !holders[i].Feeding || holders[i].Pinning) Release(i);
            if (holders.Count == 0) return;
            if ((biteLeft -= Time.deltaTime) > 0f) return;
            biteLeft = biteEvery;
            Bite();
            return;
        }
        // Time, death, a stun or a knockdown let a shambler go. A runner pinning them, only its death or a teammate
        // knocking it down (they can't reach it themselves, but for the perk).
        for (int i = holders.Count - 1; i >= 0; i--)
        {
            var holder = holders[i];
            if (holder.Enemy == null || !holder.Enemy.IsAlive) { Release(i); continue; }
            if (holder.Pinning)
            {
                if (holder.Melee != null && holder.Melee.IsKnockedDown) Release(i);
                continue;
            }
            if (Time.time >= holder.Until || (holder.Melee != null && (holder.Melee.IsStunned || holder.Melee.IsKnockedDown)))
                Release(i);
        }
        if (holders.Count == 0) return;
        // Set to go off by itself (Hold.shieldBurstAuto), the shield bursts that far into the pin; and the shove key bursts
        // it here as well as in PlayerMovement's held branch, read straight off the key whatever locks the controls, so
        // nothing in the movement's frame can swallow the press (the user pressed V again and again: 2026-11-11).
        bool pressed = player != null && player.Input != null && player.InputAvailable && player.Input.ShoveKeyPressed;
        bool auto = shieldBurstAuto >= 0f && Time.time - pinnedAt >= Mathf.Max(shieldBurstFrom, shieldBurstAuto);
        if ((CanBreakFree && pressed || CanBurstShield && (pressed || auto)) && TryBurstShield())
        {
            if (holders.Count == 0) return;
        }
        if (TickPin()) return;
        if ((biteLeft -= Time.deltaTime) > 0f) return;
        biteLeft = biteEvery;
        Bite();
    }

    /// <summary>
    /// The shove key while pinned (PlayerMovement): with shield left, the whole shield bursts (PlayerDamageReceiver.
    /// SpendShield, so it breaks with its usual look and sound), every runner on them is thrown off and knocked down for
    /// shieldBurstKnockdown, and they get up. One shield buys one escape (the user's playtest of 2026-11-11: "쉴드 있으면
    /// 쉴드 대가로 러너한테서 한번 벗어나게"). False with no pin, no shield, or a pin younger than shieldBurstFrom.
    /// </summary>
    internal bool TryBurstShield()
    {
        // A Knight's kick, when it is ready, goes first: it costs nothing.
        if (CanBreakFree && Time.time - pinnedAt >= shieldBurstFrom)
        {
            breakFreeAt = Time.time + Mathf.Max(0f, BreakFreeEvery);
            ThrowOff(false);
            GetComponent<PlayerFistAudio>()?.PlayHeavyHit();
            return true;
        }
        if (!CanBurstShield || Time.time - pinnedAt < shieldBurstFrom) return false;
        ThrowOff(true);
        return true;
    }

    // Everything on them thrown off at once: the shield burst (spent) or a Knight's kick (free).
    private void ThrowOff(bool spendShield)
    {
        EnemyHealth first = null;
        foreach (var holder in holders) if (holder.Pinning && holder.Enemy != null) { first = holder.Enemy; break; }
        if (first == null) return;
        var cause = new DamageHit(DamageHit.NewAttackId(), 0f, DamageKind.Attack, transform.position + Vector3.up * .4f,
            transform.position - first.transform.position, first.gameObject, false, 0f, true);
        if (spendShield && !receiver.SpendShield(cause)) return;
        // Everything on them lets go here at once, the runners pinning them and any zombie kneeling to eat them (a burst
        // that left an eater on them kept them down and bitten: the 2026-11-11 review). The host hears of it (ShareHold)
        // and of each knockdown (a copy's MeleeCombatTarget forwards it), so on every PC they go flying off. A brute
        // can't be knocked down; it reels instead.
        for (int i = holders.Count - 1; i >= 0; i--)
        {
            var holder = holders[i];
            if (holder.Enemy != null)
            {
                Vector3 away = Vector3.ProjectOnPlane(holder.Enemy.transform.position - transform.position, Vector3.up);
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                holder.Enemy.GetComponent<EnemyCombatAI>()?.CancelAttack();
                if (holder.Melee != null && holder.Melee.IsAvailable)
                {
                    if (holder.Melee.Steadfast) holder.Melee.Interrupt(shieldBurstKnockdown);
                    else holder.Melee.KnockDown(away, shieldBurstKnockdown);
                }
                if (holder.Pinning) Burst(holder.Melee, away);
            }
            Release(i);
        }
        biteLeft = pinProgress = 0f;
        burstGraceUntil = Time.time + shieldBurstGrace;
        Jolt(shieldBurstJolt);
    }

    // Where the shield met the runner: the shove counter's burst in this PC's view, shared so the others see it there too.
    private void Burst(MeleeCombatTarget target, Vector3 away)
    {
        if (target == null) return;
        var camera = GetComponentInChildren<Camera>();
        if (camera == null) return;
        var view = camera.transform;
        Vector3 to = target.transform.position + Vector3.up * .8f - view.position;
        to = to.sqrMagnitude > 1e-4f ? to.normalized : view.forward;
        Vector3 contact = view.position + Vector3.Slerp(view.forward, to, .6f).normalized * .7f;
        GetComponent<ParryFeedback>()?.CounterBurst(contact, (to * .35f + view.up * .5f).normalized, 1.2f);
        if (target.TryGetComponent(out EnemyNetwork network)) network.ShareParry(contact, away, true);
    }

    // The pin closes on a kill: sooner at low health, sooner with each runner that joined. True when it killed.
    private bool TickPin()
    {
        int pinners = Pinners;
        if (pinners == 0) return false;
        float health = receiver.MaximumHealth > 0f ? Mathf.Clamp01(receiver.Health / receiver.MaximumHealth) : 1f;
        float seconds = pinSeconds * Mathf.Lerp(pinLowHealthShare, 1f, health);
        pinProgress += Time.deltaTime * (1f + joinSpeedup * (pinners - 1)) / Mathf.Max(.05f, seconds);
        if (pinProgress < 1f) return false;
        foreach (var holder in holders)
            if (holder.Pinning && holder.Enemy != null) { Eaten(holder.Enemy); return true; }
        return false;
    }

    // Two or more at once bite, each; alone, only one that keeps coming back, or eats them, or pins them.
    private void Bite()
    {
        bool together = holders.Count >= biteFrom;
        foreach (var holder in holders.ToArray())
        {
            if (holder.Enemy == null) continue;
            if (!together && !holder.Feeding && (!grabs.TryGetValue(holder.Enemy, out int times) || times <= regrabsToBite)) continue;
            float damage = receiver.IsDowned ? downedBiteDamage * biteEvery : holder.Pinning ? pinBiteDamage : biteDamage * biteEvery;
            if (damage <= 0f) continue;
            receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), damage, DamageKind.Attack,
                transform.position + Vector3.up * 1.4f, transform.position - holder.Enemy.transform.position, holder.Enemy.gameObject,
                false, 0f, true));
            Feedback.Bitten();
            if (!receiver.IsAlive) return;
        }
    }

    private void Eaten(EnemyHealth by)
    {
        receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Max(1f, receiver.Health), DamageKind.Attack,
            transform.position + Vector3.up * 1.4f, transform.position - by.transform.position, by.gameObject,
            false, 0f, true, isFatal: true));
        LetAllGo();
    }

    // A blow to a shambler holding them, from anyone, frees them from it (Guts & Blackpowder: damaged or stunned). A
    // runner pinning them lets go only when it dies (Update).
    private void OnEnemyDamaged(CombatDamageReport report)
    {
        if (holders.Count == 0 || !(report.Target is EnemyHealth enemy)) return;
        int at = IndexOf(enemy);
        if (at >= 0 && !holders[at].Pinning) Release(at);
    }

    private int IndexOf(EnemyHealth enemy)
    {
        for (int i = 0; i < holders.Count; i++) if (holders[i].Enemy == enemy) return i;
        return -1;
    }

    private void Release(int at)
    {
        var holder = holders[at];
        holders.RemoveAt(at);
        if (holder.Grip != null) holder.Grip.LetGo();
        if (holder.Enemy != null)
            Share(holder.Enemy, false, holder.Pinning ? HoldKind.Pin : holder.Feeding ? HoldKind.Feed : HoldKind.Grab);
        if (Pinners == 0) pinProgress = 0f;
    }

    private void LetAllGo()
    {
        for (int i = holders.Count - 1; i >= 0; i--) Release(i);
        biteLeft = pinProgress = 0f;
    }

    private void Jolt(Vector2 jolt)
        => GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(new Vector3(jolt.x, 0f, jolt.y * (Random.value < .5f ? -1f : 1f)));

    // In a room the host runs the zombies: it hears who holds this player from their own PC.
    private void Share(EnemyHealth enemy, bool holding, HoldKind kind)
    {
        if (network != null && enemy.TryGetComponent(out EnemyNetwork enemyNetwork)) network.ShareHold(enemyNetwork, holding, kind);
    }
}
