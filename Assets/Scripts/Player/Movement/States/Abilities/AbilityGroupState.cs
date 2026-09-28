using UnityEngine;

public sealed class AbilityGroupState : MovementBaseState
{
    private readonly string label;
    private readonly bool gravity;
    public AbilityGroupState(PlayerMovement ctx, MovementBaseState parent, string name, bool applyGravity = true) : base(ctx, parent)
    { label = name; gravity = applyGravity; }
    public override string Name => label;
    public override bool CanApplyGravity => gravity;
    public override bool ControlsTraversal => label == "Parkour" || base.ControlsTraversal;
    public override bool AllowsAutomaticPosture => false;
    public override bool AllowsBufferedJump => false;
    public override bool TryJump() => false;
    public override void OnLanding() { }
    public override void Enter()
    {
        ctx.Actions.ClearJumpBuffer();
        if (ctx.Posture.airBodyTucked || ctx.Posture.airCrouching) ctx.Posture.RebaseAirCrouchFeet();
        if (label == "Parkour") ctx.Abilities.Dodge.CancelWindow();
    }
}
