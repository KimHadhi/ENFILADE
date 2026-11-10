
using System;
using UnityEngine;

public readonly struct WeaponShot
{
    public readonly long AttackId;
    public readonly Vector3 Origin, Direction;
    public readonly AttackTraceResult Trace;
    /// <summary>Where the bullet went on after its first hit, through or off it (AttackTrace.Bullet); null when it stopped.</summary>
    public readonly BulletLeg[] Later;
    public readonly Vector2 Recoil;
    public readonly int AmmoAfterShot;
    /// <summary>
    /// Which of the shot's pellets this is (WeaponData.Pellets); 0 for a single bullet. The shot itself (its sound, flash,
    /// light, kick and brass) belongs to pellet 0; each pellet has its own trace, tracer and marks.
    /// </summary>
    public readonly int Pellet;

    public WeaponShot(
        long id,
        Vector3 origin,
        Vector3 direction,
        AttackTraceResult trace,
        Vector2 recoil,
        int ammo,
        BulletLeg[] later = null,
        int pellet = 0)
    {
        AttackId = id;
        Origin = origin;
        Direction = direction;
        Trace = trace;
        Later = later;
        Recoil = recoil;
        AmmoAfterShot = ammo;
        Pellet = pellet;
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(FrameOrder.Weapons)]
public sealed class WeaponController : MonoBehaviour
{
    [Header("Weapon Settings")]
    [SerializeField] private WeaponData weaponData;

    [Tooltip("Optional MonoBehaviour implementing IWeaponInput; otherwise finds one on this object.")]
    [SerializeField] private MonoBehaviour inputSource;

    [Tooltip("Camera used to choose the aim point. The actual shot starts at Muzzle Point.")]
    [SerializeField] private Transform aimOrigin;
    [Tooltip("Shot origin. Empty uses the MuzzleEffects object under this player.")]
    [SerializeField] private Transform muzzlePoint;
    [Tooltip("Gun model root: posed procedurally and hidden while no ranged weapon is equipped.")]
    [SerializeField] private Transform weaponRoot;
    [SerializeField, Min(0f)] private float missedShoveRecovery = .25f;

    [SerializeField] private LayerMask hitLayers = ~0;
    [Tooltip("Scene view only: draw each shot ray for 0.2 seconds.")]
    [SerializeField] private bool drawRays;

    public WeaponRuntime Runtime { get; private set; }
    /// <summary>
    /// What the crosshair is on (the middle of the screen), this frame. The body turns the gun to point there (FPSBody's
    /// GunConvergeLayer) and the shots go there from the muzzle. False until a ranged weapon has looked this frame.
    /// </summary>
    public Vector3 AimPoint { get; private set; }
    public bool HasAimPoint { get; private set; }
    public WeaponData EquippedData => Runtime != null ? Runtime.Data : null;
    public WeaponShot LastShot { get; private set; }
    public float AimBlend { get; private set; }
    public bool IsHighReady { get; private set; }
    public float CurrentSpread { get; private set; }
    public float MovementMultiplier => EquippedData == null ? 1f : Mathf.Lerp(1f, EquippedData.AdsMoveMultiplier, AimBlend);

    public event Action<WeaponShot> ShotFired;
    /// <summary>Trigger pulled on an empty magazine (no shot, no automatic reload).</summary>
    public event Action DryFired;
    /// <summary>Reload began; the argument is true for an empty-magazine reload.</summary>
    public event Action<bool> ReloadStarted;
    /// <summary>Reload finished and moved ammunition (not raised for a cancelled reload).</summary>
    public event Action ReloadCompleted;
    /// <summary>A shell reload moved on: the next shell goes in, or its end (WeaponRuntime.ReloadStep).</summary>
    public event Action<WeaponRuntime.ReloadStep> ReloadStepped;
    /// <summary>B switched the held gun's fire mode.</summary>
    public event Action<WeaponFireMode> FireModeChanged;
    /// <summary>The customize menu (C) switched the held gun's sight.</summary>
    public event Action SightChanged;
    /// <summary>The customize menu put a muzzle or underbarrel part on, or L switched the flashlight.</summary>
    public event Action AttachmentChanged;

    /// <summary>The held gun goes away over this many seconds (its holster clip); PlayerWeaponSlots then draws the next.</summary>
    public event Action<float> Holstering;
    /// <summary>Y: the held gun is looked over (its inspect clip).</summary>
    public event Action InspectStarted;
    /// <summary>A shot, an aim, a reload or a move that needs the hands cut the look over short.</summary>
    public event Action InspectStopped;
    /// <summary>The gun is coming out (Draw Duration) or going away (Holster Duration): it can't fire, aim or reload.</summary>
    public bool IsSwitching => switchLeft > 0f;
    private float switchLeft, inspectLeft;
    private bool holstering, inspecting;

    /// <summary>
    /// Puts the held gun away over its Holster Duration, ending a reload as Siege's swap does. Returns the seconds it takes,
    /// or 0 when it goes at once (no holster time, nothing shown, a melee weapon).
    /// </summary>
    public float BeginHolster()
    {
        if (Runtime == null || Runtime.Data.Category == WeaponCategory.Melee || !ShowsWeapon || Runtime.Data.HolsterDuration <= 0f) return 0f;
        if (holstering) return switchLeft;
        StopInspecting();
        Runtime.Interrupt();
        AimBlend = 0f;
        holstering = true;
        switchLeft = Runtime.Data.HolsterDuration;
        Holstering?.Invoke(switchLeft);
        return switchLeft;
    }

    /// <summary>Y: looks the held gun over, if it is up and idle.</summary>
    public void Inspect()
    {
        if (Runtime == null || Runtime.Data.Category == WeaponCategory.Melee || !ShowsWeapon || inspecting || switchLeft > 0f
            || Runtime.IsReloading || AimBlend > .01f || IsHighReady) return;
        inspecting = true;
        inspectLeft = 10f;
        InspectStarted?.Invoke();
    }

    /// <summary>How long the look over the body plays (FPSBody, from its clip); it is over by itself after that.</summary>
    internal void SetInspectLength(float seconds)
    {
        if (!inspecting) return;
        if (seconds <= 0f) { inspecting = false; return; }
        inspectLeft = seconds;
    }

    private void StopInspecting()
    {
        if (!inspecting) return;
        inspecting = false;
        InspectStopped?.Invoke();
    }

    /// <summary>B: the held gun's next fire mode (WeaponData Fire Modes), unless it is being reloaded.</summary>
    public void CycleFireMode()
    {
        if (Runtime == null || Runtime.IsReloading || Runtime.Data.Category == WeaponCategory.Melee) return;
        if (Runtime.CycleFireMode()) FireModeChanged?.Invoke(Runtime.FireMode);
    }

    /// <summary>The gun of ours drawn now (its sights and parts), for the customize menu; null without one.</summary>
    internal FPSGun HeldGun => presentation is FPSBody body ? body.Held : null;

    /// <summary>The customize menu is open for this player: the body holds the gun out before the eyes to show it.</summary>
    internal bool Customizing { get; set; }

    /// <summary>Why the held gun's parts can't be changed now, or null (the customize menu says it).</summary>
    internal string CustomizeRefusal => Runtime == null || EquippedData == null ? "총을 들고 있지 않아요"
        : Runtime.IsReloading ? "장전 중에는 바꿀 수 없어요" : IsSwitching ? "총을 꺼내는 중이에요" : null;

    /// <summary>
    /// The customize menu (C, GunCustomizeMenu) puts sight, muzzle part or underbarrel part <paramref name="index"/> on the
    /// held gun. False when it can't be changed now or it is on already; the click plays as for a sight swap.
    /// </summary>
    internal bool ChooseSight(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowSight(EquippedData, index)) return false;
        SightChanged?.Invoke();
        return true;
    }

    internal bool ChooseMuzzle(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowMuzzle(EquippedData, index)) return false;
        AttachmentChanged?.Invoke();
        return true;
    }

    internal bool ChooseUnderbarrel(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowUnderbarrel(EquippedData, index)) return false;
        AttachmentChanged?.Invoke();
        return true;
    }

    internal bool ChooseGrip(int index)
    {
        if (CustomizeRefusal != null || !(presentation is FPSBody body) || !body.ShowGrip(EquippedData, index)) return false;
        AttachmentChanged?.Invoke();
        return true;
    }

    /// <summary>The held gun's grip (-1 for no choice), for friends' PCs, and a friend's copy taking theirs.</summary>
    public int GripIndex => presentation is FPSBody body && EquippedData != null ? body.GripOf(EquippedData) : -1;

    internal void ShowGrip(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowGrip(EquippedData, index);
    }

    /// <summary>The held gun's shown sight (GunSights), -1 for a gun without a choice of sights: for friends' PCs.</summary>
    public int SightIndex => presentation is FPSBody body && EquippedData != null ? body.SightOf(EquippedData) : -1;

    /// <summary>A friend's copy: the sight their PC shows on the held gun (PlayerNetworkCombat).</summary>
    internal void ShowSight(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowSight(EquippedData, index);
    }

    /// <summary>A friend's copy: the fire mode their PC has the held gun in (PlayerNetworkCombat).</summary>
    internal void ShowFireMode(WeaponFireMode mode) => Runtime?.ShowFireMode(mode);

    /// <summary>L: the held gun's flashlight on or off.</summary>
    public void ToggleLight()
    {
        if (Runtime == null || !(presentation is FPSBody body)) return;
        if (body.ToggleLight()) AttachmentChanged?.Invoke();
    }

    /// <summary>
    /// The held gun wears a suppressor: its shot is heard by zombies at the gun's Suppressed Noise share, sounds quieter and
    /// throws no flash or light (WeaponFeedback, MuzzleFlash, MuzzleLight).
    /// </summary>
    public bool Suppressed => presentation is FPSBody body && body.HeldSuppressed;

    /// <summary>The held gun's muzzle and underbarrel parts (-1 for no choice) and its flashlight, for friends' PCs.</summary>
    public int MuzzleIndex => presentation is FPSBody body && EquippedData != null ? body.MuzzleOf(EquippedData) : -1;
    public int UnderbarrelIndex => presentation is FPSBody body && EquippedData != null ? body.UnderbarrelOf(EquippedData) : -1;
    public bool LightOn => presentation is FPSBody body && EquippedData != null && body.LightOf(EquippedData);

    /// <summary>A friend's copy: the parts and flashlight their PC has on the held gun (PlayerNetworkCombat).</summary>
    internal void ShowMuzzle(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowMuzzle(EquippedData, index);
    }

    internal void ShowUnderbarrel(int index)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowUnderbarrel(EquippedData, index);
    }

    internal void ShowLight(bool on)
    {
        if (presentation is FPSBody body && EquippedData != null) body.ShowLight(EquippedData, on);
    }

    // The held gun's grip (GunAttachments).
    private GunAttachments.Kind Grip => presentation is FPSBody body ? body.HeldGripKind : GunAttachments.Kind.None;

    /// <summary>How long the sight takes to come up (s): the gun's Ads Transition Time, sooner with an angled grip on.</summary>
    public float AdsTime => weaponData == null ? .16f
        : weaponData.AdsTransitionTime * (Grip == GunAttachments.Kind.AngledGrip ? weaponData.GripAimTime : 1f);

    // How far zombies hear this gun's shot: a suppressor cuts it to the gun's share.
    private float ShotNoise => weaponData.NoiseRadius * (Suppressed ? weaponData.SuppressedNoise : 1f);
    public Transform MuzzlePoint => muzzlePoint;
    public Transform AimOrigin => aimOrigin;
    /// <summary>The posed gun model root; the body's hands hold it at the weapon's grip points.</summary>
    public Transform WeaponRoot => weaponRoot;
    /// <summary>A ranged weapon is out and its model is shown.</summary>
    public bool ShowsWeapon => weaponRoot != null && weaponRoot.gameObject.activeInHierarchy
        && Runtime != null && Runtime.Data.Category != WeaponCategory.Melee;

    private IWeaponInput input;
    private PlayerDamageReceiver health;
    private GameObject owner;
    private PlayerMovement movement;
    private PlayerLook look;
    private IWeaponPose presentation;
    private DynamicCrosshairView crosshair;
    private Camera viewCamera;
    private PlayerCameraEffects cameraEffects;
    private Transform eyes;
    // The gun is posed from the eyes; the shot goes where the camera's ray (the crosshair) meets something.
    private Transform PoseView => eyes != null ? eyes : aimOrigin;
    // A move that needs both hands puts the gun away until this long after it ends (s), so a wall kick or a ledge jump
    // that lasts a frame doesn't flash it back.
    private const float BareHandsLinger = .25f;
    // A raised gun comes down only once its lowered pose is this clear of what raised it (m), so breathing and sway at
    // the edge don't flick it up and down.
    private const float RaisedClearance = .04f;
    // ...and stays up at least this long after it clears (s): walking, the head bobs across the edge with every step.
    private const float RaisedHold = .5f;
    // The hold is only for the edge. A gun this clear (m) is past anything the head bob reaches, so it comes down at once,
    // and so does one the player wants back (fire or sights) as soon as it is clear (user, 2026-11-09: the gun stayed up
    // far too long after it could fire again).
    private const float WideClearance = .12f;
    // A shot asked for while the gun is coming back down goes off as soon as it is down, if within this long (s), so the
    // press that brought it down isn't lost on a semi-automatic gun.
    private const float QueuedFireWindow = .3f;
    private float raisedHoldLeft, queuedFireLeft;
    // How long an empty magazine has waited with the gun ready to fire (s), for the automatic reload.
    private float emptyReady;
    private float bloom, timeSinceShot, shoveRecovery, bareHandsLeft;
    private int recoilIndex, dodgeActivation;
    private bool survivalInterrupted, shownAiming;

    [NonSerialized] private bool initialized;

    [SerializeField, HideInInspector] private int savedAmmo = -1;
    [SerializeField, HideInInspector] private int savedReserve = -1;
    [SerializeField, HideInInspector] private WeaponData savedAmmoData;

    private void Start() => Initialize();

    private void OnEnable()
    {
        if (Application.isPlaying)
            Initialize();
    }

    private void Initialize()
    {
        if (Runtime != null || initialized)
            return;

        initialized = true;

        var player = GetComponentInParent<PlayerMovement>();
        movement = player;
        owner = player != null ? player.gameObject : gameObject;
        look = owner.GetComponent<PlayerLook>();
        health = owner.GetComponent<PlayerDamageReceiver>();

        if (aimOrigin == null)
        {
            var camera = owner.GetComponentInChildren<Camera>();

            if (camera != null)
                aimOrigin = camera.transform;
        }

        input = inputSource != null
            ? inputSource as IWeaponInput
            : GetComponent<IWeaponInput>();

        if (aimOrigin == null || input == null)
        {
            Debug.LogError(
                "WeaponController needs an aim Camera/Transform and IWeaponInput on its object.",
                this);
            return;
        }

        if (muzzlePoint == null) muzzlePoint = owner.GetComponentInChildren<MuzzleEffects>(true)?.transform;
        // The gun rides the eyes, which the camera leaves behind in the F5 view, so it stays in the body's hands.
        eyes = Application.isPlaying && movement != null ? PlayerView.EyesOf(movement) : null;
        if (eyes == aimOrigin) eyes = null;
        if (eyes != null && weaponRoot != null && weaponRoot.parent == aimOrigin) weaponRoot.SetParent(eyes, false);
        // KINEMATION FPS Animation Framework holds the gun on the body (FPSBody); its Sway layer sways it.
        presentation = FPSBody.Attach(this, weaponRoot, muzzlePoint);
        viewCamera = aimOrigin.GetComponent<Camera>();
        cameraEffects = owner.GetComponentInChildren<PlayerCameraEffects>();
        crosshair = movement?.IsLocal != false ? DynamicCrosshairView.FindHud() : null;
        dodgeActivation = movement?.Abilities?.Dodge.Activation ?? 0;
        // Shot/reload presentation (sound, muzzle, brass, camera kick) subscribes to this controller's events.
        if (Application.isPlaying && GetComponent<WeaponFeedback>() == null) gameObject.AddComponent<WeaponFeedback>();
        if (Application.isPlaying && GetComponent<MuzzleLight>() == null) gameObject.AddComponent<MuzzleLight>();
        if (Application.isPlaying && GetComponent<MuzzleFlash>() == null) gameObject.AddComponent<MuzzleFlash>();

        if (weaponData == null) return;
        Runtime = new WeaponRuntime(
            weaponData,
            savedAmmoData == weaponData && savedAmmo >= 0
                ? savedAmmo
                : (int?)null, savedAmmoData == weaponData && savedReserve >= 0 ? savedReserve : (int?)null);
    }

    private void LateUpdate()
    {
        bool bare = TraversalHands.For(movement) == TraversalHandUse.Bare;
        if (bare && bareHandsLeft <= 0f) Runtime?.Interrupt();
        bareHandsLeft = bare ? BareHandsLinger : Mathf.Max(0f, bareHandsLeft - Time.deltaTime);
        presentation?.SetVisible(Runtime != null && Runtime.Data.Category != WeaponCategory.Melee && bareHandsLeft <= 0f);
        if (Runtime == null) { ResetPresentation(); return; }

        if (health != null && !health.IsAlive)
        {
            Runtime.Interrupt();
            ResetPresentation();
            return;
        }

        var slots = GetComponent<PlayerWeaponSlots>();
        if (slots != null && !slots.CanReadInput) Runtime.CancelBurst();
        if (input == null) return;
        ProcessFrame(movement != null ? movement.DeltaTime : Time.deltaTime, movement != null && movement.IsNetworkDriven ? new WeaponInputFrame(false, false, false, shownAiming) : input.ReadInput());
    }

    /// <summary>On someone else's copy: they raised or lowered their sights.</summary>
    internal void ShowAiming(bool aiming) => shownAiming = aiming;

    /// <summary>On someone else's copy: their reload began, or stopped.</summary>
    internal void ShowReload(bool reloading, bool empty)
    {
        if (Runtime == null || reloading == Runtime.IsReloading) return;
        if (!reloading) { Runtime.Interrupt(); return; }
        Runtime.ShowReload(empty);
        ReloadStarted?.Invoke(empty);
    }

    /// <summary>
    /// On someone else's copy: they fired. The muzzle, the tracer, the sound and the bullet's mark happen here, the same
    /// way as for this PC's own shots; their PC already judged the hit.
    /// </summary>
    internal void ShowShot(Vector3 origin, Vector3 direction, int pellet = 0)
    {
        if (Runtime == null || weaponData == null) return;
        // The same path their PC drew, walls passed and ricochets included: the randomness comes from the shot itself.
        var look = new DamageHit(0, 0f, DamageKind.Attack, origin, direction, owner, method: DamageMethod.Bullet);
        var trace = AttackTrace.Bullet(look, origin, direction, weaponData.Range, hitLayers, 1f, weaponData.Penetration, false, out BulletLeg[] later);
        LastShot = new WeaponShot(DamageHit.NewAttackId(), muzzlePoint != null ? muzzlePoint.position : origin, direction, trace, Vector2.zero, Runtime.Ammo, later, pellet);
        // The host's zombies hear a friend's shot as it reaches this PC (ZombieMind), once per shot however many pellets.
        if (pellet == 0) Noise.Make(origin, ShotNoise, owner);
        ShotFired?.Invoke(LastShot);
        // The gun's kick once per shot, however many pellets it threw.
        if (pellet == 0) presentation?.OnShot(weaponData, Vector2.zero);
    }

    public void Equip(WeaponRuntime weapon)
    {
        Initialize();
        look?.ClearRecoil();
        Runtime?.Interrupt();
        ResetPresentation();

        Runtime = weapon;
        weaponData = weapon != null ? weapon.Data : null;
        // Coming out: no shot, aim or reload until it is up (Draw Duration, its draw clip on the body).
        StopInspecting();
        holstering = false;
        switchLeft = weapon != null && weapon.Data.Category != WeaponCategory.Melee ? weapon.Data.DrawDuration : 0f;
        savedAmmoData = weaponData;
        savedAmmo = weapon != null ? weapon.Ammo : -1;
        savedReserve = weapon != null ? weapon.ReserveAmmo : -1;

        if (input is PlayerWeaponInput playerInput)
            playerInput.RequireTriggerRelease();
    }

    internal void ProcessFrame(float dt, WeaponInputFrame frame)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        // Held by a shambler the gun can't be used either (PlayerHold).
        var mover = owner != null ? owner.GetComponent<PlayerMovement>() : null;
        if (mover != null && (mover.CombatInputLocked || mover.IsHeld))
        { Runtime?.Interrupt(); ResetPresentation(); return; }
        if (Runtime == null || Runtime.Data.Category == WeaponCategory.Melee ||
            aimOrigin == null ||
            !isActiveAndEnabled ||
            (health != null && !health.IsAlive))
            return;

        var dodge = movement?.Abilities?.Dodge;
        int activation = dodge?.Activation ?? 0;
        if (activation != dodgeActivation)
        {
            if (dodge.Kind == DodgeKind.Step) InterruptForSurvival();
            dodgeActivation = activation;
        }
        bool stepDodging = dodge != null && dodge.Kind == DodgeKind.Step && dodge.IsInvulnerable;
        shoveRecovery = Mathf.Max(0f, shoveRecovery - dt);
        switchLeft = Mathf.Max(0f, switchLeft - dt);
        bloom = Mathf.MoveTowards(bloom, 0f, weaponData.BloomRecovery * dt);
        timeSinceShot += dt;
        if (timeSinceShot > .35f) recoilIndex = 0;
        // Coming out or going away holds the gun back like a move that needs the hands.
        bool bodyAction = bareHandsLeft > 0f || switchLeft > 0f || (movement != null && (movement.IsParkour
            || movement.CurrentState == PlayerMovement.MovementState.DashPush || movement.IsLandingRecovery));
        // A look over the gun ends by itself, or as soon as the hands are wanted for anything else.
        if (inspecting)
        {
            inspectLeft -= dt;
            if (inspectLeft <= 0f) inspecting = false;
            else if (frame.FirePressed || frame.FireHeld || frame.AimHeld || frame.ReloadPressed || bodyAction || stepDodging) StopInspecting();
        }
        // Asked for like an R press, so whatever holds a reload back now holds this one back until it lets go.
        if (AutoReloadDue(dt)) frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, true, frame.AimHeld);
        if (survivalInterrupted || bodyAction || stepDodging || shoveRecovery > 0f)
            frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, false, frame.AimHeld);
        survivalInterrupted = false;
        bool aiming = frame.AimHeld && !Runtime.IsReloading && !bodyAction && shoveRecovery <= 0f
            && !stepDodging && !frame.ReloadPressed;
        float nextAim = Mathf.MoveTowards(AimBlend, aiming ? 1f : 0f, dt / AdsTime);
        Vector3 target = AimTarget(weaponData.Range);
        AimPoint = target;
        HasAimPoint = true;
        // Probe the requested sight position even while high ready suppresses ADS. Otherwise
        // a clear hip pose and blocked ADS pose can alternate, causing zoom/ready flicker.
        Vector3 nominalMuzzle = presentation != null ? presentation.Prepare(PoseView, weaponData, aiming ? 1f : nextAim) : PoseView.position;
        float margin = IsHighReady ? RaisedClearance : 0f;
        bool blocked = WeaponAimSolver.IsObstructed(owner, PoseView, nominalMuzzle, target, hitLayers, margin);
        AimBlend = blocked || presentation?.IsHighReady == true ? 0f : nextAim;
        if (presentation != null)
        {
            nominalMuzzle = presentation.Prepare(PoseView, weaponData, AimBlend);
            blocked |= WeaponAimSolver.IsObstructed(owner, PoseView, nominalMuzzle, target, hitLayers, margin);
            if (blocked && AimBlend > 0f)
            { AimBlend = 0f; presentation.Prepare(PoseView, weaponData, 0f); }
        }
        bool readyToAct = !Runtime.IsReloading && !bodyAction && shoveRecovery <= 0f && !stepDodging;
        bool wantsGun = aiming || (readyToAct && (frame.FirePressed || frame.FireHeld));
        if (blocked) raisedHoldLeft = RaisedHold;
        else if (IsHighReady && raisedHoldLeft > 0f)
        {
            if (wantsGun || !WeaponAimSolver.IsObstructed(owner, PoseView, nominalMuzzle, target, hitLayers, WideClearance))
                raisedHoldLeft = 0f;
            else { raisedHoldLeft -= dt; blocked = true; }
        }
        bool quickLower = !blocked && wantsGun;
        IsHighReady = presentation != null ? presentation.Pose(owner, PoseView, target, blocked, Runtime, dt, hitLayers, quickLower) : blocked;
        if (IsHighReady) AimBlend = 0f;
        bool cannotFire = IsHighReady || shoveRecovery > 0f || bodyAction;
        if (cannotFire) Runtime.CancelBurst();
        if (blocked || !readyToAct) queuedFireLeft = 0f;
        else if (frame.FirePressed && IsHighReady) queuedFireLeft = QueuedFireWindow;
        else if (!cannotFire && queuedFireLeft > 0f)
        { frame = new WeaponInputFrame(true, frame.FireHeld, frame.ReloadPressed, frame.AimHeld); queuedFireLeft = 0f; }
        else queuedFireLeft = Mathf.Max(0f, queuedFireLeft - dt);
        bool reloadingBeforeTick = Runtime.IsReloading;
        // The trigger during a shell reload ends it once the shell in hand is in (KINEMATION's TacticalShotgun plays its end).
        if (reloadingBeforeTick && !cannotFire && (frame.FirePressed || frame.FireHeld)) Runtime.StopAfterShell();
        if (cannotFire || reloadingBeforeTick)
            frame = new WeaponInputFrame(false, false, frame.ReloadPressed, frame.AimHeld);
        UpdateSpread();

        bool wasReloading = Runtime.IsReloading;
        int stepsBefore = Runtime.StepCount;
        int shots = Runtime.Tick(dt, frame);

        if (!wasReloading && Runtime.IsReloading) ReloadStarted?.Invoke(Runtime.IsEmptyReload);
        else if (wasReloading && !Runtime.IsReloading) ReloadCompleted?.Invoke();
        else if (Runtime.IsReloading && Runtime.StepCount != stepsBefore) ReloadStepped?.Invoke(Runtime.Step);
        if (shots == 0 && frame.FirePressed && Runtime.Ammo == 0 && !Runtime.IsReloading) DryFired?.Invoke();

        for (int i = 0; i < shots; i++)
        {
            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : PoseView.position;
            target = AimTarget(weaponData.Range);
            if (WeaponAimSolver.IsObstructed(owner, PoseView, origin, target, hitLayers))
            { Runtime.RefundUnfiredShots(shots - i); IsHighReady = true; AimBlend = 0f; break; }
            // From the muzzle to what the crosshair is on, which stays in the middle of the screen: the gun turns to point
            // there (FPSBody's GunConvergeLayer), as the gun did before KINEMATION (user, 2026-11-09: "총이 조준선을 옮기면
            // 안 되고 조준선 쪽으로 총이 이동해야지").
            Vector3 toSight = (target - origin).normalized;
            Vector3 aimed = WeaponAimSolver.SpreadDirection(toSight, CurrentSpread, UnityEngine.Random.insideUnitCircle);
            // Siege's recoil is nearly the same down the sights as from the hip; aiming only steadies it a little.
            Vector2 recoil = weaponData.RecoilKick(recoilIndex++) * Mathf.Lerp(1f, .85f, AimBlend)
                * (Crouched ? weaponData.CrouchRecoil : 1f);
            // A vertical grip steadies the climb (Siege's).
            if (Grip == GunAttachments.Kind.VerticalGrip) recoil.x *= weaponData.GripRecoil;

            // A shotgun's pellets each fly their own way round where the shot goes, each a bullet of its own (own attack,
            // own hit); one shot of a single-bullet gun is pellet 0 on its own. The first pellet flies dead on where the
            // shot goes and the rest gather toward the middle of their cone (half of them inside half its radius, where an
            // even scatter puts a quarter), so a close shot lands as one clump on the aim point and opens up with distance
            // (user, 2026-11-10: the even scatter read as random and weak; a slug's feel was the pick).
            int pellets = weaponData.Pellets;
            for (int pellet = 0; pellet < pellets; pellet++)
            {
                long id = DamageHit.NewAttackId();
                Vector3 direction = aimed;
                if (pellet > 0)
                {
                    Vector2 scatter = UnityEngine.Random.insideUnitCircle;
                    direction = WeaponAimSolver.SpreadDirection(aimed, weaponData.PelletSpread, scatter * scatter.magnitude);
                }

                // Body damage falls off with range, as in Rainbow Six Siege; a headshot kills at any range there, so the
                // head's multiplier makes up what the range took (WeaponData.FalloffAt).
                float falloff = AttackTrace.FirstExternalHit(owner, origin, direction, weaponData.Range, hitLayers, out RaycastHit first)
                    ? weaponData.FalloffAt(first.distance) : 1f;
                var attack = new DamageHit(
                    id,
                    weaponData.Damage * falloff,
                    DamageKind.Attack,
                    origin,
                    direction,
                    owner,
                    postureDamage: weaponData.PostureDamage,
                    method: DamageMethod.Bullet);

                var trace = AttackTrace.Bullet(
                    attack,
                    origin,
                    direction,
                    weaponData.Range,
                    hitLayers, weaponData.HeadshotMultiplier / Mathf.Max(.01f, falloff), weaponData.Penetration, true, out BulletLeg[] later);

                var shot = new WeaponShot(
                    id,
                    origin,
                    direction,
                    trace,
                    recoil,
                    Runtime.Ammo + shots - i - 1,
                    later,
                    pellet);

                LastShot = shot;
                PushProp(trace, direction, 1f);
                if (later != null)
                    foreach (var leg in later) PushProp(leg.Trace, leg.Onward.Direction, leg.Share);

                if (drawRays)
                {
                    Debug.DrawRay(
                        origin,
                        trace.End - origin,
                        trace.HasHit ? Color.red : Color.cyan,
                        0.2f);
                }

                // Zombies hear it (ZombieMind), once per shot however many pellets.
                if (shot.Pellet == 0) Noise.Make(origin, ShotNoise, owner);
                // Presentation (WeaponFeedback, MuzzleEffects, impact and blood effects) subscribes here.
                ShotFired?.Invoke(shot);
            }
            bloom = Mathf.Min(weaponData.MaxBloom, bloom + weaponData.BloomPerShot);
            timeSinceShot = 0f;
            look?.ApplyRecoil(recoil, weaponData);
            presentation?.OnShot(weaponData, recoil);
            UpdateSpread();
        }
        UpdateCrosshair();
        cameraEffects?.RefreshWeaponFov();
    }

    // ArcCW's automatic reload (arccw_automaticreload): an empty magazine with rounds in reserve reloads 0.2 s after the
    // gun could fire again. Someone else's copy reloads when their PC says so.
    private bool AutoReloadDue(float dt)
    {
        bool waiting = weaponData.AutoReloadDelay >= 0f && (movement == null || !movement.IsNetworkDriven)
            && Runtime.Ammo == 0 && Runtime.ReserveAmmo > 0 && !Runtime.IsReloading && Runtime.CooldownRemaining <= 0f;
        emptyReady = waiting ? emptyReady + dt : 0f;
        return waiting && emptyReady > weaponData.AutoReloadDelay;
    }

    // ArcCW's crouch rule: on the ground and crouched.
    private bool Crouched => movement != null && movement.IsGrounded && movement.IsCrouching;

    // A loose prop the bullet struck takes the share of the push the bullet still carried.
    private void PushProp(in AttackTraceResult trace, Vector3 direction, float share)
    {
        var prop = trace.HasHit ? PhysicsProp.Of(trace.Hit.collider) : null;
        if (prop != null) prop.Push(direction.normalized * (weaponData.PropImpulse * share), trace.Hit.point);
    }

    // The crosshair's ray. In the F5 view it starts level with the eyes, so nothing between the camera and the player
    // catches it; in first person the eyes are the camera.
    private Vector3 AimTarget(float range)
    {
        Vector3 start = aimOrigin.position, forward = aimOrigin.forward;
        if (eyes != null) start += forward * Mathf.Max(0f, Vector3.Dot(eyes.position - start, forward));
        return AttackTrace.FirstExternalHit(owner, start, forward, range, hitLayers, out RaycastHit hit)
            ? hit.point : start + forward * range;
    }

    public void InterruptForSurvival(bool shove = false)
    {
        Runtime?.Interrupt(); AimBlend = 0f;
        cameraEffects?.RefreshWeaponFov();
        crosshair?.SetCombatHidden(false);
        survivalInterrupted = true;
        if (shove) shoveRecovery = missedShoveRecovery;
    }
    public void ConfirmShoveHit() { shoveRecovery = 0f; }
    private void UpdateSpread()
    {
        float moving = movement == null ? 0f : Mathf.Clamp01(new Vector2(movement.ActualVelocity.x, movement.ActualVelocity.z).magnitude / Mathf.Max(.1f, movement.WalkSpeed));
        float air = movement != null && !movement.IsGrounded ? weaponData.AirborneSpread : 0f;
        // Down the sights the shot goes where the sight is, as Rainbow Six Siege's does: walking and firing add nothing
        // there, the recoil climbing the view is the whole of a spray's difficulty. Only a jump still throws it off, at half.
        float loose = 1f - AimBlend;
        CurrentSpread = (Mathf.Lerp(weaponData.HipSpread, weaponData.AdsSpread, AimBlend)
            + (bloom + moving * weaponData.MovingSpread) * loose + air * Mathf.Lerp(1f, .5f, AimBlend))
            * (Crouched ? weaponData.CrouchSpread : 1f);
    }
    private void UpdateCrosshair()
    {
        if (crosshair == null) return;
        crosshair.SetCombatHidden(AimBlend > .001f);
        if (viewCamera == null) return;
        float pixels = Mathf.Tan(CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad) * viewCamera.pixelHeight * .5f;
        var canvas = crosshair.GetComponentInParent<Canvas>();
        crosshair.SetDynamicSpread(pixels / (canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f));
    }
    private void ResetPresentation()
    {
        AimBlend = bloom = shoveRecovery = CurrentSpread = raisedHoldLeft = queuedFireLeft = emptyReady = 0f; recoilIndex = 0; IsHighReady = false;
        HasAimPoint = false;
        presentation?.Reset(); crosshair?.SetDynamicSpread(0f);
        crosshair?.SetCombatHidden(false);
        cameraEffects?.RefreshWeaponFov();
    }

    private void OnDisable()
    {
        look?.ClearRecoil();
        if (Runtime != null)
        {
            savedAmmo = Runtime.Ammo;
            savedReserve = Runtime.ReserveAmmo;
            savedAmmoData = Runtime.Data;
            Runtime.Interrupt();
        }
        ResetPresentation();
    }
}
