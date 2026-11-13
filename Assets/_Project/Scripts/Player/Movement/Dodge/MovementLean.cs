using UnityEngine;

/// <summary>
/// Q/E: the lean the key holds, and the slip a micro dodge makes when it catches a real blow (MicroSlip): out to just
/// clear the blow's line, held while it goes by, then back to the held lean. The body never moves; only the lean does,
/// so the camera, the body's bend, the upper body's hit volumes and the other PCs all follow it as they follow a lean.
/// </summary>
public sealed class MovementLean
{
    private readonly PlayerMovement ctx;
    // lean is what everything shows; held is where the key alone would have it.
    private float lean, held;
    private float slipTime = -1f, slipStart;
    private int slipSide, slipActivation;
    public float Roll => -lean * ctx.AbilitySettings.leanAngle;
    public float Offset => lean * ctx.AbilitySettings.leanOffset;
    /// <summary>How far the lean has gone, -1 to 1. Carried to the other PCs as it is.</summary>
    internal float Share { get => lean; set => lean = held = value; }
    internal MovementLean(PlayerMovement context) { ctx = context; }

    internal void Tick(float dt)
    {
        if (dt <= 0f || Time.timeScale <= 0f) return;
        bool allowed = ctx.Abilities.CanUseGroundActions && ctx.InputAvailable;
        if (!allowed && ctx.Abilities.Dodge.Kind == DodgeKind.Micro) ctx.Abilities.Dodge.CancelWindow();
        int side = allowed ? ctx.Input.LeanSide : 0;
        var dodge = ctx.Abilities.Dodge;
        // Returning to center (or reversing lean) is no longer a fresh evasive action.
        // Preserve a confirmed success; its slip may finish after key release.
        if (dodge.Kind == DodgeKind.Micro && !dodge.HasSucceeded && side != dodge.LeanSide)
            dodge.CancelWindow();
        if (side != 0 && ctx.Input.LeanPressed)
            ctx.Abilities.Dodge.TryStart(DodgeKind.Micro, side,
                ctx.AbilitySettings.microInvulnerability, ctx.AbilitySettings.dodgeCooldown);
        // Fresh defensive input replaces the slip instead of stacking two directions.
        if (!allowed || slipActivation != ctx.Abilities.Dodge.Activation) slipTime = -1f;
        held = Mathf.Lerp(held, CollisionLimitedLean(side), 1f - Mathf.Exp(-ctx.AbilitySettings.leanSharpness * dt));
        if (slipTime >= 0f) AdvanceSlip();
        else lean = held;
    }

    // Real time, so the slip is as quick inside the evade's slow motion; a pause (Tick's early return) still freezes it.
    private void AdvanceSlip()
    {
        var slip = MicroSlip.For(ctx);
        if (slip == null) { slipTime = -1f; lean = held; return; }
        slipTime += Mathf.Min(.05f, Time.unscaledDeltaTime);
        float room = Mathf.Abs(CollisionLimitedLean(slipSide));
        float clear = Mathf.Min(room, slip.Clearance / Mathf.Max(.01f, ctx.AbilitySettings.leanOffset));
        // Out at least as far as the slip needs, further if the key holds the lean further that way.
        float outward = slipSide * Mathf.Max(clear, held * slipSide);
        float back = slip.SlipSeconds + slip.HoldSeconds;
        if (slipTime < slip.SlipSeconds) lean = Mathf.Lerp(slipStart, outward, Ease.Out(slipTime / slip.SlipSeconds));
        else if (slipTime < back) lean = outward;
        else if (slipTime < back + slip.ReturnSeconds) lean = Mathf.Lerp(outward, held, Ease.InOut((slipTime - back) / slip.ReturnSeconds));
        else { lean = held; slipTime = -1f; }
    }

    internal void OnDodgeSucceeded(DodgeKind kind, int side)
    {
        if (kind != DodgeKind.Micro || side == 0 || !ctx.Abilities.CanUseGroundActions) return;
        // Only a confirmed, intercepted attack reaches this callback. Pressing Q/E alone never slips.
        slipSide = side;
        slipActivation = ctx.Abilities.Dodge.Activation;
        slipTime = 0f;
        slipStart = lean; // No camera teleport on the hit frame.
        MicroSlip.For(ctx).OrNull()?.Show(ctx, side);
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
    internal void Reset() { lean = held = slipStart = 0f; slipTime = -1f; slipSide = slipActivation = 0; }
}
