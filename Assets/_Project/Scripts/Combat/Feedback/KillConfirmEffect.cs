using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Which kill screen a kill gets.</summary>
public enum KillScreen
{
    /// <summary>Every ordinary kill: a brightened, glowing, cool frame.</summary>
    Ordinary,
    /// <summary>Execution: the special pulse with a crimson vignette.</summary>
    Execution,
    /// <summary>Perfect-defense counter: the special pulse with a cold steel vignette.</summary>
    Counter,
    /// <summary>Not a kill: a parry's short screen pulse (ParryFeedback), on the same volume.</summary>
    Parry,
}

/// <summary>One parry's screen pulse. Flash, Lens and the fringing peak spike on contact and snap back.</summary>
public struct ParryScreenLook
{
    public float Flash, Chromatic, Lens, Saturation, Contrast, Bloom, Vignette, Hold, Fade;
    public Color Filter, VignetteColor;
}

/// <summary>
/// Kill screens, two looks on one runtime volume, which a parry's short pulse also borrows. Tuning and which kills get
/// which: GunFeel → Kill screen.
///
/// Ordinary kills: the picture lifts for a moment — slightly brighter, highlights glowing (bloom), a
/// faint cool blue-white wash and light chromatic fringing — holds for 0.5 s, then fades over 0.25 s. No blur: the
/// frame must stay readable mid-fight. At low health LowHealthGrade keeps the colour drained, since colour coming back
/// reads as healing, so the same kill adds a white flash and a lens punch that snap back, harder fringing, crisp
/// contrast and a bigger glow, in step with how drained the screen is (GunFeel → Kill screen: ordinary kills at low health).
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
    private Volume volume;
    private ColorAdjustments color;
    private WhiteBalance balance;
    private Bloom bloom;
    private Vignette vignette;
    private ChromaticAberration chromatic;
    private LensDistortion lens;
    private float age = 99f, hold = .5f, fade = .25f, rise;
    private float impactSeconds, impactFlash = ImpactFlash, baseExposure, baseChromatic, baseLens;
    private bool special;

    public float Weight => volume != null && volume.enabled ? volume.weight : 0f;
    /// <summary>How strongly a kill (or parry) screen shows right now, 0 when none plays.</summary>
    public static float Showing => instance != null ? instance.Weight : 0f;
    /// <summary>The look currently playing (meaningful only while Weight > 0).</summary>
    public KillScreen Current { get; private set; }

    /// <summary>heavy = headshot kills and executions.</summary>
    public static void Trigger(KillScreen kind, bool heavy)
    {
        if (!Application.isPlaying) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        if (kind == KillScreen.Ordinary)
        {
            if (settings.killScreenEffect) self.PulseOrdinary(heavy, settings);
            return;
        }
        if (settings.killScreenEffect) self.PulseSpecial(kind, heavy, settings);
        HitStop(heavy, settings);
        var view = Camera.main;
        if (view != null)
            CameraViewPunch.For(view).OrNull()?.Punch(new Vector3(-.35f, Random.Range(-.25f, .25f), Random.Range(-1.2f, 1.2f)) * (heavy ? 1.3f : 1f),
                heavy ? -1.6f : -1.1f, heavy ? .3f : .22f);
    }

    /// <summary>A parry's screen pulse. It never cuts a kill's special screen short.</summary>
    public static void Parry(in ParryScreenLook look)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.killScreenEffect) return;
        var self = Get();
        if (self.special && self.Weight > 0f && self.Current != KillScreen.Parry) return;
        self.color.saturation.value = look.Saturation;
        self.color.contrast.value = look.Contrast;
        self.color.colorFilter.value = look.Filter;
        self.balance.temperature.value = 0f;
        self.bloom.intensity.value = look.Bloom;
        self.bloom.threshold.value = .95f;
        self.bloom.scatter.value = .7f;
        self.bloom.tint.value = new Color(1f, .9f, .74f);
        self.vignette.intensity.value = Mathf.Clamp01(look.Vignette);
        self.vignette.color.value = look.VignetteColor;
        self.baseExposure = 0f;
        self.baseChromatic = Mathf.Clamp01(look.Chromatic);
        self.baseLens = Mathf.Clamp(look.Lens, -1f, 0f);
        self.impactFlash = look.Flash;
        self.impactSeconds = .1f;
        self.hold = Attack + Mathf.Max(0f, look.Hold);
        self.fade = Mathf.Max(.05f, look.Fade);
        self.Begin(KillScreen.Parry);
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
    }

    private void PulseOrdinary(bool headshot, GunFeelSettings settings)
    {
        // An ordinary kill never cuts a running execution / counter screen short.
        if (special && Weight > 0f) return;
        float boost = headshot ? settings.ordinaryKillHeadshotBoost : 1f;
        // At low health the grade keeps the colour drained, so the kill speaks through what it leaves alone: a white
        // flash and a lens punch that snap back, harder fringing, a crisp contrast and a bigger glow.
        float drained = LowHealthGrade.Drain;
        color.saturation.value = settings.ordinaryKillSaturation;
        color.contrast.value = Mathf.Lerp(settings.ordinaryKillContrast, settings.drainedKillContrast, drained);
        color.colorFilter.value = settings.ordinaryKillTint;
        balance.temperature.value = settings.ordinaryKillTemperature;
        bloom.intensity.value = settings.ordinaryKillBloom * boost * Mathf.Lerp(1f, settings.drainedKillBloomScale, drained);
        bloom.threshold.value = settings.ordinaryKillBloomThreshold;
        // Tight scatter: only the brightest edges glow instead of a haze over every lit wall.
        bloom.scatter.value = .65f;
        bloom.tint.value = settings.ordinaryKillBloomTint;
        vignette.intensity.value = 0f;
        // When healthy, exposure, fringing and lens stay flat for the whole ordinary kill screen (no impact snap).
        baseExposure = settings.ordinaryKillExposure;
        baseChromatic = Mathf.Lerp(settings.ordinaryKillChromatic, settings.drainedKillChromatic, drained);
        baseLens = settings.drainedKillLens * drained;
        impactSeconds = drained > .01f ? settings.drainedKillImpactSeconds : 0f;
        impactFlash = settings.drainedKillFlash * drained;
        hold = Mathf.Max(Attack, settings.ordinaryKillHoldSeconds);
        fade = Mathf.Max(.01f, settings.ordinaryKillFadeSeconds);
        Begin(KillScreen.Ordinary);
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
        impactFlash = ImpactFlash;
        hold = Attack + settings.killPulseHoldSeconds;
        fade = Mathf.Max(.05f, settings.killPulseSeconds * (heavy ? 1.15f : 1f));
        Begin(kind);
    }

    private void Begin(KillScreen kind)
    {
        PostProcessLoan.Take(this);
        // A kill during an earlier screen rises from where that one is instead of dropping to zero first.
        rise = Weight;
        special = kind != KillScreen.Ordinary;
        Current = kind;
        age = 0f;
        volume.enabled = true;
        Apply();
    }

    private void EndPulse()
    {
        if (volume != null) { volume.weight = 0f; volume.enabled = false; }
        PostProcessLoan.Return(this);
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
        // Snap in over Attack, hold at full strength until Hold, then fade. The ordinary kill screen eases out smoothly; the
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
        color.postExposure.value = baseExposure + impactFlash * impact;
        chromatic.intensity.value = impactSeconds > 0f ? Mathf.Clamp01(baseChromatic * (.45f + .75f * impact)) : baseChromatic;
        lens.intensity.value = baseLens * (.2f + .8f * impact);

        if (age >= hold + fade) EndPulse();
    }

    private static void HitStop(bool heavy, GunFeelSettings settings)
    {
        float seconds = heavy ? settings.headshotKillHitStopSeconds : settings.killHitStopSeconds;
        if (seconds <= 0f) return;
        var player = PlayerRoster.Local;
        if (player == null) return;
        var time = player.GetComponent<CombatTimeEffects>();
        if (time == null) time = player.gameObject.AddComponent<CombatTimeEffects>();
        time.RequestSlowdown(Get(), settings.killHitStopScale, seconds);
    }
}
