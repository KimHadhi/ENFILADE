using UnityEngine;

/// <summary>
/// Down after a landing that hurt, or thrown by a blow (Dynamic Landing): MovementLanding.HeavyPose says how, and
/// HeavyDuration how long until the player is up again. No control meanwhile; the skid along the ground dies away
/// (Ability.heavyLandingBraking). The view still turns, within PlayerBody's arc around the body.
/// </summary>
public sealed class HeavyLandingState : MovementBaseState
{
    public HeavyLandingState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override string Name => "HeavyLanding";
    public override MovementPosture Posture => MovementPosture.Crouching;
    public override void UpdateLogic(float dt)
    {
        if (!ctx.IsGrounded || ctx.CurrentStateDuration >= ctx.Abilities.Landing.HeavyDuration) ctx.Abilities.ReturnToLocomotion();
    }
    public override void UpdatePhysics(float dt)
    {
        ctx.HorizontalVelocity *= Mathf.Max(0f, 1f - ctx.AbilitySettings.heavyLandingBraking * dt);
        if (ctx.IsGrounded) ctx.VerticalVelocity = -2f;
    }
}
