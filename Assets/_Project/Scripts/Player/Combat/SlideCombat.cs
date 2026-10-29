using System.Collections.Generic;
using UnityEngine;

/// <summary>Slide contact rules, independent of the dash push and dodge invulnerability.</summary>
public sealed class SlideCombat
{
    private readonly PlayerMovement ctx;
    private readonly HashSet<MeleeCombatTarget> contacted = new HashSet<MeleeCombatTarget>();
    private readonly HashSet<MeleeCombatTarget> passed = new HashSet<MeleeCombatTarget>();
    private long attackId;
    private float windowRemaining, boostCooldown;
    public bool IsJustSlide => ctx.IsSliding && windowRemaining > .00001f;

    internal SlideCombat(PlayerMovement context) { ctx = context; }
    internal void Tick(float dt)
    {
        windowRemaining = Mathf.Max(0f, windowRemaining - dt);
        boostCooldown = Mathf.Max(0f, boostCooldown - dt);
    }
    internal void Begin()
    {
        contacted.Clear(); passed.Clear(); windowRemaining = 0f;
        attackId = DamageHit.NewAttackId();
    }
    internal void End() { windowRemaining = 0f; }
    internal void Reset() { End(); boostCooldown = 0f; contacted.Clear(); passed.Clear(); }
    public bool TryBoost()
    {
        if (!ctx.IsSliding || ctx.CombatInputLocked || !ctx.DamageReceiver.IsAlive
            || ctx.CurrentSlideSpeed <= .1f || boostCooldown > 0f) return false;
        ctx.CurrentSlideSpeed += ctx.AbilitySettings.slideBoostSpeed;
        ctx.HorizontalVelocity = ctx.SlideDirection * ctx.CurrentSlideSpeed;
        windowRemaining = ctx.AbilitySettings.justSlideWindow;
        boostCooldown = Mathf.Max(windowRemaining, ctx.AbilitySettings.slideBoostCooldown);
        return true;
    }

    private MeleeCombatTarget Target(Collider collider)
    {
        if (collider == null || collider.transform.IsChildOf(ctx.transform)
            || (ctx.AbilitySettings.interactionLayers.value & (1 << collider.gameObject.layer)) == 0) return null;
        var target = collider.GetComponentInParent<MeleeCombatTarget>();
        return target != null && target.IsAvailable && target.GetComponent<IDamageReceiver>() != null ? target : null;
    }

    internal bool Hit(Collider collider, Vector3 point, Vector3 normal, Vector3 direction)
    {
        if (!ctx.IsSliding || ctx.CurrentSlideSpeed <= .1f) return false;
        var target = Target(collider);
        if (target == null) return false;
        if (contacted.Contains(target))
        {
            if (!passed.Contains(target)) return false;
            ctx.DodgePassThrough.IgnoreSlideTarget(target, direction);
            return true;
        }
        bool perfect = IsJustSlide;
        bool canPass = perfect || target.BodySize == EnemyBodySize.Small;
        // Set up collision pairs before damage can kill/deactivate the target.
        if (canPass) ctx.DodgePassThrough.IgnoreSlideTarget(target, direction);
        contacted.Add(target);
        if (canPass) passed.Add(target);
        var settings = ctx.AbilitySettings;
        var result = target.GetComponent<IDamageReceiver>().ReceiveHit(new DamageHit(attackId,
            settings.slideDamage, DamageKind.Attack, point, direction, ctx.gameObject));
        if (result == DamageResult.Damaged)
        {
            if (target.BodySize == EnemyBodySize.Small || (perfect && target.BodySize == EnemyBodySize.Medium))
                target.KnockDown(direction, settings.slideKnockdownDuration);
            else if (!perfect && target.BodySize == EnemyBodySize.Medium)
            {
                target.ReceiveShove(direction * settings.slidePushImpulse, point);
                target.Interrupt(settings.slidePushStun);
            }
        }
        if (canPass)
        {
            // Ragdoll activation can rebuild native collision pairs or enable bone colliders.
            ctx.DodgePassThrough.IgnoreSlideTarget(target, direction);
            if (!perfect)
            {
                ctx.CurrentSlideSpeed *= settings.slideSmallSpeedRetention;
                ctx.HorizontalVelocity = direction * ctx.CurrentSlideSpeed;
            }
        }
        else
        {
            // Use the existing posture-specific wall response, including its bounce thresholds.
            ctx.StateMachine.Current.HandleWallCollision(normal, Mathf.Clamp01(Vector3.Dot(direction, -normal)));
        }
        return true;
    }

    // Ignored colliders produce no controller callbacks. Sweep the actual upcoming movement
    // in distance order, stopping at solid obstacles so enemies behind walls cannot be hit.
    internal void BeforeMove(Vector3 displacement)
    {
        if (!ctx.IsSliding || displacement.sqrMagnitude < .000001f) return;
        var body = ctx.Motor.controller;
        Vector3 scale = ctx.transform.lossyScale;
        float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        Vector3 center = ctx.transform.TransformPoint(body.center);
        float half = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * .5f - radius);
        Vector3 a = center - Vector3.up * half, b = center + Vector3.up * half;
        Vector3 direction = displacement.normalized;
        var hits = Physics.CapsuleCastAll(a, b, radius, direction, displacement.magnitude,
            ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(ctx.transform)) continue;
            if (hit.normal.y >= Mathf.Cos(ctx.Motor.SupportAngle * Mathf.Deg2Rad)) continue;
            var target = Target(hit.collider);
            if (target == null) break;
            if (!passed.Contains(target) && !(IsJustSlide || target.BodySize == EnemyBodySize.Small)) break;
            if (Physics.GetIgnoreCollision(body, hit.collider) && !passed.Contains(target)) continue;
            Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            Hit(hit.collider, hit.point, normal.sqrMagnitude > .01f ? normal : -direction, direction);
        }
    }
}
