using UnityEngine;

public sealed class SupineState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Supine;

    public SupineState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.SupineSpeed;

    public override void UpdatePhysics(float dt)
    {
        Vector2 input = ctx.CurrentInput;
        // 누운 자세는 오직 S(후진) 계열 입력만 물리 반영
        if (input.y > -0.01f)
        {
            input = Vector2.zero;
        }

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