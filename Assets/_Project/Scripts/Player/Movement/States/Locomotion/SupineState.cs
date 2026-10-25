using UnityEngine;

public sealed class SupineState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Supine;

    public SupineState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.SupineSpeed;

    public override void ProcessInput(Vector2 input)
    {
        // Keep the same controls after the slide comes to rest.
        if (input.y > 0.1f && ctx.IsGrounded && TryJump()) return;
        base.ProcessInput(input);
    }

    public override void UpdatePhysics(float dt)
    {
        Vector2 input = ctx.CurrentInput;
        // W is reserved for getting up. A/D and S move while supine.
        input.y = Mathf.Min(input.y, 0f);

        float inputMagnitude = Mathf.Clamp01(input.magnitude);
        Vector3 inputDirection = (ctx.transform.right * input.x + ctx.transform.forward * input.y).normalized;
        float lateralWeight = Mathf.Abs(input.x);
        float visualBoost = Mathf.Lerp(1f, ctx.StrafeCompensation, lateralWeight);

        float finalSpeed = LocomotionSpeed * visualBoost;
        Vector3 targetVelocity = inputDirection * (finalSpeed * inputMagnitude);

        float t = 1f - Mathf.Exp(-ctx.GroundSharpness * dt);
        ctx.HorizontalVelocity = Vector3.Lerp(ctx.HorizontalVelocity, targetVelocity, t);

        if (ctx.HorizontalVelocity.sqrMagnitude < 0.0001f)
        {
            ctx.HorizontalVelocity = Vector3.zero;
        }
    }
}
