using System;
using UnityEngine;

/// <summary>
/// Fist prototype. Owns charge/release, frontal defense and single-target guard break. With the katana out
/// (PlayerKatana) the guard and parry here are the blade's too, in its own cone and with its block letting a share
/// through; its swings are its own, and V is the body's shove as with a gun.
/// </summary>
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
    private Transform aim;
    private MeleeCombatTarget target;
    private Collider targetCollider;
    private float chargeTime, shoveTime, cooldown, shoveRest, grabTime, grabRecoveryRemaining, lungeRemaining, punchCharge;
    private float parryRemaining, guardBufferRemaining, guardIdleTime, rootUntil = float.NegativeInfinity;
    private int spamCount;
    private ParryFeedback parryFeedback;
    private PlayerKatana blade;
    // How long the guard has been up this time (game s): a katana's block lets more through the longer it is held.
    private float guardTime;
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
    /// <summary>The katana is in hand (PlayerKatana): it guards and parries here as the fists do.</summary>
    public bool HoldsBlade => isActiveAndEnabled && blade != null && blade.IsActive;
    // Fists or the katana: either one guards and parries.
    private bool Defends => IsActive || HoldsBlade;
    /// <summary>The feet are planted after a parry (real time): the legs give no speed, the hands are free.</summary>
    public bool IsRooted => Defends && Time.unscaledTime < rootUntil;
    public event Action<float> Punched;

    /// <summary>
    /// The speed the legs may go with the hands doing what they are doing (LocomotionState asks): nothing while rooted
    /// after a parry, a slow walk with the guard held up (as Sekiro's guard walk), otherwise speed as it is.
    /// </summary>
    public float LimitMoveSpeed(float speed)
    {
        if (!Defends) return speed;
        if (IsRooted) return 0f;
        if (IsGuarding) return Mathf.Min(speed, player.WalkSpeed * guardMoveShare);
        // A katana's heavy cut plants the feet a little while it is under way (PlayerKatana).
        return HoldsBlade ? blade.LimitMoveSpeed(speed, player.WalkSpeed) : speed;
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
        // The katana, for when the melee slot holds it (bundle 5 of the addon port).
        blade = GetComponent<PlayerKatana>();
        if (blade == null && Application.isPlaying) blade = gameObject.AddComponent<PlayerKatana>();
        CancelAction();
    }
    /// <summary>On someone else's copy: their guard went up or down.</summary>
    internal void ShowGuard(bool guarding) => IsGuarding = guarding;
    /// <summary>On someone else's copy: their punch left. Their PC judged what it hit.</summary>
    internal void ShowPunch(float charge) => Punched?.Invoke(charge);
    private void Update()
    {
        if (player != null && player.IsNetworkDriven) return;
        if (!Defends || !player.InputAvailable) { CancelAction(); TickGuardInput(player.DeltaTime, default); return; }
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
        if (!Defends || !player.InputAvailable || player.Execution?.IsExecuting == true) { CancelAction(); return; }
        if (IsGuardBreaking) return;
        if (MovementBlocksAttack) { SuspendActionsForMovement(); return; }
        punchSweep.Tick(0f); shoveSweep.Tick(0f);
        if (HoldsBlade) blade.LateTick();
    }
    /// <summary>A counter hands the shove straight back (ShoveCounter): the fists can shove or punch again at once.</summary>
    internal void RefreshShove() { cooldown = shoveRest = 0f; }
    /// <summary>A throwing knife is in the off hand (PlayerKnives, which runs after this every frame): no punch or shove goes out; the guard is left alone.</summary>
    internal void HoldOffense() => CancelOffense();
    public void ProcessFrame(float dt, MeleeInputFrame frame)
    {
        if (dt <= 0f) return;
        if (!Defends || player.Execution?.IsExecuting == true) { CancelAction(); return; }
        cooldown = Mathf.Max(0f, cooldown - dt);
        shoveRest = Mathf.Max(0f, shoveRest - dt);
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
        // Guard has priority over charge, punch/shove active frames and their recovery. A katana's blade in its cut
        // finishes it first; the press waits in the buffer meanwhile.
        bool guardFree = !HoldsBlade || blade.CanGuard;
        if (guardBufferRemaining > 0f) { if (guardFree) BeginGuard(frame.GuardHeld); }
        else if (frame.GuardHeld && !IsGuarding && guardFree)
        {
            // A long hold through an uninterruptible action resumes block, never a fresh parry.
            CancelOffense(); IsGuarding = true; guardAttemptHeld = true; defenseConnected = false;
        }
        guardTime = IsGuarding || IsParryActive ? guardTime + dt : 0f;
        if (IsGuarding) return;
        // The katana's own cuts; V is the body's shove with it, as with a gun (MovementAbilities).
        if (HoldsBlade) { blade.ProcessFrame(dt, frame); return; }
        punchSweep.Tick(dt); shoveSweep.Tick(dt);
        if (!player.IsGrounded) lungeRemaining = 0f;
        if (lungeRemaining > 0f)
        {
            float slice = Mathf.Min(dt, lungeRemaining); lungeRemaining -= slice;
            player.Motor.AddDisplacement(lungeDirection * (lungeDistance * slice / 0.1f));
        }
        if (frame.ShovePressed && cooldown <= 0f)
        { punchSweep.Cancel(); shoveSweep.Cancel(); shovePending = true; shoveTime = 0f; charging = false; IsGuarding = false; parryRemaining = 0f; }
        if (shovePending)
        {
            shoveTime += dt;
            if (frame.ShoveHeld && shoveTime >= holdThreshold)
            {
                shovePending = false; cooldown = guardBreakCooldown;
                TryStartGuardBreak(); // Holding never also emits the tap shove on release.
            }
            else if (frame.ShoveReleased)
            {
                // Fatigue only slows the next shove; punches keep their own cooldown.
                shovePending = false;
                if (shoveRest <= 0f)
                {
                    // The wait is set before the shove goes out: a counter on its first contact clears it again (RefreshShove).
                    cooldown = 0.55f;
                    shoveRest = player.Abilities.Combat.Fatigue.Spend(cooldown, player.AbilitySettings);
                    Shove();
                }
            }
            else if (!frame.ShoveHeld) shovePending = false;
            return;
        }
        if (frame.AttackPressed && cooldown <= 0f)
        { punchSweep.Cancel(); shoveSweep.Cancel(); parryRemaining = 0f; charging = true; chargeTime = 0f; }
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
        IsGuarding = held; guardIdleTime = guardBufferRemaining = guardTime = 0f;
        CancelOffense(); cooldown = 0f;
    }
    private void CancelOffense()
    {
        punchSweep.Cancel(); shoveSweep.Cancel();
        charging = shovePending = false; chargeTime = shoveTime = lungeRemaining = 0f;
        if (blade != null) blade.Cancel();
    }
    private void SuspendActionsForMovement()
    {
        float buffered = guardBufferRemaining;
        CancelAction();
        guardBufferRemaining = buffered;
    }
    private void ResetParrySpam() { spamCount = 0; pendingWhiff = false; }
    public void NotifyDamaged() { ResetParrySpam(); defenseConnected = true; }
    private void Punch(float charge)
    {
        lungeDirection = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        lungeRemaining = player.IsGrounded ? 0.1f : 0f;
        // Swing first: the sweep can land on its first sample inside Begin.
        punchLanded = false; punchCharge = charge; fistAudio?.PlaySwing();
        // A Knight's fists hit harder, flesh and posture (PlayerClass).
        float fist = PlayerClass.FistFactor(player);
        punchSweep.Begin(player, aim, Mathf.Lerp(damage.x, damage.y, charge) * fist,
            Mathf.Lerp(postureDamage.x, postureDamage.y, charge) * fist, reach, .22f, attackActiveTime,
            attackMaxTargets, hitLayers, OnPunchLanded, OnPunchWallHit);
        Punched?.Invoke(charge);
    }
    private void OnPunchLanded()
    {
        cooldown = 0f;
        // One hit cue per punch, even when it lands on several targets.
        if (!punchLanded)
        {
            if (punchCharge >= heavyPunchCharge) fistAudio?.PlayHeavyHit();
            else fistAudio?.PlayHit();
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
        // With the katana out the counter is a quick cut (PlayerKatana shows it on Punched); its swing comes with it.
        else if (HoldsBlade) blade.CounterLanded(landed);
        Punched?.Invoke(1f);
    }
    /// <summary>The counter's back elbow, driven back on the attacker's side (+1 right): the fist pulls back past the shoulder.</summary>
    public event Action<int> Elbowed;
    public void ShowCounterElbow(bool landed, int side)
    {
        if (IsActive) { fistAudio?.PlaySwing(); if (landed) fistAudio?.PlayHeavyHit(); }
        else if (HoldsBlade) blade.CounterLanded(landed);
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
    public bool TryDefend(DamageHit hit, out DamageResult result) => TryDefend(hit, out result, out _);
    /// <summary>
    /// As TryDefend; leak is the share of a blocked blow that gets through anyway: a katana's block isn't a wall (TFA's
    /// block, PlayerKatana.GuardLeak). The fists' block and every parry take it all (0).
    /// </summary>
    public bool TryDefend(DamageHit hit, out DamageResult result, out float leak)
    {
        result = DamageResult.Ignored;
        leak = 0f;
        if (!Defends || !player.InputAvailable || (!IsGuarding && !IsParryActive) || IsGuardBreaking
            || MovementBlocksAttack || hit.Kind != DamageKind.Attack || !hit.CanBeGuarded) return false;
        Vector3 incoming = hit.Source != null ? hit.Source.transform.position - player.transform.position : -hit.Direction;
        incoming = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 facing = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        // The katana guards a narrower cone than the fists (TFA's BlockCone, 135°).
        float cone = HoldsBlade ? blade.GuardAngle : guardAngle;
        if (incoming.sqrMagnitude < 0.01f || Vector3.Dot(incoming, facing) < Mathf.Cos(cone * 0.5f * Mathf.Deg2Rad)) return false;
        bool parry = IsParryActive;
        result = parry ? DamageResult.Parried : DamageResult.Blocked;
        if (!parry && HoldsBlade) leak = blade.GuardLeak(guardTime);
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
            if (grabRecoveryRemaining <= 0f) { CancelAction(); cooldown = guardBreakCooldown; }
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
        if (blade != null) blade.Cancel();
        if (target != null) target.Release(this);
        target = null; targetCollider = null;
        if (guardAttemptHeld && !defenseConnected) pendingWhiff = true;
        charging = shovePending = IsGuarding = guardAttemptHeld = false;
        chargeTime = shoveTime = lungeRemaining = parryRemaining = guardBufferRemaining = 0f;
    }
    private void OnDisable() { CancelAction(); input?.RequireTriggerRelease(); }
}
