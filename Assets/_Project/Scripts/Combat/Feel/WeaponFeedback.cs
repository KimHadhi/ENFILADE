using UnityEngine;

/// <summary>
/// "I fired" layer for one WeaponController: shot sound, muzzle flash with light/smoke/sparks, ejected brass,
/// visual camera kick and crosshair kick, plus dry-fire, low-ammo and reload sounds. Presentation only: it reads
/// the controller's events and never changes ammunition, timing or aim. Added automatically by WeaponController.
/// Global tuning: Resources/Combat/GunFeel. Per-weapon kick and sound: WeaponData → Feel.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(310)]
public sealed class WeaponFeedback : MonoBehaviour
{
    private const int CasingCount = 24;
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
    private Transform effectsRoot, flash, flashParent;
    private MeshRenderer flashRenderer;
    private Light flashLight;
    private MaterialPropertyBlock block;
    private ParticleSystem smoke, sparks;
    private float flashRemaining, flashDuration, flashIntensity;
    private readonly Casing[] casings = new Casing[CasingCount];
    private int nextCasing;
    private bool trackingReload, magInPlayed, boltPlayed, emptyReload;

    private void Awake()
    {
        weapon = GetComponent<WeaponController>();
        movement = GetComponentInParent<PlayerMovement>();
        block = new MaterialPropertyBlock();
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
        HideFlash();
        trackingReload = false;
    }

    private void OnDestroy()
    {
        if (effectsRoot != null) Destroy(effectsRoot.gameObject);
        if (flash != null) Destroy(flash.gameObject);
    }

    private void OnShot(WeaponShot shot)
    {
        var data = weapon.EquippedData;
        if (data == null) return;
        var settings = GunFeelSettings.Instance;
        EnsureView();
        float ads = weapon.AimBlend;

        if (settings.gunSounds)
            FeelAudio.Play(FeelAudio.ShotFor(data.SoundProfile), data.FireSounds, settings.gunshotVolume * data.FireVolume,
                1f + Random.Range(-1f, 1f) * settings.gunshotPitchJitter);
        AmmoCue(shot.AmmoAfterShot, data, settings);
        if (settings.muzzleEffects) Muzzle(data, settings);
        if (settings.shellCasings && data.EjectShells) Eject();

        if (punch != null)
        {
            float scale = Mathf.Lerp(1f, settings.adsViewPunchScale, ads);
            Vector3 p = data.ViewPunch;
            var kick = new Vector3(p.x * Random.Range(.85f, 1.1f), p.y * Random.Range(-1f, 1f),
                p.z * Random.Range(.55f, 1f) * (Random.value < .5f ? -1f : 1f));
            punch.Punch(kick * scale, data.ViewPunchFov * scale, data.ShakeTrauma * scale);
        }
        if (crosshair == null) crosshair = FindFirstObjectByType<DynamicCrosshairView>();
        crosshair?.Kick(4f + data.ViewPunch.x * 4f);

        // A bullet that reached a receiver but did nothing (guard, parry, invulnerability) gets its own grey marker.
        var result = shot.Trace.Damage;
        if (result == DamageResult.Blocked || result == DamageResult.Parried || result == DamageResult.Invulnerable)
            HitMarkerHUD.Show(HitMarkerKind.Blocked);
    }

    /// <summary>Last round: an empty click. Low magazine: a rising metallic tick, louder toward empty.</summary>
    private static void AmmoCue(int remaining, WeaponData data, GunFeelSettings settings)
    {
        if (!settings.gunSounds) return;
        if (remaining <= 0)
        {
            FeelAudio.Play(FeelSound.DryFire, settings.dryFireSounds, settings.mechanicalVolume * .8f, 1.25f);
            return;
        }
        if (data.MagazineSize < 4) return;
        float fraction = remaining / (float)data.MagazineSize;
        if (fraction > settings.lowAmmoFraction) return;
        float urgency = 1f - fraction / Mathf.Max(.01f, settings.lowAmmoFraction);
        FeelAudio.Play(FeelSound.LowAmmo, settings.lowAmmoSounds, settings.mechanicalVolume * Mathf.Lerp(.3f, .9f, urgency),
            Mathf.Lerp(1f, 1.35f, urgency));
    }

