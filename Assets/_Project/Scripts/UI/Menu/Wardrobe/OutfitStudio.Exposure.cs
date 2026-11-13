using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// OutfitStudio: Exposure. One part of the class; the rest is in OutfitStudio.cs and the files beside it.
public sealed partial class OutfitStudio
{
    // The exposure's material; null when its shader isn't in the game (the pictures come out as rendered then).
    private static Material Lift
    {
        get
        {
            if (lift == null && !liftTried)
            {
                liftTried = true;
                var shader = Shader.Find(ExposureShader);
                if (shader != null && shader.isSupported) lift = new Material(shader) { name = "Outfit Exposure", hideFlags = HideFlags.HideAndDontSave };
                else Debug.LogWarning($"[OutfitStudio] {ExposureShader} 셰이더가 없어서 어두운 옷도 밝히지 않고 찍습니다.");
            }
            return lift;
        }
    }

    // The standing piece's first picture (in the film) read back small: the mean brightness of what it covers, in linear
    // light and as if uncovered (the film's colour is the piece's times its cover), weighted by cover, and the brightest
    // twentieth. From those the gain: up to the target, within the most, short of the ceiling. A light piece keeps 1.
    private void Meter()
    {
        metered = true;
        gain = 1f;
        var small = RenderTexture.GetTemporary(MeterSide, MeterSide, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(film, small);
            RenderTexture.active = small;
            if (meter == null) meter = new Texture2D(MeterSide, MeterSide, TextureFormat.RGBA32, false) { name = "Outfit Studio Meter", hideFlags = HideFlags.HideAndDontSave };
            meter.ReadPixels(new Rect(0f, 0f, MeterSide, MeterSide), 0, 0, false);
            var pixels = meter.GetPixels32();
            var bright = new List<float>(pixels.Length);
            double weighted = 0d, weight = 0d;
            foreach (var pixel in pixels)
            {
                float cover = pixel.a / 255f;
                if (cover < MeterCover) continue;
                float shine = (.2126f * Linear(pixel.r) + .7152f * Linear(pixel.g) + .0722f * Linear(pixel.b)) / cover;
                bright.Add(shine);
                weighted += shine * cover;
                weight += cover;
            }
            if (bright.Count == 0 || weight <= 0d) return;
            float mean = (float)(weighted / weight);
            bright.Sort();
            float brightest = bright[Mathf.Min(bright.Count - 1, Mathf.FloorToInt(bright.Count * .95f))];
            float want = Mathf.Clamp(OutfitPictureLook.ExposureTarget / Mathf.Max(mean, 1e-4f), 1f, OutfitPictureLook.ExposureMost);
            if (brightest > 0f) want = Mathf.Min(want, Mathf.Max(1f, OutfitPictureLook.ExposureCeiling / brightest));
            gain = want;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitStudio] " + problem.Message);
            gain = 1f;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(small);
        }
    }

    // A stored 8-bit value (sRGB in a linear project) in linear light.
    private static float Linear(byte value) =>
        QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.GammaToLinearSpace(value / 255f) : value / 255f;

    /// <summary>The picture as a readable texture of <paramref name="size"/> pixels a side (to be saved); null when nothing stands.</summary>
    public Texture2D Capture(int size) => Capture(size, size);

    /// <summary>
    /// The picture as a readable texture <paramref name="width"/> by <paramref name="height"/> pixels (frame it for that
    /// aspect first); null when nothing stands.
    /// </summary>
    public Texture2D Capture(int width, int height)
    {
        var plain = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            if (!Shoot(plain)) return null;
            RenderTexture.active = plain;
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Outfit Picture", wrapMode = TextureWrapMode.Clamp };
            picture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            picture.Apply();
            return picture;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(plain);
        }
    }

    // The scene's lights off and the studio's on, the piece shown, for this one render; everything back after.
    private bool Render(RenderTexture target)
    {
        switchedOff.Clear();
        if (!isolated)
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light != null && light.enabled && !Ours(light))
                {
                    light.enabled = false;
                    switchedOff.Add(light);
                }
        var was = lens.targetTexture;
        try
        {
            foreach (var (light, _) in rig) light.enabled = true;
            foreach (var renderer in shown) if (renderer != null) renderer.enabled = true;
            lens.targetTexture = target;
            lens.Render();
            return true;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitStudio] " + problem.Message);
            return false;
        }
        finally
        {
            lens.targetTexture = was;
            foreach (var renderer in shown) if (renderer != null) renderer.enabled = false;
            foreach (var (light, _) in rig) if (light != null) light.enabled = false;
            foreach (var light in switchedOff) if (light != null) light.enabled = true;
            switchedOff.Clear();
        }
    }

    private bool Ours(Light light)
    {
        foreach (var (mine, _) in rig) if (mine == light) return true;
        return false;
    }
}
