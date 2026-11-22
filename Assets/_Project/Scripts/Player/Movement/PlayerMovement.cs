using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(FrameOrder.Movement)]
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
    /// <summary>The body that plays the moves, asked how a vault's clip puts its hands down (PlayerBody).</summary>
    internal IObstacleMotionSource ObstacleMotions => obstacleMotions ??= GetComponent<IObstacleMotionSource>();
    private IObstacleMotionSource obstacleMotions;
    internal MovementPostureController Posture { get; private set; }
    internal MovementActions Actions { get; private set; }
    internal MovementInput Input { get; private set; }
    internal MovementStateMachine StateMachine { get; private set; }
    internal MovementAbilities Abilities { get; private set; }
    internal MovementMirror Mirror { get; private set; }
    /// <summary>Data/Resources/Tables/Player/Player.csv: rows Posture, Vitals, Stats, Ultimate, Fists, Execution and Body.</summary>
    public const string TableName = "Player";
    /// <summary>This PC plays this character. A remote one reads no input and puts nothing on this screen.</summary>
    public bool IsLocal { get; private set; } = true;
    /// <summary>A remote character the network moves and poses (MovementMirror). Its own movement runs nothing but its posture.</summary>
    public bool IsNetworkDriven { get; private set; }
    /// <summary>
    /// Something other than its own legs carries this body for now (the developer's noclip flight, Dev/Noclip). Its
    /// movement then runs nothing but its posture, as for a copy the network moves, so no state, gravity or capsule move
    /// competes for the body's place. Whoever sets it calls ResetVelocity as it takes the body and as it gives it back.
    /// </summary>
    internal bool Carried { get; set; }
    public PlayerDamageReceiver DamageReceiver { get; private set; }
    public PlayerStats Stats { get; private set; }
    public UltimateCharge Ultimate { get; private set; }
    public PlayerMeleeCombat MeleeCombat { get; internal set; }
    public PlayerExecution Execution { get; private set; }
    public PlayerPerfectDefense PerfectDefense { get; private set; }
    public DodgeEnemyPassThrough DodgePassThrough { get; private set; }
    /// <summary>
    /// The body is held where it stands by what it is doing: a guard break, an execution, a counter, a Détente's
    /// treatment by hand (DetenteKit, the surgeon's long work). No step is taken and no state of the movement runs.
    /// </summary>
    public bool BodyLocked => (MeleeCombat != null && MeleeCombat.IsGuardBreaking) || (Execution != null && Execution.IsExecuting)
        || (PerfectDefense != null && PerfectDefense.IsCountering) || (IsLocal && DetenteKit.HoldsHands);
    /// <summary>
    /// The hands are busy: no shot, swing, shove or turn of the view. Everything that holds the body holds the hands;
    /// what takes the hands alone belongs here and not in BodyLocked. The two were one flag until 2026-11-18 and the
    /// movement read "hands busy" as "body held", so the callout wheel, which only needs the mouse, stopped the
    /// character where it stood and on a zipline. The wheel is no part of either now: it is a screen and holds the
    /// view and the trigger through InputHold (CalloutWheel.Holder).
    /// </summary>
    public bool CombatInputLocked => BodyLocked;
    private WeaponController weapons;
    /// <summary>A reload has both hands on the gun, so a vault begun during one puts the gun away and stops it (ParkourTraversal).</summary>
    public bool HandsOnReload
    {
        get
        {
            if (weapons == null) weapons = GetComponent<WeaponController>();
            return weapons != null && weapons.Runtime != null && weapons.Runtime.IsReloading;
        }
    }
    public bool IsInitialized => Motor != null && Posture != null && Actions != null && Input != null
        && Abilities != null && StateMachine != null && DamageReceiver != null && Stats != null && Ultimate != null;
    // Native capsule/pivot dimensions can be crouched when Unity reloads scripts in Play Mode.
    // Keep the original dimensions across that reload rather than treating them as standing.
    [SerializeField, HideInInspector] private bool runtimeBodyCaptured;
    [SerializeField, HideInInspector] internal Vector4 runtimeBodyDefaults;
    /// <summary>
    /// How far the worn shoes put the body's eyes over (+) or under (-) the standing camera height, in metres. The body
    /// keeps its size in any shoes, so heels make it taller and bare feet shorter (the user, 2026-11-12: "힐 신으면 키
    /// 커졌으면"); the camera rides the eyes, and its resting place in the capsule (CrouchPivot) rises with them so a
    /// ladder or a punch that steadies the camera there doesn't move the view. Set by PlayerBody.StandOnSoles.
    /// </summary>
    internal float EyeRise { get; set; }
    [SerializeField] private MovementAbilitySettings abilitySettings = new MovementAbilitySettings();
    public MovementAbilitySettings AbilitySettings => abilitySettings;
    public bool IsParkour => StateMachine != null && StateMachine.Current.ControlsTraversal;
    public float LeanRoll => Abilities != null ? Abilities.Lean.Roll : 0f;
    public float LeanOffset => Abilities != null ? Abilities.Lean.Offset : 0f;

    public Vector3 HorizontalVelocity { get => Motor.HorizontalVelocity; set => Motor.HorizontalVelocity = value; }
    public float VerticalVelocity { get => Motor.VerticalVelocity; set => Motor.VerticalVelocity = value; }
    public Vector3 SlideDirection { get => Actions.SlideDirection; set => Actions.SlideDirection = value; }
    /// <summary>The way the sliding body faces, held for the slide whatever the view does (MovementActions.SlideFacesBack).</summary>
    public float SlideFacingYaw => Actions.SlideFacingYaw;
    public float CurrentSlideSpeed { get => Actions.CurrentSlideSpeed; set => Actions.CurrentSlideSpeed = value; }
    public bool SlideBrakeArmed { get => Actions.SlideBrakeArmed; set => Actions.SlideBrakeArmed = value; }
    public Vector3 DiveBodyForward { get => Actions.DiveBodyForward; set => Actions.DiveBodyForward = value; }
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
    public float CurrentStateDuration { get => StateMachine.Duration; set => StateMachine.Duration = value; }
    public bool IsGrounded => Motor.isGrounded;
    /// <summary>On the ground, crawling and waiting to be got up (PlayerLife). Prone, and the prone controls for getting up do nothing.</summary>
    public bool IsDowned => DamageReceiver != null && DamageReceiver.IsDowned;
    /// <summary>
    /// Gone down from on their feet a moment ago: the body is still falling onto its front (PlayerBody.Downed), and the
    /// crawl waits until it is down (Move.downedFallTime), so the fall is never carried along the ground.
    /// </summary>
    public bool DownedFalling => IsDowned && Time.time - downedFallAt < downedFallTime;
    private float downedFallAt = float.NegativeInfinity;
    /// <summary>
    /// Going down from one of these on the ground, they fall before they lie (DownedFalling, PlayerBody.Downed). From a
    /// crouch, lying, a slide, a climb or the air they are low or down already and go straight to the crawl.
    /// </summary>
    public static bool FallsWhenDowned(MovementState from) =>
        from == MovementState.Standing || from == MovementState.StepDodge || from == MovementState.DashPush;
    public bool IsDead => DamageReceiver != null && !DamageReceiver.IsAlive;
    private PlayerHold hold;
    /// <summary>A shambler holds them (PlayerHold): they can't step anywhere or use a gun, but can shove or punch it off.</summary>
    public bool IsHeld
    {
        get
        {
            if (hold == null) hold = GetComponent<PlayerHold>();
            return hold != null && hold.IsHeld;
        }
    }
    private bool shownPinned;
    /// <summary>
    /// A runner pins them flat on their back (PlayerHold, G&B-2): held, and they can't fight it unless FightsPinned. On
    /// another PC, what their own PC says (PlayerNetworkCombat).
    /// </summary>
    public bool IsPinned
    {
        get
        {
            if (hold == null) hold = GetComponent<PlayerHold>();
            return hold != null ? hold.IsPinned : shownPinned;
        }
    }
    /// <summary>Pinned, but a melee class perk lets them fight the runner themselves (PlayerHold.FightsPinned).</summary>
    public bool FightsPinned => IsPinned && hold != null && hold.FightsPinned;
    /// <summary>Another PC: whether their own PC has them pinned (PlayerNetworkCombat).</summary>
    internal void ShowPinned(bool pinned) => shownPinned = pinned;
    private bool shownHeld;
    /// <summary>
    /// Some zombie has them, standing, eating or pinning (PlayerHold.IsHeld); on another PC, what their own PC says
    /// (PlayerNetworkCombat). Friends see them through walls then (DownedAura), to come and pull it off.
    /// </summary>
    public bool InZombieGrip
    {
        get
        {
            if (hold == null) hold = GetComponent<PlayerHold>();
            return hold != null ? hold.IsHeld : shownHeld || shownPinned;
        }
    }
    /// <summary>Another PC: whether their own PC has them in a zombie's grip (PlayerNetworkCombat).</summary>
    internal void ShowHeld(bool held) => shownHeld = held;
    /// <summary>This player's frame time: in a room, slow motion runs on them alone (CombatTimeEffects).</summary>
    public float DeltaTime => Time.deltaTime * Pace;
    /// <summary>How fast this player's own time runs: below 1 only in a room's slow motion.</summary>
    public float Pace => TimeEffects != null ? TimeEffects.Pace : 1f;
    private CombatTimeEffects TimeEffects => timeEffects != null ? timeEffects : timeEffects = GetComponent<CombatTimeEffects>();
    private CombatTimeEffects timeEffects;
    public bool LandedThisFrame => Motor.landedThisFrame;
    public bool IsSliding => StateMachine != null && StateMachine.IsIn<SlidingGroupState>();
    public bool IsDiving => StateMachine != null && StateMachine.IsIn<DivingGroupState>();
    public bool IsCrouching => CurrentState == MovementState.Crouching;
    /// <summary>
    /// Crouched in stance, as stealth hears and sees it: crouching, or a step dodge made from a crouch, a low slide that
    /// stays at crouch height (MovementAbilities.ActionPosture, mirrored to copies). IsCrouching is the Crouching state
    /// alone, so a crouched dodge counted as a standing sprint and its 8 m dodge sound woke calm zombies (user,
    /// 2026-11-12: "앉은 채로 빠르게 회피로 뒤로 접근한 다음에 암살할 수 있었는데 이젠 그게 안 돼서 아쉬움").
    /// </summary>
    public bool IsCrouchStance => CurrentState == MovementState.Crouching
        || (CurrentState == MovementState.StepDodge && Abilities != null && Abilities.ActionPosture == MovementPosture.Crouching);
    public bool IsAirCrouching => Posture.airCrouching;
    /// <summary>How far an air crouch has drawn the capsule's bottom up above the feet (m).</summary>
    public float AirTuck => Posture.AirTuck;
    /// <summary>How far landing has moved the feet up to the capsule since the last call (m).</summary>
    internal float TakeTuckRebase()
    {
        float shift = Posture.tuckRebase;
        Posture.tuckRebase = 0f;
        return shift;
    }
    public bool IsProning => StateMachine != null && StateMachine.IsIn<ProneState>();
    /// <summary>Lying on the ground, face down or on the back: zombies kneel and eat whoever lies there (EnemyCombatAI Feed).</summary>
    public bool IsLyingDown => IsProning || CurrentState == MovementState.Supine;
    public Vector3 Velocity => HorizontalVelocity + Vector3.up * VerticalVelocity;
    public Vector3 ActualVelocity => Motor.actualVelocity;
    public Vector2 CurrentInput => InputAvailable && !ControlLocked && !IsGettingUp && Input != null ? Input.Move : Vector2.zero;
    /// <summary>Down after a heavy landing or thrown by a blow: movement keys and buttons do nothing (the view still turns).</summary>
    public bool ControlLocked => Abilities != null && Abilities.Landing.LocksControl;
    /// <summary>Getting up off the back (Ability.supineRiseTime): the body can't move off until it is up; the hands stay free
    /// for the gun, the shove and the fists.</summary>
    public bool IsGettingUp => Actions != null && Actions.IsRising;
    /// <summary>A landing roll or a heavy landing has the body busy: no attacks or aiming.</summary>
    public bool IsLandingRecovery => CurrentState == MovementState.LandingRoll || CurrentState == MovementState.HeavyLanding;
    /// <summary>How hard the last touchdown was: Light, Hard (from Ability.hardLandingSpeed) or Heavy (it hurt, or a blow).</summary>
    public MovementLanding.Tier LastLandingTier => Abilities != null ? Abilities.Landing.LastTier : MovementLanding.Tier.Light;
    /// <summary>
    /// This PC plays this character and it is alive: whatever it is doing is its own to go on with. Nothing here is
    /// about the keys.
    /// </summary>
    public bool CanAct => IsLocal && isActiveAndEnabled && (DamageReceiver == null || DamageReceiver.IsAlive);
    /// <summary>
    /// The body's keys reach this character now: it can act, the game runs, and no screen or other window has those keys
    /// (InputFocus.TakesMove, which asks InputHold who holds what). Not whether the pointer is locked: a screen may show
    /// the pointer and leave the legs to the player.
    /// Two things may ask this, and only these two: what an input frame holds (while it is false every button reads as
    /// let go and the stick as zero: MovementInput.Sample), and whether a press may START something. It is never a
    /// reason to end what is already under way: an execution, a hang, a pin already pulled end for reasons of the game
    /// alone (the user, 2026-11-18: "입력 포커스 전환과 캐릭터 액션 상태 머신이 충돌하지 않도록 분리 처리"). It used to bundle
    /// "alive" with "the keys are mine", so everything that asked it each frame stopped the moment the chat opened.
    /// </summary>
    public bool InputAvailable => CanAct && Time.timeScale > 0f && (!Application.isPlaying || InputFocus.TakesMove());

    /// <summary>The jog's speed straight ahead (m/s), the pace a direction key alone gives (its clip's, MovementGait; the run's
    /// until 0.9.283). Named for the row it took over (Move.walkSpeed 4.9 was the run until 0.9.245); the walk proper is walk
    /// mode's.</summary>
    public float WalkSpeed => Actions != null ? Actions.Gait.JogAhead : 2.7f;
    /// <summary>The sprint's speed straight ahead (m/s), the fastest the legs go (its clip's, or the gun's run's).</summary>
    public float SprintSpeed => Actions != null ? Actions.Gait.SprintAhead : 6f;
    public float ProneSpeed => proneSpeed;
    public float SupineSpeed => supineSpeed;
    public float GroundSharpness => groundSharpness;
    public float GroundKeepPaceTurn => groundKeepPaceTurn;
    public float AirControlSharpness => airControlSharpness;

    public float SlideMinStartSpeed => slideMinStartSpeed;
    public float SlideFriction => slideFriction;
    public float DiveSlideFriction => diveSlideFriction;
    public float SupineSlideFriction => supineSlideFriction;
    public float AirSlideFrictionMultiplier => airSlideFrictionMultiplier;
    public float SlopeSlideGravity => slopeSlideGravity;
    public float SlideEndSpeed => slideEndSpeed;
    public float SlideCancelMaxSpeed => slideCancelMaxSpeed;
    public float DiveSlideCancelMaxSpeed => diveSlideCancelMaxSpeed;
    public float SupineSlideCancelMaxSpeed => supineSlideCancelMaxSpeed;
    public float SlideBrakeDeceleration => slideBrakeDeceleration;
    public float SlideCancelMinDuration => slideCancelMinDuration;
    public float SlideCooldown => slideCooldown;
    public float SlideCooldownTimer { get => Actions.slideCooldownTimer; set => Actions.slideCooldownTimer = value; }
    public float SlideSteerSharpness => slideSteerSharpness;
    public float SlideLookSteerRate => slideLookSteerRate;

    public float DoorBreakSpeedThreshold => doorBreakSpeedThreshold;
    public float DoorBreakSpeedRetention => doorBreakSpeedRetention;
    public float WallBounceBounciness => wallBounceBounciness;
    public float SupineBounceThreshold => supineBounceThreshold;

    public float StandingColliderHeight => Posture.standingColliderHeight;
    /// <summary>
    /// A seventy-second of the standing height (m). Distances and speeds ported from Beatrun are in units of its player,
    /// who stands 72 tall, so one of them times this keeps its share of the body at any capsule size.
    /// </summary>
    public float HeightUnit => StandingColliderHeight / 72f;
    public float CrouchColliderHeight => crouchColliderHeight;
    public float ProneColliderHeight => proneColliderHeight;

    public float SprintProgress { get => Actions.sprintProgress; set => Actions.sprintProgress = value; }
    /// <summary>The gait's speed straight ahead now (m/s), see MovementGait.</summary>
    public float RunSpeed => Actions != null ? Actions.Gait.Speed : WalkSpeed;
    /// <summary>The fastest the legs go straight ahead (m/s): the sprint's.</summary>
    public float TopRunSpeed => SprintSpeed;
    /// <summary>0–1: how far the player is into a sprint (Shift). Sent to the other PCs as the sprint itself: until 0.9.245 a
    /// run W built up counted too, so a friend's copy took the sprint pose that this PC's own body never did.</summary>
    public float RunIntent => Actions == null ? 0f : SprintProgress;

    private PlayerBody body;
    private PlayerBody Body => body != null ? body : body = GetComponent<PlayerBody>();
    // Whether the body has a sprint toward these keys (PlayerBody.SprintsToward): with a gun out, only where the gun runs.
    internal bool SprintsToward(Vector2 keys) => Body == null || Body.SprintsToward(keys);
    /// <summary>A gun out has the legs: its own walk, jog, run and crouched walk (PlayerBody, BodyMoves.csv).</summary>
    internal bool GunHasLegs => Body != null && Body.GunHasLegs;
    /// <summary>How fast the legs' own clips go (m/s); 0 for a body with none (PlayerBody.LegSpeed).</summary>
    internal float LegSpeed(float heading, float gait, bool crouched, float aim) => Body != null ? Body.LegSpeed(heading, gait, crouched, aim) : 0f;
    /// <summary>What a clip's own body does (PlayerBody.MotionOf); null for a body without it.</summary>
    internal ClipMotion MotionOf(string clip) => Body != null ? Body.MotionOf(clip) : null;
    /// <summary>How far the legs are on the gun's aim walk (0-1).</summary>
    internal float AimShare => Body != null ? Body.AimShare : 0f;
    /// <summary>How fast a clip that ends running carries the body now (m/s), 0 for none (PlayerBody.RunOutPace).</summary>
    internal float RunOutPace => Body != null ? Body.RunOutPace : 0f;
    /// <summary>The fall is a long one (Ability.bigFallHeight): wind, heartbeat, screen warp and flailing.</summary>
    public bool IsBigFall => Abilities != null && Abilities.Landing.IsBigFall;
    /// <summary>Fall speed from actual descent, excluding time held against an edge (m/s).</summary>
    public float FallSpeed => Abilities != null ? Abilities.Landing.FallSpeed : 0f;
    public float SlopeLimit => Motor.controller != null ? Motor.controller.slopeLimit : 45f;
    public float ControllerRadius => Motor.controller != null ? Motor.controller.radius : 0.5f;

    public InputAction SprintAction => Input.sprintAction;


    private void Awake()
    {
        // Nothing here takes input by message: the keys are read off their actions (KeyBindings, PlayerWeaponInput). On
        // Send Messages, PlayerInput still called "On" + every action's name on each part of this object, so a part's own
        // method of that name was handed an InputValue it never took and threw MissingMethodException on every press
        // (PlayerFace's OnShove once, ClassKit's OnFire for 제압 사격 on every click from 0.9.42 to 0.9.80). C# events
        // with no one listening send nothing, so no part's name can collide with an action again.
        if (TryGetComponent(out PlayerInput playerInput)) playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        EnsureInitialized();
    }
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
        // Combat components are composed here at runtime; their balance numbers come from Data/Resources/Tables/Player/Player.csv.
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
        PerfectDefense = GetComponent<PlayerPerfectDefense>().OrNull() ?? gameObject.AddComponent<PlayerPerfectDefense>();
        GameTables.Bind(TableName, "Defense", PerfectDefense);
        PerfectDefense.Initialize(this);
        if (Application.isPlaying && IsLocal && GetComponent<ParryFeedback>() == null) GameTables.Bind(TableName, "Parry", gameObject.AddComponent<ParryFeedback>());
        DodgePassThrough =GetComponent<DodgeEnemyPassThrough>().OrNull() ?? gameObject.AddComponent<DodgeEnemyPassThrough>();
        if (GetComponent<PlayerLeanHitboxes>() == null) gameObject.AddComponent<PlayerLeanHitboxes>();
        // The "[F] climb down" prompt over a ladder's top in reach (backlog 141).
        if (Application.isPlaying && IsLocal && GetComponent<LadderPromptHUD>() == null) gameObject.AddComponent<LadderPromptHUD>();
        // "[CTRL] 눌러 낙하" under the crosshair while a move the crouch key lets go of is on (2026-11-11).
        if (Application.isPlaying && IsLocal && GetComponent<ParkourPromptHUD>() == null) gameObject.AddComponent<ParkourPromptHUD>();
        // Stealth (2026-11-11): who is noticing them and how sure (marks over heads, wedges round the crosshair), the
        // sight cones and sound rings on the ground, and the stone thrown with T (friends' copies throw theirs too).
        if (Application.isPlaying && IsLocal && GetComponent<StealthMarkersHUD>() == null) gameObject.AddComponent<StealthMarkersHUD>();
        if (Application.isPlaying && IsLocal && GetComponent<StealthView>() == null) gameObject.AddComponent<StealthView>();
        // What is drawn through walls (the Ghost's sight, pings, holders, a friend's CHARGE outline) never takes this
        // player's own body, gun or scope lens for the wall on their own screen (OwnViewMask, 2026-11-14).
        if (Application.isPlaying && IsLocal && GetComponent<OwnViewMask>() == null) gameObject.AddComponent<OwnViewMask>();
        // What the body is doing with its hands, said to the hands' owner every frame (HandOwner): friends' copies too.
        if (Application.isPlaying && GetComponent<BodyHandFacts>() == null) gameObject.AddComponent<BodyHandFacts>();
        if (Application.isPlaying && GetComponent<PlayerThrow>() == null) gameObject.AddComponent<PlayerThrow>();
        // The class throwable on G (2026-11-12, GMod Horde's quick grenade); friends' copies throw theirs too.
        if (Application.isPlaying && GetComponent<PlayerGrenades>() == null) gameObject.AddComponent<PlayerGrenades>();
        // The step dodge seen in the world (afterimages, wind lines, the pop and smoke), friends' copies included.
        if (Application.isPlaying && GetComponent<DodgeDash>() == null) gameObject.AddComponent<DodgeDash>();
        // The class at work (stage 7): starting guns, the class key, adrenaline, CHARGE, a Point's guns reloading aside.
        if (Application.isPlaying && GetComponent<ClassKit>() == null) gameObject.AddComponent<ClassKit>();
        // The Garry's Mod HUD addons' modules (HudAddons.Boot), brought up with this player's HUD; the class UI is drawn in
        // their parts (HudAddons.PlayerHud.Class).
        if (Application.isPlaying && IsLocal) HudAddons.Boot.Install();
        // Callouts (memo 4, G&B's wheel): solo has no network, so it can't wait for PlayerNetworkCombat to add it.
        if (Application.isPlaying && GetComponent<PlayerCallouts>() == null) gameObject.AddComponent<PlayerCallouts>();
        // The visible body (true first person) appears once Resources/Characters/PlayerBody names a character.
        if (Application.isPlaying && GetComponent<PlayerBody>() == null && PlayerBodyProfile.Load() != null)
        {
            GameTables.Bind(TableName, "Body", gameObject.AddComponent<PlayerBody>());
            // Its hands hold the gun, the fists and ledges.
            GameTables.Bind(TableName, "Hands", gameObject.AddComponent<PlayerBodyHands>());
            // Its feet make the footsteps.
            GameTables.Bind(TableName, "Footsteps", gameObject.AddComponent<PlayerFootsteps>());
            // Its skirt, coat and hair hang free.
            GameTables.Bind(PlayerBodyCloth.TableName, PlayerBodyCloth.TableRow,
                gameObject.AddComponent<PlayerBodyCloth>(), gameObject.AddComponent<PlayerBodyClothColliders>());
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
            GameTables.Bind(PlayerSpectate.TableName, PlayerSpectate.TableRow, gameObject.AddComponent<PlayerSpectate>());
            gameObject.AddComponent<MoneyDrops>();
            gameObject.AddComponent<PerkCardScreen>(); // the perk cards a full gauge buys, and their health perk
            gameObject.AddComponent<PlayerLifeHUD>();
            gameObject.AddComponent<DeathScreen>(); // dying: our death card over the death screen addon (HudAddons.DeathScreenModule; Settings → HUD)
            gameObject.AddComponent<TeammateMarkerHUD>();
            gameObject.AddComponent<TeamHealthPanel>();
            gameObject.AddComponent<ScoreboardHUD>();
            gameObject.AddComponent<ShoveFatigueHUD>();
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
            // Teammates side by side keep a body's width apart, not two capsules' (backlog 524).
            GameTables.Bind(TableName, "Teammate", gameObject.AddComponent<TeammateSpacing>());
            GameTables.Bind(TableName, "FallScreen", gameObject.AddComponent<PlayerFallScreen>());
            // Rookie mode's view from behind through parkour, and the execution cinematic: this PC's screen only.
            gameObject.AddComponent<ThirdPersonCamera>();
        }
        if (controller.height < Posture.standingColliderHeight - 0.02f)
            ChangeState(controller.height <= proneColliderHeight + 0.02f ? MovementState.Proning : MovementState.Crouching);
        return true;
    }

    private void Update()
    {
        if (!EnsureInitialized()) return;
        if (IsNetworkDriven || Carried) { Posture.UpdateHeightsAndTransform(Time.deltaTime); return; }
        float dt = DeltaTime;
        if (dt <= 0f) return;
        // A window behind another one changes one thing here: the keys are not the game's, so the input frame reads empty
        // (InputAvailable asks InputFocus.TakesMove, which asks for the window's focus). The body goes on: it falls,
        // lands, finishes its move and stands. Until 2026-11-18 this whole Update returned without focus, a guard from the
        // movement's first days, before anything asked whose the keys were. An unfocused player then hung where it was,
        // in the air too, its last velocity still read by the body's run cycle and sent to the room; and a host is the
        // PC everyone else's world runs on (the user tests a room with two windows on one PC, so the host is unfocused
        // whenever the guest's window is used: "방장(호스트) 알트탭 시 허브 및 맵 전환 진행이 멈추는 현상").
        Input.Sample(InputAvailable && !ControlLocked, !IsGettingUp);
        Abilities.BeginFrame(dt);
        Actions.TickTimers(dt);
        Motor.TickTimers(dt);
        StateMachine.Duration += dt;
        Motor.CheckGroundedAndSlope();
        // The dead still fall, unless PlayerRagdoll has switched their capsule off.
        if (BodyLocked || !IsLocal || !DamageReceiver.IsAlive)
        {
            Input.Sample(false);
            Actions.Gait.Tick(dt, Vector2.zero);
            HorizontalVelocity = Vector3.zero;
            Motor.ApplyGravity(dt);
            Posture.UpdateHeightsAndTransform(dt);
            if (Motor.controller.enabled) Motor.Move(dt);
            return;
        }
        // Held by shamblers (PlayerHold): rooted where they stand, but V shoves (with fists, PlayerMeleeCombat has it).
        // Pinned by a runner they lie flat on their back where it knocked them, and can't fight it off (but for a perk).
        if (IsHeld)
        {
            bool fists = MeleeCombat != null && MeleeCombat.IsActive;
            bool pinned = IsPinned;
            // Pinned with shield left, V bursts the shield and throws the runners off, fists out or not (PlayerHold). Read
            // past the control lock: the pin keeps them in a locked heavy landing, where ShovePressed never comes true.
            if (pinned && InputAvailable && Input.ShoveKeyPressed && hold != null && hold.TryBurstShield()) { }
            else if (Input.ShovePressed && !fists && (!pinned || FightsPinned)) Abilities.Combat.TryShove();
            if (pinned) StayPinned();
            Input.Sample(false);
            Actions.Gait.Tick(dt, Vector2.zero);
            HorizontalVelocity = Vector3.zero;
            Motor.ApplyGravity(dt);
            Posture.UpdateHeightsAndTransform(dt);
            if (Motor.controller.enabled) Motor.Move(dt);
            return;
        }
        Vector2 input = CurrentInput;
        Actions.ProcessSprintRampUp(dt, input);
        Actions.Gait.Tick(dt, input);
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
        // Every Update has run, the network's moves of the others included: ride on whoever is underfoot.
        if (IsInitialized && !IsNetworkDriven && Time.deltaTime > 0f) Motor.Ride();
        if (Time.timeScale > 0f) Abilities?.Combat.SampleShove(0f);
    }
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Motor?.RecordContact(hit);
        StateMachine?.Current.OnColliderHit(hit);
        // Walking into a physics prop carries it along; standing on top of one does not drive it into the ground.
        var prop = hit.normal.y < .7f ? PhysicsProp.Of(hit.collider) : null;
        if (prop != null) prop.PushFromWalker(ActualVelocity, AbilitySettings.propPushForce, gameObject);
    }

    public void ChangeState(MovementState state) => StateMachine.ChangeState(state);

    // Pinned: down on the back (a leap that caught them in the air puts them there on landing), and held lying there,
    // short of getting up; once the pin ends the get-up plays on from there (HeavyLandingState).
    private void StayPinned()
    {
        if (CurrentState != MovementState.HeavyLanding)
        {
            if (IsGrounded) KnockBack(-transform.forward * .1f);
            return;
        }
        float lying = Abilities.Landing.HeavyDuration * PlayerHold.PinnedDownShare;
        if (CurrentStateDuration > lying) CurrentStateDuration = lying;
    }
    /// <summary>
    /// Lying on the ground, rolls from the front onto the back or back again; the head stays where it is (PlayerBody.Lying).
    /// PlayerLook asks for it when the mouse pushes on past the neck's limit. False when not lying still on the ground.
    /// </summary>
    public bool TryRollOver()
    {
        if (!IsGrounded || IsDowned || !InputAvailable) return false;
        if (CurrentState == MovementState.Proning) { ChangeState(MovementState.Supine); return true; }
        if (CurrentState == MovementState.Supine) { ChangeState(MovementState.Proning); return true; }
        return false;
    }
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
    private void RaiseStateChanged(MovementState from, MovementState to)
    {
        // Up off the back, Beatrun holds the player for a second while the body gets up (quickturn.lua, JumpTurnRecovery).
        // Here only moving off waits; the hands are free.
        if (from == MovementState.Supine && (to == MovementState.Standing || to == MovementState.Crouching) && !IsNetworkDriven)
            Actions.riseTimer = AbilitySettings.supineRiseTime;
        StateChanged?.Invoke(from, to);
    }
    internal void RaiseLanded(float impactSpeed) => Landed?.Invoke(impactSpeed);
    internal void RaiseJumped() => Jumped?.Invoke();
    internal void RaiseDashPushHit(Vector3 point) => DashPushHit?.Invoke(point);
    internal void RaiseLadderStepped(bool leftHand) => LadderStepped?.Invoke(leftHand);
    internal void RaiseLadderSlideStopped() => LadderSlideStopped?.Invoke();
    public void StartSlide(float speed, bool applyBoost = true) => Actions.StartSlide(speed, applyBoost);
    public void StartLandingSlide(MovementState state, float speed, Vector3 fallback) => Actions.StartLandingSlide(state, speed, fallback);
    public float GetSlideSlopeDot(Vector3 direction) => Motor.GetSlideSlopeDot(direction);
    public bool IsSlideStepContact(ControllerColliderHit hit) => Motor.IsSlideStepContact(hit);
    public bool HasCeilingAbove(float height) => Posture.HasCeilingAbove(height);
    public bool HasCeilingAhead(Vector3 direction, float distance, float height) => Posture.HasCeilingAhead(direction, distance, height);
    public bool HasVentCeilingAhead(Vector3 direction, float forwardDistance = 0.5f) => Posture.HasVentCeilingAhead(direction, forwardDistance);

    public void ResetVelocity()
    {
        PerfectDefense.OrNull()?.Cancel(); DodgePassThrough.OrNull()?.Finish();
        Execution.OrNull()?.Cancel();
        MeleeCombat.OrNull()?.CancelAction();
        if (!IsInitialized) return;
        if (IsNetworkDriven) { StateMachine.Mirror(MovementState.Standing); return; }
        if (Posture.airBodyTucked || Posture.airCrouching) Posture.RebaseAirCrouchFeet();
        Motor.Reset();
        Actions.Reset();
        if (IsDowned)
        {
            if (IsGrounded && FallsWhenDowned(CurrentState)) downedFallAt = Time.time;
            ChangeState(MovementState.Proning);
        }
        else if (!HasCeilingAbove(StandingColliderHeight)) ChangeState(MovementState.Standing);
        else if (!HasCeilingAbove(CrouchColliderHeight)) ChangeState(MovementState.Crouching);
        else ChangeState(MovementState.Proning);
        // Put back on the feet, not getting up off the back.
        Actions.riseTimer = 0f;
        Abilities?.Reset();
    }

    private void OnDisable()
    {
        PlayerRoster.Remove(this);
        ResetVelocity();
    }
}
