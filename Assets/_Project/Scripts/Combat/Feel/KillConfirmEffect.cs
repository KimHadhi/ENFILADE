using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Which kill screen a kill gets.</summary>
public enum KillScreen
{
    /// <summary>Every ordinary kill: GTA V-style brightened, glowing, cool frame.</summary>
    Gta,
    /// <summary>Execution: the special pulse with a crimson vignette.</summary>
    Execution,
    /// <summary>Perfect-defense counter: the special pulse with a cold steel vignette.</summary>
    Counter,
}

/// <summary>
/// Kill screens, two looks on one runtime volume. Tuning and which kills get which: GunFeel → Kill screen.
///
/// GTA V style (ordinary kills): the picture lifts for a moment — slightly brighter, highlights glowing (bloom), a
/// faint cool blue-white wash and light chromatic fringing — holds for 0.5 s, then fades over 0.25 s. No blur: the
/// frame must stay readable mid-fight.
///
/// Special (executions and counters): a heavier, rarer pulse — desaturated, contrasty grade with a coloured vignette
/// (crimson / steel), an impact that squeezes the lens, fringes hard and flashes bright then snaps back within ~0.14 s
/// while the grade lingers, plus a brief real-time hit-stop and a camera thump. An ordinary kill never cuts it short.
///
/// The normal screen is never touched. If the main camera renders without post-processing (the project default),
/// post-processing is borrowed only while an effect plays, together with a neutral volume that cancels the project's
/// default volume profile (ACES tonemapping, bloom, film grain, motion blur ...), so only the kill look itself shows and
/// the camera is handed back unchanged afterwards. A camera that already uses post-processing keeps its own look.
/// </summary>
[DisallowMultipleComponent]
public sealed class KillConfirmEffect : MonoBehaviour
{
    private const float Attack = .03f, ImpactFlash = .35f;
    private static KillConfirmEffect instance;
    private Volume volume, neutral;
    private ColorAdjustments color;
    private WhiteBalance balance;
    private Bloom bloom;
    private Vignette vignette;
    private ChromaticAberration chromatic;
    private LensDistortion lens;
    private float age = 99f, hold = .5f, fade = .25f, rise;
    private float impactSeconds, baseExposure, baseChromatic, baseLens;
    private bool special;
    private UniversalAdditionalCameraData borrowed;

    public float Weight => volume != null && volume.enabled ? volume.weight : 0f;
    /// <summary>The look currently playing (meaningful only while Weight > 0).</summary>
    public KillScreen Current { get; private set; }
    /// <summary>True only while an effect runs on a camera whose post-processing was borrowed for it.</summary>
    public bool IsBorrowingPostProcessing => borrowed != null;

    /// <summary>heavy = headshot kills and executions.</summary>
    public static void Trigger(KillScreen kind, bool heavy)
    {
        if (!Application.isPlaying) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        if (kind == KillScreen.Gta)
        {
            if (settings.killScreenEffect) self.PulseGta(heavy, settings);
            return;
        }
        if (settings.killScreenEffect) self.PulseSpecial(kind, heavy, settings);
        HitStop(heavy, settings);
        var view = Camera.main;
        if (view != null)
            CameraViewPunch.For(view)?.Punch(new Vector3(-.35f, Random.Range(-.25f, .25f), Random.Range(-1.2f, 1.2f)) * (heavy ? 1.3f : 1f),
                heavy ? -1.6f : -1.1f, heavy ? .3f : .22f);
    }

