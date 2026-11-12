using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// How the screen looks while SkullSlowMotion slows the game: the colour cools toward the skull's blue and drains a
/// little, the edges darken, colours split a little toward the screen's edge, and moving things and camera turns smear.
/// The moment time gives way gets a short pulse of its own, a lens warp and a stronger split that fade out. All of it
/// follows how slowed the game is, so it comes and goes with the slowdown. Takes a PostProcessLoan only while it shows.
/// Numbers: Tables/SkullSlowMotion.
/// </summary>
[DisallowMultipleComponent]
public sealed class SlowMotionLook : MonoBehaviour
{
    // Over the kill screen (200) and the low-health grade (200.5).
    private const float Priority = 201f;

    [SerializeField, Range(-100f, 0f), Tooltip("Saturation at the slowest (-100 is black and white).")]
    private float lookSaturation = -30f;
    [SerializeField, Range(-50f, 50f), Tooltip("Contrast at the slowest.")]
    private float lookContrast = 10f;
    [SerializeField, Tooltip("Colour filter at the slowest.")]
    private Color lookTint = new Color(.8f, .87f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("How dark the edges get at the slowest.")]
    private float lookVignette = .3f;
    [SerializeField, Range(.01f, 1f), Tooltip("How far the dark edges fade into the middle.")]
    private float lookVignetteSoftness = .45f;
    [SerializeField, Tooltip("Colour of the dark edges.")]
    private Color lookVignetteColor = new Color(.02f, .05f, .14f);
    [SerializeField, Range(0f, 1f), Tooltip("Colour split toward the screen's edge while slowed.")]
    private float lookSplit = .15f;
    [SerializeField, Range(0f, 1f), Tooltip("Colour split added at the moment it begins, fading over lookPulseSeconds.")]
    private float lookSplitPulse = .5f;
    [SerializeField, Range(-1f, 1f), Tooltip("Lens warp at the moment it begins, fading over lookPulseSeconds: below 0 the edges stretch out, above 0 the middle bulges.")]
    private float lookWarp = -.18f;
    [SerializeField, Min(.05f), Tooltip("Real seconds the beginning's pulse takes to fade.")]
    private float lookPulseSeconds = .45f;
    [SerializeField, Range(0f, 1f), Tooltip("Motion blur at the slowest (0 is none).")]
    private float lookBlur = .4f;

    private Volume volume;
    private ColorAdjustments color;
    private Vignette vignette;
    private ChromaticAberration split;
    private LensDistortion warp;
    private MotionBlur blur;

    private void Awake()
    {
        var host = new GameObject("Slow Motion Look");
        host.transform.SetParent(transform, false);
        volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 1f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Slow Motion Look (Runtime)";
        profile.hideFlags = HideFlags.HideAndDontSave;
        color = profile.Add<ColorAdjustments>(false);
        color.saturation.overrideState = color.contrast.overrideState = color.colorFilter.overrideState = true;
        vignette = profile.Add<Vignette>(false);
        vignette.intensity.overrideState = vignette.smoothness.overrideState = vignette.color.overrideState = true;
        split = profile.Add<ChromaticAberration>(false);
        split.intensity.overrideState = true;
        warp = profile.Add<LensDistortion>(false);
        warp.intensity.overrideState = true;
        blur = profile.Add<MotionBlur>(false);
        blur.intensity.overrideState = blur.mode.overrideState = blur.quality.overrideState = true;
        blur.mode.value = MotionBlurMode.CameraAndObjects;
        blur.quality.value = MotionBlurQuality.Medium;
        volume.sharedProfile = profile;
        volume.enabled = false;
    }

    private void OnDisable() => Show(false);

    private void OnDestroy() { if (volume != null && volume.sharedProfile != null) Destroy(volume.sharedProfile); }

    private void LateUpdate()
    {
        float depth = SkullSlowMotion.Depth, age = SkullSlowMotion.Age;
        float pulse = age >= 0f && age < lookPulseSeconds ? 1f - age / lookPulseSeconds : 0f;
        pulse *= pulse;
        bool showing = depth > .001f || pulse > .001f;
        Show(showing);
        if (!showing) return;
        color.saturation.value = lookSaturation * depth;
        color.contrast.value = lookContrast * depth;
        color.colorFilter.value = Color.Lerp(Color.white, lookTint, depth);
        vignette.intensity.value = lookVignette * depth;
        vignette.smoothness.value = lookVignetteSoftness;
        vignette.color.value = lookVignetteColor;
        split.intensity.value = Mathf.Clamp01(lookSplit * depth + lookSplitPulse * pulse);
        warp.intensity.value = lookWarp * pulse;
        blur.intensity.value = lookBlur * depth;
    }

    private void Show(bool on)
    {
        if (volume == null || volume.enabled == on) return;
        volume.enabled = on;
        if (on) PostProcessLoan.Take(this); else PostProcessLoan.Return(this);
    }
}
