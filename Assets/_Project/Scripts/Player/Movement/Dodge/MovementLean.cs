using UnityEngine;

public sealed class MovementLean
{
    private readonly PlayerMovement ctx;
    private float lean;
    private float reactionTime;
    private float reactionDuration, reactionStartLean;
    private Vector3 reactionDirection;
    private int reactionSide, reactionActivation;
    public float Roll => -lean * ctx.AbilitySettings.leanAngle;
    public float Offset => lean * ctx.AbilitySettings.leanOffset;
    internal MovementLean(PlayerMovement context) { ctx = context; }

    internal void Tick(float dt)
    {
        if (dt <= 0f || Time.timeScale <= 0f) return;
        bool allowed = ctx.Abilities.CanUseGroundActions && ctx.InputAvailable;
        if (!allowed && ctx.Abilities.Dodge.Kind == DodgeKind.Micro) ctx.Abilities.Dodge.CancelWindow();
        int side = allowed ? ctx.Input.LeanSide : 0;
        var dodge = ctx.Abilities.Dodge;
        // Returning to center (or reversing lean) is no longer a fresh evasive action.
        // Preserve a confirmed success; its short reaction may finish after key release.
        if (dodge.Kind == DodgeKind.Micro && !dodge.HasSucceeded && side != dodge.LeanSide)
            dodge.CancelWindow();
        if (side != 0 && ctx.Input.LeanPressed)
            ctx.Abilities.Dodge.TryStart(DodgeKind.Micro, side,
                ctx.AbilitySettings.microInvulnerability, ctx.AbilitySettings.dodgeCooldown);
        // Fresh defensive input replaces the old success step instead of stacking two directions.
        if (!allowed || reactionActivation != ctx.Abilities.Dodge.Activation) reactionTime = 0f;
        if (reactionTime > 0f) AdvanceReaction(dt);
        else lean = Mathf.Lerp(lean, CollisionLimitedLean(side), 1f - Mathf.Exp(-ctx.AbilitySettings.leanSharpness * dt));
    }

    private void AdvanceReaction(float dt)
    {
        if (reactionTime <= 0f || dt <= 0f || Time.timeScale <= 0f) return;
        // Only a confirmed micro-dodge reaction uses real time. Normal step movement
        // and defensive windows keep their game-time pacing; a pause still freezes both.
        float reactionDelta = Mathf.Min(.05f, dt / Mathf.Min(1f, Time.timeScale));
        float slice = Mathf.Min(reactionDelta, reactionTime);
        float before = 1f - reactionTime / reactionDuration;
        reactionTime -= slice;
        float after = 1f - reactionTime / reactionDuration;
        // Integrate the eased path, including the final partial frame, rather than
        // instantly starting/stopping a constant speed or snapping the camera pose.
        float travel = Mathf.SmoothStep(0f, 1f, after) - Mathf.SmoothStep(0f, 1f, before);
        ctx.Motor.AddDisplacement(reactionDirection * (ctx.AbilitySettings.microSuccessDistance * travel));
        float leanDuration = Mathf.Min(reactionDuration, Mathf.Max(.02f, ctx.AbilitySettings.microSuccessLeanDuration));
        float leanProgress = Mathf.Clamp01((reactionDuration - reactionTime) / leanDuration);
        lean = Mathf.Lerp(reactionStartLean, CollisionLimitedLean(reactionSide), Mathf.SmoothStep(0f, 1f, leanProgress));
    }

    internal void OnDodgeSucceeded(DodgeKind kind, int side)
    {
        if (kind != DodgeKind.Micro || side == 0 || !ctx.Abilities.CanUseGroundActions) return;
        // Only a confirmed, intercepted attack reaches this callback. Pressing Q/E never translates the player.
        reactionDirection = Vector3.ProjectOnPlane(ctx.transform.right * side, Vector3.up).normalized;
        reactionSide = side;
        reactionActivation = ctx.Abilities.Dodge.Activation;
        reactionTime = reactionDuration = Mathf.Max(.02f, ctx.AbilitySettings.microSuccessDuration);
        reactionStartLean = lean; // No camera teleport on the hit frame.
    }
    private float CollisionLimitedLean(int side)
    {
        float target = side;
        if (side == 0) return 0f;
        Vector3 eye = ctx.transform.position + Vector3.up * (ctx.IsCrouching ? ctx.CrouchColliderHeight : ctx.StandingColliderHeight) * .85f;
        if (Physics.SphereCast(eye, .12f, ctx.transform.right * side, out RaycastHit hit,
            ctx.AbilitySettings.leanOffset + .08f, ctx.groundLayers, QueryTriggerInteraction.Ignore))
            target *= Mathf.Clamp01((hit.distance - .03f) / Mathf.Max(.01f, ctx.AbilitySettings.leanOffset));
        return target;
    }
    internal void Reset() { lean = reactionTime = reactionDuration = reactionStartLean = 0f; reactionSide = reactionActivation = 0; }
}
