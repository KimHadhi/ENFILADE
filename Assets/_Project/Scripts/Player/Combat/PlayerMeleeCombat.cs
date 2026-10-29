using System;
using UnityEngine;

/// <summary>Fist prototype. Owns charge/release, frontal defense and single-target guard break.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-150)]
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
    [Header("Guard / Parry")]
    [Tooltip("패링/가드 성공 없이 연속 재입력했을 때의 단계별 패링 시간(게임 시간 초).")]
    [SerializeField] private float[] parryWindowDurations = { .35f, .20f, .10f, .05f };
    [SerializeField, Min(0f)] private float spamResetDelay = .8f;
    [SerializeField, Min(0f)] private float guardInputBuffer = .12f;
    [SerializeField, Range(30f, 180f)] private float guardAngle = 150f;
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
    private float chargeTime, shoveTime, cooldown, grabTime, grabRecoveryRemaining, lungeRemaining;
    private float parryRemaining, guardBufferRemaining, guardIdleTime;
    private int spamCount;
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
    private bool MovementBlocksAttack => player.IsParkour || player.IsDiving || player.IsSliding
        || player.CurrentState == PlayerMovement.MovementState.StepDodge
        || player.CurrentState == PlayerMovement.MovementState.DashPush
        || player.IsLandingRecovery;
    public bool IsActive => isActiveAndEnabled && slots != null && slots.UsesFists && player != null && player.IsInitialized && player.DamageReceiver.IsAlive;
    public event Action<float> Punched;
    public event Action<DamageResult> Defended;

    private void OnEnable()
    {
        player = GetComponentInParent<PlayerMovement>();
        slots = GetComponent<PlayerWeaponSlots>(); input = GetComponent<PlayerWeaponInput>();
        var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        aim = camera != null ? camera.transform : transform;
        if (player != null) player.MeleeCombat = this;
        if (Application.isPlaying && camera != null && GetComponent<FistsView>() == null) gameObject.AddComponent<FistsView>();
        fistAudio = GetComponent<PlayerFistAudio>();
        if (fistAudio == null && Application.isPlaying) fistAudio = gameObject.AddComponent<PlayerFistAudio>();
        CancelAction();
    }
    private void Update()
    {
        if (!IsActive || !player.InputAvailable) { CancelAction(); TickGuardInput(Time.deltaTime, default); return; }
        if (IsGuardBreaking)
        {
            if (input != null && input.TryReadGuardBuffer(out var guardFrame)) ProcessFrame(Time.deltaTime, guardFrame);
            else CancelAction();
            return;
        }
        if (input == null || !input.TryReadMeleeInput(out var frame)) { CancelAction(); return; }
        ProcessFrame(Time.deltaTime, frame);
    }
    private void LateUpdate()
    {
        // PlayerLook updates after input processing. Include this frame's final camera rotation.
        if (Time.timeScale <= 0f) return;
        if (!IsActive || !player.InputAvailable || player.Execution?.IsExecuting == true) { CancelAction(); return; }
        if (IsGuardBreaking) return;
        if (MovementBlocksAttack) { SuspendActionsForMovement(); return; }
        punchSweep.Tick(0f); shoveSweep.Tick(0f);
    }
    public void ProcessFrame(float dt, MeleeInputFrame frame)
    {
        if (dt <= 0f) return;
        if (!IsActive || player.Execution?.IsExecuting == true) { CancelAction(); return; }
        cooldown = Mathf.Max(0f, cooldown - dt);
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
        // Guard has priority over charge, punch/shove active frames and their recovery.
        if (guardBufferRemaining > 0f) BeginGuard(frame.GuardHeld);
        else if (frame.GuardHeld && !IsGuarding)
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
            { shovePending = false; Shove(); cooldown = 0.55f; }
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
        float duration = last >= 0 ? parryWindowDurations[spamCount] : .35f;
        parryRemaining = float.IsNaN(duration) || float.IsInfinity(duration) ? .35f : Mathf.Max(.001f, duration);
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
    private void Punch(float charge)
    {
        lungeDirection = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        lungeRemaining = player.IsGrounded ? 0.1f : 0f;
        // Swing first: the sweep can land on its first sample inside Begin.
        punchLanded = false; fistAudio?.PlaySwing();
        punchSweep.Begin(player, aim, Mathf.Lerp(damage.x, damage.y, charge),
            Mathf.Lerp(postureDamage.x, postureDamage.y, charge), reach, .22f, attackActiveTime,
            attackMaxTargets, hitLayers, OnPunchLanded, OnPunchWallHit);
        Punched?.Invoke(charge);
    }
    private void OnPunchLanded()
    {
        cooldown = 0f;
        // One hit cue per punch, even when it lands on several targets.
        if (!punchLanded) fistAudio?.PlayHit();
        punchLanded = true;
    }
    private void OnPunchWallHit()
    {
        cooldown = 0f;
        fistAudio?.PlayWallHit();
    }
    public void ShowCounterPunch(bool landed)
    {
        if (IsActive) { fistAudio?.PlaySwing(); if (landed) fistAudio?.PlayHit(); }
        Punched?.Invoke(1f);
    }
    private void Shove()
    {
        shoveSweep.Begin(player, aim, 3f, hitLayers, player.AbilitySettings.shoveActiveTime);
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
            hit.Source?.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(25f, 0.8f);
        }
        Defended?.Invoke(result); return true;
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
        if (target != null) target.Release(this);
        target = null; targetCollider = null;
        if (guardAttemptHeld && !defenseConnected) pendingWhiff = true;
        charging = shovePending = IsGuarding = guardAttemptHeld = false;
        chargeTime = shoveTime = lungeRemaining = parryRemaining = guardBufferRemaining = 0f;
    }
    private void OnDisable() { CancelAction(); input?.RequireTriggerRelease(); }
}