    private static KillConfirmEffect Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Kill Confirm Effect", groupName: "Screen").AddComponent<KillConfirmEffect>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 200f;
        volume.weight = 0f;
        var profile = NewProfile("Kill Confirm (Runtime)");
        // Only the parameters these effects animate are overridden; everything else falls through.
        color = profile.Add<ColorAdjustments>(false);
        color.postExposure.overrideState = color.saturation.overrideState = color.contrast.overrideState = color.colorFilter.overrideState = true;
        balance = profile.Add<WhiteBalance>(false);
        balance.temperature.overrideState = true;
        bloom = profile.Add<Bloom>(false);
        bloom.intensity.overrideState = bloom.threshold.overrideState = bloom.scatter.overrideState = bloom.tint.overrideState = true;
        vignette = profile.Add<Vignette>(false);
        vignette.intensity.overrideState = vignette.smoothness.overrideState = vignette.color.overrideState = true;
        vignette.smoothness.value = .45f;
        chromatic = profile.Add<ChromaticAberration>(false);
        chromatic.intensity.overrideState = true;
        lens = profile.Add<LensDistortion>(false);
        lens.intensity.overrideState = lens.scale.overrideState = true;
        lens.scale.value = 1f;
        volume.sharedProfile = profile;
        volume.enabled = false;

