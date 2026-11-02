using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-100)]
public sealed partial class PlayerMovement : MonoBehaviour
{
    public enum MovementState
    {
        Standing,
        Crouching,
        Sliding,
        Diving,
        SupineDiving,
        HeadFirstSliding,
        SupineSliding,
        Proning,
        Supine,
        StepDodge,
        LandingRoll,
        DashPush,
        Vault,
        Mantle,
        WallClimb,
        // Value 15 was WallRun. Keep all later serialized state values stable.
        WallKick = 16,
        BarGrab,
        BarSwing,
        LacheRelease,
        LedgeHang,
        LedgeJump,
        HeavyLanding,
        Ladder,
        Zipline
        // Values 25 and 26 belonged to the removed beam states. Reserve them for serialized compatibility.
    }


    internal MovementMotor Motor { get; private set; }
    internal MovementPostureController Posture { get; private set; }
    internal MovementActions Actions { get; private set; }
    internal MovementInput Input { get; private set; }
    internal MovementStateMachine StateMachine { get; private set; }
    internal MovementAbilities Abilities { get; private set; }
    internal MovementMirror Mirror { get; private set; }
    /// <summary>Resources/Tables/Player.csv: rows Posture, Vitals, Stats, Ultimate, Fists, Execution and Body.</summary>
    public const string TableName = "Player";
    /// <summary>This PC plays this character. A remote one reads no input and puts nothing on this screen.</summary>
    public bool IsLocal { get; private set; } = true;
    /// <summary>A remote character the network moves and poses (MovementMirror). Its own movement runs nothing but its posture.</summary>
    public bool IsNetworkDriven { get; private set; }
    public PlayerDamageReceiver DamageReceiver { get; private set; }
    public PlayerStats Stats { get; private set; }
    public UltimateCharge Ultimate { get; private set; }
    public PlayerMeleeCombat MeleeCombat { get; internal set; }
    public PlayerExecution Execution { get; private set; }
    public PlayerPerfectDefense PerfectDefense { get; private set; }
    public DodgeEnemyPassThrough DodgePassThrough { get; private set; }
    public bool CombatInputLocked => (MeleeCombat != null && MeleeCombat.IsGuardBreaking) || (Execution != null && Execution.IsExecuting);
    public bool IsInitialized => Motor != null && Posture != null && Actions != null && Input != null
        && Abilities != null && StateMachine != null && DamageReceiver != null && Stats != null && Ultimate != null;
    // Native capsule/pivot dimensions can be crouched when Unity reloads scripts in Play Mode.
    // Keep the original dimensions across that reload rather than treating them as standing.
    [SerializeField, HideInInspector] private bool runtimeBodyCaptured;
    [SerializeField, HideInInspector] internal Vector4 runtimeBodyDefaults;
    [SerializeField] private MovementAbilitySettings abilitySettings = new MovementAbilitySettings();
    public MovementAbilitySettings AbilitySettings => abilitySettings;
    public bool IsParkour => StateMachine != null && StateMachine.Current.ControlsTraversal;
    public bool IsDodging => Abilities != null && Abilities.Dodge.IsInvulnerable;
    public float LeanRoll => Abilities != null ? Abilities.Lean.Roll : 0f;
    public float LeanOffset => Abilities != null ? Abilities.Lean.Offset : 0f;

    public Vector3 HorizontalVelocity { get => Motor.HorizontalVelocity; set => Motor.HorizontalVelocity = value; }
    public float VerticalVelocity { get => Motor.VerticalVelocity; set => Motor.VerticalVelocity = value; }
    public Vector3 SlideDirection { get => Actions.SlideDirection; set => Actions.SlideDirection = value; }
    public float CurrentSlideSpeed { get => Actions.CurrentSlideSpeed; set => Actions.CurrentSlideSpeed = value; }
    public bool SlideBrakeArmed { get => Actions.SlideBrakeArmed; set => Actions.SlideBrakeArmed = value; }
    public Vector3 DiveBodyForward { get => Actions.DiveBodyForward; set => Actions.DiveBodyForward = value; }
    public string CurrentStatePath => StateMachine != null ? StateMachine.Current.Path : "Uninitialized";
    public bool IsSlideGroundSnap => Motor.IsSlideGroundSnap;

