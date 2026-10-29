using UnityEngine;

/// <summary>Resolves a landing before legacy slide transitions. Fall damage bypasses dodge i-frames.</summary>
public sealed class MovementLanding
{
    private readonly PlayerMovement ctx;
    private float rollBuffer;
    private float impactSpeed;
    private bool trackingFall;
    private float highestFeetY;
    public float LastImpactSpeed { get; private set; }
    public float LastFallDistance { get; private set; }
    /// <summary>
    /// This fall is a long one: falling faster than bigFallMinSpeed with at least bigFallHeight between the top of the
    /// fall and the ground below (or no ground in reach). A jump across a gap never counts. Ends on touching down,
    /// catching hold or diving.
    /// </summary>
    public bool IsBigFall { get; private set; }
    internal MovementLanding(PlayerMovement context) { ctx = context; }

    internal void Tick(float dt)
    {
        rollBuffer = Mathf.Max(0f, rollBuffer - dt);
        if (!ctx.IsGrounded && ctx.Input.CrouchPressed) rollBuffer = ctx.AbilitySettings.rollInputWindow;
    }

    internal void BeforeMove()
    {
        if (!ctx.IsGrounded && !ctx.IsParkour)
        {
            float feetY = ctx.transform.TransformPoint(ctx.Motor.controller.center - Vector3.up * ctx.Motor.controller.height * 0.5f).y;
            if (!trackingFall) { highestFeetY = feetY; trackingFall = true; }
            highestFeetY = Mathf.Max(highestFeetY, feetY);
            impactSpeed = Mathf.Max(0f, -ctx.VerticalVelocity);
            WatchBigFall(feetY);
        }
        else IsBigFall = false;
    }

    private void WatchBigFall(float feetY)
    {
        var settings = ctx.AbilitySettings;
        if (ctx.IsDiving) { IsBigFall = false; return; }
        if (IsBigFall || -ctx.VerticalVelocity < settings.bigFallMinSpeed) return;
        float left = settings.bigFallHeight - (highestFeetY - feetY);
        Vector3 feet = ctx.transform.position;
        feet.y = feetY + .05f;
        IsBigFall = left <= 0f || !Physics.Raycast(feet, Vector3.down, left + .05f, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }

    internal void SupportedByTraversal() { impactSpeed = 0f; trackingFall = false; IsBigFall = false; }

    internal bool OnLanding()
    {
        LastImpactSpeed = impactSpeed;
        float feetY = ctx.transform.TransformPoint(ctx.Motor.controller.center - Vector3.up * ctx.Motor.controller.height * 0.5f).y;
        LastFallDistance = trackingFall ? Mathf.Max(0f, highestFeetY - feetY) : 0f;
        impactSpeed = 0f;
        trackingFall = IsBigFall = false;
        ctx.Abilities.Traversal.OnGrounded();

        float excess = LastImpactSpeed - ctx.AbilitySettings.safeFallSpeed;
        if (excess <= 0f || LastFallDistance <= ctx.AbilitySettings.safeFallDistance) { rollBuffer = 0f; return false; }

        Vector3 direction = ctx.Abilities.InputDirection(ctx.transform.forward);
        bool canRoll = !ctx.IsParkour && !ctx.IsDiving && !ctx.IsProning &&
            !ctx.HasCeilingAbove(ctx.CrouchColliderHeight) &&
            ctx.Abilities.Traversal.Detector.ClearCapsule(ctx.transform.position + direction * 0.6f, ctx.CrouchColliderHeight);

        if (rollBuffer > 0f && canRoll)
        {
            rollBuffer = 0f;
            ctx.Abilities.ActionDirection = direction;
            ctx.ChangeState(PlayerMovement.MovementState.LandingRoll);
            ctx.Abilities.InputConsumed = true;
            ctx.Actions.ClearJumpBuffer();

            // [카메라 연출 추가] 구르기 시작 시 피치(x축)를 강하게 앞으로 쏠리게 함
            ctx.GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(new Vector3(75f, 0f, 0f), 0f);

            return true;
        }
        rollBuffer = 0f;
        ctx.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),
            excess * ctx.AbilitySettings.fallDamagePerSpeed, DamageKind.Fall,
            ctx.transform.position, Vector3.up));
        return !ctx.DamageReceiver.IsAlive;
    }

    internal void Reset() { impactSpeed = rollBuffer = LastImpactSpeed = LastFallDistance = 0f; trackingFall = IsBigFall = false; }
}