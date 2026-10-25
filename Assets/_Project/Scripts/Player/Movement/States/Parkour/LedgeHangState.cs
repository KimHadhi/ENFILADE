using UnityEngine;

/// <summary>Persistent support: catching never implicitly commits to climbing over.</summary>
public sealed class LedgeHangState : MovementBaseState
{
    private bool forwardWasHeld, requestMantle;
    private bool requestJump;
    private Vector3 catchStart;
    public LedgeHangState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override void Enter()
    {
        catchStart = ctx.transform.position;
        forwardWasHeld = ctx.CurrentInput.y > 0.1f;
        requestMantle = requestJump = false;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Actions.ClearJumpBuffer();
        ctx.Abilities.Landing.SupportedByTraversal();
    }
    public override void ProcessInput(Vector2 input)
    {
        if (!ctx.InputAvailable || ctx.Input.CrouchPressed) { Drop(); return; }
        bool forward = input.y > 0.1f;
        bool facing = ctx.Abilities.Traversal.FacingLedge();
        // Looking away prepares a directional jump, never a delayed mantle.
        if (facing) requestJump = false;
        else requestMantle = false;
        if (ctx.Input.JumpPressed && !facing) { requestJump = true; requestMantle = false; }
        else requestMantle |= facing && (ctx.Input.JumpPressed || (forward && !forwardWasHeld));
        forwardWasHeld = forward;
    }
    public override void UpdateLogic(float dt)
    {
        var ledge = ctx.Abilities.Traversal.Ledge;
        if (ledge.Surface == null || !ledge.Surface.enabled || !ledge.Surface.gameObject.activeInHierarchy) { Drop(); return; }
        if (requestJump && ctx.CurrentStateDuration >= ctx.AbilitySettings.ledgeCatchDuration)
        { ctx.Abilities.Traversal.JumpFromLedge(); return; }
        if (requestMantle && ctx.CurrentStateDuration >= ctx.AbilitySettings.ledgeCatchDuration)
        {
            requestMantle = false;
            ctx.Abilities.Traversal.MantleFromLedge();
        }
    }
    public override void UpdatePhysics(float dt)
    {
        var traversal = ctx.Abilities.Traversal;
        if (traversal.Ledge.Surface == null) { Drop(); return; }
        if (traversal.FacingLedge() && ctx.CurrentStateDuration >= ctx.AbilitySettings.ledgeCatchDuration
            && Mathf.Abs(ctx.CurrentInput.x) > 0.1f)
        {
            Vector3 normal = traversal.Ledge.Surface.transform.TransformDirection(traversal.Ledge.LocalNormal);
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            float remaining = ctx.CurrentInput.x * ctx.AbilitySettings.ledgeShimmySpeed * dt;
            // Small probes stop at missing edges rather than crossing a gap in one frame.
            int slices = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(remaining) / 0.05f));
            for (int i = 0; i < slices; i++)
            {
                if (!traversal.Detector.TryShimmy(traversal.Ledge, right * (remaining / slices), out LedgeTarget nextLedge)) break;
                traversal.Ledge = nextLedge;
            }
        }
        Vector3 hang = traversal.Ledge.Surface.transform.TransformPoint(traversal.Ledge.LocalHang);
        float t = Mathf.Clamp01((ctx.CurrentStateDuration + dt) / ctx.AbilitySettings.ledgeCatchDuration);
        Vector3 next = Vector3.Lerp(catchStart, hang, Mathf.SmoothStep(0f, 1f, t));
        if (!traversal.Detector.ClearPath(ctx.transform.position, next, ctx.StandingColliderHeight)) { Drop(); return; }
        ctx.Motor.RequestDisplacement(next - ctx.transform.position);
    }
    private void Drop()
    {
        ctx.Abilities.Traversal.LockLedge();
        ctx.Abilities.ReturnToLocomotion();
        ctx.VerticalVelocity = -2f;
    }
}

public sealed class LedgeJumpState : MovementBaseState
{
    public LedgeJumpState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override bool CanApplyGravity => true;
    public override void Enter()
    {
        Vector3 velocity = ctx.Abilities.Traversal.LedgeJumpVelocity;
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
        ctx.Actions.LeaveGround(); ctx.Actions.ClearJumpBuffer();
    }
    public override void UpdateLogic(float dt)
    { if (ctx.CurrentStateDuration > 0f) ctx.Abilities.ReturnToLocomotion(); }
}