    /// <summary>The movement state changed (from, to); raised once the new state has entered.</summary>
    public event Action<MovementState, MovementState> StateChanged;
    /// <summary>The player touched down, with the fall speed at impact (m/s). Raised after the landing became a roll, a
    /// heavy landing, a slide or a step; LastLandingTier says how hard it was.</summary>
    public event Action<float> Landed;
    /// <summary>A jump left the ground. Wall kicks, ledge jumps and bar releases are states of their own.</summary>
    public event Action Jumped;
    /// <summary>A dash push met someone, at this point, and stopped there.</summary>
    public event Action<Vector3> DashPushHit;
    /// <summary>One rung up a ladder, by the hand going up (true for the left).</summary>
    public event Action<bool> LadderStepped;
    /// <summary>A slide down a ladder stopped on the ladder, not at its foot.</summary>
    public event Action LadderSlideStopped;

    public MovementState CurrentState => StateMachine != null ? StateMachine.CurrentState : MovementState.Standing;
    public MovementState PreviousState => StateMachine != null ? StateMachine.PreviousState : MovementState.Standing;
    public float CurrentStateDuration { get => StateMachine.Duration; set => StateMachine.Duration = value; }
    public bool IsGrounded => Motor.isGrounded;
    /// <summary>On the ground, crawling and waiting to be got up (PlayerLife). Prone, and the prone controls for getting up do nothing.</summary>
    public bool IsDowned => DamageReceiver != null && DamageReceiver.IsDowned;
    public bool IsDead => DamageReceiver != null && !DamageReceiver.IsAlive;
    /// <summary>This player's frame time: in a room, slow motion runs on them alone (CombatTimeEffects).</summary>
    public float DeltaTime => Time.deltaTime * (TimeEffects != null ? TimeEffects.Pace : 1f);
    private CombatTimeEffects TimeEffects => timeEffects != null ? timeEffects : timeEffects = GetComponent<CombatTimeEffects>();
    private CombatTimeEffects timeEffects;
    public bool LandedThisFrame => Motor.landedThisFrame;
    public bool IsSliding => StateMachine != null && StateMachine.IsIn<SlidingGroupState>();
    public bool IsJustSliding => Abilities != null && Abilities.Slide.IsJustSlide;
    public bool IsDiving => StateMachine != null && StateMachine.IsIn<DivingGroupState>();
    public bool IsCrouching => CurrentState == MovementState.Crouching;
    public bool IsAirCrouching => Posture.airCrouching;
    public bool IsProning => StateMachine != null && StateMachine.IsIn<ProneState>();
    public Vector3 Velocity => HorizontalVelocity + Vector3.up * VerticalVelocity;
    public Vector3 ActualVelocity => Motor.actualVelocity;
    public Vector2 CurrentInput => InputAvailable && !ControlLocked && Input != null ? Input.Move : Vector2.zero;
    /// <summary>Down after a heavy landing or thrown by a blow: movement keys and buttons do nothing (the view still turns).</summary>
    public bool ControlLocked => Abilities != null && Abilities.Landing.LocksControl;
    /// <summary>A landing roll or a heavy landing has the body busy: no attacks or aiming.</summary>
    public bool IsLandingRecovery => CurrentState == MovementState.LandingRoll || CurrentState == MovementState.HeavyLanding;
    /// <summary>How hard the last touchdown was: Light, Hard (from Ability.hardLandingSpeed) or Heavy (it hurt, or a blow).</summary>
    public MovementLanding.Tier LastLandingTier => Abilities != null ? Abilities.Landing.LastTier : MovementLanding.Tier.Light;
    public bool InputAvailable => IsLocal && isActiveAndEnabled && Time.timeScale > 0f
        && (DamageReceiver == null || DamageReceiver.IsAlive)
        && (!Application.isPlaying || (Application.isFocused && Cursor.lockState == CursorLockMode.Locked));

