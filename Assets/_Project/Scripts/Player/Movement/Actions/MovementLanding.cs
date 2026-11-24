using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>
/// How hard each touchdown was, and what it does (numbers: Player.csv row Ability). Resolved before legacy slide
/// transitions.
/// - Light, a drop of less than hardLandingHeight (a storey of chapter 1's flats, 3 m, is under it): nothing here; the
///   body, the sounds and the view punch scale with it, and crouched it is a quiet landing (PlayerMovementAudio).
/// - Hard (Beatrun's hard land), from the speed such a drop lands at (HardSpeed) on the feet: the run stops, the ground speed drops and eases back
///   over hardLandingRecoverTime, and Shift or a W run cannot build speed for hardLandingNoSprintTime. A landing slide
///   takes it instead.
/// - Heavy (Dynamic Landing), once the fall hurts (faster than safeFallSpeed after more than safeFallDistance): fall
///   damage, which bypasses dodge i-frames, then the HeavyLanding state takes the player down and up again without
///   control: onto hands and knees, flat on the face from a huge fall, or onto the back when moving backwards.
/// - Ctrl within rollInputWindow before a hard or heavy touchdown rolls instead: no damage, no slowdown, speed kept. It goes
///   on one speed contract (RollSpeed): its clip's smoothed pace (ClipMotion.GlideAt), scaled to the speed it came in with
///   until its legs are under it, its run-out played at the rate that brings its pace to the gait the keys ask for; as its body
///   begins to rise the keys choose again how it ends (Choose): running on, down into a crouch, or standing up.
///   Slower than rollLandSpeed it lands on the spot for its clip's length instead, as Beatrun's safety roll does, and still
///   takes rollInPlaceDamageShare of the damage. A fall that
///   turned into a big fall can't be rolled: in
///   Beatrun the panic starts at the speed where no roll saves you.
/// - KnockBack throws the player; they land heavily on their back facing the blow, hurt or not.
/// Also tracks the big fall (wind, heartbeat, warp, flailing).
/// </summary>
public sealed class MovementLanding
{
    public enum Tier { Light, Hard, Heavy }
    /// <summary>A roll: one that stands up out of itself, a landing on the spot, one that runs on out of itself, a slide's jump
    /// that stands up and one that runs on. The pose sends it as it is numbered (MovementMirror).</summary>
    public enum Roll { StandsUp, OnTheSpot, RunsOn, Dive, DiveRunsOn }
    /// <summary>How a heavy landing goes down: onto hands and knees, flat on the face, or onto the back.</summary>
    public enum Pose { Front, FaceDown, Back }

