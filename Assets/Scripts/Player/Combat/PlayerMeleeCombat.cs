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
    [SerializeField, Min(0f)] private float attackCooldown = 0.4f;
    [SerializeField, Min(0f)] private float lungeDistance = 0.12f;
    [Header("Guard / Parry")]
    [SerializeField, Range(0.05f, 0.4f)] private float parryWindow = 0.18f;
    [SerializeField, Range(30f, 180f)] private float guardAngle = 120f;
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
    private Transform aim;
    private MeleeCombatTarget target;
    private Collider targetCollider;
    private float chargeTime, shoveTime, cooldown, guardTime, parryCooldown, grabTime, grabRecoveryRemaining, lungeRemaining;
    private bool charging, shovePending, parryArmed, grabConnected;
    private Vector3 lungeDirection;
    public bool IsGuarding { get; private set; }
    public bool IsCharging => charging;
    public float ChargeRatio => charging ? Mathf.Clamp01(chargeTime / fullChargeTime) : 0f;
    public bool IsGuardBreaking => target != null;
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
        CancelAction();
    }
    private void Update()
    {
        if (!IsActive || !player.InputAvailable) { CancelAction(); return; }
        if (IsGuardBreaking)
        {
            var playerInput = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null && playerInput.actions.FindActionMap("Player")?.enabled != true) CancelAction();
            else TickGuardBreak(Time.deltaTime);
            return;
        }
        if (input == null || !input.TryReadMeleeInput(out var frame)) { CancelAction(); return; }
        ProcessFrame(Time.deltaTime, frame);
    }
    public void ProcessFrame(float dt, MeleeInputFrame frame)
    {
        if (!IsActive || dt <= 0f || player.Execution?.IsExecuting == true) return;
        cooldown = Mathf.Max(0f, cooldown - dt); parryCooldown = Mathf.Max(0f, parryCooldown - dt);
        if (IsGuardBreaking) { TickGuardBreak(dt); return; }
        if (player.IsParkour || player.IsDiving || player.IsSliding || player.CurrentState == PlayerMovement.MovementState.StepDodge
            || player.CurrentState == PlayerMovement.MovementState.Tackle || player.CurrentState == PlayerMovement.MovementState.LandingRoll)
        { CancelAction(); return; }
        if (!player.IsGrounded) lungeRemaining = 0f;
        if (lungeRemaining > 0f)
        {
            float slice = Mathf.Min(dt, lungeRemaining); lungeRemaining -= slice;
            player.Motor.AddDisplacement(lungeDirection * (lungeDistance * slice / 0.1f));
        }
        if (frame.ShovePressed && cooldown <= 0f)
        { shovePending = true; shoveTime = 0f; charging = false; IsGuarding = false; }
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
        if (frame.GuardPressed && cooldown <= 0f)
        {
            IsGuarding = true; guardTime = 0f; charging = false;
            parryArmed = parryCooldown <= 0f;
            if (parryArmed) parryCooldown = 0.35f;
        }
        IsGuarding &= frame.GuardHeld;
        if (IsGuarding) { guardTime += dt; return; }
        if (frame.AttackPressed && cooldown <= 0f) { charging = true; chargeTime = 0f; }
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
    private void Punch(float charge)
    {
        lungeDirection = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        lungeRemaining = player.IsGrounded ? 0.1f : 0f;
        if (FindContact(reach, out var hit, out var point))
        {
            var receiver = hit.GetComponentInParent<IDamageReceiver>();
            float posture = Mathf.Lerp(postureDamage.x, postureDamage.y, charge);
            var result = receiver?.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Lerp(damage.x, damage.y, charge),
                DamageKind.Attack, point, aim.forward, player.gameObject, true, posture));
            if (result == DamageResult.Damaged) hit.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(posture);
        }
        Punched?.Invoke(charge);
    }
    private void Shove()
    {
        if (!FindContact(reach, out var hit, out var point)) return;
        var result = hit.GetComponentInParent<IDamageReceiver>()?.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), 3f,
            DamageKind.Attack, point, aim.forward, player.gameObject, true, 8f));
        if (result != DamageResult.Damaged) return;
        hit.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(8f, 0.45f);
        Vector3 impulse = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized * 4f;
        var shove = hit.GetComponentInParent<IShoveReceiver>();
        if (shove != null) shove.ReceiveShove(impulse, point);
        else if (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic) hit.attachedRigidbody.AddForce(impulse, ForceMode.Impulse);
    }
    public bool TryDefend(DamageHit hit, out DamageResult result)
    {
        result = DamageResult.Ignored;
        if (!IsActive || !player.InputAvailable || !IsGuarding || IsGuardBreaking || hit.Kind != DamageKind.Attack || !hit.CanBeGuarded) return false;
        Vector3 incoming = hit.Source != null ? hit.Source.transform.position - player.transform.position : -hit.Direction;
        incoming = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 facing = Vector3.ProjectOnPlane(aim.forward, Vector3.up).normalized;
        if (incoming.sqrMagnitude < 0.01f || Vector3.Dot(incoming, facing) < Mathf.Cos(guardAngle * 0.5f * Mathf.Deg2Rad)) return false;
        bool parry = parryArmed && guardTime <= parryWindow;
        result = parry ? DamageResult.Parried : DamageResult.Blocked;
        if (parry)
        {
            parryArmed = false;
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
        IsGuarding = charging = false; lungeRemaining = 0f;
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
            Vector3 closest = overlap.ClosestPoint(aim.position);
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
        if (target != null) target.Release(this);
        target = null; targetCollider = null;
        charging = shovePending = IsGuarding = parryArmed = false;
        chargeTime = shoveTime = lungeRemaining = 0f;
    }
    private void OnDisable() { CancelAction(); input?.RequireTriggerRelease(); }
}
