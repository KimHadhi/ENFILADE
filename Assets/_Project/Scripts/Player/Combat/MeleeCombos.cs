using System;
using UnityEngine;

/// <summary>Which combo move a strike is (MeleeCombos); None is an ordinary punch.</summary>
public enum ComboStrike : byte { None, Riposte, Spin, Chase }

/// <summary>
/// The fists' combos: moves that go on a press made in time, judged on this player's own PC. All of them are generous
/// and none costs anything when missed (the user's rule: landing one adds, missing one simply doesn't happen).
/// - Riposte (user, 2026-11-11: "패링 후 바로 공격할 수 있게"): for Riposte.window real seconds after a parry, an attack
///   press strikes at once, the guard held or not, as Sekiro's deflect-then-strike and For Honor's parry punish: the
///   parry's hit stop lets go, the feet are freed, the body closes the gap the parry pushed open and the free hand drives
///   a straight into the parried attacker, harder than a full charge. When the parry opened a Knight's perfect-defense
///   counter, that counter takes the click first (PlayerPerfectDefense reads it earlier in the frame), so the two never
///   both go.
/// - Link (user, 2026-11-11 #10, League's auto-attack reset: "평타 Q 평타", the attack after the Q a special spinning
///   move): a skill hands the punch straight back. The fists' shove, the guard break once it lands, a dash push that
///   meets someone and the crouched sweep each leave Link.window seconds in which an attack press is a spinning backfist
///   at once: wider, through up to Link.targets bodies, harder than a full charge. The shove keeps its own wait.
/// - Chase (Sekiro's shuriken into Chasing Slice): for Chase.window seconds after a stone is thrown (T), an attack press
///   with someone under the crosshair within Chase.range dashes at them and lands the spinning backfist. With nobody
///   there the press is an ordinary punch and the chance stays open.
/// Each move that goes rings a short tick, higher the rarer it is. The dash in is committed: no dodge starts until the
/// strike, a guard press calls it off. Numbers: Tables/Combos.csv.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.MeleeCombat + 1)]
public sealed class MeleeCombos : MonoBehaviour
{
    public const string TableName = "Combos";

    [Serializable]
    public sealed class RiposteNumbers
    {
        [Min(0f), Tooltip("After a parry, an attack press this soon (real s) is a riposte.")]
        public float window = .5f;
        [Min(0f), Tooltip("Its damage against a full-charged punch's.")]
        public float damage = 1.6f;
        [Min(0f), Tooltip("Its posture damage against a full-charged punch's.")]
        public float posture = 2f;
        [Min(1f), Tooltip("Speed of the step in to the parried attacker (m/s).")]
        public float dashSpeed = 14f;
        [Min(0f), Tooltip("The fist goes after this long (s) even if the step hasn't closed in.")]
        public float dashTimeout = .2f;
        [Min(.1f), Tooltip("The step stops this far from the attacker's body (m, from the middle of the player's capsule).")]
        public float stopDistance = .55f;
        [Min(.3f), Tooltip("The straight lands on the attacker's body this close to the eyes (m), nothing solid between.")]
        public float reach = 1.8f;
    }

    [Serializable]
    public sealed class LinkNumbers
    {
        [Min(0f), Tooltip("After a skill (fists' shove, guard break, dash push, crouched sweep), an attack press this soon (s) is the spinning backfist.")]
        public float window = .6f;
        [Min(0f), Tooltip("Its damage against a full-charged punch's.")]
        public float damage = 1.3f;
        [Min(0f), Tooltip("Its posture damage against a full-charged punch's.")]
        public float posture = 1.5f;
        [Min(0f), Tooltip("Reach beyond a punch's (m).")]
        public float reach = .3f;
        [Min(.05f), Tooltip("How wide it sweeps (m; a punch: 0.22).")]
        public float radius = .45f;
        [Min(1), Tooltip("Bodies it can go through.")]
        public int targets = 3;
    }