    public float WalkSpeed => walkSpeed;
    public float SprintSpeed => sprintSpeed;
    public float CrouchSpeed => crouchSpeed;
    public float ProneSpeed => proneSpeed;
    public float SupineSpeed => supineSpeed;
    public float StrafeCompensation => strafeCompensation;
    public float GroundSharpness => groundSharpness;
    public float AirControlSharpness => airControlSharpness;

    public float SlideMinStartSpeed => slideMinStartSpeed;
    public float SlideKickBoost => slideKickBoost;
    public float SlideFriction => slideFriction;
    public float DiveSlideFriction => diveSlideFriction;
    public float SupineSlideFriction => supineSlideFriction;
    public float AirSlideFrictionMultiplier => airSlideFrictionMultiplier;
    public float SlopeSlideGravity => slopeSlideGravity;
    public float SlideEndSpeed => slideEndSpeed;
    public float SlideCancelMaxSpeed => slideCancelMaxSpeed;
    public float DiveSlideCancelMaxSpeed => diveSlideCancelMaxSpeed;
    public float SlideBrakeDeceleration => slideBrakeDeceleration;
    public float SlideCancelMinDuration => slideCancelMinDuration;
    public float SlideCooldown => slideCooldown;
    public float SlideCooldownTimer { get => Actions.slideCooldownTimer; set => Actions.slideCooldownTimer = value; }
    public float SlideSteerSharpness => slideSteerSharpness;
    public float GroundSnapDistance => groundSnapDistance;

    public float DoorBreakSpeedThreshold => doorBreakSpeedThreshold;
    public float DoorBreakSpeedRetention => doorBreakSpeedRetention;
    public float WallBounceBounciness => wallBounceBounciness;
    public float SupineBounceThreshold => supineBounceThreshold;

    public float DiveAirResistance => diveAirResistance;
    public float DiveMomentumRetention => diveMomentumRetention;
    public float AirborneTimer => Motor.airborneTimer;

    public float StandingColliderHeight => Posture.standingColliderHeight;
    public float CrouchColliderHeight => crouchColliderHeight;
    public float ProneColliderHeight => proneColliderHeight;

    public Vector3 GroundNormal => Motor.groundNormal;
    public float SprintProgress { get => Actions.sprintProgress; set => Actions.sprintProgress = value; }
    /// <summary>The built-up run speed (m/s), see MovementMomentum.</summary>
    public float RunSpeed => Actions != null ? Actions.Momentum.Speed : walkSpeed;
    /// <summary>The fastest a forward run builds to (m/s).</summary>
    public float TopRunSpeed => Mathf.Max(topSpeed, sprintSpeed);
    /// <summary>0–1: how much the player runs forward on purpose (Shift, or W building a run).</summary>
    public float RunIntent => Actions == null ? 0f : Mathf.Max(SprintProgress, Actions.Momentum.Driving ? 1f : 0f);
    /// <summary>The fall is a long one (Ability.bigFallHeight): wind, heartbeat, screen warp and flailing.</summary>
    public bool IsBigFall => Abilities != null && Abilities.Landing.IsBigFall;
    /// <summary>Fall speed from actual descent, excluding time held against an edge (m/s).</summary>
    public float FallSpeed => Abilities != null ? Abilities.Landing.FallSpeed : 0f;
    public float SlopeLimit => Motor.controller != null ? Motor.controller.slopeLimit : 45f;
    public float ControllerRadius => Motor.controller != null ? Motor.controller.radius : 0.5f;

    public InputAction SprintAction => Input.sprintAction;
    public InputAction DiveAction => Input.diveAction;
    public InputAction JumpAction => Input.jumpAction;


