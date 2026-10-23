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
    }

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
        if (!tackle)
        {
            var camera = ctx.GetComponentInChildren<Camera>();
            shoveSweep.Begin(ctx, camera != null ? camera.transform : ctx.transform, ctx.AbilitySettings.shoveDamage,
                ctx.AbilitySettings.interactionLayers, ctx.AbilitySettings.shoveActiveTime);
            return;
        }
        Vector3 direction = tackle ? ctx.Abilities.ActionDirection : ctx.transform.forward;
        Vector3 start = ctx.transform.position + Vector3.up * Mathf.Min(1f, ctx.Motor.controller.height * 0.65f);
        float radius = ctx.AbilitySettings.shoveRadius;
        var hits = Physics.SphereCastAll(start, radius, direction, ctx.AbilitySettings.shoveReach,
            ctx.AbilitySettings.interactionLayers & ~(1 << ctx.gameObject.layer), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var damage = hit.collider.GetComponentInParent<IDamageReceiver>();
            var shove = hit.collider.GetComponentInParent<IShoveReceiver>();
            var body = hit.rigidbody;
            var breakable = hit.collider.GetComponentInParent<IBreakable>();
            Object identity = damage as Object ?? shove as Object ?? (Object)body ?? hit.collider;

            if (!contacted.Add(identity)) break;

            Vector3 impulse = direction * ctx.AbilitySettings.shoveImpulse * (tackle ? 2f : 1f);
            var result = damage?.ReceiveHit(new DamageHit(attackId, tackle ? ctx.AbilitySettings.tackleDamage : ctx.AbilitySettings.shoveDamage,
                DamageKind.Attack, hit.point, direction, ctx.gameObject));
            if (result == DamageResult.Damaged)
                hit.collider.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(tackle ? 20f : 8f, 0.45f);

            if (shove != null) shove.ReceiveShove(impulse, hit.point);
            else if (body != null && !body.isKinematic) body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);

            if (tackle && breakable != null && ctx.HorizontalVelocity.magnitude >= ctx.DoorBreakSpeedThreshold)
                breakable.Break(hit.point, hit.normal, ctx.HorizontalVelocity.magnitude);

            // [개선된 카메라 연출 및 히트 스톱]
            if (tackle)
            {
                // 1. 단순 피치(위아래)뿐만 아니라 요(좌우), 롤(기울기)을 랜덤하게 섞어 거친 충돌 느낌 부여
                ctx.GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(
                    new Vector3(-20f, Random.Range(-5f, 5f), Random.Range(-8f, 8f)), -20f);

                // 2. 역경직(Hit Stop): 0.08초 동안 게임 속도를 10%로 줄여 타격감 극대화
                // 완벽 방어 슬로모션과 시간 복구를 공유한다.
                var timeEffects = ctx.GetComponent<CombatTimeEffects>() ?? ctx.gameObject.AddComponent<CombatTimeEffects>();
                timeEffects.RequestSlowdown(ctx, .1f, .08f);
            }

            break;
        }
    }

    internal void Reset() { CancelShove(); cooldown = 0f; attackId = 0; contacted.Clear(); }
}
