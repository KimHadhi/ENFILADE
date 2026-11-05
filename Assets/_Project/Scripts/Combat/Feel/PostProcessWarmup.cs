using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The first kill screen stalled the frame it came on: the camera's first post-processing makes its render targets, and
/// each mix of effects (bloom, colour split, lens warp, motion blur) makes its own shader program the first frame it is
/// drawn. This draws every mix the kill screen, the low-health grade and the slow-motion look can make, one frame each
/// at strengths too faint to see, as soon as the player's camera is there, so the cost lands on spawning instead of on
/// the first kill. Once a run (the programs stay made); CombatWarmup starts it.
/// </summary>
public sealed class PostProcessWarmup : MonoBehaviour
{
    // Enough to switch an effect on, far too little to see.
    private const float Faint = 1e-4f;
    // Over everything the game lends post-processing to (PostProcessLoan's neutral volume is 199, the effects 200-201).
    private const float Priority = 300f;
    // Seconds to wait for the player's camera before giving up.
    private const float CameraWait = 30f;
    // Bloom, colour split, lens warp, motion blur: the mixes those effects make together, one frame each.
    private static readonly bool[][] Mixes =
    {
        new[] { false, false, false, false }, new[] { true, false, false, false }, new[] { true, true, false, false },
        new[] { true, true, true, false }, new[] { false, true, false, true }, new[] { false, true, true, true },
        new[] { true, true, true, true }, new[] { true, true, false, true },
    };

    private static bool done;
    private VolumeProfile profile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => done = false;

    public static void Run()
    {
        if (done || !Application.isPlaying) return;
        done = true;
        new GameObject("Post-processing Warmup").AddComponent<PostProcessWarmup>();
    }

    private IEnumerator Start()
    {
        for (float waited = 0f; Camera.main == null; waited += Time.unscaledDeltaTime)
        {
            if (waited > CameraWait) { Destroy(gameObject); yield break; }
            yield return null;
        }
        var volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 1f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Post-processing Warmup (Runtime)";
        profile.hideFlags = HideFlags.HideAndDontSave;
        var bloom = profile.Add<Bloom>(false);
        bloom.intensity.overrideState = true;
        var split = profile.Add<ChromaticAberration>(false);
        split.intensity.overrideState = true;
        var warp = profile.Add<LensDistortion>(false);
        warp.intensity.overrideState = true;
        // As SlowMotionLook draws it.
        var blur = profile.Add<MotionBlur>(false);
        blur.intensity.overrideState = blur.mode.overrideState = blur.quality.overrideState = true;
        blur.mode.value = MotionBlurMode.CameraAndObjects;
        blur.quality.value = MotionBlurQuality.Medium;
        volume.sharedProfile = profile;
        PostProcessLoan.Take(this);
        foreach (var mix in Mixes)
        {
            bloom.intensity.value = mix[0] ? Faint : 0f;
            split.intensity.value = mix[1] ? Faint : 0f;
            warp.intensity.value = mix[2] ? Faint : 0f;
            blur.intensity.value = mix[3] ? Faint : 0f;
            // This frame is drawn with the mix before the next is set.
            yield return null;
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        PostProcessLoan.Return(this);
        if (profile != null) Destroy(profile);
    }
}