    private void Awake() => EnsureInitialized();
    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        EnsureInitialized();
        PlayerRoster.Add(this);
    }

    /// <summary>
    /// Makes this copy someone else's character on this PC: no input, camera, listener or screen effects, just the body
    /// and its sounds. Call it before the copy first activates, so its screen-only parts are never added.
    /// </summary>
    public void MakeRemote(bool networkDriven = false)
    {
        IsLocal = false;
        IsNetworkDriven = networkDriven;
        foreach (var part in GetComponentsInChildren<Behaviour>(true))
            if (part is Camera || part is AudioListener || part is PlayerLook || part is PlayerCameraEffects
                || part is PlayerInput || part is PlayerTestInteraction) part.enabled = false;
    }

    internal bool EnsureInitialized()
    {
        if (IsInitialized) return true;
        var controller = GetComponent<CharacterController>();
        if (controller == null) return false;
        if (crouchPivot == null) crouchPivot = transform.Find("CrouchPivot");
        if (!runtimeBodyCaptured)
        {
            runtimeBodyDefaults = new Vector4(controller.height, controller.radius, controller.stepOffset,
                crouchPivot != null ? crouchPivot.localPosition.y : 0f);
            runtimeBodyCaptured = true;
        }
        Abilities?.Reset();
        groundLayers &= ~(1 << gameObject.layer);
        // Player.csv owns the movement numbers: Posture (capsule heights, eye offsets), Move (speeds, jump, slide, dive)
        // and Ability (dodge, lean, parkour, fall damage, shove). Inspector values only fill empty cells.
        GameTables.Bind(TableName, "Posture", this);
        GameTables.Bind(TableName, "Move", this);
        GameTables.BindNested(TableName, "Ability", this, abilitySettings);
        Input = new MovementInput(this);
        Motor = new MovementMotor(this, controller);
        Posture = new MovementPostureController(this);
        Actions = new MovementActions(this);
        Abilities = new MovementAbilities(this);
        Mirror = new MovementMirror(this);
        // Combat components are composed here at runtime; their balance numbers come from Resources/Tables/Player.csv.
        DamageReceiver = GetComponent<PlayerDamageReceiver>();
        if (DamageReceiver == null) DamageReceiver = gameObject.AddComponent<PlayerDamageReceiver>();
        GameTables.Bind(TableName, "Vitals", DamageReceiver);
        DamageReceiver.Initialize(this);
        Stats = GetComponent<PlayerStats>();
        if (Stats == null) Stats = gameObject.AddComponent<PlayerStats>();
        GameTables.Bind(TableName, "Stats", Stats);
        Ultimate = GetComponent<UltimateCharge>();
        if (Ultimate == null) Ultimate = gameObject.AddComponent<UltimateCharge>();
        GameTables.Bind(TableName, "Ultimate", Ultimate);
        StateMachine = MovementStateMachine.Create(this);
        StateMachine.Changed += RaiseStateChanged;
        Execution = GetComponent<PlayerExecution>();
        if (Execution == null) Execution = gameObject.AddComponent<PlayerExecution>();
        GameTables.Bind(TableName, "Execution", Execution);
        PerfectDefense = GetComponent<PlayerPerfectDefense>() ?? gameObject.AddComponent<PlayerPerfectDefense>();
        GameTables.Bind(TableName, "Defense", PerfectDefense);
        PerfectDefense.Initialize(this);
        if (Application.isPlaying && IsLocal && GetComponent<ParryFeedback>() == null) GameTables.Bind(TableName, "Parry", gameObject.AddComponent<ParryFeedback>());
        DodgePassThrough =GetComponent<DodgeEnemyPassThrough>() ?? gameObject.AddComponent<DodgeEnemyPassThrough>();
        if (GetComponent<PlayerLeanHitboxes>() == null) gameObject.AddComponent<PlayerLeanHitboxes>();
        // The visible body (true first person) appears once Resources/Characters/PlayerBody names a character.
        if (Application.isPlaying && GetComponent<PlayerBody>() == null && PlayerBodyProfile.Load() != null)
        {
            GameTables.Bind(TableName, "Body", gameObject.AddComponent<PlayerBody>());
            // Its hands hold the gun, the fists and ledges.
            GameTables.Bind(TableName, "Hands", gameObject.AddComponent<PlayerBodyHands>());
            // Its feet make the footsteps.
            GameTables.Bind(TableName, "Footsteps", gameObject.AddComponent<PlayerFootsteps>());
        }
        // Movement sounds anyone nearby hears: the voices they come out of and action sounds from the movement events.
        if (Application.isPlaying && GetComponent<CharacterSounds>() == null)
        {
            GameTables.Bind(TableName, "Sounds", gameObject.AddComponent<CharacterSounds>());
            GameTables.Bind(TableName, "MovementAudio", gameObject.AddComponent<PlayerMovementAudio>());
            GameTables.Bind(TableName, "FixtureAudio", gameObject.AddComponent<PlayerFixtureAudio>());
        }
        // Going down, getting up, dying and the team wipe are decided on the PC that plays this character.
        if (Application.isPlaying && IsLocal && GetComponent<PlayerLife>() == null)
        {
            GameTables.Bind(PlayerLife.TableName, PlayerLife.TableRow, gameObject.AddComponent<PlayerLife>(), gameObject.AddComponent<PlayerRevive>());
            gameObject.AddComponent<PlayerLifeHUD>();
            gameObject.AddComponent<TeammateMarkerHUD>();
            gameObject.AddComponent<ScoreboardHUD>();
        }
        if (Application.isPlaying && GetComponent<PlayerRagdoll>() == null)
            GameTables.Bind(PlayerRagdoll.TableName, PlayerRagdoll.TableRow, gameObject.AddComponent<PlayerRagdoll>());
        if (Application.isPlaying && GetComponent<PlayerTally>() == null) gameObject.AddComponent<PlayerTally>();
        if (Application.isPlaying && IsNetworkDriven && GetComponent<DownedAura>() == null)
            GameTables.Bind(DownedAura.TableName, DownedAura.TableRow, gameObject.AddComponent<DownedAura>());
        // What only the player feels: breathing, wind and fall sounds, then the camera's punches and a long fall's warp.
        if (Application.isPlaying && IsLocal && GetComponent<PlayerMovementPunch>() == null)
        {
            GameTables.Bind(TableName, "Breathing", gameObject.AddComponent<PlayerBreathing>());
            GameTables.Bind(TableName, "Wind", gameObject.AddComponent<PlayerWindAudio>());
            GameTables.Bind(TableName, "FallAudio", gameObject.AddComponent<PlayerFallAudio>());
            GameTables.Bind(TableName, "Punch", gameObject.AddComponent<PlayerMovementPunch>());
            GameTables.Bind(TableName, "FallScreen", gameObject.AddComponent<PlayerFallScreen>());
        }
        if (controller.height < Posture.standingColliderHeight - 0.02f)
            ChangeState(controller.height <= proneColliderHeight + 0.02f ? MovementState.Proning : MovementState.Crouching);
        return true;
    }

    private void Update()
    {
        if (!EnsureInitialized()) return;
        if (IsNetworkDriven) { Posture.UpdateHeightsAndTransform(Time.deltaTime); return; }
        float dt = DeltaTime;
        if (dt <= 0f || !Application.isFocused) return;
        Input.Sample(InputAvailable && !ControlLocked);
        Abilities.BeginFrame(dt);
        Actions.TickTimers(dt);
        Motor.TickTimers(dt);
        StateMachine.Duration += dt;
        Motor.CheckGroundedAndSlope();
        // The dead still fall, so nobody's body hangs in the air.
        if (CombatInputLocked || !IsLocal || !DamageReceiver.IsAlive)
        {
            Input.Sample(false);
            Actions.Momentum.Tick(dt, Vector2.zero);
            HorizontalVelocity = Vector3.zero;
            Motor.ApplyGravity(dt);
            Posture.UpdateHeightsAndTransform(dt);
            Motor.Move(dt);
            return;
        }
        Vector2 input = CurrentInput;
        Actions.ProcessSprintRampUp(dt, input);
        Actions.Momentum.Tick(dt, input);
        if (!Abilities.ProcessInput()) StateMachine.Current.ProcessInput(input);
        StateMachine.Current.UpdateLogic(dt);
        Actions.ProcessJump();
        Abilities.Lean.Tick(dt);
        Posture.UpdateAirCrouch();
        StateMachine.Current.UpdatePhysics(dt);
        if (StateMachine.Current.CanApplyGravity) Motor.ApplyGravity(dt);
        Posture.UpdateHeightsAndTransform(dt);
        Motor.Move(dt);
    }

    private void LateUpdate()
    {
        if (Time.timeScale > 0f) Abilities?.Combat.SampleShove(0f);
    }
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Motor?.RecordContact(hit);
        StateMachine?.Current.OnColliderHit(hit);
        // Walking into a physics prop carries it along; standing on top of one does not drive it into the ground.
        var prop = hit.normal.y < .7f ? PhysicsProp.Of(hit.collider) : null;
        if (prop != null) prop.PushFromWalker(ActualVelocity, AbilitySettings.propPushForce);
    }

    public void ChangeState(MovementState state) => StateMachine.ChangeState(state);
    /// <summary>Puts the player at this point, as a spawn does.</summary>
    internal void MoveTo(Vector3 position)
    {
        var controller = Motor.controller;
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
        ResetVelocity();
    }
    /// <summary>
    /// Throws the player at this velocity (meant for big enemies' blows; aim it upward). Without control until they
    /// land, they go down on their back facing where the blow came from and get up again (Ability.backLandingTime).
    /// The fall itself hurts only by the usual fall damage rule. A throw that does not lift them goes down at once.
    /// </summary>
    public void KnockBack(Vector3 velocity)
    {
        if (EnsureInitialized() && DamageReceiver.IsAlive) Abilities.Landing.KnockBack(velocity);
    }
    private void RaiseStateChanged(MovementState from, MovementState to) => StateChanged?.Invoke(from, to);
    internal void RaiseLanded(float impactSpeed) => Landed?.Invoke(impactSpeed);
    internal void RaiseJumped() => Jumped?.Invoke();
    internal void RaiseDashPushHit(Vector3 point) => DashPushHit?.Invoke(point);
    internal void RaiseLadderStepped(bool leftHand) => LadderStepped?.Invoke(leftHand);
    internal void RaiseLadderSlideStopped() => LadderSlideStopped?.Invoke();
    public void StartSlide(float speed, bool applyBoost = true) => Actions.StartSlide(speed, applyBoost);
    public void StartLandingSlide(MovementState state, float speed, Vector3 fallback, bool applyBoost) => Actions.StartLandingSlide(state, speed, fallback, applyBoost);
    public void LaunchDive(Vector2 input) => Actions.LaunchDive(input);
    public float GetSpeedScaledPop(float speed) => Actions.GetSpeedScaledPop(speed);
    public float GetSlideSlopeDot(Vector3 direction) => Motor.GetSlideSlopeDot(direction);
    public bool IsSlideStepContact(ControllerColliderHit hit) => Motor.IsSlideStepContact(hit);
    public bool HasGroundAhead(Vector3 direction, float distanceAhead, float checkDepth) => Motor.HasGroundAhead(direction, distanceAhead, checkDepth);
    public bool HasCeilingAbove(float height) => Posture.HasCeilingAbove(height);
    public bool HasCeilingAhead(Vector3 direction, float distance, float height) => Posture.HasCeilingAhead(direction, distance, height);
    public bool HasVentCeilingAhead(Vector3 direction, float forwardDistance = 0.5f) => Posture.HasVentCeilingAhead(direction, forwardDistance);
    public void UpdateHeightsAndTransform(float dt) => Posture.UpdateHeightsAndTransform(dt);

    public void ResetVelocity()
    {
        PerfectDefense?.Cancel(); DodgePassThrough?.Finish();
        Execution?.Cancel();
        MeleeCombat?.CancelAction();
        if (!IsInitialized) return;
        if (IsNetworkDriven) { StateMachine.Mirror(MovementState.Standing); return; }
        if (Posture.airBodyTucked || Posture.airCrouching) Posture.RebaseAirCrouchFeet();
        Motor.Reset();
        Actions.Reset();
        if (IsDowned) ChangeState(MovementState.Proning);
        else if (!HasCeilingAbove(StandingColliderHeight)) ChangeState(MovementState.Standing);
        else if (!HasCeilingAbove(CrouchColliderHeight)) ChangeState(MovementState.Crouching);
        else ChangeState(MovementState.Proning);
        Abilities?.Reset();
    }

    private void OnDisable()
    {
        PlayerRoster.Remove(this);
        ResetVelocity();
    }
}