        // Every URP effect at its neutral default, overriding the default volume profile while post-processing is borrowed.
        var host = new GameObject("Neutral Post");
        host.transform.SetParent(transform, false);
        neutral = host.AddComponent<Volume>();
        neutral.isGlobal = true;
        neutral.priority = 199f;
        neutral.weight = 1f;
        var baseline = NewProfile("Kill Confirm Neutral (Runtime)");
        baseline.Add<Tonemapping>(true); baseline.Add<Bloom>(true); baseline.Add<FilmGrain>(true); baseline.Add<MotionBlur>(true);
        baseline.Add<PaniniProjection>(true); baseline.Add<ScreenSpaceLensFlare>(true); baseline.Add<DepthOfField>(true);
        baseline.Add<ColorAdjustments>(true); baseline.Add<Vignette>(true); baseline.Add<ChromaticAberration>(true);
        baseline.Add<LensDistortion>(true); baseline.Add<WhiteBalance>(true); baseline.Add<LiftGammaGain>(true);
        baseline.Add<SplitToning>(true); baseline.Add<ShadowsMidtonesHighlights>(true); baseline.Add<ColorCurves>(true);
        baseline.Add<ColorLookup>(true); baseline.Add<ChannelMixer>(true);
        neutral.sharedProfile = baseline;
        neutral.enabled = false;
    }

    private static VolumeProfile NewProfile(string name)
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = name;
        profile.hideFlags = HideFlags.HideAndDontSave;
        return profile;
    }

    private void OnDisable() => EndPulse();

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (volume != null && volume.sharedProfile != null) Destroy(volume.sharedProfile);
        if (neutral != null && neutral.sharedProfile != null) Destroy(neutral.sharedProfile);
    }

    private void PulseGta(bool headshot, GunFeelSettings settings)
    {
        // An ordinary kill never cuts a running execution / counter screen short.
        if (special && Weight > 0f) return;
        float boost = headshot ? settings.gtaHeadshotBoost : 1f;
        color.saturation.value = settings.gtaKillSaturation;
        color.contrast.value = settings.gtaKillContrast;
        color.colorFilter.value = settings.gtaKillTint;
        balance.temperature.value = settings.gtaKillTemperature;
        bloom.intensity.value = settings.gtaKillBloom * boost;
        bloom.threshold.value = settings.gtaKillBloomThreshold;
        // Tight scatter: only the brightest edges glow instead of a haze over every lit wall.
        bloom.scatter.value = .65f;
        bloom.tint.value = settings.gtaKillBloomTint;
        vignette.intensity.value = 0f;
        // Exposure, fringing and lens stay flat for the whole GTA screen (no impact snap).
        baseExposure = settings.gtaKillExposure;
        baseChromatic = settings.gtaKillChromatic;
        baseLens = 0f;
        impactSeconds = 0f;
        hold = Mathf.Max(Attack, settings.gtaKillHoldSeconds);
        fade = Mathf.Max(.01f, settings.gtaKillFadeSeconds);
        Begin(KillScreen.Gta);
    }

    private void PulseSpecial(KillScreen kind, bool heavy, GunFeelSettings settings)
    {
        float boost = heavy ? settings.headshotKillBoost : 1f;
        bool execution = kind == KillScreen.Execution;
        color.saturation.value = Mathf.Max(-100f, settings.killSaturation * boost);
        color.contrast.value = Mathf.Min(100f, 14f * boost);
        color.colorFilter.value = Color.white;
        balance.temperature.value = 0f;
        // A soft flare on the brightest highlights, tinted toward the vignette's mood.
        bloom.intensity.value = settings.killBloom * boost;
        bloom.threshold.value = 1f;
        bloom.scatter.value = .65f;
        bloom.tint.value = execution ? new Color(1f, .82f, .76f) : new Color(.8f, .9f, 1f);
        vignette.intensity.value = Mathf.Clamp01(settings.killVignette * boost);
        vignette.color.value = execution ? settings.executionVignetteColor : settings.counterVignetteColor;
        // Exposure is not boosted for heavy kills: the extra punch comes from colour, vignette and aberration instead.
        baseExposure = settings.killExposure;
        baseChromatic = Mathf.Clamp01(settings.killChromatic * boost);
        baseLens = Mathf.Clamp(settings.killLensDistortion * boost, -1f, 0f);
        impactSeconds = settings.killImpactSeconds;
        hold = Attack + settings.killPulseHoldSeconds;
        fade = Mathf.Max(.05f, settings.killPulseSeconds * (heavy ? 1.15f : 1f));
        Begin(kind);
    }

    private void Begin(KillScreen kind)
    {
        BorrowCamera();
        // A kill during an earlier screen rises from where that one is instead of dropping to zero first.
        rise = Weight;
        special = kind != KillScreen.Gta;
        Current = kind;
        age = 0f;
        volume.enabled = true;
        Apply();
    }

    private void BorrowCamera()
    {
        if (borrowed != null) return;
        var view = Camera.main;
        var data = view != null ? view.GetUniversalAdditionalCameraData() : null;
        if (data == null || data.renderPostProcessing) return; // already post-processed: keep its own look
        data.renderPostProcessing = true;
        borrowed = data;
        neutral.enabled = true;
    }

    private void EndPulse()
    {
        if (volume != null) { volume.weight = 0f; volume.enabled = false; }
        if (neutral != null) neutral.enabled = false;
        if (borrowed != null) borrowed.renderPostProcessing = false;
        borrowed = null;
        special = false;
    }

    private void Update()
    {
        if (!volume.enabled) return;
        // Real time: the effect must play through its own hit-stop and through perfect-defense slow motion.
        age += Time.unscaledDeltaTime;
        Apply();
    }

    private void Apply()
    {
        // Snap in over Attack, hold at full strength until Hold, then fade. The GTA screen eases out smoothly; the
        // special one decays quadratically like a struck bell.
        float weight;
        if (age < Attack) weight = Mathf.Lerp(rise, 1f, age / Attack);
        else if (age < hold) weight = 1f;
        else
        {
            float t = Mathf.Clamp01((age - hold) / fade);
            weight = special ? (1f - t) * (1f - t) : Mathf.SmoothStep(1f, 0f, t);
        }
        volume.weight = weight;

        // Special impact: the lens squeeze, hard fringing and a bright flash spike on the hit and snap back quickly,
        // leaving the grade and vignette to linger.
        float impact = impactSeconds > 0f ? 1f - Mathf.SmoothStep(0f, 1f, age / impactSeconds) : 0f;
        color.postExposure.value = baseExposure + ImpactFlash * impact;
        chromatic.intensity.value = special ? Mathf.Clamp01(baseChromatic * (.45f + .75f * impact)) : baseChromatic;
        lens.intensity.value = baseLens * (.2f + .8f * impact);

        if (age >= hold + fade) EndPulse();
    }

    private static void HitStop(bool heavy, GunFeelSettings settings)
    {
        float seconds = heavy ? settings.headshotKillHitStopSeconds : settings.killHitStopSeconds;
        if (seconds <= 0f) return;
        var player = FindFirstObjectByType<PlayerMovement>();
        if (player == null) return;
        var time = player.GetComponent<CombatTimeEffects>();
        if (time == null) time = player.gameObject.AddComponent<CombatTimeEffects>();
        time.RequestSlowdown(Get(), settings.killHitStopScale, seconds);
    }
}
