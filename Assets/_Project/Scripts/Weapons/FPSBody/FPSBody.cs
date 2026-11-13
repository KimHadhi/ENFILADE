using System.Collections.Generic;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using UnityEngine;

/// <summary>
/// The body holds its guns with KINEMATION FPS Animation Framework, set up on it as FPSAF's demo humanoid player is
/// (FPSHumanoidPlayer): FPSAnimator, its bone controller, input controller and playables controller (FPSAF's own, on the
/// Animator's graph, where PlayerBody plays the body's clips as the Animator output), the recoil animation, the rig with
/// its IK weapon bone, and per gun the demo's humanoid animator profile. FPSAF then poses the upper body every frame: the
/// gun's pose clip on the arms, the spine and head following the view (Look), the lean, the aim down the sight (Ads), sway,
/// recoil, the gun held back from walls (GunCover) and both arms on the gun (IK).
/// The clips are KINEMATION's own, remade for this body by KINEMATION Retarget Pro (GunBakes), and FPSAF's Pose Sampler
/// starts the gun where Retarget Pro put it on this body. The camera socket FPSAF aims the sight at is the body's eyes,
/// where the camera rides.
/// This component is the demo's FPSController for our player: it feeds FPSAF what our player does (the view, the walk,
/// the lean, aiming, sprinting, jumps and crouches) and tells the held gun (FPSGun, the demo's Weapon) when our
/// WeaponController draws it, fires it or reloads it. WeaponController keeps every rule; this only shows them.
/// Numbers come from Tables/FPSBody.csv, row Rig.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunHold)]
public sealed partial class FPSBody : MonoBehaviour, IWeaponPose
{
    public const string TableName = "FPSBody";
    private const string TableRow = "Rig";