    [Serializable]
    public sealed class ChaseNumbers
    {
        [Min(0f), Tooltip("After a stone is thrown (T), an attack press this soon (s) dashes at whoever the crosshair is on.")]
        public float window = .9f;
        [Min(1f), Tooltip("How far away they may be (m).")]
        public float range = 9f;
        [Min(0f), Tooltip("How far off the crosshair they may stand (m, the radius of the look's sweep).")]
        public float aimRadius = .5f;
        [Min(1f), Tooltip("Speed of the dash (m/s).")]
        public float dashSpeed = 18f;
        [Min(0f), Tooltip("The backfist goes after this long (s) even if the dash hasn't closed in.")]
        public float dashTimeout = .45f;
        [Min(.1f), Tooltip("The dash stops this far from their body (m, from the middle of the player's capsule).")]
        public float stopDistance = .6f;
        [Min(0f), Tooltip("Its damage against a full-charged punch's.")]
        public float damage = 1.4f;
        [Min(0f), Tooltip("Its posture damage against a full-charged punch's.")]
        public float posture = 1.5f;
        [Min(.3f), Tooltip("The backfist lands on their body this close to the eyes (m), nothing solid between.")]
        public float reach = 1.8f;
    }

    [SerializeField] private RiposteNumbers riposte = new RiposteNumbers();
    [SerializeField] private LinkNumbers link = new LinkNumbers();
    [SerializeField] private ChaseNumbers chase = new ChaseNumbers();

    // Dodges wait this long (real s) past the last frame of a dash in; the strike frees them at once.
    private const float DodgeHold = .1f;
    // How fast the look turns onto the one dashed at (1/s: the share of the turn left taken each second).
    private const float LookRate = 25f;
    // The tick each move rings (Sounds.csv HitConfirm/Body, the hit marker's dry tick), pitched by the move.
    private const string TickBank = "HitConfirm", TickKey = "Body";

    private PlayerMovement player;
    private PlayerMeleeCombat melee;
    private PlayerThrow thrower;
    private SoundBank ticks;
    private MeleeCombatTarget parried, dashTarget;
    private ComboStrike dashStrike;
    private readonly RaycastHit[] hits = new RaycastHit[24], sightHits = new RaycastHit[16];
    // clock: this player's own game time (its pace in a room), for the windows counted in game seconds.
    private float clock, riposteUntil = float.NegativeInfinity, linkUntil = float.NegativeInfinity, chaseUntil = float.NegativeInfinity;
    private float dashTime, dashTimeout, dashSpeed, dashStop, lastGap, lastStep;
    private bool ticksLoaded;

    public RiposteNumbers Riposte => riposte;
    /// <summary>Closing in for a combo strike: the fists take no other press meanwhile.</summary>
    public bool IsDashing => dashTarget != null;
    /// <summary>A parry's riposte can still go (real time).</summary>
    public bool RiposteOpen => parried != null && Time.unscaledTime < riposteUntil;
    /// <summary>A skill just went: the next press is the spinning backfist.</summary>
    public bool LinkOpen => clock < linkUntil;
    /// <summary>A stone just left the hand: the next press with someone under the crosshair dashes at them.</summary>
    public bool ChaseOpen => clock < chaseUntil;

    /// <summary>The player's combos, made and bound to the table on first use.</summary>
    public static MeleeCombos For(PlayerMovement player)
    {
        if (player == null || !Application.isPlaying) return null;
        if (player.TryGetComponent(out MeleeCombos combos)) return combos;
        combos = player.gameObject.AddComponent<MeleeCombos>();
        GameTables.BindNested(TableName, "Riposte", combos, combos.riposte);
        GameTables.BindNested(TableName, "Link", combos, combos.link);
        GameTables.BindNested(TableName, "Chase", combos, combos.chase);
        return combos;
    }

    private void Awake() => player = GetComponent<PlayerMovement>();

    /// <summary>
    /// A skill just went (the fists' shove, a guard break that landed, a dash push that met someone, a crouched sweep):
    /// the punch is handed straight back as the spinning backfist for a moment. With a gun out it simply runs out.
    /// </summary>
    internal void OpenLink()
    {
        if (Mine) linkUntil = clock + link.window;
    }

    private void OnThrown(Vector3 from, Vector3 velocity) => chaseUntil = clock + chase.window;

