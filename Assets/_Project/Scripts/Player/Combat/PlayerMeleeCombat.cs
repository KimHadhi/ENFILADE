using System;
using UnityEngine;

/// <summary>Fist prototype. Owns charge/release, frontal defense and single-target guard break.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.MeleeCombat)]
public sealed class PlayerMeleeCombat : MonoBehaviour
{
    [Header("Fists")]
    [SerializeField, Min(0.1f)] private float reach = 1.6f;
    [SerializeField, Min(0.1f)] private float fullChargeTime = 0.9f;
    [SerializeField] private Vector2 damage = new Vector2(10f, 28f);
    [SerializeField] private Vector2 postureDamage = new Vector2(10f, 30f);
    [SerializeField, Min(0f)] private float attackCooldown = 0.2f;
    [Tooltip("적이나 벽에 적중하면 후딜 없이 다음 입력으로 연타. 허공에 빗나갔을 때만 Attack Cooldown 적용.")]
    [SerializeField, Min(.02f)] private float attackActiveTime = .18f;
    [Tooltip("한 번의 주먹 공격으로 맞힐 수 있는 서로 다른 대상 수.")]
    [SerializeField, Min(1)] private int attackMaxTargets = 2;
    [SerializeField, Min(0f)] private float lungeDistance = 0.12f;
    [SerializeField, Range(0f, 1f), Tooltip("Punches charged at least this far land with the heavy hit sound.")]
    private float heavyPunchCharge = .6f;
    [Header("Guard / Parry")]
    [Tooltip("Parry window per press, game seconds (Sekiro's deflect: 12 frames, then shorter each time the guard is let go and pressed again soon, down to 4 frames and then none). 0 means that press only guards.")]
    [SerializeField] private float[] parryWindowDurations = { .2f, .133f, .067f, 0f };
    [SerializeField, Min(0f), Tooltip("Guard left alone this long (s) gives the full window back, as a parry or a blocked hit does (Sekiro: 30 frames).")]
    private float spamResetDelay = .5f;
    [SerializeField, Min(0f)] private float guardInputBuffer = .12f;
    [SerializeField, Range(30f, 180f)] private float guardAngle = 150f;
    [SerializeField, Range(0f, 1f), Tooltip("Holding the guard up, the legs go at most this share of the walk speed, so guarding all the time costs something.")]
    private float guardMoveShare = .5f;
    [SerializeField, Min(0f), Tooltip("After a parry's hit stop the feet stay planted this much longer (real s). Punching, guarding and dodging still work.")]
    private float parryRootSeconds = .15f;
    [Header("V: Shove / Guard Break")]
    [SerializeField, Min(0.05f)] private float holdThreshold = 0.14f;
    [SerializeField, Min(0.1f)] private float guardBreakReach = 2.2f;
    [SerializeField, Min(1f)] private float guardBreakApproachSpeed = 12f;
    [SerializeField, Min(0.05f)] private float guardBreakApproachTimeout = 0.24f;
    [SerializeField, Min(0f)] private float guardBreakStrikeDelay = 0.04f;
    [SerializeField, Min(0f)] private float guardBreakRecovery = 0.16f;
    [SerializeField, Min(0f)] private float guardBreakCooldown = 0.35f;
    [SerializeField, Min(0f)] private float guardBreakStun = 1.4f;
    [SerializeField, Min(0f)] private float guardBreakPostureDamage = 55f;
    [SerializeField] private LayerMask hitLayers = ~0;
    private PlayerMovement player;
    private PlayerWeaponSlots slots;
    private PlayerWeaponInput input;
    private PlayerFistAudio fistAudio;
    private MeleeCombos combos;
    private Transform aim;
    private MeleeCombatTarget target;
    private Collider targetCollider;
    // strikeHold: a combo strike's active time, over which a guard still held doesn't come back up and cancel it.
    // shoveWait: V's own wait after a shove or a guard break. It no longer holds the punches (League's auto-attack reset,
    // user 2026-11-11): a skill hands the punch straight back, as the spinning backfist (MeleeCombos link).
    private float chargeTime, shoveTime, cooldown, shoveRest, shoveWait, grabTime, grabRecoveryRemaining, lungeRemaining, punchCharge, strikeHold;
    private float parryRemaining, guardBufferRemaining, guardIdleTime, rootUntil = float.NegativeInfinity;
    private int spamCount;
    private ParryFeedback parryFeedback;
    private bool charging, shovePending, grabConnected, guardAttemptHeld, defenseConnected, pendingWhiff, punchLanded;
    private Vector3 lungeDirection;
    private readonly MeleeAttackSweep punchSweep = new MeleeAttackSweep();
    private readonly ShoveSweep.Attack shoveSweep = new ShoveSweep.Attack();
    public bool IsGuarding { get; private set; }
    public bool IsParryActive => parryRemaining > 0f;
    public int ParrySpamCount => spamCount;
    public float ParryRemaining => parryRemaining;
    public bool IsCharging => charging;
    public float ChargeRatio => charging ? Mathf.Clamp01(chargeTime / fullChargeTime) : 0f;
    public bool IsGuardBreaking => target != null;
    // Pinned by a runner the fists do nothing, unless a melee perk lets them fight it lying there (PlayerHold.FightsPinned).
    private bool MovementBlocksAttack => player.IsParkour || player.IsDiving || player.IsSliding || player.IsDowned
        || player.CurrentState == PlayerMovement.MovementState.StepDodge
        || player.CurrentState == PlayerMovement.MovementState.DashPush
        || (player.IsLandingRecovery && !player.FightsPinned) || (player.IsPinned && !player.FightsPinned);
    public bool IsActive => isActiveAndEnabled && slots != null && slots.UsesFists && player != null && player.IsInitialized && player.DamageReceiver.IsAlive;
    /// <summary>The feet are planted after a parry (real time): the legs give no speed, the hands are free.</summary>
    public bool IsRooted => IsActive && Time.unscaledTime < rootUntil;
    public event Action<float> Punched;
    /// <summary>The combo move of the punch Punched is announcing, read during that event (FistsView); None otherwise.</summary>
    public ComboStrike LastStrike { get; private set; }
    /// <summary>This player's combos (the beat's step, for FistsView); null on a copy made before play.</summary>
    internal MeleeCombos Combos => combos;

