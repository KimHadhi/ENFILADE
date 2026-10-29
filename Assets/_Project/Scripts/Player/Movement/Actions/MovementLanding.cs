using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// How hard each touchdown was, and what it does (numbers: Player.csv row Ability). Resolved before legacy slide
/// transitions.
/// - Light, slower than hardLandingSpeed (under ~3 m): nothing here; the body, the sounds and the view punch scale with it.
/// - Hard (Beatrun's hard land), from hardLandingSpeed on the feet: the run stops, the ground speed drops and eases back
///   over hardLandingRecoverTime, and Shift or a W run cannot build speed for hardLandingNoSprintTime. A landing slide
///   takes it instead.
/// - Heavy (Dynamic Landing), once the fall hurts (faster than safeFallSpeed after more than safeFallDistance): fall
///   damage, which bypasses dodge i-frames, then the HeavyLanding state takes the player down and up again without
///   control: onto hands and knees, flat on the face from a huge fall, or onto the back when moving backwards.
/// - Ctrl within rollInputWindow before a hard or heavy touchdown rolls instead: no damage, no slowdown, speed kept.
/// - KnockBack throws the player; they land heavily on their back facing the blow, hurt or not.
/// Also tracks the big fall (wind, heartbeat, warp, flailing).
/// </summary>
public sealed class MovementLanding
{
    public enum Tier { Light, Hard, Heavy }
    /// <summary>How a heavy landing goes down: onto hands and knees, flat on the face, or onto the back.</summary>
    public enum Pose { Front, FaceDown, Back }

    private readonly PlayerMovement ctx;
    private float rollBuffer;
    private float impactSpeed;
    private bool trackingFall;
    private float highestFeetY;
    private float recoverLeft, noSprintLeft, knockYaw;
    public float LastImpactSpeed { get; private set; }
    public float LastFallDistance { get; private set; }
    /// <summary>How hard the last touchdown was.</summary>
    public Tier LastTier { get; private set; }
    /// <summary>How the heavy landing under way goes down.</summary>
    public Pose HeavyPose { get; private set; }
    /// <summary>The way the body faces while it is down after a heavy landing (world yaw, degrees).</summary>
    public float HeavyYaw { get; private set; }
    /// <summary>How long the heavy landing under way keeps the player down (s).</summary>
    public float HeavyDuration
    {
        get
        {
            var settings = ctx.AbilitySettings;
            return HeavyPose == Pose.Back ? settings.backLandingTime
                : HeavyPose == Pose.FaceDown ? settings.faceDownLandingTime : settings.heavyLandingTime;
        }
    }
    /// <summary>Share of the usual ground speed while getting over a hard landing; 1 otherwise.</summary>
    public float RecoveryScale
    {
        get
        {
            var settings = ctx.AbilitySettings;
            if (recoverLeft <= 0f || settings.hardLandingRecoverTime <= 0f) return 1f;
            float share = 1f - recoverLeft / settings.hardLandingRecoverTime;
            return Mathf.Lerp(settings.hardLandingMoveScale, 1f, Mathf.SmoothStep(0f, 1f, share));
        }
    }
    /// <summary>Shift and a W run cannot build speed yet after a hard landing.</summary>
    public bool SprintBlocked => noSprintLeft > 0f;
    /// <summary>Thrown by a blow and not down yet.</summary>
    public bool IsKnockedBack { get; private set; }
    /// <summary>The player has no control: thrown by a blow, or down after a heavy landing.</summary>
    public bool LocksControl => IsKnockedBack || ctx.CurrentState == State.HeavyLanding;
    /// <summary>
    /// This fall is a long one: falling faster than bigFallMinSpeed with at least bigFallHeight between the top of the
    /// fall and the ground below (or no ground in reach). A jump across a gap never counts. Ends on touching down,
    /// catching hold or diving.
    /// </summary>
    public bool IsBigFall { get; private set; }

    internal MovementLanding(PlayerMovement context)
    {
        ctx = context;
        ctx.Landed += OnLanded;
    }

    internal void Tick(float dt)
    {
        rollBuffer = Mathf.Max(0f, rollBuffer - dt);
        recoverLeft = Mathf.Max(0f, recoverLeft - dt);
        noSprintLeft = Mathf.Max(0f, noSprintLeft - dt);
        if (!ctx.IsGrounded && ctx.Input.CrouchPressed) rollBuffer = ctx.AbilitySettings.rollInputWindow;
    }

    internal void BeforeMove()
    {
        if (!ctx.IsGrounded && !ctx.IsParkour)
        {
            float feetY = FeetY;
            if (!trackingFall) { highestFeetY = feetY; trackingFall = true; }
            highestFeetY = Mathf.Max(highestFeetY, feetY);
            impactSpeed = Mathf.Max(0f, -ctx.VerticalVelocity);
            WatchBigFall(feetY);
        }
        else IsBigFall = false;
    }

    private float FeetY => ctx.transform.TransformPoint(ctx.Motor.controller.center - Vector3.up * ctx.Motor.controller.height * 0.5f).y;

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

