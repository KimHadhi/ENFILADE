using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// DeathScreen: The world's look. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    // Mid grey as light (an 18% grey card): where the addon's colour change is matched (ApplyGrade).
    private const float MidGrey = .18f;
    // How far either side of mid grey the contrast's slope is measured (a ratio of light).
    private const float SlopeStep = 1.1f;
    // The nearest URP lets the lens focus (m): everything past it is blurred.
    private const float FocusNear = .1f;

    private void Grade(bool on)
    {
        if (on && volume == null) MakeVolume();
        if (volume == null || volume.enabled == on) return;
        volume.enabled = on;
        if (on) PostProcessLoan.Take(this);
        else PostProcessLoan.Return(this);
    }

    private void MakeVolume()
    {
        var host = new GameObject("Death Grade");
        host.transform.SetParent(transform, false);
        volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 1f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Death Grade (Runtime)";
        profile.hideFlags = HideFlags.HideAndDontSave;
        grade = profile.Add<ColorAdjustments>(false);
        grade.saturation.overrideState = grade.colorFilter.overrideState = grade.contrast.overrideState = grade.postExposure.overrideState = true;
        // The addon blurs the whole picture alike, near and far (GMod's pp/blurscreen). URP's only blur is depth of field:
        // its bokeh with the lens focused as near as it goes blurs everything past that alike (ApplyGrade), with round
        // blades so it reads as a blur rather than as a lens.
        blur = profile.Add<DepthOfField>(false);
        blur.mode.overrideState = blur.focusDistance.overrideState = blur.aperture.overrideState = blur.focalLength.overrideState = true;
        blur.bladeCount.overrideState = blur.bladeCurvature.overrideState = blur.bladeRotation.overrideState = true;
        blur.focusDistance.value = FocusNear;
        blur.aperture.value = 1f;
        blur.bladeCount.value = 9;
        blur.bladeCurvature.value = 1f;
        blur.bladeRotation.value = 0f;
        volume.sharedProfile = profile;
        volume.enabled = false;
        ApplyGrade(0f, 0f);
    }

    // The addon's colour change (GMod's colour modify, all its numbers grown by the colour level as the addon does) works
    // on the screen's 0-1 values: each channel gets its add and the brightness, then the contrast multiplies it, then
    // the saturation pulls it toward grey. URP grades light in log space, so the change is matched where the eye judges
    // a picture, on mid grey: the exposure takes how much brighter or darker grey comes out, the contrast how much
    // steeper the tones round it get, and the colour filter each channel's own add there. Saturation is the same number
    // in both. The blur grows evenly with its level, as the addon's passes do.
    private void ApplyGrade(float colour, float blurred)
    {
        if (grade == null) return;
        float add = look.brightness * colour, times = 1f + (look.contrast - 1f) * colour;
        float grey = Moved(MidGrey, add, times);
        grade.postExposure.value = Mathf.Log(grey / MidGrey, 2f);
        // URP's contrast scales distances from mid grey in log light by 1 + contrast / 100.
        float slope = Mathf.Log(Moved(MidGrey * SlopeStep, add, times) / Moved(MidGrey / SlopeStep, add, times)) / (2f * Mathf.Log(SlopeStep));
        grade.contrast.value = Mathf.Clamp((slope - 1f) * 100f, -100f, 100f);
        grade.colorFilter.value = new Color(Channel(look.addRed), Channel(look.addGreen), Channel(look.addBlue), 1f);
        grade.saturation.value = Mathf.Clamp((Mathf.Lerp(1f, look.colour, colour) - 1f) * 100f, -100f, 100f);
        bool blurs = look.blur > 0f;
        blur.mode.value = blurs ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off;
        blur.focalLength.value = FocalFor(blurs ? Mathf.Clamp01(look.blur) * blurred : 0f);

        float Channel(float extra) => grey > 0f ? Moved(MidGrey, add + extra * colour, times) / grey : 1f;
    }

    // Light through the addon's change of the screen's 0-1 value: the add, then the multiply.
    private static float Moved(float light, float add, float times)
        => Mathf.GammaToLinearSpace(Mathf.Max(0f, (Mathf.LinearToGammaSpace(light) + add) * times));

    // The focal length (mm) that blurs everything past FocusNear by this share of URP's largest bokeh (0-1). With the
    // aperture at 1, URP's blur at a depth is (1 - focus / depth) · f² / (1000 · focus - f), f in mm, so the share is
    // that last part: f² + share · f - 1000 · focus · share = 0. At 0 the shortest lens leaves about 1% (no blur seen).
    private static float FocalFor(float share)
    {
        if (share <= 0f) return 1f;
        float root = Mathf.Sqrt(share * share + 4000f * FocusNear * share);
        return Mathf.Clamp((root - share) * .5f, 1f, 300f);
    }

    private void Warm()
    {
        card.Build();
        Filter();
        _ = WhistleClip();
        if (!warming && phase == Phase.Hidden && isActiveAndEnabled) StartCoroutine(WarmGrade());
    }

    // The blur's passes drawn for a few frames while the loading screen is up, at a strength nobody sees.
    private IEnumerator WarmGrade()
    {
        warming = true;
        look = full;
        Grade(true);
        ApplyGrade(0f, 0f);
        for (int frame = 0; frame < 3; frame++) yield return null;
        if (phase == Phase.Hidden) Grade(false);
        warming = false;
    }
}
