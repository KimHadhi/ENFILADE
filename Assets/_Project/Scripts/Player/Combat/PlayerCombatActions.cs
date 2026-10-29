using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using State = PlayerMovement.MovementState;

/// <summary>
/// The body's V. Running, with a gun or fists, it is the dash push, after Dynamic Landing: the body is thrown ahead at
/// twice the run for a moment, and the first enemy it meets is thrown along the ground (shoving whoever that one runs
/// into) and knocked down. The dash stops there, and its own cooldown starts when it ends, hit or miss. Otherwise, with
/// a gun out, V is the crowd shove (ShoveSweep); the fists' V is PlayerMeleeCombat's.
/// </summary>
public sealed class PlayerCombatActions
{
    // Moving at least this share of the speed along the view counts as running forward (Dynamic Landing: 0.7).
    private const float RunAlignment = .7f;

    private readonly PlayerMovement ctx;
    private readonly HashSet<Object> contacted = new HashSet<Object>();
    private float cooldown, dashCooldown;
    private long attackId;
    private readonly ShoveSweep.Attack shoveSweep = new ShoveSweep.Attack();

    /// <summary>The speed of the dash under way (m/s).</summary>
    public float DashSpeed { get; private set; }

    internal PlayerCombatActions(PlayerMovement context) { ctx = context; }
    internal void Tick(float dt)
    {
        cooldown = Mathf.Max(0f, cooldown - dt);
        dashCooldown = Mathf.Max(0f, dashCooldown - dt);
        SampleShove(dt);
    }
    internal void CancelShove() { shoveSweep.Cancel(); }
    internal void SampleShove(float dt)
    {
        if (!ctx.InputAvailable || ctx.CombatInputLocked || ctx.IsParkour || ctx.IsDiving || ctx.IsSliding
            || ctx.CurrentState == State.StepDodge
            || ctx.CurrentState == State.DashPush
            || ctx.CurrentState == State.LandingRoll)
        { CancelShove(); return; }
        shoveSweep.Tick(dt);
        if (shoveSweep.HitCount > 0) ctx.GetComponent<WeaponController>()?.ConfirmShoveHit();
    }

    internal bool TryShove()
    {
        if (cooldown > 0f) return false;
        ctx.GetComponent<WeaponController>()?.InterruptForSurvival(true);
        cooldown = ctx.AbilitySettings.shoveCooldown;
        var camera = ctx.GetComponentInChildren<Camera>();
        shoveSweep.Begin(ctx, camera != null ? camera.transform : ctx.transform, ctx.AbilitySettings.shoveDamage,
            ctx.AbilitySettings.interactionLayers, ctx.AbilitySettings.shoveActiveTime);
        if (shoveSweep.HitCount > 0) ctx.GetComponent<WeaponController>()?.ConfirmShoveHit();
        return true;
    }

