
using System;
using UnityEngine;

public readonly struct WeaponShot
{
    public readonly long AttackId;
    public readonly Vector3 Origin, Direction;
    public readonly AttackTraceResult Trace;
    public readonly Vector2 Recoil;
    public readonly int AmmoAfterShot;

    public WeaponShot(
        long id,
        Vector3 origin,
        Vector3 direction,
        AttackTraceResult trace,
        Vector2 recoil,
        int ammo)
    {
        AttackId = id;
        Origin = origin;
        Direction = direction;
        Trace = trace;
        Recoil = recoil;
        AmmoAfterShot = ammo;
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
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

    private IWeaponInput input;
    private PlayerDamageReceiver health;
    private GameObject owner;
    private PlayerMovement movement;
    private PlayerLook look;
    private WeaponPresentation presentation;
    private DynamicCrosshairView crosshair;
    private Camera viewCamera;
    private PlayerCameraEffects cameraEffects;
    private float bloom, timeSinceShot, shoveRecovery;
    private int recoilIndex, dodgeActivation;
    private bool survivalInterrupted;

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
        presentation = new WeaponPresentation(weaponRoot, muzzlePoint);
        viewCamera = aimOrigin.GetComponent<Camera>();
        cameraEffects = owner.GetComponentInChildren<PlayerCameraEffects>();
        crosshair = FindFirstObjectByType<DynamicCrosshairView>();
        dodgeActivation = movement?.Abilities?.Dodge.Activation ?? 0;
        // Shot/reload presentation (sound, muzzle, brass, camera kick) subscribes to this controller's events.
        if (Application.isPlaying && GetComponent<WeaponFeedback>() == null) gameObject.AddComponent<WeaponFeedback>();

        if (weaponData == null) return;
        Runtime = new WeaponRuntime(
            weaponData,
            savedAmmoData == weaponData && savedAmmo >= 0
                ? savedAmmo
                : (int?)null, savedAmmoData == weaponData && savedReserve >= 0 ? savedReserve : (int?)null);
    }

    private void LateUpdate()
    {
        presentation?.SetVisible(Runtime != null && Runtime.Data.Category != WeaponCategory.Melee);
        if (Runtime == null) { ResetPresentation(); return; }

        if (health != null && !health.IsAlive)
        {
            Runtime.Interrupt();
            ResetPresentation();
            return;
        }

        var slots = GetComponent<PlayerWeaponSlots>();
        if (slots != null && !slots.CanReadInput) Runtime.CancelBurst();
        if (input != null) ProcessFrame(Time.deltaTime, input.ReadInput());
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
        bool bodyAction = movement != null && (movement.IsParkour || movement.CurrentState == PlayerMovement.MovementState.Tackle
            || movement.CurrentState == PlayerMovement.MovementState.LandingRoll);
        if (survivalInterrupted || bodyAction || stepDodging || shoveRecovery > 0f)
            frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, false, frame.AimHeld);
        survivalInterrupted = false;
        bool aiming = frame.AimHeld && !Runtime.IsReloading && !bodyAction && shoveRecovery <= 0f
            && !stepDodging && !frame.ReloadPressed;
        float nextAim = Mathf.MoveTowards(AimBlend, aiming ? 1f : 0f, dt / weaponData.AdsTransitionTime);
        Vector3 target = WeaponAimSolver.AimPoint(owner, aimOrigin, weaponData.Range, hitLayers);
        // Probe the requested sight position even while high ready suppresses ADS. Otherwise
        // a clear hip pose and blocked ADS pose can alternate, causing zoom/ready flicker.
        Vector3 nominalMuzzle = presentation != null ? presentation.Prepare(aimOrigin, weaponData, aiming ? 1f : nextAim) : aimOrigin.position;
        bool blocked = WeaponAimSolver.IsObstructed(owner, aimOrigin, nominalMuzzle, target, hitLayers);
        AimBlend = blocked || presentation?.IsHighReady == true ? 0f : nextAim;
        if (presentation != null)
        {
            nominalMuzzle = presentation.Prepare(aimOrigin, weaponData, AimBlend);
            blocked |= WeaponAimSolver.IsObstructed(owner, aimOrigin, nominalMuzzle, target, hitLayers);
            if (blocked && AimBlend > 0f)
            { AimBlend = 0f; presentation.Prepare(aimOrigin, weaponData, 0f); }
        }
        IsHighReady = presentation != null ? presentation.Pose(owner, aimOrigin, target, blocked, Runtime, dt, hitLayers) : blocked;
        if (IsHighReady) AimBlend = 0f;
        bool cannotFire = IsHighReady || shoveRecovery > 0f || bodyAction;
        if (cannotFire) Runtime.CancelBurst();
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

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : aimOrigin.position;
            target = WeaponAimSolver.AimPoint(owner, aimOrigin, weaponData.Range, hitLayers);
            if (WeaponAimSolver.IsObstructed(owner, aimOrigin, origin, target, hitLayers))
            { Runtime.RefundUnfiredShots(shots - i); IsHighReady = true; AimBlend = 0f; break; }
            Vector3 direction = WeaponAimSolver.SpreadDirection((target - origin).normalized, CurrentSpread, UnityEngine.Random.insideUnitCircle);
            Vector2 recoil = weaponData.RecoilAt(recoilIndex++) * Mathf.Lerp(1f, .65f, AimBlend);

            var attack = new DamageHit(
                id,
                weaponData.Damage,
                DamageKind.Attack,
                origin,
                direction,
                owner,
                postureDamage: weaponData.PostureDamage,
                method: DamageMethod.Bullet);

            var trace = AttackTrace.Trace(
                attack,
                origin,
                direction,
                weaponData.Range,
                hitLayers, weaponData.HeadshotMultiplier);

            var shot = new WeaponShot(
                id,
                origin,
                direction,
                trace,
                recoil,
                Runtime.Ammo + shots - i - 1);

            LastShot = shot;

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
        CurrentSpread = Mathf.Lerp(weaponData.HipSpread, weaponData.AdsSpread, AimBlend)
            + (bloom + moving * weaponData.MovingSpread + air) * Mathf.Lerp(1f, .25f, AimBlend);
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
        AimBlend = bloom = shoveRecovery = CurrentSpread = 0f; recoilIndex = 0; IsHighReady = false;
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
