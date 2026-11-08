
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

    public WeaponShot(
        long id,
        Vector3 origin,
        Vector3 direction,
        AttackTraceResult trace,
        Vector2 recoil,
        int ammo,
        BulletLeg[] later = null)
    {
        AttackId = id;
        Origin = origin;
        Direction = direction;
        Trace = trace;
        Later = later;
        Recoil = recoil;
        AmmoAfterShot = ammo;
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
    internal void ShowShot(Vector3 origin, Vector3 direction)
    {
        if (Runtime == null || weaponData == null) return;
        // The same path their PC drew, walls passed and ricochets included: the randomness comes from the shot itself.
        var look = new DamageHit(0, 0f, DamageKind.Attack, origin, direction, owner, method: DamageMethod.Bullet);
        var trace = AttackTrace.Bullet(look, origin, direction, weaponData.Range, hitLayers, 1f, weaponData.Penetration, false, out BulletLeg[] later);
        LastShot = new WeaponShot(DamageHit.NewAttackId(), muzzlePoint != null ? muzzlePoint.position : origin, direction, trace, Vector2.zero, Runtime.Ammo, later);
        ShotFired?.Invoke(LastShot);
        presentation?.OnShot(weaponData, Vector2.zero);
    }

    public void Equip(WeaponRuntime weapon)
    {
        Initialize();
        look?.ClearRecoil();
        Runtime?.Interrupt();
        ResetPresentation();

        Runtime = weapon;
        weaponData = weapon != null ? weapon.Data : null;
        savedAmmoData = weaponData;
        savedAmmo = weapon != null ? weapon.Ammo : -1;
        savedReserve = weapon != null ? weapon.ReserveAmmo : -1;

        if (input is PlayerWeaponInput playerInput)
            playerInput.RequireTriggerRelease();
    }

    internal void ProcessFrame(float dt, WeaponInputFrame frame)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        if (owner != null && owner.GetComponent<PlayerMovement>()?.CombatInputLocked == true)
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
        bloom = Mathf.MoveTowards(bloom, 0f, weaponData.BloomRecovery * dt);
        timeSinceShot += dt;
        if (timeSinceShot > .35f) recoilIndex = 0;
        bool bodyAction = bareHandsLeft > 0f || (movement != null && (movement.IsParkour
            || movement.CurrentState == PlayerMovement.MovementState.DashPush || movement.IsLandingRecovery));
        // Asked for like an R press, so whatever holds a reload back now holds this one back until it lets go.
        if (AutoReloadDue(dt)) frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, true, frame.AimHeld);
        if (survivalInterrupted || bodyAction || stepDodging || shoveRecovery > 0f)
            frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, false, frame.AimHeld);
        survivalInterrupted = false;
        bool aiming = frame.AimHeld && !Runtime.IsReloading && !bodyAction && shoveRecovery <= 0f
            && !stepDodging && !frame.ReloadPressed;
        float nextAim = Mathf.MoveTowards(AimBlend, aiming ? 1f : 0f, dt / weaponData.AdsTransitionTime);
        Vector3 target = AimTarget(weaponData.Range);
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
        if (cannotFire || reloadingBeforeTick)
            frame = new WeaponInputFrame(false, false, frame.ReloadPressed, frame.AimHeld);
        UpdateSpread();

        bool wasReloading = Runtime.IsReloading;
        int shots = Runtime.Tick(dt, frame);

        if (!wasReloading && Runtime.IsReloading) ReloadStarted?.Invoke(Runtime.IsEmptyReload);
        else if (wasReloading && !Runtime.IsReloading) ReloadCompleted?.Invoke();
        if (shots == 0 && frame.FirePressed && Runtime.Ammo == 0 && !Runtime.IsReloading) DryFired?.Invoke();

        for (int i = 0; i < shots; i++)
        {
            long id = DamageHit.NewAttackId();

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : PoseView.position;
            target = AimTarget(weaponData.Range);
            if (WeaponAimSolver.IsObstructed(owner, PoseView, origin, target, hitLayers))
            { Runtime.RefundUnfiredShots(shots - i); IsHighReady = true; AimBlend = 0f; break; }
            Vector3 direction = WeaponAimSolver.SpreadDirection((target - origin).normalized, CurrentSpread, UnityEngine.Random.insideUnitCircle);
            Vector2 recoil = weaponData.RecoilAt(recoilIndex++) * Mathf.Lerp(1f, .65f, AimBlend)
                * (Crouched ? weaponData.CrouchRecoil : 1f);

            var attack = new DamageHit(
                id,
                weaponData.Damage,
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
                hitLayers, weaponData.HeadshotMultiplier, weaponData.Penetration, true, out BulletLeg[] later);

            var shot = new WeaponShot(
                id,
                origin,
                direction,
                trace,
                recoil,
                Runtime.Ammo + shots - i - 1,
                later);

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

            // Presentation (WeaponFeedback, MuzzleEffects, impact and blood effects) subscribes here.
            ShotFired?.Invoke(shot);
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
        CurrentSpread = (Mathf.Lerp(weaponData.HipSpread, weaponData.AdsSpread, AimBlend)
            + (bloom + moving * weaponData.MovingSpread + air) * Mathf.Lerp(1f, .25f, AimBlend))
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