    /// <summary>
    /// The speed the legs may go with the hands doing what they are doing (LocomotionState asks): nothing while rooted
    /// after a parry, a slow walk with the guard held up (as Sekiro's guard walk), otherwise speed as it is.
    /// </summary>
    public float LimitMoveSpeed(float speed)
    {
        if (!IsActive) return speed;
        if (IsRooted) return 0f;
        return IsGuarding ? Mathf.Min(speed, player.WalkSpeed * guardMoveShare) : speed;
    }

    private void OnEnable()
    {
        player = GetComponentInParent<PlayerMovement>();
        slots = GetComponent<PlayerWeaponSlots>(); input = GetComponent<PlayerWeaponInput>();
        var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        // Punches start from the eyes, which stay with the body in the F5 view.
        aim = camera != null ? PlayerView.EyesOf(player) : transform;
        if (player != null) player.MeleeCombat = this;
        if (Application.isPlaying && camera != null && GetComponent<FistsView>() == null) gameObject.AddComponent<FistsView>();
        fistAudio = GetComponent<PlayerFistAudio>();
        if (fistAudio == null && Application.isPlaying) fistAudio = gameObject.AddComponent<PlayerFistAudio>();
        if (fistAudio != null) fistAudio.Prepare();
        combos = MeleeCombos.For(player);
        CancelAction();
    }
    /// <summary>On someone else's copy: their guard went up or down.</summary>
    internal void ShowGuard(bool guarding) => IsGuarding = guarding;
    /// <summary>On someone else's copy: their punch left. Their PC judged what it hit.</summary>
    internal void ShowPunch(float charge) => Punched?.Invoke(charge);
    private void Update()
    {
        if (player != null && player.IsNetworkDriven) return;
        if (!IsActive || !player.InputAvailable) { CancelAction(); TickGuardInput(player.DeltaTime, default); return; }
        if (IsGuardBreaking)
        {
            if (input != null && input.TryReadGuardBuffer(out var guardFrame)) ProcessFrame(player.DeltaTime, guardFrame);
            else CancelAction();
            return;
        }
        if (input == null || !input.TryReadMeleeInput(out var frame)) { CancelAction(); return; }
        ProcessFrame(player.DeltaTime, frame);
    }
    private void LateUpdate()
    {
        // PlayerLook updates after input processing. Include this frame's final camera rotation.
        if (Time.timeScale <= 0f || (player != null && player.IsNetworkDriven)) return;
        if (!IsActive || !player.InputAvailable || player.Execution?.IsExecuting == true) { CancelAction(); return; }
        if (IsGuardBreaking) return;
        if (MovementBlocksAttack) { SuspendActionsForMovement(); return; }
        punchSweep.Tick(0f); shoveSweep.Tick(0f);
    }
    /// <summary>A counter hands the shove straight back (ShoveCounter): the fists can shove or punch again at once.</summary>
    internal void RefreshShove() { cooldown = shoveRest = shoveWait = 0f; }
    public void ProcessFrame(float dt, MeleeInputFrame frame)
    {
        if (dt <= 0f) return;
        if (!IsActive || player.Execution?.IsExecuting == true) { CancelAction(); return; }
        cooldown = Mathf.Max(0f, cooldown - dt);
        shoveRest = Mathf.Max(0f, shoveRest - dt);
        shoveWait = Mathf.Max(0f, shoveWait - dt);
        strikeHold = Mathf.Max(0f, strikeHold - dt);
        TickGuardInput(dt, frame);
        if (IsGuardBreaking)
        {
            float buffered = guardBufferRemaining;
            TickGuardBreak(dt);
            guardBufferRemaining = buffered;
            return;
        }
        if (MovementBlocksAttack)
        { SuspendActionsForMovement(); return; }
        // A combo's dash in takes no other press; a guard press calls it off and guards (MeleeCombos).
        if (combos != null && combos.IsDashing)
        {
            if (!frame.GuardPressed) return;
            combos.CancelDash();
        }
        // A combo strike goes on the press itself, ahead of the guard and of any wait (MeleeCombos): the riposte after a
        // parry, the guard still held or not.
        else if (frame.AttackPressed && combos != null && combos.TryTake()) return;
        // Guard has priority over charge, punch/shove active frames and their recovery.
        if (guardBufferRemaining > 0f) BeginGuard(frame.GuardHeld);
        else if (frame.GuardHeld && !IsGuarding && strikeHold <= 0f)
        {
            // A long hold through an uninterruptible action resumes block, never a fresh parry.
            CancelOffense(); IsGuarding = true; guardAttemptHeld = true; defenseConnected = false;
        }
        if (IsGuarding) return;
        punchSweep.Tick(dt); shoveSweep.Tick(dt);
        if (!player.IsGrounded) lungeRemaining = 0f;
        if (lungeRemaining > 0f)
        {
            float slice = Mathf.Min(dt, lungeRemaining); lungeRemaining -= slice;
            player.Motor.AddDisplacement(lungeDirection * (lungeDistance * slice / 0.1f));
        }
        if (frame.ShovePressed && cooldown <= 0f && shoveWait <= 0f)
        { punchSweep.Cancel(); shoveSweep.Cancel(); shovePending = true; shoveTime = 0f; charging = false; IsGuarding = false; parryRemaining = 0f; }
        if (shovePending)
        {
            shoveTime += dt;
            if (frame.ShoveHeld && shoveTime >= holdThreshold)
            {
                shovePending = false; shoveWait = guardBreakCooldown;
                TryStartGuardBreak(); // Holding never also emits the tap shove on release.
            }
            else if (frame.ShoveReleased)
            {
                // Fatigue only slows the next shove; punches keep their own cooldown.
                shovePending = false;
                if (shoveRest <= 0f)
                {
                    // The wait is set before the shove goes out: a counter on its first contact clears it again (RefreshShove).
                    shoveWait = 0.55f;
                    shoveRest = player.Abilities.Combat.Fatigue.Spend(shoveWait, player.AbilitySettings);
                    Shove();
                    combos?.OpenLink();
                }
            }
            else if (!frame.ShoveHeld) shovePending = false;
            return;
        }
        // The shove still under way finishes: a punch thrown right after it mustn't rob it of its contact.
        if (frame.AttackPressed && cooldown <= 0f)
        { punchSweep.Cancel(); parryRemaining = 0f; charging = true; chargeTime = 0f; }
        if (!charging) return;
        if (frame.AttackHeld) chargeTime = Mathf.Min(fullChargeTime, chargeTime + dt);
        if (frame.AttackReleased)
        {
            float amount = Mathf.Clamp01(chargeTime / fullChargeTime);
            charging = false; cooldown = attackCooldown;
            Punch(amount);
        }
        else if (!frame.AttackHeld) charging = false;
    }
    private void TickGuardInput(float dt, MeleeInputFrame frame)
    {
        parryRemaining = Mathf.Max(0f, parryRemaining - dt);
        guardBufferRemaining = Mathf.Max(0f, guardBufferRemaining - dt);
        if (!frame.GuardHeld)
        {
            if (guardAttemptHeld)
            {
                if (!defenseConnected) pendingWhiff = true;
                guardAttemptHeld = false; guardIdleTime = 0f;
            }
            else guardIdleTime += dt;
            IsGuarding = false;
            if (guardIdleTime >= Mathf.Max(0f, spamResetDelay)) ResetParrySpam();
        }
        else guardIdleTime = 0f;
        if (frame.GuardPressed) guardBufferRemaining = Mathf.Max(.001f, guardInputBuffer);
    }
    private void BeginGuard(bool held)
    {
        int last = parryWindowDurations != null ? parryWindowDurations.Length - 1 : -1;
        spamCount = Mathf.Clamp(spamCount + (pendingWhiff ? 1 : 0), 0, Mathf.Max(0, last));
        // A Knight's windows are longer, the shrinking ones as much (PlayerClass).
        float duration = (last >= 0 ? parryWindowDurations[spamCount] : .2f) * PlayerClass.ParryWindowFactor(player);
        // A window of 0 is Sekiro's mashing floor: that press only guards.
        parryRemaining = float.IsNaN(duration) || float.IsInfinity(duration) ? .2f : Mathf.Max(0f, duration);
        pendingWhiff = !held; defenseConnected = false; guardAttemptHeld = held;
        IsGuarding = held; guardIdleTime = guardBufferRemaining = 0f;
        CancelOffense(); cooldown = 0f;
    }
    private void CancelOffense()
    {
        punchSweep.Cancel(); shoveSweep.Cancel();
        charging = shovePending = false; chargeTime = shoveTime = lungeRemaining = 0f;
    }
    private void SuspendActionsForMovement()
    {
        float buffered = guardBufferRemaining;
        CancelAction();
        guardBufferRemaining = buffered;
    }
    private void ResetParrySpam() { spamCount = 0; pendingWhiff = false; }
    public void NotifyDamaged() { ResetParrySpam(); defenseConnected = true; }
    /// <summary>A riposte lets the feet go at once (MeleeCombos): the parry's planted feet would hold its step in.</summary>
    internal void FreeFeet() => rootUntil = float.NegativeInfinity;
    /// <summary>
    /// A combo strike goes now (MeleeCombos): a punch at full charge times the move's own numbers, on the press, the guard
    /// dropped for it. Aimed at someone, it lands on them when they are within the move's reach of the eyes with nothing
    /// solid between; otherwise, or when that fails, it sweeps along the aim as any punch.
    /// </summary>
    internal void StrikeCombo(ComboStrike strike, MeleeCombatTarget at = null)
    {
        if (!IsActive || combos == null) return;
        punchSweep.Cancel();
        charging = shovePending = false; chargeTime = shoveTime = 0f;
        IsGuarding = guardAttemptHeld = false; parryRemaining = guardBufferRemaining = 0f;
        cooldown = 0f; strikeHold = attackActiveTime + .05f;
        combos.Numbers(strike, out float damageFactor, out float postureFactor, out float strikeReach, out float radius, out int targets);
        Punch(1f, strike, damageFactor, postureFactor, strikeReach, at, radius, targets);
    }
    // strikeReach: an aimed strike's reach from the eyes, or how much further than a punch a sweep reaches. radius and
    // targets: a combo's wider sweep (0: a punch's own).
    private void Punch(float charge, ComboStrike strike = ComboStrike.None, float damageFactor = 1f, float postureFactor = 1f,
        float strikeReach = 0f, MeleeCombatTarget at = null, float radius = 0f, int targets = 0)
    {
        lungeDirection = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        lungeRemaining = player.IsGrounded ? 0.1f : 0f;
        // The beat counts from the punch leaving, before the sweep can land on its first sample (MeleeCombos).
        combos?.OnPunchThrown(strike);
        // Swing first: the sweep can land on its first sample inside Begin.
        punchLanded = false; punchCharge = charge; fistAudio?.PlaySwing();
        // A Knight's fists hit harder, flesh and posture (PlayerClass).
        float fist = PlayerClass.FistFactor(player);
        float dealt = Mathf.Lerp(damage.x, damage.y, charge) * fist * damageFactor;
        float posture = Mathf.Lerp(postureDamage.x, postureDamage.y, charge) * fist * postureFactor;
        if (at == null || !StrikeTarget(at, dealt, posture, strikeReach))
            punchSweep.Begin(player, aim, dealt, posture, reach + (at == null ? strikeReach : 0f), radius > 0f ? radius : .22f,
                attackActiveTime, targets > 0 ? targets : attackMaxTargets, hitLayers, OnPunchLanded, OnPunchWallHit);
        LastStrike = strike;
        Punched?.Invoke(charge);
        LastStrike = ComboStrike.None;
    }
    // The aimed combo strike: on that body when it is within reach of the eyes with nothing solid between, as the
    // perfect-defense counter's punch lands (PlayerPerfectDefense.Strike).
    private bool StrikeTarget(MeleeCombatTarget at, float dealt, float posture, float strikeReach)
    {
        var body = at.GetComponentInChildren<Collider>();
        var victim = at.GetComponent<IDamageReceiver>();
        if (body == null || victim == null || !at.IsAvailable) return false;
        Vector3 point = body.ClosestPoint(aim.position), delta = point - aim.position;
        if (delta.magnitude > Mathf.Max(reach, strikeReach) || !HasLineOfSight(body, point)) return false;
        // This PC's own player's fists carry their perk, as the sweep's do (PlayerPerks).
        if (player.IsLocal) dealt *= PlayerPerks.FistFactor;
        var result = victim.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), dealt, DamageKind.Attack, point,
            delta.sqrMagnitude > .0001f ? delta.normalized : aim.forward, player.gameObject, true, posture));
        if (result != DamageResult.Damaged) return false;
        at.ApplyPostureDamage(posture);
        OnPunchLanded();
        return true;
    }
    private void OnPunchLanded()
    {
        cooldown = 0f;
        // One hit cue per punch, even when it lands on several targets; and one beat waiting after it (MeleeCombos).
        if (!punchLanded)
        {
            if (punchCharge >= heavyPunchCharge) fistAudio?.PlayHeavyHit();
            else fistAudio?.PlayHit();
            combos?.OnPunchLanded();
        }
        punchLanded = true;
    }
    private void OnPunchWallHit()
    {
        cooldown = 0f;
        fistAudio?.PlayWallHit();
    }
    public void ShowCounterPunch(bool landed)
    {
        if (IsActive) { fistAudio?.PlaySwing(); if (landed) fistAudio?.PlayHeavyHit(); }
        Punched?.Invoke(1f);
    }
    /// <summary>The counter's back elbow, driven back on the attacker's side (+1 right): the fist pulls back past the shoulder.</summary>
    public event Action<int> Elbowed;
    public void ShowCounterElbow(bool landed, int side)
    {
        if (IsActive) { fistAudio?.PlaySwing(); if (landed) fistAudio?.PlayHeavyHit(); }
        Elbowed?.Invoke(side);
    }
    /// <summary>
    /// On someone else's copy: their counter's back elbow (+1 right). Its fist pulls back as on their own screen (FistsView),
    /// and the body's hands follow it. Their PC judged it, and its swing and hit sounds already came as fist sounds.
    /// </summary>
    internal void ShowElbow(int side) => Elbowed?.Invoke(side);
    private void Shove()
    {
        shoveSweep.Begin(player, aim, 3f, hitLayers, player.AbilitySettings.shoveActiveTime);
        // Standing still, it lunges at someone just out of reach, as the gun's shove does (ShoveMoves).
        ShoveMoves.For(player).Lunge(aim, player.AbilitySettings.shoveSweepReach, hitLayers);
    }
    public bool TryDefend(DamageHit hit, out DamageResult result)
    {
        result = DamageResult.Ignored;
        if (!IsActive || !player.InputAvailable || (!IsGuarding && !IsParryActive) || IsGuardBreaking
            || MovementBlocksAttack || hit.Kind != DamageKind.Attack || !hit.CanBeGuarded) return false;
        Vector3 incoming = hit.Source != null ? hit.Source.transform.position - player.transform.position : -hit.Direction;
        incoming = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 facing = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        if (incoming.sqrMagnitude < 0.01f || Vector3.Dot(incoming, facing) < Mathf.Cos(guardAngle * 0.5f * Mathf.Deg2Rad)) return false;
        bool parry = IsParryActive;
        result = parry ? DamageResult.Parried : DamageResult.Blocked;
        ResetParrySpam(); defenseConnected = true;
        if (parry)
        {
            parryRemaining = 0f;
            var attacker = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
            // A Knight's parry wears the attacker down harder: a pack zombie breaks on its second (PlayerClass).
            attacker?.ApplyPostureDamage(25f * PlayerClass.ParryPostureFactor(player), 0.8f);
            // The deflect plants the feet: the hit stop holds everything, then the legs stay put a moment longer.
            if (parryFeedback == null) parryFeedback = GetComponent<ParryFeedback>();
            float stop = parryFeedback != null ? parryFeedback.HitStopFor(attacker != null && attacker.IsStaggered) : 0f;
            rootUntil = Time.unscaledTime + stop + parryRootSeconds;
            player.HorizontalVelocity = Vector3.zero;
            // An attack press now goes straight back at them (MeleeCombos riposte).
            combos?.OpenRiposte(attacker);
        }
        return true;
    }
    private bool TryStartGuardBreak()
    {
        if (!player.IsGrounded || !FindContact(guardBreakReach, out var hit, out _)) return false;
        var candidate = hit.GetComponentInParent<MeleeCombatTarget>();
        if (candidate == null || !candidate.TryReserve(this)) return false;
        target = candidate; targetCollider = hit; grabTime = grabRecoveryRemaining = 0f; grabConnected = false;
        IsGuarding = charging = false; lungeRemaining = parryRemaining = 0f;
        player.HorizontalVelocity = Vector3.zero;
        player.Abilities.Dodge.CancelWindow(); player.Abilities.Lean.Reset();
        CombatCueAudio.Play(gameObject, CombatCue.Grab);
        return true;
    }
    private void TickGuardBreak(float dt)
    {
        if (dt <= 0f) return;
        if (target == null || !target.IsAvailable || targetCollider == null || !player.IsGrounded)
        { CancelAction(); return; }
        grabTime += dt;
        if (grabConnected)
        {
            grabRecoveryRemaining -= dt;
            // Landed, it hands the punch straight back as the spinning backfist; V waits on its own.
            if (grabRecoveryRemaining <= 0f) { CancelAction(); shoveWait = guardBreakCooldown; combos?.OpenLink(); }
            return;
        }
        Vector3 center = player.transform.position + Vector3.up * Mathf.Min(1f, player.ProneColliderHeight + 0.5f);
        Vector3 delta = Vector3.ProjectOnPlane(targetCollider.ClosestPoint(center) - center, Vector3.up);
        if (delta.magnitude > guardBreakReach + 0.3f) { CancelAction(); return; }
        // Recheck cover and identity throughout approach; never teleport through intervening geometry.
        if (!HasLineOfSight(targetCollider, targetCollider.ClosestPoint(aim.position))) { CancelAction(); return; }
        if (delta.magnitude <= 0.85f && grabTime >= guardBreakStrikeDelay)
        {
            grabConnected = true;
            grabRecoveryRemaining = guardBreakRecovery;
            target.ApplyPostureDamage(guardBreakPostureDamage, guardBreakStun);
            fistAudio?.PlayHeavyHit();
        }
        else if (grabTime >= guardBreakApproachTimeout) CancelAction();
        else if (delta.magnitude > 0.65f)
            player.Motor.AddDisplacement(delta.normalized * Mathf.Min(delta.magnitude - 0.65f, guardBreakApproachSpeed * dt));
    }
    private bool FindContact(float distance, out Collider collider, out Vector3 point)
    {
        collider = null; point = default;
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.SphereCastAll(aim.position, 0.22f, aim.forward, distance, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player.transform) || hit.distance >= nearest) continue;
            nearest = hit.distance; collider = hit.collider; point = hit.point;
        }
        // Sphere casts omit colliders which already overlap the origin.
        foreach (var overlap in Physics.OverlapSphere(aim.position, 0.22f, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (overlap.transform.IsChildOf(player.transform)) continue;
            if (!ShoveSweep.TryGetContactPoint(overlap, aim.position, aim.forward, .22f, 0f, out var closest)) continue;
            if (Vector3.Dot(closest - aim.position, aim.forward) < -0.01f) continue;
            collider = overlap; point = closest; break;
        }
        return collider != null && HasLineOfSight(collider, point);
    }
    private bool HasLineOfSight(Collider candidate, Vector3 point)
    {
        Vector3 direction = point - aim.position;
        foreach (var hit in Physics.RaycastAll(aim.position, direction.normalized, direction.magnitude, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player.transform) || hit.collider == candidate) continue;
            var receiver = candidate.GetComponentInParent<IDamageReceiver>();
            if (receiver != null && hit.collider.GetComponentInParent<IDamageReceiver>() == receiver) continue;
            return false;
        }
        return true;
    }
    public void CancelAction()
    {
        punchSweep.Cancel(); shoveSweep.Cancel();
        combos?.CancelDash();
        if (target != null) target.Release(this);
        target = null; targetCollider = null;
        if (guardAttemptHeld && !defenseConnected) pendingWhiff = true;
        charging = shovePending = IsGuarding = guardAttemptHeld = false;
        chargeTime = shoveTime = lungeRemaining = parryRemaining = guardBufferRemaining = 0f;
    }
    private void OnDisable() { CancelAction(); input?.RequireTriggerRelease(); }
}