    [SerializeField, Range(0f, 1f), Tooltip("Aiming a gun with a stock, how much of the way the eyes come down onto it, so the butt "
        + "stays in the shoulder (1) instead of the gun coming up to the eyes (0).")]
    private float cheekWeld = 1f;
    [SerializeField, Tooltip("The most the neck, upper chest and chest bend for the cheek weld (degrees).")]
    private Vector3 cheekWeldLimits = new Vector3(30f, 15f, 10f);
    [SerializeField, Min(0f), Tooltip("How long a finished reload takes to settle back into the hold (s). KINEMATION's 0.15 made the arms "
        + "jump from the reload to the hold.")]
    private float reloadSettle = .35f;
    [SerializeField, Range(0f, 90f), Tooltip("Furthest the spine twists toward the view where the legs don't turn with it (degrees). "
        + "FPSAF's Look reaches its full twist at 90.")]
    private float twistLimit = 60f;
    [SerializeField, Range(0f, 1f), Tooltip("How much of the view the spine follows while sprinting (FPSAF demo LookLayerWeight).")]
    private float sprintLookWeight = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("Sprinting from this share of the way between walk and sprint (PlayerMovement.SprintProgress).")]
    private float sprintFrom = .5f;
    [SerializeField, Min(0f), Tooltip("How quickly a reload cut short lets its clip go (s).")]
    private float reloadBlendOut = .15f;
    [SerializeField, Min(.001f), Tooltip("How deep the gun's front face is taken when the muzzle is found from the gun's shape (m).")]
    private float muzzleDepth = .015f;
    [SerializeField, Min(.01f), Tooltip("How quickly crouch and sprint weights follow the body (s).")]
    private float weightTime = .15f;
    [SerializeField, Min(.01f), Tooltip("How long FPSAF takes the upper body over when a gun is drawn, and gives it back when it is put away (s).")]
    private float holdBlendTime = .2f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of FPSAF's Sway (the gun trailing the look and the steps) kept at the hip. Down the "
        + "sights there is none, as in Rainbow Six Siege: the sight stays where the view is (user, 2026-11-09: \"흔들거려서\").")]
    private float hipSway = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of FPSAF's Additive layer (KINEMATION's recoil animation kicking the gun) kept down "
        + "the sights. Its turn and lift are already cut there (aimedRecoilTurn, aimedRecoilLift), so the gun can kick back in full.")]
    private float aimedKick = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of KINEMATION's recoil animation kicking the gun model at all (visual recoil): in full "
        + "it shook the gun more than the user liked (2026-11-09: \"비쥬얼 리코일 … 좀 쎄네\").")]
    private float gunKick = .6f;
    [SerializeField, Tooltip("Play KINEMATION's camera shake with each shot from the hip. Off: the view's kick (WeaponFeedback) is the "
        + "only shot shake, as in Rainbow Six Siege. Down the sights it never plays.")]
    private bool kinemationShake;
    [SerializeField, Range(0f, 1f), Tooltip("Down the sights, the share of KINEMATION's recoil animation turning the gun and wandering "
        + "that stays (times its own aiming multipliers): Siege's guns kick back into the shoulder with the sight on the hit point.")]
    private float aimedRecoilTurn = .2f;
    [SerializeField, Range(0f, 1f), Tooltip("Down the sights, the share of the recoil animation lifting and pushing the gun sideways "
        + "that stays; its kick straight back stays whole.")]
    private float aimedRecoilLift = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("Aiming more than this much, or on the way in, FPSAF's little moves (a stop, a crouch, a "
        + "landing, a jump, a lean) don't play: they swung the sight off the target. At 0.5 a stop begun early in the zoom bobbed "
        + "the sight all through it (user, 2026-11-11).")]
    private float aimedNoMotions;

    // Shots further apart than this many fire intervals have let go of the trigger: the recoil then settles.
    private const float TriggerReleaseIntervals = 1.5f;
    // KINEMATION's example controller: still below this speed.
    private const float StillSpeed = .1f;
    // A gun still held back more than this share holds its fire.
    private const float RaisedFiring = .05f;
    // Half-life (s) of a raised gun coming down when the player wants to fire or aim and the way is clear: fully raised
    // to RaisedFiring in about 0.11 s.
    private const float QuickLowerHalfLife = .025f;
    // A hip muzzle is learnt only within this angle of where the eyes look (degrees); a hip hold is well inside it.
    private const float HipMuzzleCone = 35f;
    // GunCoverLayer's input property (InputConfig_Body).
    private const string CoverProperty = "CoverWeight";
    // The Animator parameters FPSAF's profiles read (Characters/BodyHost).
    private static readonly int CrouchWeightId = Animator.StringToHash("CrouchWeight"), SprintPoseWeightId = Animator.StringToHash("SprintPoseWeight"),
        VelocityId = Animator.StringToHash("Velocity"), FullBodyWeightId = Animator.StringToHash("FullBodyWeight");

    private WeaponController controller;
    private PlayerMovement movement;
    private PlayerBody body;
    private FPSBodyAssets assets;
    private Transform weaponRoot, muzzle, lookFrame, eyes, view;
    // The player's own camera (a magnified sight's lens renders with its settings, GunScope).
    private Camera viewCamera;
    private Vector3 muzzleHomePosition;
    private Quaternion muzzleHomeRotation;

    // What is set up on the current body.
    private Animator animator;
    private FPSBodyRig rig;
    private UserInputController input;
    private FPSPlayablesController playables;
    private FPSBodyAnimator framework;
    private RecoilAnimation recoil;
    private FPSBodyCamera bodyCamera;
    private PlayerBodyHands hands;
    private readonly List<FPSGun> guns = new();
    private readonly List<Object> made = new();
    private readonly HashSet<string> reported = new();

    private FPSGun held;
    private WeaponData shownData;
    private bool shown = true, linked, aiming, firing, highReady, hasLook, wasGrounded = true, wasCrouched, wasMoving;
    private float lastShot, shotInterval, lastAim, lastYaw, lastPitch, leanSign, crouchWeight, sprintWeight, cover, holdWeight;
    // When the gun was last drawn, and how long FPSAF's draw motion plays (s): the hip muzzle is learnt after it.
    private float drawnAt, drawSettle;
    // How FPSAF last held the gun (KeepGunInView): both hands on it and their fingers, once it has held the gun drawn now
    // clean (its hold from the eyes is the gun's own, FPSGun.HoldFromEyes).
    private KTransform rightHandInGun, leftHandInGun;
    private bool hasGunInView;
    private readonly List<(Transform bone, bool left)> fingers = new();
    private readonly List<Quaternion> fingerGrip = new();

    /// <summary>Holds <paramref name="weapons"/>' guns with FPSAF and returns this as the gun's pose.</summary>
    internal static IWeaponPose Attach(WeaponController weapons, Transform weaponRoot, Transform muzzle)
    {
        if (weapons == null) return null;
        var component = weapons.GetComponent<FPSBody>();
        if (component == null) component = weapons.gameObject.AddComponent<FPSBody>();
        GameTables.Bind(TableName, TableRow, component);
        component.Begin(weapons, weaponRoot, muzzle);
        return component;
    }

    /// <summary>The FPSAF body on the player that owns <paramref name="weapons"/>, while it holds one of its guns.</summary>
    internal static FPSBody Holding(WeaponController weapons)
    {
        if (weapons == null) return null;
        var component = weapons.GetComponent<FPSBody>();
        return component != null && component.HoldsGun ? component : null;
    }

    private bool HoldsGun => held != null && linked && framework != null;

    private void Begin(WeaponController weapons, Transform root, Transform muzzlePoint)
    {
        controller = weapons;
        assets = FPSBodyAssets.Instance;
        weaponRoot = root;
        muzzle = muzzlePoint;
        movement = GetComponentInParent<PlayerMovement>();
        eyes = movement != null ? PlayerView.EyesOf(movement) : controller.AimOrigin;
        if (movement != null)
        {
            lookFrame = movement.transform.Find("CrouchPivot/BobPivot/LookPivot");
            if (lookFrame == null) lookFrame = movement.transform.Find("CrouchPivot/LookPivot");
        }
        var camera = GetComponentInChildren<Camera>(true);
        view = camera != null ? camera.transform : null;
        viewCamera = camera;
        if (muzzle != null) { muzzleHomePosition = muzzle.localPosition; muzzleHomeRotation = muzzle.localRotation; }
        controller.ReloadStarted += OnReloadStarted;
        controller.ReloadCompleted += OnReloadCompleted;
        controller.ReloadStepped += OnReloadStepped;
        controller.Holstering += OnHolstering;
        controller.InspectStarted += OnInspectStarted;
        controller.InspectStopped += OnInspectStopped;
        if (movement != null) movement.Jumped += OnJumped;
        // A pistol's close hold (FPSBody.CloseHold) takes its numbers from Tables/CloseHold.csv.
        BindCloseHold();
        // A shove with the gun out swings the gun (FPSBody.Bash).
        BeginBash();
        // The guns carried but not held are worn on the body for everyone to see (HolsteredGuns).
        if (Application.isPlaying && GetComponent<HolsteredGuns>() == null) gameObject.AddComponent<HolsteredGuns>();
        if (assets == null || assets.inputConfig == null || assets.rig == null || assets.upperBody == null || assets.idleProfile == null)
            Report("Resources/Combat/FPSBody is missing or incomplete; the body can't hold KINEMATION's guns.");
    }

    private void OnDestroy()
    {
        if (controller != null)
        {
            controller.ReloadStarted -= OnReloadStarted;
            controller.ReloadCompleted -= OnReloadCompleted;
            controller.ReloadStepped -= OnReloadStepped;
            controller.Holstering -= OnHolstering;
            controller.InspectStarted -= OnInspectStarted;
            controller.InspectStopped -= OnInspectStopped;
        }
        if (movement != null) movement.Jumped -= OnJumped;
        if (body != null) body.UpperBodyFromFramework = false;
        EndBash();
        ReleaseContacts();
        ForgetMade();
    }

    private PlayerBody Body
    {
        get
        {
            if (body == null) body = GetComponent<PlayerBody>();
            return body;
        }
    }

    private void Update()
    {
        if (controller == null || assets == null) return;
        var bodyAnimator = Body != null ? body.BodyAnimator : null;
        if (bodyAnimator != animator) Install(bodyAnimator);
        if (framework == null) return;
        float dt = Time.deltaTime;
        if (controller.EquippedData != shownData) Equip(controller.EquippedData);
        if (dt > 0f) Feed(dt);
        // FPSAF has the upper body (spine, both arms) only standing or crouched with both hands on the gun
        // (PlayerBodyHands.FrameworkShare). In the other moves (a vault, a slide, a dive, a roll) the move's own armed clip
        // poses it, as before KINEMATION, and the hands hold the gun where FPSAF last held it (KeepGunInView). FPSAF held
        // the whole upper body straight through those moves: a vault stood high with its hand off the obstacle, a slide sat
        // upright (user, 2026-11-07).
        body.UpperBodyFromFramework = HoldsGun && Framework >= .5f;
        // FPSAF's upper-body layer comes in with the gun and goes with it; the gun's pose is already full in it (its pose
        // asset blends in at once), so the body never passes through its bind pose on the way.
        holdWeight = Mathf.MoveTowards(holdWeight, HoldsGun ? 1f : 0f, dt / Mathf.Max(.01f, holdBlendTime));
        playables.SetControllerWeight(holdWeight * Framework);
        // Down the sights a bolt or pump worked after a shot sways the view only as much as it moves the gun (FPSBody.Contacts).
        if (bodyCamera != null) bodyCamera.SetGunWeight(holdWeight * Framework * CycleViewShare);
    }
}
