
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
    /// <summary>
    /// The seed of this shot's look and sound (SharedRandom): made by the shooter's PC from its client id, the shot's id and
    /// the pellet, and sent with the shot, so every PC draws its flash, brass, sound and marks the same.
    /// </summary>
    public readonly uint Seed;

    public WeaponShot(
        long id,
        Vector3 origin,
        Vector3 direction,
        AttackTraceResult trace,
        Vector2 recoil,
        int ammo,
        BulletLeg[] later = null,
        int pellet = 0,
        uint seed = 0u)
    {
        AttackId = id;
        Origin = origin;
        Direction = direction;
        Trace = trace;
        Later = later;
        Recoil = recoil;
        AmmoAfterShot = ammo;
        Pellet = pellet;
        Seed = seed != 0u ? seed : SharedRandom.Seed(SharedRandom.LocalClient, (ulong)id, (ulong)pellet);
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(FrameOrder.Weapons)]
public sealed partial class WeaponController : MonoBehaviour
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
    /// <summary>
    /// The held pistol is in its close hold (FPSBody.CloseHold): pulled in to the chest and canted as John Wick fights, it
    /// fires from the hip at what the crosshair is on however close an enemy is, and no enemy holds it back.
    /// </summary>
    public bool IsHeldClose { get; private set; }
    public float CurrentSpread { get; private set; }
    public float MovementMultiplier => EquippedData == null ? 1f : Mathf.Lerp(1f, EquippedData.AdsMoveMultiplier, AimBlend);

    public event Action<WeaponShot> ShotFired;
    /// <summary>Trigger pulled on an empty magazine (no shot, no automatic reload).</summary>
    public event Action DryFired;
    /// <summary>Reload began; the argument is true for an empty-magazine reload.</summary>
    public event Action<bool> ReloadStarted;
    /// <summary>
    /// Reload finished and left the gun loaded: in full, or cut short past its bolt (WeaponRuntime keeps what a cut-short
    /// reload did). Not raised for one cut short before that.
    /// </summary>
    public event Action ReloadCompleted;
    /// <summary>A shell reload moved on: the next shell goes in, or its end (WeaponRuntime.ReloadStep).</summary>
    public event Action<WeaponRuntime.ReloadStep> ReloadStepped;
    /// <summary>B switched the held gun's fire mode.</summary>
    public event Action<WeaponFireMode> FireModeChanged;
    /// <summary>A shotgun's choke has drawn its pellets all the way in (WeaponData.PelletCone): its short signal.</summary>
    public event Action ChokeCharged;
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
    /// <summary>Y: the held gun is being looked over (its kill count shows, AmmoHUD).</summary>
    public bool IsInspecting => inspecting;
    private float switchLeft, inspectLeft;
    private bool holstering, inspecting;
    // A gun just drawn still to get the parts and fire mode this PC's player last left on it (GunSetups).
    private bool setupDue;

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
            || Runtime.IsReloading || AimBlend > .01f || IsHighReady || IsHeldClose) return;
        // A look over the gun turns it in both hands: not with the left hand on something that keeps it (HandOwner).
        if (!HandsFor(HandUses.GunInspect, true)) return;
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

    // The held gun's grip (GunAttachments).
    private GunAttachments.Kind Grip => presentation is FPSBody body ? body.HeldGripKind : GunAttachments.Kind.None;

    /// <summary>
    /// How long the sight takes to come up (s): the gun's Ads Transition Time, sooner with an angled grip on, and sooner
    /// still for a Ghost holding 빠른 조준 (GhostCards.AimSpeed: this PC's own player only).
    /// </summary>
    public float AdsTime => (weaponData == null ? .16f
        : weaponData.AdsTransitionTime * (Grip == GunAttachments.Kind.AngledGrip ? weaponData.GripAimTime : 1f)) / GhostCards.AimSpeed(movement);

    // How far zombies hear this gun's shot: a suppressor, part of the gun or on its muzzle, cuts it to the gun's share.
    private float ShotNoise => weaponData.NoiseWith(Suppressed);
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
    // How long a pistol's close hold stays after its reason went (s; FPSBody.CloseHold's linger).
    private float closeLeft;
    // How long an empty magazine has waited with the gun ready to fire (s), for the automatic reload.
    private float emptyReady;
    // The body's sprint (PlayerMovement.SprintProgress) as last seen, to tell one being asked for from one dying away.
    private float sprintSeen;
    private PlayerBody runningBody;

    // Sprinting with the gun out the gun's run has the arms and the gun (PlayerBody.GunRuns).
    private bool GunRuns
    {
        get
        {
            if (runningBody == null && movement != null) runningBody = movement.GetComponent<PlayerBody>();
            return runningBody != null && runningBody.GunRuns;
        }
    }
    // The held gun's WeaponRuntime.Loads as last seen: a reload cut short past its bolt loads the gun outside a frame's
    // tick (a swap, a climb), and its completion is told then.
    private int loadsSeen;
    // A shotgun's choke (WeaponData.PelletCone): how far it has drawn in (0-1), and the ring's snap as it gets there (s).
    private float chokeCharge, chokePop;
    private const float ChokePopTime = .14f, ChokePopSqueeze = .3f;
    /// <summary>How far the held shotgun's choke has drawn its pellets in (0-1), eased: quick at first, settling at the end.</summary>
    public float ChokeShare => 1f - (1f - chokeCharge) * (1f - chokeCharge);
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

        if (muzzlePoint == null) muzzlePoint = owner.GetComponentInChildren<MuzzleEffects>(true).OrNull()?.transform;
        // The gun rides the eyes, which the camera leaves behind in the F5 view, so it stays in the body's hands.
        eyes = Application.isPlaying && movement != null ? PlayerView.EyesOf(movement) : null;
        if (eyes == aimOrigin) eyes = null;
        if (eyes != null && weaponRoot != null && weaponRoot.parent == aimOrigin) weaponRoot.SetParent(eyes, false);
        // KINEMATION FPS Animation Framework holds the gun on the body (FPSBody); its Sway layer sways it.
        presentation = FPSBody.Attach(this, weaponRoot, muzzlePoint);
        viewCamera = aimOrigin.GetComponent<Camera>();
        cameraEffects = owner.GetComponentInChildren<PlayerCameraEffects>();
        crosshair = movement.OrNull()?.IsLocal != false ? DynamicCrosshairView.FindHud() : null;
        dodgeActivation = movement.OrNull()?.Abilities?.Dodge.Activation ?? 0;
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
        // A climb that needs both hands, or an execution's knife stroke (ExecutionKnife), puts the gun away; so does a
        // feeling shown from the callout wheel while it has both hands (HandOwner, body.emote: PlayerBody.Emote).
        bool bare = TraversalHands.For(movement) == TraversalHandUse.Bare || ExecutionKnife.HoldsHands(movement) || EmoteHasHands;
        if (bare && bareHandsLeft <= 0f) Runtime?.Interrupt();
        if (Runtime != null && Runtime.Loads != loadsSeen)
        {
            loadsSeen = Runtime.Loads;
            ReloadCompleted?.Invoke();
        }
        bareHandsLeft = bare ? BareHandsLinger : Mathf.Max(0f, bareHandsLeft - Time.deltaTime);
        presentation?.SetVisible(Runtime != null && Runtime.Data.Category != WeaponCategory.Melee && bareHandsLeft <= 0f);
        if (Runtime == null) { ResetPresentation(); return; }
        // The body builds a drawn gun a frame or so later; its remembered parts go on once it holds it.
        if (setupDue && (!Mine || GunSetups.Restore(this))) setupDue = false;

        if (health != null && !health.IsAlive)
        {
            Runtime.Interrupt();
            ResetPresentation();
            return;
        }

        // A burst once fired goes to its last round whoever has the keys. It was cut here whenever the input could not be
        // read (PlayerWeaponSlots.CanReadInput: the chat or a menu up), the keys' owner reaching into a shot under way;
        // the hands taken or held, which do stop it, are ProcessFrame's (Interrupt), and a blocked gun's too (CancelBurst).
        if (input == null) return;
        ProcessFrame(movement != null ? movement.DeltaTime : Time.deltaTime, movement != null && movement.IsNetworkDriven ? new WeaponInputFrame(false, false, false, shownAiming) : input.ReadInput());
    }

    /// <summary>On someone else's copy: they raised or lowered their sights.</summary>
    internal void ShowAiming(bool aiming) => shownAiming = aiming;

    /// <summary>
    /// On someone else's copy: their reload began, or stopped; <paramref name="from"/> is how far into its clip it began
    /// (theirs going on from a cut-short one, WeaponRuntime.ReloadStartShare).
    /// </summary>
    internal void ShowReload(bool reloading, bool empty, float from = 0f)
    {
        if (Runtime == null || reloading == Runtime.IsReloading) return;
        if (!reloading) { Runtime.Interrupt(); return; }
        Runtime.ShowReload(empty, from);
        ReloadStarted?.Invoke(empty);
    }

    /// <summary>
    /// On someone else's copy: they fired. The muzzle, the tracer, the sound and the bullet's marks happen here, the same
    /// way as for this PC's own shots, along the path their PC traced (ShotPath: where it hit, went through, glanced off
    /// and came out, put on this PC's copies). Nothing is traced or judged again here (SyncAudit.md, item 1): traced again
    /// against this PC's copies, which stand a little elsewhere, the holes, sparks, tracer and blood were not where the
    /// shooter saw them.
    /// </summary>
    internal void ShowShot(in ShotPath path)
    {
        if (Runtime == null || weaponData == null) return;
        var trace = path.Place(owner, hitLayers, out BulletLeg[] later);
        LastShot = new WeaponShot(DamageHit.NewAttackId(), muzzlePoint != null ? muzzlePoint.position : path.Origin, path.Direction, trace,
            Vector2.zero, Runtime.Ammo, later, path.Pellet, path.Seed);
        // The host's zombies hear a friend's shot as it reaches this PC (ZombieMind), once per shot however many pellets.
        if (path.Pellet == 0 && !ShotMuffle.Hides(trace)) Noise.Make(path.Origin, ShotNoise, owner);
        // Drawn and heard from the shooter's seed, as on their PC (SharedRandom).
        using (SharedRandom.Scope(LastShot.Seed)) ShotFired?.Invoke(LastShot);
        // A corpse it struck bleeds here as on their PC, where their bullet struck the doll (ZombieCorpse.OnStruck), each
        // strike from the shot's seed afresh, as there.
        path.ForEachCorpse(BleedCorpse);
        // The gun's kick once per shot, however many pellets it threw.
        if (path.Pellet == 0) presentation?.OnShot(weaponData, Vector2.zero);
    }

    private void BleedCorpse(ZombieCorpse corpse, Vector3 point, Vector3 direction)
    {
        using (SharedRandom.Scope(LastShot.Seed)) corpse.BleedShot(point, direction);
    }

    public void Equip(WeaponRuntime weapon)
    {
        Initialize();
        look.OrNull()?.ClearRecoil();
        Runtime?.Interrupt();
        ResetPresentation();

        Runtime = weapon;
        weaponData = weapon != null ? weapon.Data : null;
        // Coming out: no shot, aim or reload until it is up (Draw Duration, its draw clip on the body).
        StopInspecting();
        holstering = false;
        // A Point's Gun Kata brings it out at once (PlayerClass.DrawFactor).
        switchLeft = weapon != null && weapon.Data.Category != WeaponCategory.Melee ? weapon.Data.DrawDuration * PlayerClass.DrawFactor(movement) : 0f;
        savedAmmoData = weaponData;
        savedAmmo = weapon != null ? weapon.Ammo : -1;
        savedReserve = weapon != null ? weapon.ReserveAmmo : -1;
        loadsSeen = weapon != null ? weapon.Loads : 0;
        setupDue = weapon != null && weapon.Data.Category != WeaponCategory.Melee;

        if (input is PlayerWeaponInput playerInput)
            playerInput.RequireTriggerRelease();
    }

    public void InterruptForSurvival(bool shove = false)
    {
        Runtime?.Interrupt(); AimBlend = 0f;
        cameraEffects.OrNull()?.RefreshWeaponFov();
        if (crosshair != null) crosshair.SetCombatHidden(false);
        survivalInterrupted = true;
        if (shove) shoveRecovery = missedShoveRecovery;
    }
    public void ConfirmShoveHit() { shoveRecovery = 0f; }
    /// <summary>
    /// A gun's shove whose bash meets them later (FPSBody.BashStrikeIn): the gun stays out of use at least this long (s),
    /// so it can't fire or aim out of a swing still on its way; a hit frees it at once as ever (ConfirmShoveHit).
    /// </summary>
    internal void HoldForShove(float seconds) { shoveRecovery = Mathf.Max(shoveRecovery, seconds); }
    private void UpdateSpread()
    {
        float moving = movement == null ? 0f : Mathf.Clamp01(new Vector2(movement.ActualVelocity.x, movement.ActualVelocity.z).magnitude / Mathf.Max(.1f, movement.WalkSpeed));
        // A Point's 곡예 사격 card steadies shots in the air (PlayerClass.AirSpreadFactor), as it does in a slide (MovingSpreadFactor).
        float air = movement != null && !movement.IsGrounded ? weaponData.AirborneSpread * PlayerClass.AirSpreadFactor(movement) : 0f;
        // Down the sights the shot goes where the sight is, as Rainbow Six Siege's does: walking and firing add nothing
        // there, the recoil climbing the view is the whole of a spray's difficulty. Only a jump still throws it off, at half.
        float loose = 1f - AimBlend;
        // Fanning bucks the revolver off line with every slap of the palm.
        float fan = Runtime != null && Runtime.FireMode == WeaponFireMode.Fanning ? weaponData.FanSpread : 0f;
        // The last factors take all of it away in a Point's Gun Kata with its 탄막 춤 card (PlayerClass.KataSpreadFactor) and
        // for a Ghost's first shot once its sights are up with 빠른 조준 (SnapSpread); a shotgun's pattern is its own pellet
        // cone, added on top where its pellets are aimed, so it stays.
        CurrentSpread = (Mathf.Lerp(weaponData.HipSpread, weaponData.AdsSpread, AimBlend)
            + (bloom + moving * weaponData.MovingSpread * PlayerClass.MovingSpreadFactor(movement) + fan) * loose + air * Mathf.Lerp(1f, .5f, AimBlend))
            * (Crouched ? weaponData.CrouchSpread : 1f) * PlayerClass.KataSpreadFactor(movement) * SnapSpread;
    }
    private void UpdateCrosshair()
    {
        if (crosshair == null) return;
        // A choke shotgun keeps its ring down the sights, closing in as the choke draws, as the Peacekeeper's reticle does.
        // While a grenade cooks its ring takes the crosshair's place, as in the video the user picked (2026-11-22 v2-card-nade
        // "원본대로": the quick grenade is a weapon of its own, so the gun's crosshair isn't up while it is in hand).
        crosshair.SetCombatHidden((AimBlend > .001f && !weaponData.HasChoke) || GrenadeHUD.RingShown);
        if (viewCamera == null) return;
        var canvas = crosshair.GetComponentInParent<Canvas>();
        // Canvas pixels a spread of tangent 1 covers from the screen's middle.
        float perTangent = viewCamera.pixelHeight * .5f / Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad)
            / (canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f);
        crosshair.SetDynamicSpread(Mathf.Tan(CurrentSpread * Mathf.Deg2Rad) * perTangent);
        // A shotgun's circle: the crosshair draws it as a ring in place of its lines, the steady crosshair too, so what the
        // ring holds is what the pellets fill. The choke drawn all the way in snaps it tighter for a moment.
        float snap = 1f - ChokePopSqueeze * Mathf.Sin(Mathf.Clamp01(chokePop / ChokePopTime) * Mathf.PI);
        crosshair.SetPatternSpread(weaponData.Pellets > 1
            ? Mathf.Tan(weaponData.PelletCone(AimBlend, ChokeShare) * Mathf.Deg2Rad) * perTangent * snap : 0f);
    }
    private void ResetPresentation()
    {
        AimBlend = bloom = shoveRecovery = CurrentSpread = raisedHoldLeft = queuedFireLeft = emptyReady = chokeCharge = chokePop = closeLeft = 0f;
        recoilIndex = 0; IsHighReady = IsHeldClose = false;
        HasAimPoint = false;
        presentation?.Reset();
        // Unity's null check, not `?.`: a closing scene disables and destroys its objects one root at a time, so the HUD's
        // crosshair can be gone before this runs from OnDisable, and `?.` only sees a reference that was never set.
        if (crosshair != null)
        {
            crosshair.SetDynamicSpread(0f);
            crosshair.SetPatternSpread(0f);
            crosshair.SetCombatHidden(false);
        }
        cameraEffects.OrNull()?.RefreshWeaponFov();
    }

    private void OnDisable()
    {
        look.OrNull()?.ClearRecoil();
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
