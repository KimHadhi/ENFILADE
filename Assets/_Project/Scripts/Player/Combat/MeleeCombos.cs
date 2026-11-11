using System;
using UnityEngine;

/// <summary>Which combo move a strike is (MeleeCombos); None is an ordinary punch.</summary>
public enum ComboStrike : byte { None, Riposte }

/// <summary>
/// The fists' combos: moves that go on a press made in time, judged on this player's own PC. All of them are generous
/// and none costs anything when missed (the user's rule: landing one adds, missing one simply doesn't happen).
/// - Riposte (user, 2026-11-11: "패링 후 바로 공격할 수 있게"): for Riposte.window real seconds after a parry, an attack
///   press strikes at once, the guard held or not, as Sekiro's deflect-then-strike and For Honor's parry punish: the
///   parry's hit stop lets go, the feet are freed, the body closes the gap the parry pushed open and the free hand drives
///   a straight into the parried attacker, harder than a full charge. When the parry opened a Knight's perfect-defense
///   counter, that counter takes the click first (PlayerPerfectDefense reads it earlier in the frame), so the two never
///   both go.
/// The dash in is committed: no dodge starts until the strike, a guard press calls it off. Numbers: Tables/Combos.csv.
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

    [SerializeField] private RiposteNumbers riposte = new RiposteNumbers();

    // Dodges wait this long (real s) past the last frame of a dash in; the strike frees them at once.
    private const float DodgeHold = .1f;
    // How fast the look turns onto the one dashed at (1/s: the share of the turn left taken each second).
    private const float LookRate = 25f;

    private PlayerMovement player;
    private PlayerMeleeCombat melee;
    private MeleeCombatTarget parried, dashTarget;
    private ComboStrike dashStrike;
    private float riposteUntil = float.NegativeInfinity, dashTime, dashTimeout, dashSpeed, dashStop, lastGap, lastStep;

    public RiposteNumbers Riposte => riposte;
    /// <summary>Closing in for a combo strike: the fists take no other press meanwhile.</summary>
    public bool IsDashing => dashTarget != null;
    /// <summary>A parry's riposte can still go (real time).</summary>
    public bool RiposteOpen => parried != null && Time.unscaledTime < riposteUntil;

    /// <summary>The player's combos, made and bound to the table on first use.</summary>
    public static MeleeCombos For(PlayerMovement player)
    {
        if (player == null || !Application.isPlaying) return null;
        if (player.TryGetComponent(out MeleeCombos combos)) return combos;
        combos = player.gameObject.AddComponent<MeleeCombos>();
        GameTables.BindNested(TableName, "Riposte", combos, combos.riposte);
        return combos;
    }

    private void Awake() => player = GetComponent<PlayerMovement>();

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
        return false;
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
        BeginDash(target, ComboStrike.Riposte, riposte.dashSpeed, riposte.dashTimeout, riposte.stopDistance);
        return true;
    }

    /// <summary>Damage, posture and reach of a combo strike against a full-charged punch's (PlayerMeleeCombat).</summary>
    internal void Numbers(ComboStrike strike, out float damage, out float posture, out float reach)
    {
        damage = posture = 1f; reach = 0f;
        if (strike == ComboStrike.Riposte) { damage = riposte.damage; posture = riposte.posture; reach = riposte.reach; }
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
        if (dashTarget != null && Time.timeScale > 0f) TickDash(player.DeltaTime);
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
        parried = null; riposteUntil = float.NegativeInfinity;
    }
}
