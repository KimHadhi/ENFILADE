using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The main camera renders without post-processing (the project default). Effects that need it take a loan: while any
/// loan is out, post-processing is on together with a neutral volume (priority 199) that cancels the project's default
/// profile (ACES tonemapping, bloom, film grain, motion blur ...), so only the effects' own volumes show, and the camera
/// is handed back unchanged when the last loan returns. A camera that already post-processes keeps its own look.
/// Effect volumes sit above 199 to show through the neutral one.
/// </summary>
public static class PostProcessLoan
{
    public const float NeutralPriority = 199f;
    private static readonly HashSet<object> owners = new HashSet<object>();
    private static UniversalAdditionalCameraData lent;
    private static Volume neutral;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { owners.Clear(); lent = null; neutral = null; }

    public static bool Holds(object owner) => lent != null && owners.Contains(owner);

    public static void Take(object owner)
    {
        if (owner == null) return;
        owners.Add(owner);
        if (lent != null) return;
        var view = Camera.main;
        var data = view != null ? view.GetUniversalAdditionalCameraData() : null;
        if (data == null || data.renderPostProcessing) return;
        data.renderPostProcessing = true;
        lent = data;
        Neutral().enabled = true;
    }

    public static void Return(object owner)
    {
        // An owner destroyed with its scene never returns its loan.
        if (!owners.Remove(owner)) return;
        owners.RemoveWhere(o => o is Object unityObject && unityObject == null);
        if (owners.Count > 0) return;
        if (lent != null) lent.renderPostProcessing = false;
        lent = null;
        if (neutral != null) neutral.enabled = false;
    }

    // Every URP effect at its neutral default.
    private static Volume Neutral()
    {
        if (neutral != null) return neutral;
        var host = new GameObject("Neutral Post (Runtime)");
        Object.DontDestroyOnLoad(host);
        neutral = host.AddComponent<Volume>();
        neutral.isGlobal = true;
        neutral.priority = NeutralPriority;
        neutral.weight = 1f;
        var baseline = ScriptableObject.CreateInstance<VolumeProfile>();
        baseline.name = "Neutral Post (Runtime)";
        baseline.hideFlags = HideFlags.HideAndDontSave;
        baseline.Add<Tonemapping>(true); baseline.Add<Bloom>(true); baseline.Add<FilmGrain>(true); baseline.Add<MotionBlur>(true);
        baseline.Add<PaniniProjection>(true); baseline.Add<ScreenSpaceLensFlare>(true); baseline.Add<DepthOfField>(true);
        baseline.Add<ColorAdjustments>(true); baseline.Add<Vignette>(true); baseline.Add<ChromaticAberration>(true);
        baseline.Add<LensDistortion>(true); baseline.Add<WhiteBalance>(true); baseline.Add<LiftGammaGain>(true);
        baseline.Add<SplitToning>(true); baseline.Add<ShadowsMidtonesHighlights>(true); baseline.Add<ColorCurves>(true);
        baseline.Add<ColorLookup>(true); baseline.Add<ChannelMixer>(true);
        neutral.sharedProfile = baseline;
        neutral.enabled = false;
        return neutral;
    }
}
