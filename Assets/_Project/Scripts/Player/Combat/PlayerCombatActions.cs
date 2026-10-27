using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerCombatActions
{
    private readonly PlayerMovement ctx;
    private readonly HashSet<Object> contacted = new HashSet<Object>();
    private float cooldown;
    private long attackId;
    private readonly ShoveSweep.Attack shoveSweep = new ShoveSweep.Attack();
    internal PlayerCombatActions(PlayerMovement context) { ctx = context; }
    internal void Tick(float dt)
    {
        cooldown = Mathf.Max(0f, cooldown - dt);
        SampleShove(dt);
    }
    internal void CancelShove() { shoveSweep.Cancel(); }
    internal void SampleShove(float dt)
    {
        if (!ctx.InputAvailable || ctx.CombatInputLocked || ctx.IsParkour || ctx.IsDiving || ctx.IsSliding
            || ctx.CurrentState == PlayerMovement.MovementState.StepDodge
            || ctx.CurrentState == PlayerMovement.MovementState.Tackle
            || ctx.CurrentState == PlayerMovement.MovementState.LandingRoll)
        { CancelShove(); return; }
        shoveSweep.Tick(dt);
        if (shoveSweep.HitCount > 0) ctx.GetComponent<WeaponController>()?.ConfirmShoveHit();
    }

    internal bool TryShove()
    {
        if (cooldown > 0f) return false;
        ctx.GetComponent<WeaponController>()?.InterruptForSurvival(true);
        cooldown = ctx.AbilitySettings.shoveCooldown;
        contacted.Clear(); attackId = DamageHit.NewAttackId();
        if (ctx.Input.SprintHeld && ctx.CurrentInput.y > 0.1f &&
            new Vector2(ctx.ActualVelocity.x, ctx.ActualVelocity.z).magnitude >= ctx.AbilitySettings.tackleMinSpeed)
        {
            ctx.Abilities.ActionDirection = Vector3.ProjectOnPlane(ctx.transform.forward, Vector3.up).normalized;
            ctx.ChangeState(PlayerMovement.MovementState.Tackle);
        }
        else HitForward(false);
        return true;
    }

    internal void HitForward(bool tackle)
    {
        if (!tackle)
        {
            var camera = ctx.GetComponentInChildren<Camera>();
            shoveSweep.Begin(ctx, camera != null ? camera.transform : ctx.transform, ctx.AbilitySettings.shoveDamage,
                ctx.AbilitySettings.interactionLayers, ctx.AbilitySettings.shoveActiveTime);
            if (shoveSweep.HitCount > 0) ctx.GetComponent<WeaponController>()?.ConfirmShoveHit();
            return;
        }
        Vector3 direction = ctx.Abilities.ActionDirection;
        Vector3 start = ctx.transform.position + Vector3.up * Mathf.Min(1f, ctx.Motor.controller.height * 0.65f);
        float radius = ctx.AbilitySettings.shoveRadius;
        var hits = Physics.SphereCastAll(start, radius, direction, ctx.AbilitySettings.shoveReach,
            ctx.AbilitySettings.interactionLayers & ~(1 << ctx.gameObject.layer), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            ApplyBodyImpact(hit.collider, hit.point, hit.normal, direction);
            break;
        }
    }

    private void ApplyBodyImpact(Collider collider, Vector3 point, Vector3 normal, Vector3 direction)
    {
        var damage = collider.GetComponentInParent<IDamageReceiver>();
        var shove = collider.GetComponentInParent<IShoveReceiver>();
        var body = collider.attachedRigidbody;
        var breakable = collider.GetComponentInParent<IBreakable>();
        Object identity = damage as Object ?? shove as Object ?? (Object)body ?? collider;

        if (!contacted.Add(identity)) return;

        Vector3 impulse = direction * ctx.AbilitySettings.shoveImpulse * 2f;
        var result = damage?.ReceiveHit(new DamageHit(attackId, ctx.AbilitySettings.tackleDamage,
            DamageKind.Attack, point, direction, ctx.gameObject));
        if (result == DamageResult.Damaged)
            collider.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(20f, 0.45f);

        var prop = PhysicsProp.Of(collider);
        if (shove != null) shove.ReceiveShove(impulse, point);
        else if (prop != null) prop.Push(direction * ctx.AbilitySettings.propShoveImpulse * 2f, point,
            Mathf.Max(ctx.AbilitySettings.shovePushSpeed, ctx.HorizontalVelocity.magnitude));
        else if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, point, ForceMode.Impulse);

        if (breakable != null && ctx.HorizontalVelocity.magnitude >= ctx.DoorBreakSpeedThreshold)
            breakable.Break(point, normal, ctx.HorizontalVelocity.magnitude);

        // Share the tackle impact feedback and time ownership with perfect defense.
        ctx.GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(
            new Vector3(-20f, Random.Range(-5f, 5f), Random.Range(-8f, 8f)), -20f);
        var timeEffects = ctx.GetComponent<CombatTimeEffects>() ?? ctx.gameObject.AddComponent<CombatTimeEffects>();
        timeEffects.RequestSlowdown(ctx, .1f, .08f);
    }
    internal void Reset() { CancelShove(); cooldown = 0f; attackId = 0; contacted.Clear(); }
}
