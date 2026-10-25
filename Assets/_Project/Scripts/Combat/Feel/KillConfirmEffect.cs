using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// GTA-style kill confirmation for rare, deliberate kills: a short full-screen pulse (desaturate, exposure pop,
/// vignette, chromatic aberration, a slight lens squeeze), a brief real-time hit-stop and a camera thump. It is loud
/// for a moment and gone in half a second. Tuning and which kills get it: GunFeel → Kill screen effect.
///
/// The normal screen is never touched. If the main camera renders without post-processing (the project default),
/// post-processing is borrowed only while a pulse plays, together with a neutral volume that cancels the project's
/// default volume profile (ACES tonemapping, bloom, film grain, motion blur ...), so only the pulse itself shows and
/// the camera is handed back unchanged afterwards. A camera that already uses post-processing keeps its own look.
/// </summary>
[DisallowMultipleComponent]
public sealed class KillConfirmEffect : MonoBehaviour
{
    private const float Attack = .03f;
    private static KillConfirmEffect instance;
    private Volume volume, neutral;
    private ColorAdjustments color;
    private Vignette vignette;
    private ChromaticAberration chromatic;
    private LensDistortion lens;
    private float age = 99f, duration = .5f;
    private UniversalAdditionalCameraData borrowed;

    public float Weight => volume != null && volume.enabled ? volume.weight : 0f;
    /// <summary>True only while a pulse runs on a camera whose post-processing was borrowed for it.</summary>
    public bool IsBorrowingPostProcessing => borrowed != null;

    /// <summary>headshot = the heavier version (headshot kills and executions).</summary>
    public static void Trigger(bool headshot)
    {
        if (!Application.isPlaying) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        if (settings.killScreenEffect) self.Pulse(headshot, settings);
        HitStop(headshot, settings);
        var view = Camera.main;
        if (view != null)
            CameraViewPunch.For(view)?.Punch(new Vector3(-.35f, Random.Range(-.25f, .25f), Random.Range(-1.2f, 1.2f)) * (headshot ? 1.3f : 1f),
                headshot ? -1.6f : -1.1f, headshot ? .3f : .22f);
    }

    private static KillConfirmEffect Get()
    {
        if (instance == null) instance = new GameObject("Kill Confirm Effect").AddComponent<KillConfirmEffect>();
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
        // Only the parameters this effect animates are overridden; everything else falls through.
        color = profile.Add<ColorAdjustments>(false);
        color.saturation.overrideState = color.postExposure.overrideState = color.contrast.overrideState = true;
        vignette = profile.Add<Vignette>(false);
        vignette.intensity.overrideState = vignette.smoothness.overrideState = vignette.color.overrideState = true;
        vignette.smoothness.value = .45f;
        chromatic = profile.Add<ChromaticAberration>(false);
        chromatic.intensity.overrideState = true;
        lens = profile.Add<LensDistortion>(false);
        lens.intensity.overrideState = lens.scale.overrideState = true;
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

    private void Pulse(bool headshot, GunFeelSettings settings)
    {
        BorrowCamera();
        float boost = headshot ? settings.headshotKillBoost : 1f;
        color.saturation.value = Mathf.Max(-100f, settings.killSaturation * boost);
        // Exposure is not boosted for headshots: the extra punch comes from colour, vignette and aberration instead.
        color.postExposure.value = settings.killExposure;
        color.contrast.value = Mathf.Min(100f, 14f * boost);
        vignette.intensity.value = Mathf.Clamp01(settings.killVignette * boost);
        vignette.color.value = headshot ? new Color(.25f, .02f, 0f) : Color.black;
        chromatic.intensity.value = Mathf.Clamp01(settings.killChromatic * boost);
        lens.intensity.value = Mathf.Clamp(settings.killLensDistortion * boost, -1f, 0f);
        lens.scale.value = 1f;
        duration = Mathf.Max(.05f, settings.killPulseSeconds * (headshot ? 1.15f : 1f));
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
    }

    private void Update()
    {
        if (!volume.enabled) return;
        // Real time: the pulse must play through its own hit-stop and through perfect-defense slow motion.
        age += Time.unscaledDeltaTime;
        Apply();
    }

    private void Apply()
    {
        float weight;
        if (age < Attack) weight = age / Attack;
        else
        {
            float t = Mathf.Clamp01((age - Attack) / duration);
            weight = (1f - t) * (1f - t);
        }
        volume.weight = weight;
        if (age >= Attack + duration) EndPulse();
    }

    private static void HitStop(bool headshot, GunFeelSettings settings)
    {
        float seconds = headshot ? settings.headshotKillHitStopSeconds : settings.killHitStopSeconds;
        if (seconds <= 0f) return;
        var player = FindFirstObjectByType<PlayerMovement>();
        if (player == null) return;
        var time = player.GetComponent<CombatTimeEffects>();
        if (time == null) time = player.gameObject.AddComponent<CombatTimeEffects>();
        time.RequestSlowdown(Get(), settings.killHitStopScale, seconds);
    }
}
