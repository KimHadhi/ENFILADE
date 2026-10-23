using UnityEngine;

public sealed class MovementLean
{
    private readonly PlayerMovement ctx;
    private float lean;
    private float reactionTime;
    private Vector3 reactionDirection;
    public float Roll => -lean * ctx.AbilitySettings.leanAngle;
    public float Offset => lean * ctx.AbilitySettings.leanOffset;
    internal MovementLean(PlayerMovement context) { ctx = context; }

    internal void Tick(float dt)
    {
        bool allowed = ctx.Abilities.CanUseGroundActions && ctx.InputAvailable;
        if (!allowed && ctx.Abilities.Dodge.Kind == DodgeKind.Micro) ctx.Abilities.Dodge.CancelWindow();
        int side = allowed ? ctx.Input.LeanSide : 0;
        if (side != 0 && ctx.Input.LeanPressed)
            ctx.Abilities.Dodge.TryStart(DodgeKind.Micro, side,
                ctx.AbilitySettings.microInvulnerability, ctx.AbilitySettings.dodgeCooldown);
        float target = side;
        if (side != 0)
        {
            Vector3 eye = ctx.transform.position + Vector3.up * (ctx.IsCrouching ? ctx.CrouchColliderHeight : ctx.StandingColliderHeight) * 0.85f;
            if (Physics.SphereCast(eye, 0.12f, ctx.transform.right * side, out RaycastHit hit,
                ctx.AbilitySettings.leanOffset + 0.08f, ctx.groundLayers, QueryTriggerInteraction.Ignore))
                target *= Mathf.Clamp01((hit.distance - 0.03f) / Mathf.Max(0.01f, ctx.AbilitySettings.leanOffset));
        }
        lean = Mathf.Lerp(lean, target, 1f - Mathf.Exp(-ctx.AbilitySettings.leanSharpness * dt));
        if (!allowed) reactionTime = 0f;
        if (reactionTime > 0f)
        {
            float slice = Mathf.Min(ctx.DodgePassThrough.MovementDelta(dt), reactionTime);
            reactionTime -= slice;
            ctx.Motor.AddDisplacement(reactionDirection * (ctx.AbilitySettings.microSuccessDistance * slice /
                Mathf.Max(0.02f, ctx.AbilitySettings.microSuccessDuration)));
        }
    }

    internal void OnDodgeSucceeded(DodgeKind kind, int side)
    {
        if (kind != DodgeKind.Micro || !ctx.Abilities.CanUseGroundActions) return;
        // Only a confirmed, intercepted attack reaches this callback. Pressing Q/E never translates the player.
        reactionDirection = Vector3.ProjectOnPlane(ctx.transform.right * side, Vector3.up).normalized;
        reactionTime = ctx.AbilitySettings.microSuccessDuration;
    }
    internal void Reset() { lean = reactionTime = 0f; }
}
