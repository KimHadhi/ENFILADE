using UnityEngine;

/// <summary>
/// Q/E: the lean the key holds, and the slip a micro dodge makes when it catches a real blow (MicroSlip): all the way over,
/// held while the blow goes by, then back to the held lean. The body never moves; only the lean does,
/// so the camera, the body's bend, the upper body's hit volumes and the other PCs all follow it as they follow a lean.
/// </summary>
public sealed class MovementLean
{
    private readonly PlayerMovement ctx;
    // lean is what everything shows; held is where the key alone would have it, going at heldPace (a second).
    // As Rainbow Six's does (the user, 2026-11-23: "기울이기는 최신 레식처럼"), it sets off and stops smoothly, turns back from where
    // it is and goes straight over to the other side, no faster from side to side than from upright. Until 0.9.356 it set off at
    // its fastest in one frame, 3.9 m/s of head from side to side, some 24 g, and the hair was thrown about; now 1.6 m/s and 4 g,
    // one side in as long as it was (90 % in 0.23 s) and the other in 0.30 s.
    // Once begun it goes on most of the way to its side (toward) before the key turns it (Ability.leanGoes), so keys tapped over
    // and over lean it over and back smoothly, where they shook it 3 cm fourteen times a second (the user, 2026-11-23: "한번
    // 기울이거나 앉기 시작했으면 좀 진행하도록 ... 부드럽고 자연스럽게 다시 복귀해서 돌아오고 반대편으로 갈 수 있게").
    private float lean, held, heldPace;
    private int toward;
    private float slipTime = -1f, slipStart;
    private int slipSide, slipActivation;
    public float Roll => -lean * ctx.AbilitySettings.leanAngle;
    public float Offset => lean * Reach;
    /// <summary>How far a full lean takes the eyes sideways (m), as the body's lean says (BodyLean).</summary>
    internal float Reach { get; set; } = .2f;
    /// <summary>How far the lean has gone, -1 to 1. Carried to the other PCs as it is.</summary>
    internal float Share { get => lean; set { lean = held = value; heldPace = 0f; } }
    /// <summary>
    /// The console's /lean holds the lean here while it measures the feet (LeanProbe): the keys, the walls and a slip are
    /// left out while it is set, and the lean goes back to the keys once it is cleared. Held here, where the lean is
    /// worked out, so everything that reads the lean this frame reads the same one.
    /// </summary>
    internal float? Probed;
    internal MovementLean(PlayerMovement context) { ctx = context; }

    internal void Tick(float dt)
    {
        if (dt <= 0f || Time.timeScale <= 0f) return;
        if (Probed.HasValue) { Share = Mathf.Clamp(Probed.Value, -1f, 1f); slipTime = -1f; return; }
        // What the body can do (on its feet, not in a move of its own). Whose the keys are is not asked here: with them
        // taken, a held lean already reads as let go and a toggled one stays (MovementInput), and a slip under way out
        // of a blow's line is not thrown away because the chat opened.
        bool allowed = ctx.Abilities.CanUseGroundActions;
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
        float time = ctx.AbilitySettings.leanTime, target = CollisionLimitedLean(toward);
        if (side != toward && (!allowed || Mathf.Abs(held - target) <= 1f - ctx.AbilitySettings.leanGoes))
            target = CollisionLimitedLean(toward = side);
        held = Mathf.SmoothDamp(held, target, ref heldPace, time, 1f / time, dt);
        if (slipTime >= 0f) AdvanceSlip();
        else lean = held;
    }

    // Real time, so the slip is as quick inside the evade's slow motion; a pause (Tick's early return) still freezes it.
    private void AdvanceSlip()
    {
        var slip = MicroSlip.For(ctx);
        if (slip == null) { slipTime = -1f; lean = held; return; }
        slipTime += Mathf.Min(.05f, Time.unscaledDeltaTime);
        // All the way over, as far as a wall lets it.
        float outward = slipSide * Mathf.Max(Mathf.Abs(CollisionLimitedLean(slipSide)), held * slipSide);
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
            Reach + .08f, ctx.groundLayers, QueryTriggerInteraction.Ignore))
            target *= Mathf.Clamp01((hit.distance - .03f) / Mathf.Max(.01f, Reach));
        return target;
    }
    internal void Reset() { Share = slipStart = 0f; slipTime = -1f; toward = slipSide = slipActivation = 0; }
}