    internal bool TryDashPush()
    {
        var settings = ctx.AbilitySettings;
        Vector3 run = Vector3.ProjectOnPlane(ctx.ActualVelocity, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized;
        float speed = run.magnitude;
        if (dashCooldown > 0f || ctx.CurrentState != State.Standing || ctx.CurrentInput.y <= .1f
            || speed < Mathf.Max(.1f, settings.dashPushMinSpeed) || Vector3.Dot(run, forward) < speed * RunAlignment) return false;
        ctx.GetComponent<WeaponController>()?.InterruptForSurvival();
        contacted.Clear(); attackId = DamageHit.NewAttackId();
        DashSpeed = speed * settings.dashPushSpeedScale;
        ctx.Abilities.ActionDirection = forward;
        ctx.ChangeState(State.DashPush);
        return true;
    }

    internal void EndDashPush() { dashCooldown = ctx.AbilitySettings.dashPushCooldown; }

    /// <summary>
    /// Sweeps the body ahead by this frame's step plus the reach, nearest first. Light props are left to the body to
    /// kick away and a door it is fast enough for breaks; anything else solid ends the sweep, so nobody behind cover
    /// is hit. True when the dash met someone and pushed them.
    /// </summary>
    internal bool DashInto(float step)
    {
        var settings = ctx.AbilitySettings;
        var body = ctx.Motor.controller;
        Vector3 direction = ctx.Abilities.ActionDirection;
        Vector3 scale = ctx.transform.lossyScale;
        float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        Vector3 center = ctx.transform.TransformPoint(body.center);
        float half = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * .5f - radius);
        // The bottom is lifted by a step, so the floor and the steps underfoot are not in the way.
        Vector3 bottom = center - Vector3.up * Mathf.Max(0f, half - body.stepOffset), top = center + Vector3.up * half;
        var hits = Physics.CapsuleCastAll(bottom, top, radius, direction, step + settings.dashPushReach,
            settings.interactionLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        float walkable = Mathf.Cos(ctx.Motor.SupportAngle * Mathf.Deg2Rad);
        foreach (var hit in hits)
        {
            var collider = hit.collider;
            if (collider.transform.IsChildOf(ctx.transform) || hit.normal.y >= walkable || PhysicsProp.Of(collider) != null) continue;
            // A cast that starts inside a collider reports no point.
            Vector3 point = hit.distance > 0f ? hit.point : collider.bounds.center;
            var target = collider.GetComponentInParent<MeleeCombatTarget>();
            if (target != null && (!target.IsAvailable || target.IsKnockedDown)) continue;
            var damage = collider.GetComponentInParent<IDamageReceiver>();
            var shove = collider.GetComponentInParent<IShoveReceiver>();
            if (target != null || damage != null || shove != null)
            {
                Push(target, damage, shove, point, direction);
                return true;
            }
            var breakable = collider.GetComponentInParent<IBreakable>();
            if (breakable != null && DashSpeed >= ctx.DoorBreakSpeedThreshold)
            {
                if (contacted.Add(breakable as Object ?? collider)) breakable.Break(point, hit.normal, DashSpeed);
                continue;
            }
            // Something already touching the body (a wall alongside) is not in the way ahead.
            if (hit.distance > 0f) return false;
        }
        return false;
    }

    private void Push(MeleeCombatTarget target, IDamageReceiver damage, IShoveReceiver shove, Vector3 point, Vector3 direction)
    {
        var settings = ctx.AbilitySettings;
        var result = damage?.ReceiveHit(new DamageHit(attackId, settings.dashPushDamage, DamageKind.Attack, point, direction,
            ctx.gameObject, method: DamageMethod.Shove));
        bool landed = result == null || result == DamageResult.Damaged;
        Vector3 thrown = direction * DashSpeed;
        if (target != null)
        {
            if (landed) target.ApplyPostureDamage(settings.dashPushPostureDamage, 0f, false);
            // A big body, or one that guarded, only gives way like a crowd shove.
            if (landed && target.BodySize != EnemyBodySize.Large)
                target.Barge(thrown, settings.dashPushThrowTime, settings.dashPushKnockdownDuration);
            else
            {
                target.ReceiveShove(direction * settings.shovePushSpeed, point);
                target.Interrupt(settings.shoveStun);
            }
        }
        else shove?.ReceiveShove(thrown, point);

        // The dash stops dead on the body it hit, and the run with it.
        ctx.Actions.Momentum.Stop();
        ctx.GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(
            new Vector3(-20f, UnityEngine.Random.Range(-5f, 5f), UnityEngine.Random.Range(-8f, 8f)), -20f);
        // Shares time ownership with perfect defense.
        var timeEffects = ctx.GetComponent<CombatTimeEffects>() ?? ctx.gameObject.AddComponent<CombatTimeEffects>();
        timeEffects.RequestSlowdown(ctx, .1f, .08f);
        ctx.RaiseDashPushHit(point);
    }
    internal void Reset() { CancelShove(); cooldown = dashCooldown = DashSpeed = 0f; attackId = 0; contacted.Clear(); }
}