    internal void SupportedByTraversal() { impactSpeed = 0f; trackingFall = false; IsBigFall = IsKnockedBack = false; }

    internal bool OnLanding()
    {
        var settings = ctx.AbilitySettings;
        LastImpactSpeed = impactSpeed;
        LastFallDistance = trackingFall ? Mathf.Max(0f, highestFeetY - FeetY) : 0f;
        impactSpeed = 0f;
        trackingFall = IsBigFall = false;
        bool knocked = IsKnockedBack;
        IsKnockedBack = false;
        ctx.Abilities.Traversal.OnGrounded();

        bool hurts = LastImpactSpeed > settings.safeFallSpeed && LastFallDistance > settings.safeFallDistance;
        LastTier = hurts || knocked ? Tier.Heavy : LastImpactSpeed >= settings.hardLandingSpeed ? Tier.Hard : Tier.Light;
        bool rollPressed = rollBuffer > 0f;
        rollBuffer = 0f;
        if (LastTier == Tier.Light) return false;

        // Only a landing on the feet rolls or goes down; slides, dives and prone keep their own landings.
        bool onFeet = ctx.CurrentState == State.Standing || ctx.CurrentState == State.Crouching;
        if (onFeet && rollPressed && !knocked && TryRoll()) return true;
        if (hurts)
        {
            ctx.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),
                (LastImpactSpeed - settings.safeFallSpeed) * settings.fallDamagePerSpeed, DamageKind.Fall,
                ctx.transform.position, Vector3.up));
            if (!ctx.DamageReceiver.IsAlive) return true;
        }
        if (!onFeet || LastTier != Tier.Heavy) return false;
        GoDown(knocked);
        return true;
    }

    private bool TryRoll()
    {
        Vector3 direction = ctx.Abilities.InputDirection(ctx.transform.forward);
        if (ctx.HasCeilingAbove(ctx.CrouchColliderHeight) ||
            !ctx.Abilities.Traversal.Detector.ClearCapsule(ctx.transform.position + direction * 0.6f, ctx.CrouchColliderHeight))
            return false;
        ctx.Abilities.ActionDirection = direction;
        ctx.ChangeState(State.LandingRoll);
        ctx.Abilities.InputConsumed = true;
        ctx.Actions.ClearJumpBuffer();

        // [카메라 연출 추가] 구르기 시작 시 피치(x축)를 강하게 앞으로 쏠리게 함
        ctx.GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(new Vector3(75f, 0f, 0f), 0f);

        return true;
    }

    // Down after a heavy landing, facing the view (or the blow): backwards onto the back, a huge fall flat on the face,
    // otherwise onto hands and knees.
    private void GoDown(bool knocked)
    {
        var settings = ctx.AbilitySettings;
        Vector3 travel = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        bool backward = knocked || (travel.magnitude > settings.backLandingMinSpeed && Vector3.Dot(travel, ctx.transform.forward) < 0f);
        HeavyPose = backward ? Pose.Back : LastImpactSpeed > settings.faceDownLandingSpeed ? Pose.FaceDown : Pose.Front;
        HeavyYaw = knocked ? knockYaw : ctx.transform.eulerAngles.y;
        recoverLeft = noSprintLeft = 0f;
        ctx.ChangeState(State.HeavyLanding);
        ctx.Abilities.InputConsumed = true;
        ctx.Actions.ClearJumpBuffer();
    }

    // Raised once the state has had its say, so a landing slide that took a hard landing keeps its speed.
    private void OnLanded(float impact)
    {
        if (LastTier != Tier.Hard || (ctx.CurrentState != State.Standing && ctx.CurrentState != State.Crouching)) return;
        recoverLeft = ctx.AbilitySettings.hardLandingRecoverTime;
        noSprintLeft = ctx.AbilitySettings.hardLandingNoSprintTime;
    }

    /// <summary>Throws the player at this velocity (a big enemy's blow); see PlayerMovement.KnockBack.</summary>
    internal void KnockBack(Vector3 velocity)
    {
        Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        knockYaw = flat.sqrMagnitude > .01f ? Mathf.Atan2(-flat.x, -flat.z) * Mathf.Rad2Deg : ctx.transform.eulerAngles.y;
        if (ctx.CurrentState != State.Standing && ctx.CurrentState != State.Crouching) ctx.Abilities.ReturnToLocomotion();
        ctx.Actions.Momentum.Stop();
        ctx.HorizontalVelocity = flat;
        rollBuffer = recoverLeft = noSprintLeft = 0f;
        if (velocity.y <= 0f && ctx.IsGrounded)
        {
            // Thrown along the ground: down at once, skidding the way it was thrown.
            LastTier = Tier.Heavy;
            GoDown(true);
            return;
        }
        ctx.VerticalVelocity = velocity.y;
        ctx.Actions.LeaveGround();
        trackingFall = false;
        IsKnockedBack = true;
    }

    internal void Reset()
    {
        impactSpeed = rollBuffer = LastImpactSpeed = LastFallDistance = recoverLeft = noSprintLeft = 0f;
        trackingFall = IsBigFall = IsKnockedBack = false;
        LastTier = Tier.Light;
    }
}