    private PlayerMeleeCombat Melee => melee != null ? melee : melee = player != null ? player.MeleeCombat : null;
    private bool Mine => player != null && player.IsLocal && !player.IsNetworkDriven;

    /// <summary>A parry landed (PlayerMeleeCombat.TryDefend): its attacker can be riposted for a moment.</summary>
    internal void OpenRiposte(MeleeCombatTarget attacker)
    {
        if (!Mine || attacker == null) return;
        parried = attacker;
        riposteUntil = Time.unscaledTime + riposte.window;
    }

    /// <summary>
    /// An attack press reached the fists (PlayerMeleeCombat, before its guard and its waits): true when a combo took it,
    /// a strike now or a dash in that ends in one.
    /// </summary>
    internal bool TryTake()
    {
        if (!Mine || Melee == null || IsDashing) return false;
        if (RiposteOpen && TryRiposte()) return true;
        if (ChaseOpen && TryChase()) return true;
        if (LinkOpen)
        {
            linkUntil = float.NegativeInfinity;
            Tick(1.2f);
            Melee.StrikeCombo(ComboStrike.Spin);
            return true;
        }
        return false;
    }

    // Whoever the crosshair rests on within range, nothing solid between: the dash goes at them.
    private bool TryChase()
    {
        Transform eyes = PlayerView.EyesOf(player);
        if (eyes == null) return false;
        int count = Physics.SphereCastNonAlloc(eyes.position, chase.aimRadius, eyes.forward, hits, chase.range,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        MeleeCombatTarget best = null;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= nearest) continue;
            var candidate = hits[i].collider.GetComponentInParent<MeleeCombatTarget>();
            if (candidate == null || !candidate.IsAvailable || candidate.GetComponent<IDamageReceiver>() == null) continue;
            if (!InSight(eyes.position, hits[i].collider, candidate)) continue;
            nearest = hits[i].distance; best = candidate;
        }
        if (best == null) return false;
        chaseUntil = float.NegativeInfinity;
        Tick(1.35f);
        BeginDash(best, ComboStrike.Chase, chase.dashSpeed, chase.dashTimeout, chase.stopDistance);
        return true;
    }

    // Nothing solid between the eyes and the middle of that body but this player and the target themselves.
    private bool InSight(Vector3 from, Collider body, MeleeCombatTarget target)
    {
        Vector3 delta = body.bounds.center - from;
        float length = delta.magnitude;
        if (length < .01f) return true;
        int count = Physics.RaycastNonAlloc(from, delta / length, sightHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].collider.transform;
            // The stone just thrown may still be on its way there.
            if (hit.IsChildOf(transform) || hit.IsChildOf(target.transform) || ThrownStone.IsStone(sightHits[i].collider)) continue;
            // Another zombie in the way is someone to hit too, not a wall.
            if (sightHits[i].collider.GetComponentInParent<MeleeCombatTarget>() != null) continue;
            return false;
        }
        return true;
    }

    // The move's tick: the hit marker's dry tick, pitched up by how special the move is.
    private void Tick(float pitch)
    {
        if (!ticksLoaded) { ticks = SoundBank.Load(TickBank); ticksLoaded = true; }
        var clip = ticks != null ? ticks.Pick(TickKey) : null;
        if (clip == null) return;
        var tuning = ticks.SettingsOf(TickKey);
        FeelAudio.PlayClip(clip, tuning.Volume, pitch);
    }

    private bool TryRiposte()
    {
        var target = parried;
        parried = null; riposteUntil = float.NegativeInfinity;
        // A Knight's counter, opened by the same parry, is the bigger move and already had the click.
        var defense = player.PerfectDefense;
        if (defense != null && (defense.CounterAvailable || defense.IsCountering)) return false;
        if (target == null || !target.IsAvailable) return false;
        // Time lets go of the parry's catch at once, so the step and the fist run at full speed.
        if (TryGetComponent(out CombatTimeEffects time) && TryGetComponent(out ParryFeedback feedback)) time.Release(feedback);
        Melee.FreeFeet();
        Tick(1.1f);
        BeginDash(target, ComboStrike.Riposte, riposte.dashSpeed, riposte.dashTimeout, riposte.stopDistance);
        return true;
    }

    /// <summary>
    /// A combo strike's numbers (PlayerMeleeCombat): damage and posture against a full-charged punch's, its reach (beyond
    /// a punch's for a sweep, from the eyes for an aimed strike), how wide it sweeps (0: a punch's) and how many bodies.
    /// </summary>
    internal void Numbers(ComboStrike strike, out float damage, out float posture, out float reach, out float radius, out int targets)
    {
        damage = posture = 1f; reach = radius = 0f; targets = 0;
        switch (strike)
        {
            case ComboStrike.Riposte: damage = riposte.damage; posture = riposte.posture; reach = riposte.reach; break;
            case ComboStrike.Spin: damage = link.damage; posture = link.posture; reach = link.reach; radius = link.radius; targets = link.targets; break;
            case ComboStrike.Chase: damage = chase.damage; posture = chase.posture; reach = chase.reach; radius = link.radius; targets = link.targets; break;
        }
    }

    // Steps in to the target, then strikes; already close, it strikes at once.
    private void BeginDash(MeleeCombatTarget target, ComboStrike strike, float speed, float timeout, float stop)
    {
        dashTarget = target; dashStrike = strike;
        dashSpeed = speed; dashTimeout = timeout; dashStop = stop;
        dashTime = lastGap = lastStep = 0f;
        player.HorizontalVelocity = Vector3.zero;
        player.Abilities?.Dodge.HoldOff(DodgeHold);
        TickDash(0f);
    }

    /// <summary>Calls off a dash in under way (a guard press, a dodge, death): nothing strikes.</summary>
    internal void CancelDash()
    {
        if (dashTarget == null) return;
        dashTarget = null;
        player.Abilities?.Dodge.HoldOff(0f);
    }

    private void Update()
    {
        if (player == null || Time.timeScale <= 0f) return;
        clock += player.DeltaTime;
        // The stone's hand is added with the body, maybe after this; listened to once found, on this player's own PC.
        if (thrower == null && Mine && TryGetComponent(out thrower)) thrower.Thrown += OnThrown;
        if (dashTarget != null) TickDash(player.DeltaTime);
    }

    private void TickDash(float dt)
    {
        var target = dashTarget;
        var melee = Melee;
        if (target == null || !target.IsAvailable || melee == null || !melee.IsActive || !player.DamageReceiver.IsAlive
            || player.Execution?.IsExecuting == true) { CancelDash(); return; }
        player.Abilities?.Dodge.HoldOff(DodgeHold);
        var body = target.GetComponentInChildren<Collider>();
        if (body == null) { CancelDash(); return; }
        float gap = BodyGap.Across(player, body, out Vector3 toward);
        // Held up on the way (someone pressed in between): strike now rather than push at it until the timeout.
        bool stalled = lastStep > .01f && lastGap - gap < lastStep * .25f;
        if (gap > dashStop && dashTime < dashTimeout && !stalled)
        {
            if (dt <= 0f) return;
            dashTime += dt;
            lastGap = gap; lastStep = Mathf.Min(gap - dashStop, dashSpeed * dt);
            player.Motor.AddDisplacement(toward * lastStep);
            // The look comes round onto them on the way, so the fist goes where the eyes are.
            Transform eyes = PlayerView.EyesOf(player);
            if (eyes != null)
                CombatHeadMotion.For(player)?.Turn(PlayerPerfectDefense.Bearing(eyes, target) * (1f - Mathf.Exp(-LookRate * Mathf.Min(.05f, Time.unscaledDeltaTime))));
            return;
        }
        dashTarget = null;
        player.Abilities?.Dodge.HoldOff(0f);
        melee.StrikeCombo(dashStrike, target);
    }

    private void OnDisable()
    {
        CancelDash();
        parried = null; riposteUntil = linkUntil = chaseUntil = float.NegativeInfinity;
    }

    private void OnDestroy()
    {
        if (thrower != null) thrower.Thrown -= OnThrown;
    }
}
