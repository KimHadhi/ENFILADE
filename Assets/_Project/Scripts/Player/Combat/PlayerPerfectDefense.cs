using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Confirmed defenses drive sound and a one-use, real-time counter opportunity. The counter dashes in right up against
/// the attacker and then punches for real: it lands only if the body is within reach of the eyes with nothing solid in
/// between when the dash ends. Every attacker whose blow the open window let pass can be countered, as far as the dash
/// closes in before its timeout: the counter goes to the one the eyes are most on, and another enemy pressed into the
/// fist's way takes the punch instead. A press that finds nobody to counter closes the window, so time doesn't stay slowed
/// over an ordinary punch. The dash is committed: no dodge starts until it lands, so none can slow time again mid-dash.
/// An attacker in front gets a straight punch; after a Q/E slip the look has already turned onto the attacker (MicroSlip)
/// and stays on the target through the dash. An attacker to the side or behind, as when a dodge went right through it,
/// gets a back elbow: a look over that shoulder, the aim left where it was.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.PerfectDefense)]
public sealed class PlayerPerfectDefense : MonoBehaviour
{
    // Dodges wait this long (real s) past the last frame of the dash; the punch frees them at once.
    private const float DodgeHold = .1f;
    [SerializeField, Tooltip("Parries open the counter window. Off until the counter skill exists; perfect evades always open it.")]
    private bool parryOpensCounter;
    [SerializeField, Range(.01f, 1f), Tooltip("Time scale at the moment a parry breaks the attacker's posture.")]
    private float postureBreakTimeScale = .3f;
    [SerializeField, Min(0f), Tooltip("That slow motion, real seconds. Starting the execution ends it at once. 0 turns it off.")]
    private float postureBreakSlowSeconds = .35f;
    [SerializeField, Min(0f), Tooltip("Real seconds of invulnerability after a counter. The open counter window is invulnerable too; a dodge is still judged first.")]
    private float counterGuardSeconds = .5f;
    [Header("Counter strike")]
    [SerializeField, Min(1f), Tooltip("Speed of the dash into the attacker (m/s).")]
    private float counterDashSpeed = 14f;
    [SerializeField, Min(.05f), Tooltip("The punch goes after this long (s) even if the dash hasn't closed in, and may then miss. With the speed and the stop distance it sets how far away an attacker can still be countered.")]
    private float counterDashTimeout = .35f;
    [SerializeField, Min(.1f), Tooltip("The dash stops this far from the attacker's body (m, from the middle of the player's capsule).")]
    private float counterStopDistance = .5f;
    [SerializeField, Min(.3f), Tooltip("The punch lands on a body this close to the eyes (m) with nothing solid between.")]
    private float counterStrikeReach = 1.4f;
    [SerializeField, Min(0f), Tooltip("Choosing whom to counter among the attackers let pass: a metre further away weighs as much as this many degrees off where the eyes look.")]
    private float counterPickDegreesPerMetre = 10f;
    [Header("Counter by direction")]
    [SerializeField, Range(0f, 180f), Tooltip("An attacker within this many degrees of where the eyes look gets the straight punch; one further round, to the side or behind, gets the back elbow.")]
    private float elbowFromAngle = 60f;
    [SerializeField, Range(0f, 90f), Tooltip("The back elbow's look over the shoulder on the attacker's side (degrees). The aim stays where it was.")]
    private float elbowGlance = 40f;
    [SerializeField, Tooltip("That look: out;held;back, real seconds.")]
    private Vector3 elbowGlanceTiming = new Vector3(.1f, .05f, .15f);
    [SerializeField, Range(0f, 1f), Tooltip("The elbow lands out of sight, so the screen's edge on its side lights this much. 0 turns it off.")]
    private float elbowGlint = .4f;
    [SerializeField, Min(.01f), Tooltip("How long that light lasts, real seconds.")]
    private float elbowGlintSeconds = .14f;
    [SerializeField, Tooltip("Colour of that light.")]
    private Color elbowGlintColor = new Color(1f, .93f, .78f);
    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private CombatTimeEffects timeEffects;
    private MeleeCombatTarget striking;
    // Everyone whose blow the open window let pass, in order; the first one opened it.
    private readonly List<MeleeCombatTarget> evaded = new List<MeleeCombatTarget>(4);
    private float remaining, guard, strikeTime, lastGap, lastStep;
    private int rewardedDodge = -1, elbowSide;
    private bool elbow;
    private readonly RaycastHit[] sightHits = new RaycastHit[16];
    public bool CounterAvailable => remaining > 0f;
    /// <summary>Dashing in to land the counter.</summary>
    public bool IsCountering => striking != null;
    /// <summary>Waiting to counter, dashing in or just countered: hits pass through.</summary>
    public bool Shields => CounterAvailable || IsCountering || guard > 0f;
    public float Remaining => remaining;
    public void Initialize(PlayerMovement owner)
    {
        if (receiver != null) receiver.HitResolved -= OnResolved;
        player = owner; receiver = owner.DamageReceiver;
        receiver.HitResolved += OnResolved;
        timeEffects = GetComponent<CombatTimeEffects>() ?? gameObject.AddComponent<CombatTimeEffects>();
        // The evade and block cues play on this body: their voice is made now, not on the first perfect evade.
        CombatCueAudio.Prepare(gameObject);
        if (Application.isPlaying && owner.IsLocal && GetComponent<PerfectDefenseHUD>() == null) gameObject.AddComponent<PerfectDefenseHUD>();
        // The slip's numbers, the head moves and the edge light are made at spawn, not in the first fight.
        if (Application.isPlaying && owner.IsLocal) { MicroSlip.For(owner); CombatHeadMotion.For(owner); CombatEdgeGlint.For(owner); DodgeFlash.For(owner); }
    }
    private void OnEnable() { var owner = GetComponent<PlayerMovement>(); if (owner?.DamageReceiver != null) Initialize(owner); }
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        if (result == DamageResult.Blocked)
        {
            float variance = CombatFeedbackSettings.InstanceOrDefaults.hitPitchVariance;
            CombatCueAudio.Play(gameObject, CombatCue.Block, 1f + Random.Range(-variance, variance));
            if (hit.Source != null && hit.Source.TryGetComponent(out EnemyNetwork network)) network.ShareBlock();
            return;
        }
        if (result == DamageResult.Parried)
        {
            var target = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
            // A parry that breaks posture reads as the counter: the slow motion leads into the execution. It runs on from
            // the end of the break's hit stop, which holds time harder at first (the slowest request wins).
            if (target != null && target.CanExecute)
            {
                var feedback = GetComponent<ParryFeedback>();
                float lead = feedback != null ? feedback.HitStopFor(true) : 0f;
                if (postureBreakSlowSeconds > 0f) timeEffects.RequestSlowdown(this, postureBreakTimeScale, lead + postureBreakSlowSeconds, target);
            }
            // A Knight's parry opens it as a perfect dodge does (the user's memo, 퍼펙트 패리; PlayerClass).
            else if ((parryOpensCounter || PlayerClass.ParryCounters(player)) && hit.IsComboFinisher) OpenCounter(hit, false);
        }
        else if (result == DamageResult.Evaded)
        {
            // One dodge that lets two blows pass is one reward, but either attacker can be countered.
            var dodge = player.Abilities.Dodge;
            if (rewardedDodge != dodge.Activation)
            {
                rewardedDodge = dodge.Activation;
                CombatCueAudio.Play(gameObject, CombatCue.PerfectEvade);
                OpenCounter(hit, dodge.Kind == DodgeKind.Micro);
            }
            else if (CounterAvailable) Remember(AttackerOf(hit));
        }
        else if (result == DamageResult.Damaged) Cancel();
    }
    // slipped: a Q/E micro dodge let the blow pass, so the look turns onto its attacker as the head slips (MicroSlip).
    private void OpenCounter(DamageHit hit, bool slipped)
    {
        // A new defense while the window is still open adds its attacker to those already let pass.
        if (!CounterAvailable) evaded.Clear();
        var attacker = AttackerOf(hit);
        Remember(attacker);
        var settings = CombatFeedbackSettings.Instance;
        remaining = settings != null ? settings.counterWindowSeconds : 1f;
        timeEffects.RequestSlowdown(this, settings != null ? settings.perfectTimeScale : .08f, remaining, attacker);
        if (slipped) MicroSlip.For(player)?.LockOn(attacker);
    }
    private static MeleeCombatTarget AttackerOf(DamageHit hit) => hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
    private void Remember(MeleeCombatTarget target) { if (target != null && !evaded.Contains(target)) evaded.Add(target); }
    // Someone let pass is still there to counter. A blow from something that can't be countered keeps the window as before.
    private bool AnyLeft()
    {
        if (evaded.Count == 0) return true;
        foreach (var candidate in evaded) if (candidate != null && candidate.IsAvailable) return true;
        return false;
    }
    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        // The dash runs through whatever locks the hands meanwhile; only death, an execution or a lost target stop it.
        if (striking != null) { TickStrike(player != null ? player.DeltaTime : Time.deltaTime); Tick(Time.unscaledDeltaTime); return; }
        if (player == null || !player.InputAvailable || player.CombatInputLocked || player.IsParkour || player.IsDiving)
        { Cancel(); guard = Mathf.Max(0f, guard - Time.unscaledDeltaTime); return; }
        var input = GetComponent<PlayerWeaponInput>();
        // A press that finds nobody to counter spends the window: time comes back at once, before the fists read the same
        // click, so it doesn't turn into an ordinary punch thrown in slow motion.
        if (input != null && input.TryReadMeleeInput(out var frame) && frame.AttackPressed && !TryCounter() && CounterAvailable) Cancel();
        Tick(Time.unscaledDeltaTime);
    }
    public void Tick(float realDeltaTime)
    {
        guard = Mathf.Max(0f, guard - Mathf.Max(0f, realDeltaTime));
        if (!CounterAvailable) return;
        if (player == null || !player.DamageReceiver.IsAlive || player.Execution?.IsExecuting == true || !AnyLeft()) { Cancel(); return; }
        remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, realDeltaTime));
        if (remaining <= 0f) Cancel();
    }
    public bool TryCounter()
    {
        if (Time.timeScale <= 0f || !CounterAvailable || player.CombatInputLocked
            || player.IsParkour || player.IsDiving || !player.DamageReceiver.IsAlive) return false;
        var target = Pick();
        if (target == null) return false;
        // Which move: the straight punch for an attacker in front, the back elbow otherwise.
        Transform aim = Eyes();
        float bearing = Bearing(aim, target);
        bool backElbow = Mathf.Abs(bearing) > elbowFromAngle;
        // A look a slip locked onto its attacker stays on the target through the dash.
        player.TryGetComponent(out MicroSlip slip);
        bool keepLook = !backElbow && slip != null && slip.IsLocked;
        Cancel(); // Spend once before dispatching events or damage.
        elbow = backElbow; elbowSide = bearing >= 0f ? 1 : -1;
        if (keepLook) slip.KeepOn(target, counterDashTimeout + .25f);
        if (backElbow && elbowGlance > 0f)
            CombatHeadMotion.For(this)?.Glance(elbowSide * elbowGlance, elbowGlanceTiming.x, elbowGlanceTiming.y, elbowGlanceTiming.z);
        player.MeleeCombat?.CancelAction(); player.Abilities.Combat.CancelShove();
        player.Abilities.Dodge.CancelWindow();
        if (player.CurrentState == PlayerMovement.MovementState.StepDodge) player.Abilities.ReturnToLocomotion();
        player.DodgePassThrough?.Finish();
        GetComponent<WeaponController>()?.Runtime?.Interrupt();
        GetComponent<PlayerWeaponInput>()?.RequireTriggerRelease();
        // In close first, then the punch (TickStrike). No dodge starts meanwhile.
        striking = target; strikeTime = lastGap = lastStep = 0f;
        player.HorizontalVelocity = Vector3.zero;
        player.Abilities.Dodge.HoldOff(DodgeHold);
        return true;
    }
    // Of the attackers let pass whom the dash can still reach, the one the eyes are most on; a nearer one wins a close call.
    private MeleeCombatTarget Pick()
    {
        // As far as the dash closes in before its timeout, so an attacker left behind by a full step dodge is still in reach.
        float reach = counterStopDistance + counterDashSpeed * counterDashTimeout;
        Transform aim = Eyes();
        Vector3 forward = Vector3.ProjectOnPlane(aim.forward, Vector3.up);
        MeleeCombatTarget best = null;
        float lowest = float.PositiveInfinity;
        foreach (var candidate in evaded)
        {
            if (candidate == null || !candidate.IsAvailable || candidate.GetComponent<IDamageReceiver>() == null) continue;
            var body = candidate.GetComponentInChildren<Collider>();
            if (body == null || BodyGap.Across(player, body, out _) > reach) continue;
            Vector3 toward = body.bounds.center - aim.position;
            Vector3 flat = Vector3.ProjectOnPlane(toward, Vector3.up);
            float off = flat.sqrMagnitude > .0001f && forward.sqrMagnitude > .0001f ? Vector3.Angle(forward, flat) : 0f;
            float weight = off + toward.magnitude * counterPickDegreesPerMetre;
            if (weight < lowest) { lowest = weight; best = candidate; }
        }
        return best;
    }
    private void TickStrike(float dt)
    {
        var target = striking;
        if (target == null || !target.IsAvailable || player == null || !player.DamageReceiver.IsAlive || player.Execution?.IsExecuting == true)
        { striking = null; EndDash(); return; }
        player.Abilities.Dodge.HoldOff(DodgeHold);
        if (dt <= 0f) return;
        strikeTime += dt;
        float gap = BodyGap.Across(player, target.GetComponentInChildren<Collider>(), out Vector3 toward);
        // Held up on the way (another attacker pressed in between): punch now rather than push at it until the timeout.
        bool stalled = lastStep > .01f && lastGap - gap < lastStep * .25f;
        if (gap > counterStopDistance && strikeTime < counterDashTimeout && !stalled)
        {
            lastGap = gap; lastStep = Mathf.Min(gap - counterStopDistance, counterDashSpeed * dt);
            player.Motor.AddDisplacement(toward * lastStep);
            return;
        }
        striking = null;
        Strike(target);
    }
    /// <summary>Where the attacker stands off the eyes' facing, degrees across the ground (+ right).</summary>
    internal static float Bearing(Transform aim, MeleeCombatTarget target)
    {
        var body = target.GetComponentInChildren<Collider>();
        Vector3 flat = Vector3.ProjectOnPlane((body != null ? body.bounds.center : target.transform.position) - aim.position, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(aim.forward, Vector3.up);
        return flat.sqrMagnitude > .0001f && forward.sqrMagnitude > .0001f ? Vector3.SignedAngle(forward, flat, Vector3.up) : 0f;
    }
    // A real punch from the eyes: it lands on a body within reach with nothing solid in between. An enemy in the fist's way
    // takes it instead; a wall stops it.
    private void Strike(MeleeCombatTarget target)
    {
        EndDash();
        var settings = CombatFeedbackSettings.Instance;
        Transform aim = Eyes();
        var body = target.GetComponentInChildren<Collider>();
        var result = DamageResult.Ignored;
        if (body != null)
        {
            Vector3 point = body.ClosestPoint(aim.position);
            if (FirstInWay(aim.position, point, target, out RaycastHit block))
            {
                var other = block.collider.GetComponentInParent<MeleeCombatTarget>();
                target = other != null && other.IsAvailable ? other : null;
                point = block.point;
            }
            var victim = target != null ? target.GetComponent<IDamageReceiver>() : null;
            Vector3 delta = point - aim.position;
            if (victim != null && delta.magnitude <= counterStrikeReach)
            {
                // A Knight's counter, after a dodge or a parry, hits harder (the memo's 흘리기; PlayerClass).
                float knight = PlayerClass.CounterFactor(player);
                float posture = (settings != null ? settings.counterPostureDamage : 60f) * knight;
                result = victim.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), (settings != null ? settings.counterDamage : 80f) * knight,
                    DamageKind.Attack, point, delta.sqrMagnitude > .0001f ? delta.normalized : aim.forward, gameObject, false, posture,
                    isCounter: true));
                if (result == DamageResult.Damaged) target.ApplyPostureDamage(posture, .65f);
            }
        }
        // The guard after a counter starts at the punch.
        guard = counterGuardSeconds;
        bool landed = result == DamageResult.Damaged;
        if (!elbow) { player.MeleeCombat?.ShowCounterPunch(landed); return; }
        player.MeleeCombat?.ShowCounterElbow(landed, elbowSide);
        // Out of sight behind: the edge on its side lights as it lands.
        if (landed && elbowGlint > 0f) CombatEdgeGlint.For(this)?.Show(elbowSide, elbowGlintColor, elbowGlint, elbowGlintSeconds);
    }
    // The nearest thing between the eyes and the point that is neither this body nor the target.
    private bool FirstInWay(Vector3 from, Vector3 to, MeleeCombatTarget target, out RaycastHit first)
    {
        first = default;
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .0001f) return false;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].collider.transform;
            if (hit.IsChildOf(transform) || hit.IsChildOf(target.transform) || sightHits[i].distance >= nearest) continue;
            nearest = sightHits[i].distance; first = sightHits[i];
        }
        return nearest < float.PositiveInfinity;
    }
    private Transform Eyes() => GetComponentInChildren<Camera>() != null ? PlayerView.EyesOf(this) : transform;
    // The dash is over, landed, lost or called off: dodges may start again and a look kept on the target lets go.
    private void EndDash()
    {
        if (player == null) return;
        player.Abilities?.Dodge.HoldOff(0f);
        if (player.TryGetComponent(out MicroSlip slip)) slip.LetGo();
    }
    /// <summary>Closes the counter window, and a counter's dash with it.</summary>
    public void Cancel()
    {
        remaining = 0f; striking = null; evaded.Clear();
        timeEffects?.Release(this);
        EndDash();
    }
    private void OnDisable() { Cancel(); if (receiver != null) receiver.HitResolved -= OnResolved; }
}
