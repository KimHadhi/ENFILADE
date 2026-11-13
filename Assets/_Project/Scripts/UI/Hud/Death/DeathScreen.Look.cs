using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// DeathScreen: The world's look. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
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
        edge = profile.Add<Vignette>(false);
        edge.intensity.overrideState = edge.smoothness.overrideState = edge.color.overrideState = edge.rounded.overrideState = true;
        edge.color.value = new Color(.07f, 0f, .02f);
        edge.smoothness.value = .4f;
        edge.rounded.value = false;
        blur = profile.Add<DepthOfField>(false);
        blur.mode.overrideState = blur.gaussianStart.overrideState = blur.gaussianEnd.overrideState = true;
        blur.gaussianMaxRadius.overrideState = blur.highQualitySampling.overrideState = true;
        blur.highQualitySampling.value = true;
        volume.sharedProfile = profile;
        volume.enabled = false;
        ApplyGrade(0f, 0f, 0f);
    }

    private void ApplyGrade(float colour, float darkness, float blurred)
    {
        if (grade == null) return;
        grade.saturation.value = look.saturation * colour;
        grade.colorFilter.value = Color.Lerp(Color.white, look.tint, colour);
        grade.contrast.value = look.contrast * colour;
        grade.postExposure.value = look.exposure * colour;
        edge.intensity.value = Mathf.Clamp01(look.vignette * darkness);
        bool blurs = look.blurRadius > 0f;
        blur.mode.value = blurs ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Off;
        // The blurred distance closes in evenly in octaves, from past the horizon to near the eyes.
        float near = Mathf.Max(.1f, look.blurNear);
        float end = Mathf.Exp(Mathf.Lerp(Mathf.Log(BlurOff), Mathf.Log(near), blurs ? blurred : 0f));
        blur.gaussianEnd.value = end;
        blur.gaussianStart.value = end * (1f - blurred);
        blur.gaussianMaxRadius.value = Mathf.Lerp(LeastRadius, Mathf.Clamp(look.blurRadius, LeastRadius, MostRadius), blurred);
    }

    private void Warm()
    {
        card.Build();
        Filter();
        if (!warming && phase == Phase.Hidden && isActiveAndEnabled) StartCoroutine(WarmGrade());
    }

    // The blur's passes drawn for a few frames while the loading screen is up, at a distance nothing in view reaches.
    private IEnumerator WarmGrade()
    {
        warming = true;
        look = full;
        Grade(true);
        ApplyGrade(0f, 0f, 0f);
        for (int frame = 0; frame < 3; frame++) yield return null;
        if (phase == Phase.Hidden) Grade(false);
        warming = false;
    }
}
