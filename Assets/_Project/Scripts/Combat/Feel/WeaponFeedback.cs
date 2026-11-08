using UnityEngine;

/// <summary>
/// "I fired" layer for one WeaponController: shot sound, ejected brass, visual camera kick and crosshair kick, plus
/// dry-fire, low-ammo and reload sounds. The muzzle's flame is MuzzleFlash's and its light MuzzleLight's. Presentation only: it reads
/// the controller's events and never changes ammunition, timing or aim. Added automatically by WeaponController.
/// Global tuning: Resources/Combat/GunFeel. Per-weapon kick and sound: WeaponData → Feel.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ShotEffects)]
public sealed class WeaponFeedback : MonoBehaviour
{
    private const int CasingCount = 24;
    // How far someone else's gun carries (m); its mechanical sounds carry FeelAudio's usual distance.
    private const float ShotReach = 60f, MechanicalReach = 14f;
    private static readonly Color Brass = new Color(.86f, .64f, .27f);

    private struct Casing
    {
        public Transform transform;
        public Vector3 velocity, spin;
        public float age, rest;
        public int bounces;
        public bool active, resting;
    }

    private WeaponController weapon;
    private PlayerMovement movement;
    private DynamicCrosshairView crosshair;
    private Camera view;
    private CameraViewPunch punch;
    private Transform effectsRoot;
    private MaterialPropertyBlock block;
    private readonly Casing[] casings = new Casing[CasingCount];
    private int nextCasing;
    private bool trackingReload, magOutPlayed, magInPlayed, boltBackPlayed, boltPlayed, emptyReload;
    // A reload shown by KINEMATION's clip (FPSBody) is timed by that clip's own moments; magInLead and boltLead start those
    // hits early by where their clips are loudest.
    private bool timedReload;
    private ReloadMoments.Moments moments;
    private float magInLead, boltLead;

    private bool IsMine => movement == null || movement.IsLocal;

    // One's own gun sounds flat; someone else's comes from where they stand. gun picks that gun's own mechanical sounds
    // (Art/Audio/Guns/<gun>/...).
    private void Sound(FeelSound sound, AudioClip[] clips, float volume, float pitch, float reach = MechanicalReach, GunSoundProfile? gun = null)
    {
        if (IsMine) FeelAudio.Play(sound, clips, volume, pitch, gun);
        else FeelAudio.PlayAt(sound, clips, transform.position + Vector3.up, volume, pitch, reach, gun);
    }

    private GunSoundProfile? Gun => weapon != null && weapon.EquippedData != null ? weapon.EquippedData.SoundProfile : (GunSoundProfile?)null;

    private void Awake()
    {
        weapon = GetComponent<WeaponController>();
        movement = GetComponentInParent<PlayerMovement>();
        block = new MaterialPropertyBlock();
    }

    // What the first shot would otherwise stop to build. This runs while the map loads.
    private void Start()
    {
        // The brass lives under this root.
        if (effectsRoot == null) effectsRoot = FeelAssets.CreateRuntimeRoot("Weapon Feel FX", gameObject).transform;
        for (int i = 0; i < CasingCount; i++) BuildCasing(ref casings[i]);
    }