    private void OnDryFire()
    {
        var settings = GunFeelSettings.Instance;
        if (settings.gunSounds) FeelAudio.Play(FeelSound.DryFire, settings.dryFireSounds, settings.mechanicalVolume, Random.Range(.96f, 1.04f));
        if (crosshair == null) crosshair = FindFirstObjectByType<DynamicCrosshairView>();
        crosshair?.Kick(2f);
    }

    private void OnReloadStarted(bool empty)
    {
        var settings = GunFeelSettings.Instance;
        trackingReload = true; magInPlayed = boltPlayed = false; emptyReload = empty;
        if (settings.gunSounds) FeelAudio.Play(FeelSound.MagOut, settings.magOutSounds, settings.mechanicalVolume, Random.Range(.97f, 1.03f));
    }

    private void OnReloadCompleted() => trackingReload = false;

    private void UpdateReload()
    {
        if (!trackingReload) return;
        var runtime = weapon != null ? weapon.Runtime : null;
        if (runtime == null || !runtime.IsReloading) { trackingReload = false; return; }
        var settings = GunFeelSettings.Instance;
        float progress = 1f - runtime.ReloadRemaining / Mathf.Max(.01f, runtime.ReloadDuration);
        if (!magInPlayed && progress >= (emptyReload ? .5f : .62f))
        {
            magInPlayed = true;
            if (settings.gunSounds) FeelAudio.Play(FeelSound.MagIn, settings.magInSounds, settings.mechanicalVolume, Random.Range(.97f, 1.03f));
        }
        if (emptyReload && !boltPlayed && progress >= .86f)
        {
            boltPlayed = true;
            if (settings.gunSounds) FeelAudio.Play(FeelSound.Bolt, settings.boltSounds, settings.mechanicalVolume, Random.Range(.97f, 1.03f));
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (flashRemaining > 0f)
        {
            flashRemaining -= dt;
            float life = Mathf.Clamp01(flashRemaining / Mathf.Max(.001f, flashDuration));
            if (flashLight != null) flashLight.intensity = flashIntensity * life * life;
            if (flashRenderer != null)
            {
                // Full brightness for the first half, then a quick fade: reads as one bright frame, not a blink.
                Color color = GunFeelSettings.Instance.muzzleFlashColor;
                color.a = Mathf.Clamp01(life * 2f);
                block.SetColor("_Color", color);
                flashRenderer.SetPropertyBlock(block);
            }
            if (flashRemaining <= 0f) HideFlash();
        }
        UpdateCasings(dt);
        UpdateReload();
    }

    // ---------------------------------------------------------------- muzzle

    private void Muzzle(WeaponData data, GunFeelSettings settings)
    {
        Transform muzzle = weapon.MuzzlePoint;
        if (muzzle == null || data.MuzzleFlashScale <= 0f || !muzzle.gameObject.activeInHierarchy) return;
        EnsureFlash(muzzle);
        float size = data.MuzzleFlashScale * Random.Range(.8f, 1.15f);
        Vector3 world = new Vector3(.13f, .13f, .2f * Random.Range(.75f, 1.3f)) * size;
        Vector3 parentScale = muzzle.lossyScale;
        flash.localPosition = Vector3.zero;
        flash.localRotation = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
        flash.localScale = new Vector3(world.x / Safe(parentScale.x), world.y / Safe(parentScale.y), world.z / Safe(parentScale.z));
        flash.gameObject.SetActive(true);
        flashDuration = flashRemaining = settings.muzzleFlashSeconds;
        flashIntensity = settings.muzzleLightIntensity * size;
        flashLight.color = settings.muzzleFlashColor;
        flashLight.range = settings.muzzleLightRange;
        flashLight.intensity = flashIntensity;
        flashLight.enabled = flashIntensity > 0f;
        block.SetColor("_Color", settings.muzzleFlashColor);
        flashRenderer.SetPropertyBlock(block);

        EnsureParticles();
        Vector3 origin = muzzle.position, forward = muzzle.forward;
        Vector3 carry = movement != null ? movement.ActualVelocity * .6f : Vector3.zero;
        var emit = new ParticleSystem.EmitParams();
        int puffs = Random.Range(2, 4);
        for (int i = 0; i < puffs; i++)
        {
            emit.position = origin + forward * Random.Range(.02f, .08f);
            emit.velocity = forward * Random.Range(.5f, 1.3f) + Random.insideUnitSphere * .18f + Vector3.up * .12f + carry;
            emit.startSize = Random.Range(.05f, .09f) * data.MuzzleFlashScale;
            emit.startLifetime = Random.Range(.6f, 1.05f);
            emit.startColor = new Color(.78f, .78f, .76f, settings.muzzleSmokeAlpha);
            emit.rotation = Random.Range(0f, 360f);
            smoke.Emit(emit, 1);
        }
        int count = Random.Range(3, 7);
        for (int i = 0; i < count; i++)
        {
            emit.position = origin;
            emit.velocity = (forward + Random.insideUnitSphere * .28f).normalized * Random.Range(5f, 11f) + carry;
            emit.startSize = Random.Range(.008f, .016f);
            emit.startLifetime = Random.Range(.04f, .1f);
            emit.startColor = Color.Lerp(settings.muzzleFlashColor, Color.white, .4f);
            emit.rotation = 0f;
            sparks.Emit(emit, 1);
        }
    }

    private void EnsureFlash(Transform muzzle)
    {
        if (flash == null)
        {
            var host = new GameObject("Feel Muzzle Flash");
            flash = host.transform;
            host.AddComponent<MeshFilter>().sharedMesh = FeelAssets.MuzzleFlash;
            flashRenderer = host.AddComponent<MeshRenderer>();
            flashRenderer.sharedMaterials = new[] { FeelAssets.Particle(FeelAssets.FlashStar, true), FeelAssets.Particle(FeelAssets.FlashSpike, true) };
            flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flashRenderer.receiveShadows = false;
            var light = new GameObject("Feel Muzzle Light");
            light.transform.SetParent(flash, false);
            light.transform.localPosition = new Vector3(0f, 0f, .4f);
            flashLight = light.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;
        }
        if (flashParent != muzzle) { flash.SetParent(muzzle, false); flashParent = muzzle; }
    }

    private void HideFlash()
    {
        flashRemaining = 0f;
        if (flash != null) flash.gameObject.SetActive(false);
        if (flashLight != null) flashLight.enabled = false;
    }

    private void EnsureParticles()
    {
        if (effectsRoot == null) effectsRoot = FeelAssets.CreateRuntimeRoot("Weapon Feel FX", gameObject).transform;
        if (smoke == null)
        {
            smoke = FeelAssets.CreateEmitter("Muzzle Smoke", effectsRoot, FeelAssets.Particle(FeelAssets.SoftDot, false), 160, -.04f);
            var size = smoke.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 4.5f));
            var color = smoke.colorOverLifetime; color.enabled = true;
            color.color = FeelAssets.Fade(Color.white, new Color(1f, 1f, 1f, 0f));
            var drag = smoke.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 2.4f; drag.multiplyDragByParticleSize = false;
            var rotation = smoke.rotationOverLifetime; rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-.8f, .8f);
        }
        if (sparks == null)
        {
            sparks = FeelAssets.CreateEmitter("Muzzle Sparks", effectsRoot, FeelAssets.Particle(FeelAssets.SoftDot, true), 120, .4f,
                ParticleSystemRenderMode.Stretch);
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = .025f; renderer.lengthScale = 1f;
        }
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
        if (casing.transform == null)
        {
            var host = new GameObject("Casing");
            host.transform.SetParent(effectsRoot, false);
            host.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Casing;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block.Clear(); block.SetColor("_Color", Brass); renderer.SetPropertyBlock(block);
            casing.transform = host.transform;
        }
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
        if (punch == null && view != null) punch = CameraViewPunch.For(view);
    }

    private static float Safe(float value) => Mathf.Abs(value) < .0001f ? 1f : value;
}
