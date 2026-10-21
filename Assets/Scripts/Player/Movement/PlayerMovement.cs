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
        Tackle,
        Vault,
        Mantle,
        WallClimb,
        // Value 15 was WallRun. Keep all later serialized state values stable.
        WallKick = 16,
        BarGrab,
        BarSwing,
        LacheRelease,
        LedgeHang,
        LedgeJump
    }


    internal MovementMotor Motor { get; private set; }
    internal MovementPostureController Posture { get; private set; }
    internal MovementActions Actions { get; private set; }
    internal MovementInput Input { get; private set; }
    internal MovementStateMachine StateMachine { get; private set; }
    internal MovementAbilities Abilities { get; private set; }
    public PlayerDamageReceiver DamageReceiver { get; private set; }
    public UltimateCharge Ultimate { get; private set; }
    public PlayerMeleeCombat MeleeCombat { get; internal set; }
    public PlayerExecution Execution { get; private set; }
    public bool CombatInputLocked => (MeleeCombat != null && MeleeCombat.IsGuardBreaking) || (Execution != null && Execution.IsExecuting);
    public bool IsInitialized => Motor != null && Posture != null && Actions != null && Input != null
        && Abilities != null && StateMachine != null && DamageReceiver != null && Ultimate != null;
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

    public MovementState CurrentState => StateMachine != null ? StateMachine.CurrentState : MovementState.Standing;
    public MovementState PreviousState => StateMachine != null ? StateMachine.PreviousState : MovementState.Standing;
    public float CurrentStateDuration { get => StateMachine.Duration; set => StateMachine.Duration = value; }
    public bool IsGrounded => Motor.isGrounded;
    public bool LandedThisFrame => Motor.landedThisFrame;
    public bool IsSliding => StateMachine != null && StateMachine.IsIn<SlidingGroupState>();
    public bool IsDiving => StateMachine != null && StateMachine.IsIn<DivingGroupState>();
    public bool IsCrouching => CurrentState == MovementState.Crouching;
    public bool IsAirCrouching => Posture.airCrouching;
    public bool IsProning => StateMachine != null && StateMachine.IsIn<ProneState>();
    public Vector3 Velocity => HorizontalVelocity + Vector3.up * VerticalVelocity;
    public Vector3 ActualVelocity => Motor.actualVelocity;
    public Vector2 CurrentInput => InputAvailable && Input != null ? Input.Move : Vector2.zero;
    public bool InputAvailable => isActiveAndEnabled && Time.timeScale > 0f
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
    public float SlopeLimit => Motor.controller != null ? Motor.controller.slopeLimit : 45f;
    public float ControllerRadius => Motor.controller != null ? Motor.controller.radius : 0.5f;

    public InputAction CrouchAction => Input.crouchAction;
    public InputAction SprintAction => Input.sprintAction;
    public InputAction DiveAction => Input.diveAction;
    public InputAction JumpAction => Input.jumpAction;


    private void Awake() => EnsureInitialized();
    private void OnEnable() { if (Application.isPlaying) EnsureInitialized(); }

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
        Input = new MovementInput();
        Motor = new MovementMotor(this, controller);
        Posture = new MovementPostureController(this);
        Actions = new MovementActions(this);
        Abilities = new MovementAbilities(this);
        DamageReceiver = GetComponent<PlayerDamageReceiver>();
        if (DamageReceiver == null) DamageReceiver = gameObject.AddComponent<PlayerDamageReceiver>();
        DamageReceiver.Initialize(this);
        Ultimate = GetComponent<UltimateCharge>();
        if (Ultimate == null) Ultimate = gameObject.AddComponent<UltimateCharge>();
        StateMachine = MovementStateMachine.Create(this);
        Execution = GetComponent<PlayerExecution>();
        if (Execution == null) Execution = gameObject.AddComponent<PlayerExecution>();
        if (controller.height < Posture.standingColliderHeight - 0.02f)
            ChangeState(controller.height <= proneColliderHeight + 0.02f ? MovementState.Proning : MovementState.Crouching);
        return true;
    }

    private void Update()
    {
        if (!EnsureInitialized()) return;
        float dt = Time.deltaTime;
        if (dt <= 0f || !Application.isFocused || !DamageReceiver.IsAlive) return;
        Input.Sample(InputAvailable);
        Abilities.BeginFrame(dt);
        Actions.TickTimers(dt);
        Motor.TickTimers(dt);
        StateMachine.Duration += dt;
        Motor.CheckGroundedAndSlope();
        if (CombatInputLocked)
        {
            Input.Sample(false);
            HorizontalVelocity = Vector3.zero;
            Motor.ApplyGravity(dt);
            Posture.UpdateHeightsAndTransform(dt);
            Motor.Move(dt);
            return;
        }
        Vector2 input = CurrentInput;
        Actions.ProcessSprintRampUp(dt, input);
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

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Motor?.RecordContact(hit);
        StateMachine?.Current.OnColliderHit(hit);
    }

    public void ChangeState(MovementState state) => StateMachine.ChangeState(state);
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
        Execution?.Cancel();
        MeleeCombat?.CancelAction();
        if (!IsInitialized) return;
        if (Posture.airBodyTucked || Posture.airCrouching) Posture.RebaseAirCrouchFeet();
        Motor.Reset();
        Actions.Reset();
        if (!HasCeilingAbove(StandingColliderHeight)) ChangeState(MovementState.Standing);
        else if (!HasCeilingAbove(CrouchColliderHeight)) ChangeState(MovementState.Crouching);
        else ChangeState(MovementState.Proning);
        Abilities?.Reset();
    }

    private void OnDisable() => ResetVelocity();
}