    private void OnEnable()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon == null) return;
        weapon.ShotFired += OnShot;
        weapon.DryFired += OnDryFire;
        weapon.ReloadStarted += OnReloadStarted;
        weapon.ReloadCompleted += OnReloadCompleted;
    }

    private void OnDisable()
    {
        if (weapon != null)
        {
            weapon.ShotFired -= OnShot;
            weapon.DryFired -= OnDryFire;
            weapon.ReloadStarted -= OnReloadStarted;
            weapon.ReloadCompleted -= OnReloadCompleted;
        }
        trackingReload = false;
    }

    private void OnDestroy()
    {
        if (effectsRoot != null) Destroy(effectsRoot.gameObject);
    }

    private void OnShot(WeaponShot shot)
    {
        var data = weapon.EquippedData;
        if (data == null) return;
        var settings = GunFeelSettings.Instance;
        EnsureView();
        float ads = weapon.AimBlend;

        if (settings.gunSounds)
            Sound(FeelAudio.ShotFor(data.SoundProfile), data.FireSounds, settings.gunshotVolume * data.FireVolume,
                1f + Random.Range(-1f, 1f) * settings.gunshotPitchJitter, ShotReach);
        if (IsMine) AmmoCue(shot.AmmoAfterShot, data, settings);
        if (settings.shellCasings && data.EjectShells) Eject();
        if (!IsMine) return;

        if (punch != null)
        {
            float scale = Mathf.Lerp(1f, settings.adsViewPunchScale, ads);
            Vector3 p = data.ViewPunch;
            var kick = new Vector3(p.x * Random.Range(.85f, 1.1f), p.y * Random.Range(-1f, 1f),
                p.z * Random.Range(.55f, 1f) * (Random.value < .5f ? -1f : 1f));
            punch.Punch(kick * scale, data.ViewPunchFov * scale, data.ShakeTrauma * scale);
        }
        if (crosshair == null) crosshair = DynamicCrosshairView.FindHud();
        crosshair?.Kick(4f + data.ViewPunch.x * 4f);

        // A bullet that reached a receiver but did nothing (guard, parry, invulnerability) gets its own grey marker.
        var result = shot.Trace.Damage;
        if (result == DamageResult.Blocked || result == DamageResult.Parried || result == DamageResult.Invulnerable)
            HitMarkerHUD.Show(HitMarkerKind.Blocked);
    }

    /// <summary>
    /// The low-ammo warning, heard by the shooter alone: TFA Base's EmitLowAmmoSound (shared.lua) with its autodetection's
    /// limits (autodetection.lua). A shot that leaves the magazine under a third full (LowAmmoSoundThreshold 0.33) plays
    /// the gun's warning, louder the emptier it gets (RequiredVolume: 1 − share ÷ 0.33, on the gunshot's level); the shot
    /// that empties it plays the gun's last-round sound instead. With the warning comes CS2's click (the user's pick D,
    /// 2026-11-06: TFA's warnings and CS2's click together; SoundCues GunLowAmmoClick/&lt;gun&gt; times and sizes it).
    /// TFA gives magazines of four or fewer no warnings; those keep this project's empty click on their last round.
    /// </summary>
    private static void AmmoCue(int remaining, WeaponData data, GunFeelSettings settings)
    {
        if (!settings.gunSounds) return;
        int magazine = data.MagazineSize;
        if (magazine <= settings.lowAmmoSmallMagazine)
        {
            if (remaining <= 0) FeelAudio.Play(FeelSound.DryFire, settings.dryFireSounds, settings.mechanicalVolume * .8f, 1.25f);
            return;
        }
        float share = remaining / (float)magazine;
        if (share >= settings.lowAmmoFraction) return;
        float swell = 1f - share / Mathf.Max(settings.lowAmmoFraction, .01f);
        GunSoundProfile gun = data.SoundProfile;
        if (remaining <= 0)
        {
            FeelAudio.Play(FeelSound.LastRound, settings.lastRoundSounds, settings.gunshotVolume * swell, 1f, gun);
            return;
        }
        FeelAudio.Play(FeelSound.LowAmmo, settings.lowAmmoSounds, settings.gunshotVolume * swell, 1f, gun);
        FeelAudio.Play(FeelSound.LowAmmoClick, settings.lowAmmoClickSounds, settings.mechanicalVolume, 1f, gun);
    }

    private void OnDryFire()
    {
        var settings = GunFeelSettings.Instance;
        if (settings.gunSounds) FeelAudio.Play(FeelSound.DryFire, settings.dryFireSounds, settings.mechanicalVolume, Random.Range(.96f, 1.04f), Gun);
        if (crosshair == null) crosshair = DynamicCrosshairView.FindHud();
        crosshair?.Kick(2f);
    }

    private void OnReloadStarted(bool empty)
    {
        var settings = GunFeelSettings.Instance;
        trackingReload = true; magOutPlayed = magInPlayed = boltBackPlayed = boltPlayed = false; emptyReload = empty;
        var data = weapon != null ? weapon.EquippedData : null;
        timedReload = data != null && FPSBody.Holding(weapon) != null
            && ReloadMoments.TryGet(data, empty, out moments);
        if (timedReload)
        {
            magInLead = FeelAudio.PeakTime(FeelSound.MagIn, settings.magInSounds, data.SoundProfile);
            boltLead = FeelAudio.PeakTime(FeelSound.SlideForward, null, data.SoundProfile);
            return;
        }
        magOutPlayed = true;
        if (settings.gunSounds) Sound(FeelSound.MagOut, settings.magOutSounds, settings.mechanicalVolume, Random.Range(.97f, 1.03f), MechanicalReach, Gun);
    }

    private void OnReloadCompleted() => trackingReload = false;

    private void UpdateReload()
    {
        if (!trackingReload) return;
        var runtime = weapon != null ? weapon.Runtime : null;
        if (runtime == null || !runtime.IsReloading) { trackingReload = false; return; }
        var settings = GunFeelSettings.Instance;
        float duration = Mathf.Max(.01f, runtime.ReloadDuration);
        float progress = 1f - runtime.ReloadRemaining / duration;
        if (timedReload)
        {
            // Each sound when the clip moves its part: the magazine leaving and seating, the bolt or slide pulled and let go.
            Due(ref magOutPlayed, moments.MagOut, 0f, progress, duration, FeelSound.MagOut, settings.magOutSounds, settings);
            Due(ref magInPlayed, moments.MagIn, magInLead, progress, duration, FeelSound.MagIn, settings.magInSounds, settings);
            Due(ref boltBackPlayed, moments.BoltBack, 0f, progress, duration, FeelSound.SlideBack, null, settings);
            Due(ref boltPlayed, moments.BoltForward, boltLead, progress, duration, FeelSound.SlideForward, null, settings);
            return;
        }
        if (!magInPlayed && progress >= (emptyReload ? .5f : .62f))
        {
            magInPlayed = true;
            if (settings.gunSounds) Sound(FeelSound.MagIn, settings.magInSounds, settings.mechanicalVolume, Random.Range(.97f, 1.03f), MechanicalReach, Gun);
        }
        if (emptyReload && !boltPlayed && progress >= .86f)
        {
            boltPlayed = true;
            if (settings.gunSounds) Sound(FeelSound.Bolt, settings.boltSounds, settings.mechanicalVolume, Random.Range(.97f, 1.03f), MechanicalReach, Gun);
        }
    }

    // Plays a timed reload's sound once the reload reaches its share, lead seconds early; a share of -1 never comes.
    private void Due(ref bool played, float share, float lead, float progress, float duration, FeelSound sound, AudioClip[] clips,
        GunFeelSettings settings)
    {
        if (played || share < 0f || progress < share - lead / duration) return;
        played = true;
        if (settings.gunSounds) Sound(sound, clips, settings.mechanicalVolume, Random.Range(.97f, 1.03f), MechanicalReach, Gun);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        UpdateCasings(dt);
        UpdateReload();
    }

    // ---------------------------------------------------------------- brass

    private void Eject()
    {
        Transform muzzle = weapon.MuzzlePoint;
        if (muzzle == null || view == null || !muzzle.gameObject.activeInHierarchy) return;
        if (effectsRoot == null) effectsRoot = FeelAssets.CreateRuntimeRoot("Weapon Feel FX", gameObject).transform;
        Transform eye = view.transform;
        Vector3 forward = muzzle.forward, right = eye.right, up = eye.up;
        ref Casing casing = ref casings[nextCasing];
        nextCasing = (nextCasing + 1) % CasingCount;
        if (casing.transform == null) BuildCasing(ref casing);
        Vector3 port = muzzle.position - forward * .26f + right * .028f + up * .012f;
        casing.transform.SetPositionAndRotation(port, Quaternion.LookRotation(right, up));
        casing.transform.localScale = Vector3.one;
        casing.velocity = right * Random.Range(1.7f, 2.5f) + up * Random.Range(1.1f, 1.8f) - forward * Random.Range(.1f, .5f)
            + (movement != null ? movement.ActualVelocity : Vector3.zero);
        casing.spin = new Vector3(Random.Range(-900f, 900f), Random.Range(-1500f, -600f), Random.Range(-300f, 300f));
        casing.age = casing.rest = 0f; casing.bounces = 0;
        casing.active = true; casing.resting = false;
        casing.transform.gameObject.SetActive(true);
    }

    private void BuildCasing(ref Casing casing)
    {
        var host = new GameObject("Casing");
        host.SetActive(false);
        host.transform.SetParent(effectsRoot, false);
        host.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Casing;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        block.Clear(); block.SetColor("_Color", Brass); renderer.SetPropertyBlock(block);
        casing.transform = host.transform;
    }

    private void UpdateCasings(float dt)
    {
        if (dt <= 0f) return;
        Transform owner = movement != null ? movement.transform : transform;
        for (int i = 0; i < CasingCount; i++)
        {
            ref Casing casing = ref casings[i];
            if (!casing.active || casing.transform == null) continue;
            casing.age += dt;
            if (casing.age > 8f) { Retire(ref casing); continue; }
            if (casing.resting)
            {
                casing.rest += dt;
                if (casing.rest > 3f)
                {
                    float shrink = 1f - (casing.rest - 3f) / .35f;
                    if (shrink <= 0f) { Retire(ref casing); continue; }
                    casing.transform.localScale = Vector3.one * shrink;
                }
                continue;
            }
            Vector3 from = casing.transform.position;
            casing.velocity += Physics.gravity * dt;
            Vector3 to = from + casing.velocity * dt;
            if (Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(owner))
            {
                to = hit.point + hit.normal * .005f;
                // Bounce: lose most normal speed, keep some sliding speed.
                Vector3 normal = Vector3.Project(casing.velocity, hit.normal), tangent = casing.velocity - normal;
                casing.velocity = -normal * .32f + tangent * .55f;
                casing.spin *= .5f;
                casing.bounces++;
                if (casing.velocity.magnitude < .35f || casing.bounces >= 4)
                {
                    Vector3 lying = Vector3.ProjectOnPlane(casing.transform.forward, hit.normal);
                    if (lying.sqrMagnitude < .001f) lying = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
                    casing.transform.SetPositionAndRotation(hit.point + hit.normal * .0048f, Quaternion.LookRotation(lying.normalized, hit.normal));
                    casing.resting = true;
                    continue;
                }
            }
            casing.transform.position = to;
            casing.transform.rotation *= Quaternion.Euler(casing.spin * dt);
        }
    }

    private static void Retire(ref Casing casing)
    {
        casing.active = false;
        if (casing.transform != null) casing.transform.gameObject.SetActive(false);
    }

    private void EnsureView()
    {
        if (view == null && weapon != null && weapon.AimOrigin != null) view = weapon.AimOrigin.GetComponent<Camera>();
        if (view == null) view = Camera.main;
        if (punch == null && view != null && IsMine) punch = CameraViewPunch.For(view);
    }
}