    private readonly PlayerMovement ctx;
    private float rollBuffer;
    private float impactSpeed;
    private bool trackingFall, panicked;
    private float fallDistance, fallSpeedSquared, pendingDownwardSpeed;
    private float recoverLeft, noSprintLeft, knockYaw;
    public float LastImpactSpeed { get; private set; }
    public float LastFallDistance { get; private set; }
    /// <summary>Downward speed justified by real controller travel, for fall poses, sounds and impacts (m/s).</summary>
    public float FallSpeed => trackingFall ? impactSpeed : 0f;
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
                : HeavyPose == Pose.FaceDown ? settings.faceDownLandingTime : ctx.MotionOf(settings.heavyLand)?.Length ?? PlainDown;
        }
    }
    // Down this long where the body has no clip of its own for it (s).
    private const float PlainDown = 1.5f, PlainRoll = 1f;
    /// <summary>How fast and which way the feet met the ground (m/s): what the landing's clip is chosen by (PlayerBody.LandingFor).</summary>
    public Vector3 Touchdown { get; private set; }
    /// <summary>Share of the usual ground speed while getting over a hard landing; 1 otherwise. From the frame after the feet met
    /// the ground: in that one the body picks the landing's clip, which may run on at a pace of its own. Slowed already in it,
    /// the clip was chosen at a speed the frame's length decided: a gun's sprint took a jog's landing at 60 frames a second
    /// (0.9.351).</summary>
    public float RecoveryScale
    {
        get
        {
            var settings = ctx.AbilitySettings;
            if (ctx.LandedThisFrame || recoverLeft <= 0f || settings.hardLandingRecoverTime <= 0f) return 1f;
            float share = 1f - recoverLeft / settings.hardLandingRecoverTime;
            return Mathf.Lerp(settings.hardLandingMoveScale, 1f, Mathf.SmoothStep(0f, 1f, share));
        }
    }
    /// <summary>Shift and a W run cannot build speed yet after a hard landing.</summary>
    public bool SprintBlocked => noSprintLeft > 0f;
    /// <summary>The roll under way, and its clip (the PlayerBody profile's name).</summary>
    public Roll RollKind { get; private set; }
    public string RollClip { get; private set; } = "";
    /// <summary>The roll's clip's moment now (s): the body plays its clip there (PlayerBody), the other PCs get it with the pose.</summary>
    public float RollMoment => rollTime;
    /// <summary>The roll ends down in a crouch where its legs come under it (Ctrl held as its body begins to rise: Choose).</summary>
    public bool RollCrouches { get; private set; }
    /// <summary>The roll is over: a moving one that runs on at its first footfall once its legs are under it (where the walk or
    /// run takes its stride: PlayerBody.HasStepped), one that goes down into a crouch where its legs are under it, one that stands
    /// up once its body is back up, a landing on the spot at its clip's end.</summary>
    public bool RollOver => rollTime >= RollEndAt;
    /// <summary>How long into the roll its body meets the ground (s).</summary>
    public float RollDown => RollMotion != null ? RollMotion.Down - rollFrom : 0f;
    /// <summary>A slide's jump, either way it ends.</summary>
    public bool Dives => RollKind == Roll.Dive || RollKind == Roll.DiveRunsOn;
    /// <summary>Rolling over out of a slide's jump.</summary>
    public bool DiveRolls => ctx.CurrentState == State.LandingRoll && Dives;
    private ClipMotion RollMotion => ctx.MotionOf(RollClip);
    // The way the roll goes from the way the body faced as it began (degrees), and whether the keys have had their late choice.
    private float rollHeading;
    private bool rollChose;
    // The roll on its clip: the moment it began at and its moment now (s), the share of the clip's pace it goes at as it came
    // in (from what it landed at to 1 by the clip's LegsUnder: no planted foot slides), and the rate its run-out plays at (from
    // 1 at the clip's Rises to this by its LegsUnder).
    private float rollFrom, rollTime, rollIn = 1f, rollRate = 1f;
    // A copy's roll moment goes on between poses at the clip's pace, and is set again by one further off than this (s).
    private const float MirrorSlack = .15f;
    /// <summary>The roll under way runs on out of itself (a landing's, or a slide's jump).</summary>
    public bool RunsOut => RollKind == Roll.RunsOn || RollKind == Roll.DiveRunsOn;
    // Where in its clip the roll ends (s): see RollOver.
    private float RollEndAt
    {
        get
        {
            var motion = RollMotion;
            if (motion == null) return rollFrom + PlainRoll;
            if (RollKind == Roll.OnTheSpot) return motion.Length;
            if (RollCrouches) return motion.LegsUnder;
            return RunsOut && motion.StepAfter(motion.LegsUnder, out float step) ? step : motion.Up;
        }
    }
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

    // A move carries the player, except the drop at the end of a vault, which is a fall like any other.
    private bool Carried => ctx.IsParkour && !(ctx.StateMachine.Current is ObstacleTraversalState vault && vault.IsLanding);

    internal void BeforeMove()
    {
        if (ctx.IsGrounded || Carried)
        {
            ClearFall();
            return;
        }
        if (trackingFall) return;
        trackingFall = true;
        fallDistance = impactSpeed = 0f;
        // A bar/zipline release can already be moving down. Carry only the part
        // proven by the preceding movement, plus explicit downward knockback.
        float inheritedSpeed = Mathf.Min(Mathf.Max(0f, -ctx.VerticalVelocity), Mathf.Max(0f, -ctx.ActualVelocity.y));
        float startSpeed = Mathf.Max(pendingDownwardSpeed, inheritedSpeed);
        fallSpeedSquared = startSpeed * startSpeed;
        pendingDownwardSpeed = 0f;
    }

    // Integrate fall energy over distance actually travelled, not time spent airborne.
    // The final frame can be stopped by the floor: its travelled distance contributes
    // without replacing the impact speed with a near-zero movement sample.
    internal void AfterMove(float dt, float movedY)
    {
        if (!trackingFall || dt <= 0f || Carried) return;
        fallDistance = Mathf.Max(0f, fallDistance - movedY);
        if (ctx.VerticalVelocity > 0f)
        {
            fallSpeedSquared = impactSpeed = 0f;
            IsBigFall = false;
            return;
        }
        float gravity = Mathf.Abs(ctx.gravity * (ctx.IsDiving ? ctx.diveGravityScale : 1f));
        fallSpeedSquared = Mathf.Max(0f, fallSpeedSquared - 2f * gravity * movedY);
        float requestedSpeed = Mathf.Max(0f, -ctx.VerticalVelocity);
        float drop = Mathf.Max(0f, -movedY);
        bool touchedFloor = ctx.Motor.controller.isGrounded || (ctx.Motor.lastMoveFlags & CollisionFlags.Below) != 0;
        if (!touchedFloor && requestedSpeed * dt > 0.001f && drop < requestedSpeed * dt * 0.25f)
        {
            // A rim or wall is arresting the fall. Do not bank gravity while stuck,
            // and start the remaining fall here rather than at an old apex.
            float travelledSpeed = drop / dt;
            fallSpeedSquared = Mathf.Min(fallSpeedSquared, travelledSpeed * travelledSpeed);
            fallDistance = drop;
            IsBigFall = false;
        }
        impactSpeed = Mathf.Min(requestedSpeed, Mathf.Sqrt(fallSpeedSquared));
        if (touchedFloor) IsBigFall = false;
        else WatchBigFall(FeetY);
    }

    private void ClearFall()
    {
        impactSpeed = fallDistance = fallSpeedSquared = pendingDownwardSpeed = 0f;
        trackingFall = IsBigFall = panicked = false;
    }

    private float FeetY => ctx.transform.TransformPoint(ctx.Motor.controller.center - Vector3.up * ctx.Motor.controller.height * 0.5f).y;

    private void WatchBigFall(float feetY)
    {
        var settings = ctx.AbilitySettings;
        if (ctx.IsDiving) { IsBigFall = false; return; }
        if (IsBigFall || impactSpeed < settings.bigFallMinSpeed) return;
        float left = settings.bigFallHeight - fallDistance;
        Vector3 feet = ctx.transform.position;
        feet.y = feetY + .05f;
        IsBigFall = left <= 0f || !Physics.Raycast(feet, Vector3.down, left + .05f, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        // Kept to the touchdown even if something slows the fall after the panic began.
        if (IsBigFall) panicked = true;
    }

    internal void SupportedByTraversal() { ClearFall(); IsKnockedBack = false; }

    /// <summary>
    /// The speed a plain drop of hardLandingHeight lands at (m/s): the hard landing's threshold as the table gives it,
    /// in metres, turned into the speed the touchdown is judged by.
    /// </summary>
    public float HardSpeed => SpeedOfDrop(ctx.AbilitySettings.hardLandingHeight, Mathf.Abs(ctx.gravity));

    /// <summary>The speed a body dropped from rest reaches after falling this far (m/s).</summary>
    public static float SpeedOfDrop(float height, float gravity) => Mathf.Sqrt(2f * Mathf.Max(0f, gravity) * Mathf.Max(0f, height));

    /// <summary>
    /// How fast the feet meet the ground, given how fast the capsule's bottom did and how far the legs were tucked up
    /// (m/s). Tucked in the air the capsule's bottom rides that much above where the feet would hang, so it falls that
    /// much further before it touches; the feet would have met the ground that much sooner and slower. Without this a
    /// crouch pressed in the air made every drop count 0.85 m higher than it was: a storey's drop became a hard landing,
    /// and a fall just short of hurting hurt.
    /// </summary>
    public static float FeetImpact(float capsuleImpact, float tuck, float gravity)
        => Mathf.Sqrt(Mathf.Max(0f, capsuleImpact * capsuleImpact - 2f * Mathf.Max(0f, gravity) * Mathf.Max(0f, tuck)));

    /// <summary>How hard a touchdown at this speed is, hurting or thrown aside: for whoever asks without landing (the console's /drop).</summary>
    public Tier TierOf(float feetImpact, float fallDistance)
    {
        var settings = ctx.AbilitySettings;
        bool hurts = feetImpact > settings.safeFallSpeed && fallDistance > settings.safeFallDistance;
        return hurts ? Tier.Heavy : feetImpact >= HardSpeed ? Tier.Hard : Tier.Light;
    }

    /// <summary>How far the legs were tucked up when the last touchdown came (m), and the capsule's own speed then.</summary>
    public float LastTuck { get; private set; }
    public float LastCapsuleImpact { get; private set; }

    /// <param name="tuck">How far an air crouch had drawn the capsule's bottom up above the feet as it touched down (m).</param>
    internal bool OnLanding(float tuck = 0f)
    {
        var settings = ctx.AbilitySettings;
        Touchdown = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        // Judged where the feet were, not where the tucked capsule's bottom was (FeetImpact).
        float gravity = Mathf.Abs(ctx.gravity * (ctx.IsDiving ? ctx.diveGravityScale : 1f));
        LastTuck = tuck;
        LastCapsuleImpact = impactSpeed;
        LastImpactSpeed = FeetImpact(impactSpeed, tuck, gravity);
        LastFallDistance = trackingFall ? Mathf.Max(0f, fallDistance - tuck) : 0f;
        bool bigFall = panicked;
        ClearFall();
        bool knocked = IsKnockedBack;
        IsKnockedBack = false;
        ctx.Abilities.Traversal.OnGrounded();

        bool hurts = LastImpactSpeed > settings.safeFallSpeed && LastFallDistance > settings.safeFallDistance;
        LastTier = knocked ? Tier.Heavy : TierOf(LastImpactSpeed, LastFallDistance);
        bool rollPressed = rollBuffer > 0f;
        rollBuffer = 0f;
        if (LastTier == Tier.Light) return false;

        // Only a landing on the feet rolls or goes down; slides, dives and prone keep their own landings.
        bool onFeet = ctx.CurrentState == State.Standing || ctx.CurrentState == State.Crouching;
        float damage = (LastImpactSpeed - settings.safeFallSpeed) * settings.fallDamagePerSpeed;
        if (onFeet && rollPressed && !knocked && !bigFall && TryRoll())
        {
            if (hurts && RollKind == Roll.OnTheSpot) TakeFallDamage(damage * settings.rollInPlaceDamageShare);
            return true;
        }
        if (hurts && !TakeFallDamage(damage)) return true;
        if (!onFeet || LastTier != Tier.Heavy) return false;
        GoDown(knocked);
        return true;
    }

    /// <summary>Into a wall sliding fast (SlidingState.HandleWallCollision): down onto hands and knees, facing the wall.</summary>
    internal void Slam()
    {
        LastTier = Tier.Heavy;
        HeavyPose = Pose.Front;
        HeavyYaw = ctx.transform.eulerAngles.y;
        recoverLeft = noSprintLeft = 0f;
        ctx.ChangeState(State.HeavyLanding);
        ctx.Abilities.InputConsumed = true;
        ctx.Actions.ClearJumpBuffer();
    }

    /// <summary>Into a wall sliding at a run's speed (SlidingState.HandleWallCollision): a hard landing's slowdown follows.</summary>
    internal void Stumble()
    {
        recoverLeft = ctx.AbilitySettings.hardLandingRecoverTime;
        noSprintLeft = ctx.AbilitySettings.hardLandingNoSprintTime;
    }

    /// <summary>Hurts the player; false when that killed them.</summary>
    private bool TakeFallDamage(float amount)
    {
        if (amount <= 0f) return ctx.DamageReceiver.IsAlive;
        ctx.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Fall, ctx.transform.position, Vector3.up));
        return ctx.DamageReceiver.IsAlive;
    }

    private bool TryRoll()
    {
        // The way the keys ask, else the way the body comes down going: a fall backwards rolls backwards (Roll_B). Until 0.9.392
        // holding S landed on the spot (Beatrun's rule) and a roll with no keys went the way the body faced: falling backwards
        // at 4.8 m/s with S held and rolling, the body stood still for Land_1h's whole length (걸음 기록 of 2026-11-24 16:31; the
        // user: "뒤로 떨어지면서 랜딩롤 시도하면 걍 가만히 멈추는데").
        Vector3 travel = Vector3.ProjectOnPlane(ctx.HorizontalVelocity, Vector3.up);
        Vector3 direction = ctx.Abilities.InputDirection(travel.sqrMagnitude > .01f ? travel : ctx.transform.forward);
        bool inPlace = travel.magnitude < ctx.AbilitySettings.rollLandSpeed;
        if (ctx.HasCeilingAbove(ctx.CrouchColliderHeight)) return false;
        // Backlog 155: a wall where the roll would go used to cancel the roll and leave a plain crouched landing. Now it
        // rolls on the spot, so the view and the body play the same move.
        if (!inPlace && !ctx.Abilities.Traversal.Detector.ClearCapsule(ctx.transform.position + direction * 0.6f, ctx.CrouchColliderHeight))
            inPlace = true;
        // A moving roll goes at its clip's own travel and pace (0.9.288). Until then it went at the landing's speed for a second
        // with its clip squeezed into that: Roll_F (1.47 s) played half as fast again as it was made (the user, 2026-11-21:
        // "착지할 때 dive roll 모션이 존나 빨라"). And until 0.9.352 at the forward clip's whatever way it rolled.
        var settings = ctx.AbilitySettings;
        float speed = ctx.HorizontalVelocity.magnitude;
        SetRoll(inPlace ? Roll.OnTheSpot : speed >= settings.rollRunSpeed ? Roll.RunsOn : Roll.StandsUp, HeadingOf(direction, ctx.transform.eulerAngles.y));
        if (!inPlace && RollMotion == null) SetRoll(Roll.OnTheSpot, 0f);
        StartRoll(0f, speed);
        ctx.Abilities.ActionDirection = direction;
        ctx.ChangeState(State.LandingRoll);
        ctx.Abilities.InputConsumed = true;
        ctx.Actions.ClearJumpBuffer();
        // No camera kick: the roll's head turns the view over (BodyGaze). Until 0.9.286 a 75-degree pitch spring stood in
        // for it.
        return true;
    }

    /// <summary>
    /// A slide's jump (SlidingState): the body throws itself on the way it slides, rolls over and is up on its feet (BIPER Female
    /// Runner's DiveRoll_F; the user, 2026-11-22: "슬라이딩 점프는 female runner escape 모션의 다이브 롤 있으니까 다이브 롤 후
    /// 서기로 바꿔"), or with the keys held on ahead runs on out of it (DiveRollToRun_F, Ability.diveRollRun; the user, 2026-11-24,
    /// after the experts' review: "Include dive roll"), chosen again as its body begins to rise (Choose). The roll state carries
    /// it as it does a landing's roll; the body keeps facing the way it went (PlayerBody.FaceRoll). Until 0.9.335 the jump was a
    /// dive through the air onto the belly. False in the air, under a roof too low to roll, against a wall, or with no travel.
    /// </summary>
    internal bool TryDiveRoll(Vector3 direction)
    {
        if (!ctx.IsGrounded || ctx.HasCeilingAbove(ctx.CrouchColliderHeight)) return false;
        // A wall right ahead; a step or a slope up the capsule goes over is none.
        Vector3 ahead = ctx.transform.position + direction * 0.6f
            + Vector3.up * (ctx.Posture.defaultStepOffset * Mathf.Abs(ctx.transform.lossyScale.y));
        if (!ctx.Abilities.Traversal.Detector.ClearCapsule(ahead, ctx.CrouchColliderHeight)) return false;
        Roll before = RollKind;
        string clip = RollClip;
        SetRoll(GoesOn ? Roll.DiveRunsOn : Roll.Dive, 0f);
        if (RollMotion == null)
        {
            (RollKind, RollClip) = (before, clip);
            return false;
        }
        StartRoll(RollMotion.BurstTo, ctx.HorizontalVelocity.magnitude);
        ctx.Abilities.ActionDirection = direction;
        ctx.ChangeState(State.LandingRoll);
        return true;
    }

    // A roll begins at its clip's moment `from`, the body coming in at `speed` (m/s).
    private void StartRoll(float from, float speed)
    {
        rollFrom = rollTime = from;
        rollChose = RollCrouches = false;
        rollRate = 1f;
        var motion = RollMotion;
        float pace = motion != null ? motion.GlideAt(from) : 0f;
        Vector2 warp = ctx.AbilitySettings.travelWarp;
        rollIn = RollKind == Roll.OnTheSpot || pace < .1f ? 1f : Mathf.Clamp(speed / pace, Mathf.Min(warp.x, warp.y), Mathf.Max(warp.x, warp.y));
    }

    // The keys held on ahead: within RunsOnWithin of the way the view faces.
    private bool GoesOn => ctx.CurrentInput.y > .1f && Vector2.Angle(ctx.CurrentInput, Vector2.up) <= RunsOnWithin;

    /// <summary>
    /// As the roll's body begins to rise (its clip's Rises), the keys choose again how it ends: held on ahead it runs on out of it,
    /// else it stands up (the experts' late choice, the user 2026-11-23: "앗 전문가 방식으로 가자", and for the slide's jump
    /// 2026-11-24: "Include dive roll"). The roll began on the speed it landed at (Ability.rollRunSpeed) or, a slide's jump, on
    /// the keys then. The clip that takes over plays on from the same point of the roll, its own Rises (BIPER's rolls share their
    /// tumble: RollToRun_F and Roll_F are two frames apart there, the experts' measure), the bones easing the switch
    /// (BodyInertia). Only a way that has a roll running on (ahead: Ability.rollRunWays) has the choice to run on. Ctrl held, it
    /// goes down into a crouch instead where its legs come under it, never standing up first (0.9.394; the user: "다이브롤 이후
    /// 앉거나 … 부자연스럽고 튀는데"). Running on, its run-out plays at the rate (Ability.runOutRate) that brings its pace to the
    /// gait the keys ask for, the body's speed with it, so the walk or run takes over at its own speed (the experts' contract).
    /// </summary>
    private void Choose()
    {
        var motion = RollMotion;
        if (rollChose || RollKind == Roll.OnTheSpot || motion == null || rollTime < motion.Rises) return;
        rollChose = true;
        bool crouch = ctx.Input.CrouchHeld;
        bool goesOn = GoesOn && !crouch;
        Roll want = Dives ? (goesOn ? Roll.DiveRunsOn : Roll.Dive) : goesOn ? Roll.RunsOn : Roll.StandsUp;
        if (want != RollKind)
        {
            Roll before = RollKind;
            string clip = RollClip;
            SetRoll(want, rollHeading);
            if (RollKind == before || RollMotion == null) (RollKind, RollClip) = (before, clip);
            // The clip that takes over plays on from its own Rises: the same point of the roll.
            else rollTime = RollMotion.Rises;
        }
        RollCrouches = crouch;
        motion = RollMotion;
        rollRate = 1f;
        if (!RunsOut || !motion.StepAfter(motion.LegsUnder, out float exit) || exit <= motion.LegsUnder) return;
        // The run-out's own pace, from its legs under it to its first footfall, brought to the gait's.
        float pace = (motion.GlidedAt(exit) - motion.GlidedAt(motion.LegsUnder)) / (exit - motion.LegsUnder);
        Vector2 rate = ctx.AbilitySettings.runOutRate;
        if (pace > .1f) rollRate = Mathf.Clamp(ctx.Actions.Gait.SpeedFor(ctx.CurrentInput) / pace, Mathf.Min(rate.x, rate.y), Mathf.Max(rate.x, rate.y));
    }

    // The clip a roll of this kind going this way (degrees from the way they face) is: running on out of it where the pack has
    // that way, else standing up.
    private void SetRoll(Roll kind, float heading)
    {
        var settings = ctx.AbilitySettings;
        rollHeading = heading;
        string clip = kind == Roll.RunsOn ? ClipWays.Nearest(settings.rollRunWays, heading, RunsOnWithin) : null;
        if (kind == Roll.RunsOn && clip == null) kind = Roll.StandsUp;
        if (kind == Roll.DiveRunsOn && string.IsNullOrEmpty(settings.diveRollRun)) kind = Roll.Dive;
        RollKind = kind;
        RollClip = kind == Roll.RunsOn ? clip
            : kind == Roll.Dive ? settings.diveRoll
            : kind == Roll.DiveRunsOn ? settings.diveRollRun
            : kind == Roll.OnTheSpot ? ctx.HasCeilingAbove(ctx.StandingColliderHeight) ? settings.rollLandLow : settings.rollLand
            : ClipWays.Nearest(settings.rollWays, heading, 180f) ?? "";
    }

    /// <summary>Another PC's roll as its pose says: the same kind and way are the same clip, and one the keys chose again
    /// <paramref name="chosenAt"/> s into it (0 not) plays on from its own Rises there, as on the PC that rolls.</summary>
    internal void MirrorRoll(Roll kind, bool crouches, Vector3 direction, float yaw, float moment)
    {
        bool fresh = ctx.CurrentState != State.LandingRoll;
        if (fresh || kind != RollKind)
        {
            SetRoll(kind, HeadingOf(direction, yaw));
            if (fresh) rollFrom = moment;
            rollTime = moment;
        }
        else
        {
            float on = rollTime + Time.deltaTime;
            rollTime = Mathf.Abs(on - moment) > MirrorSlack ? moment : on;
        }
        RollCrouches = crouches;
    }

    // A way this far from every running roll's is none of theirs (degrees).
    private const float RunsOnWithin = 45f;

    // Degrees from the way they face, + to the right.
    private static float HeadingOf(Vector3 direction, float yaw)
        => Mathf.DeltaAngle(yaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);

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
        // An explicit downward launch carries real momentum even over a short drop.
        pendingDownwardSpeed = Mathf.Max(0f, -velocity.y);
        IsKnockedBack = true;
    }

    /// <summary>Shows how this character is falling and landing on another PC.</summary>
    internal void Mirror(Tier tier, Pose pose, float heavyYaw, bool bigFall, float fallSpeed)
    {
        LastTier = tier;
        HeavyPose = pose;
        HeavyYaw = heavyYaw;
        IsBigFall = bigFall;
        impactSpeed = fallSpeed;
        trackingFall = fallSpeed > 0f;
    }

    internal void Reset()
    {
        ClearFall();
        rollBuffer = LastImpactSpeed = LastFallDistance = recoverLeft = noSprintLeft = 0f;
        IsKnockedBack = false;
        RollKind = Roll.StandsUp;
        rollFrom = rollTime = 0f;
        RollCrouches = false;
        LastTier = Tier.Light;
    }

    /// <summary>How fast the roll goes over this frame (m/s), its clip's moment going on with it: the clip's smoothed pace
    /// (ClipMotion.GlideAt), times the share it came in at (rollIn, easing to 1 by the clip's LegsUnder), times the rate its run-out
    /// plays at (rollRate, from the clip's Rises). The tumble itself plays at the clip's own time (the user, 2026-11-23: "걸리는
    /// 시간 바꾸지 마시고요... 애니메이션 그대로 쓰라고"); a run-out at most Ability.runOutRate faster or slower ("속도가 조절되도
    /// 좋으니 … 너무 극단적으론 가진 말고", 2026-11-24). Until 0.9.394 the body went evenly from the speed it landed at to the clip's
    /// pace at its LegsUnder and from there by the clip's own root frame by frame: 4.6, 6.0, 3.6, 2.5, 7.3, 3.3 m/s through one
    /// roll's end (걸음 기록 of 2026-11-24 16:52), and the run took over at 3.3 m/s and sped to 5.5 in 0.2 s. On the spot it goes
    /// nowhere and its clip plays at its own pace. The keys' late choice comes first (Choose).</summary>
    internal float RollSpeed(float dt)
    {
        if (dt <= 0f) return 0f;
        var motion = RollMotion;
        if (motion == null || RollKind == Roll.OnTheSpot)
        {
            rollTime += dt;
            return 0f;
        }
        Choose();
        float rate = rollTime <= motion.Rises || motion.LegsUnder <= motion.Rises ? 1f
            : Mathf.Lerp(1f, rollRate, Smooth((rollTime - motion.Rises) / (motion.LegsUnder - motion.Rises)));
        rollTime += dt * rate;
        float warp = rollTime >= motion.LegsUnder || motion.LegsUnder <= rollFrom ? 1f
            : Mathf.Lerp(rollIn, 1f, Smooth((rollTime - rollFrom) / (motion.LegsUnder - rollFrom)));
        return warp * rate * motion.GlideAt(rollTime);
    }

    private static float Smooth(float share)
    {
        share = Mathf.Clamp01(share);
        return share * share * (3f - 2f * share);
    }
}
