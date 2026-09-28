using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerCombatActions
{
    private readonly PlayerMovement ctx;
    private readonly HashSet<Object> contacted = new HashSet<Object>();
    private float cooldown;
    private long attackId;
    internal PlayerCombatActions(PlayerMovement context) { ctx = context; }
    internal void Tick(float dt) { cooldown = Mathf.Max(0f, cooldown - dt); }
    internal bool TryShove()
    {
        if (cooldown > 0f) return false;
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
        Vector3 direction = tackle ? ctx.Abilities.ActionDirection : ctx.transform.forward;
        Vector3 start = ctx.transform.position + Vector3.up * Mathf.Min(1f, ctx.Motor.controller.height * 0.65f);
        float radius = ctx.AbilitySettings.shoveRadius;
        var hits = Physics.SphereCastAll(start, radius, direction, ctx.AbilitySettings.shoveReach,
            ctx.AbilitySettings.interactionLayers & ~(1 << ctx.gameObject.layer), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            // A solid wall blocks targets behind it. Multiple child colliders resolve to one receiver/body.
            var damage = hit.collider.GetComponentInParent<IDamageReceiver>();
            var shove = hit.collider.GetComponentInParent<IShoveReceiver>();
            var body = hit.rigidbody;
            var breakable = hit.collider.GetComponentInParent<IBreakable>();
            Object identity = damage as Object ?? shove as Object ?? (Object)body ?? hit.collider;
            if (!contacted.Add(identity)) break;
            Vector3 impulse = direction * ctx.AbilitySettings.shoveImpulse * (tackle ? 2f : 1f);
            damage?.ReceiveHit(new DamageHit(attackId, tackle ? ctx.AbilitySettings.tackleDamage : ctx.AbilitySettings.shoveDamage,
                DamageKind.Attack, hit.point, direction, ctx.gameObject));
            if (shove != null) shove.ReceiveShove(impulse, hit.point);
            else if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
            if (tackle && breakable != null && ctx.HorizontalVelocity.magnitude >= ctx.DoorBreakSpeedThreshold)
                breakable.Break(hit.point, hit.normal, ctx.HorizontalVelocity.magnitude);
            break;
        }
    }
    internal void Reset() { cooldown = 0f; attackId = 0; contacted.Clear(); }
}
