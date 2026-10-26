using System;
using System.Collections.Generic;
using UnityEngine;

public enum GunSoundProfile { Rifle, Pistol, Heavy }
public enum FeelSound { RifleShot, PistolShot, HeavyShot, DryFire, LowAmmo, MagOut, MagIn, Bolt, HitMarker, Kill, HeadshotKill, BodyFall }

/// <summary>
/// Shot, mechanical and confirm sounds. Every sound has a synthesized fallback built once on first use, so the
/// game has a full audio layer before any clips are imported; assigned clips (GunFeel / WeaponData) replace it.
/// Created on first use during Play; no scene setup.
/// </summary>
[DisallowMultipleComponent]
public sealed class FeelAudio : MonoBehaviour
{
    private const int FlatVoices = 12, SpatialVoices = 6, Rate = 44100;
    private static FeelAudio instance;
    private static readonly Dictionary<FeelSound, AudioClip[]> synthesized = new Dictionary<FeelSound, AudioClip[]>();
    private readonly AudioSource[] flat = new AudioSource[FlatVoices], spatial = new AudioSource[SpatialVoices];
    private readonly Dictionary<FeelSound, AudioClip> previous = new Dictionary<FeelSound, AudioClip>();
    private int nextFlat, nextSpatial;

    public static FeelSound ShotFor(GunSoundProfile profile)
        => profile == GunSoundProfile.Pistol ? FeelSound.PistolShot : profile == GunSoundProfile.Heavy ? FeelSound.HeavyShot : FeelSound.RifleShot;

    /// <summary>2D sound, for anything the player does or is told.</summary>
    public static void Play(FeelSound sound, AudioClip[] overrides, float volume, float pitch = 1f)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        var self = Get();
        var clip = self.Pick(sound, overrides);
        if (clip == null) return;
        var voice = self.flat[self.nextFlat];
        self.nextFlat = (self.nextFlat + 1) % FlatVoices;
        voice.pitch = Mathf.Clamp(pitch, .25f, 3f);
        voice.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>3D sound at a world point (bodies landing).</summary>
    public static void PlayAt(FeelSound sound, AudioClip[] overrides, Vector3 point, float volume, float pitch = 1f, float maxDistance = 14f)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        var self = Get();
        var clip = self.Pick(sound, overrides);
        if (clip == null) return;
        var voice = self.spatial[self.nextSpatial];
        self.nextSpatial = (self.nextSpatial + 1) % SpatialVoices;
        voice.transform.position = point;
        voice.maxDistance = Mathf.Max(voice.minDistance + .1f, maxDistance);
        voice.pitch = Mathf.Clamp(pitch, .25f, 3f);
        voice.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private static FeelAudio Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Feel Audio", groupName: "Audio").AddComponent<FeelAudio>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        for (int i = 0; i < FlatVoices; i++)
        {
            flat[i] = gameObject.AddComponent<AudioSource>();
            flat[i].playOnAwake = false; flat[i].spatialBlend = 0f; flat[i].priority = 32;
        }
        for (int i = 0; i < SpatialVoices; i++)
        {
            var voice = new GameObject("Feel Voice " + i).AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false; voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 1.5f; voice.dopplerLevel = 0f;
            spatial[i] = voice;
        }
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>Assigned clips first, otherwise a synthesized variation; avoids repeating the last pick.</summary>
    private AudioClip Pick(FeelSound sound, AudioClip[] overrides)
    {
        AudioClip[] pool = HasAny(overrides) ? overrides : Synthesized(sound);
        previous.TryGetValue(sound, out var last);
        AudioClip chosen = null; int candidates = 0;
        foreach (var clip in pool)
        {
            if (clip == null || (clip == last && pool.Length > 1)) continue;
            // Reservoir sampling: uniform pick without allocating.
            if (UnityEngine.Random.Range(0, ++candidates) == 0) chosen = clip;
        }
        if (chosen == null) chosen = last;
        previous[sound] = chosen;
        return chosen;
    }

    private static bool HasAny(AudioClip[] clips)
    {
        if (clips == null) return false;
        foreach (var clip in clips) if (clip != null) return true;
        return false;
    }

    private static AudioClip[] Synthesized(FeelSound sound)
    {
        if (synthesized.TryGetValue(sound, out var clips)) return clips;
        int variations = sound == FeelSound.RifleShot || sound == FeelSound.PistolShot || sound == FeelSound.HeavyShot ? 4
            : sound == FeelSound.HitMarker ? 3 : 2;
        clips = new AudioClip[variations];
        for (int i = 0; i < variations; i++)
        {
            float[] data = Synthesize(sound, new System.Random(((int)sound + 1) * 7919 + i * 104729));
            var clip = AudioClip.Create("Synth " + sound + " " + i, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clips[i] = clip;
        }
        synthesized[sound] = clips;
        return clips;
    }

    // ---------------------------------------------------------------- synthesis

    private static float[] Synthesize(FeelSound sound, System.Random random)
    {
        switch (sound)
        {
            case FeelSound.RifleShot: return Gunshot(random, .55f, crack: .0045f, body: .042f, thumpFrom: 150f, thumpTo: 46f, thumpDecay: .075f, tail: .17f, crackGain: 1f);
            case FeelSound.PistolShot: return Gunshot(random, .42f, crack: .0035f, body: .03f, thumpFrom: 210f, thumpTo: 70f, thumpDecay: .05f, tail: .11f, crackGain: 1.25f);
            case FeelSound.HeavyShot: return Gunshot(random, .8f, crack: .006f, body: .065f, thumpFrom: 110f, thumpTo: 34f, thumpDecay: .12f, tail: .26f, crackGain: .9f);
            case FeelSound.DryFire: return Finish(Mix(.09f, Click(random, 0f, .7f, 2600f), Click(random, .019f, .45f, 1900f)));
            // Dry mechanical tick (rising pitch is applied at play time), not a bell: no ringing tail.
            case FeelSound.LowAmmo: return Finish(Mix(.06f, Click(random, 0f, .9f, 3200f), Sine(1400f * Jitter(random, .02f), .006f, .35f)), .8f);
            case FeelSound.MagOut: return Finish(Mix(.3f, Clack(random, 0f, .9f, 1500f), Slide(random, .015f, .07f, .3f), Clack(random, .095f, .35f, 1100f)));
            case FeelSound.MagIn: return Finish(Mix(.3f, Slide(random, 0f, .06f, .35f), Clack(random, .065f, 1f, 1700f), Click(random, .078f, .5f, 3200f)));
            case FeelSound.Bolt: return Finish(Mix(.38f, Clack(random, 0f, .7f, 1300f), Slide(random, .02f, .07f, .25f), Clack(random, .13f, 1f, 1900f),
                Shift(Bell(1800f, .05f, .3f, 1.44f, 2.3f), .13f)));
            case FeelSound.HitMarker: return Finish(Mix(.07f, Click(random, 0f, .8f, 5200f), Sine(1900f * Jitter(random, .03f), .012f, .55f), Sine(3800f, .006f, .25f)), .85f);
            // Kill confirms stay low and dull: no bell/ding layer, which is piercing at confirm volume.
            case FeelSound.Kill: return Finish(Mix(.65f, Sweep(120f, 40f, .17f, 1f), Noise(random, 0f, .055f, .8f, 900f), Whoosh(random)), .95f);
            case FeelSound.HeadshotKill: return Finish(Mix(.7f, Sweep(130f, 38f, .2f, 1f), Noise(random, 0f, .06f, .85f, 1100f), Whoosh(random),
                Click(random, 0f, .6f, 2200f)), .95f);
            case FeelSound.BodyFall: return Finish(Mix(.45f, Sweep(95f, 42f, .09f, 1f), Noise(random, 0f, .035f, .8f, 700f),
                Shift(Noise(random, 0f, .05f, .3f, 1800f), .015f)), .9f);
            default: return new float[64];
        }
    }

    /// <summary>
    /// Layered gunshot: supersonic crack (high-passed noise), muzzle blast body (low-passed noise), chest thump
    /// (falling sine), a tiny mechanism click and a filtered tail with early reflections for space.
    /// </summary>
    private static float[] Gunshot(System.Random random, float seconds, float crack, float body, float thumpFrom, float thumpTo,
        float thumpDecay, float tail, float crackGain)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var dry = new float[n];
        float lowBody = 0f, lowTail = 0f, lowCrack = 0f, phase = 0f;
        float bodyCut = Alpha(1700f * Jitter(random, .12f)), tailCut = Alpha(520f * Jitter(random, .15f)), crackCut = Alpha(2600f);
        float thumpDecayJ = thumpDecay * Jitter(random, .1f);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate, white = (float)(random.NextDouble() * 2.0 - 1.0);
            lowCrack += crackCut * (white - lowCrack);
            lowBody += bodyCut * (white - lowBody);
            lowTail += tailCut * (white - lowTail);
            float attack = Mathf.Clamp01(t / .0006f);
            float frequency = thumpTo + (thumpFrom - thumpTo) * Mathf.Exp(-t / .022f);
            phase += 2f * Mathf.PI * frequency / Rate;
            dry[i] = (white - lowCrack) * Mathf.Exp(-t / crack) * crackGain
                + lowBody * Mathf.Exp(-t / body) * 2.6f * attack
                + Mathf.Sin(phase) * Mathf.Exp(-t / thumpDecayJ) * .95f * attack
                + lowTail * Mathf.Exp(-t / tail) * 3.2f * Mathf.Clamp01(t / .012f)
                + (t < .004f ? Mathf.Sin(2f * Mathf.PI * 3300f * t) * Mathf.Exp(-t / .0012f) * .35f : 0f);
        }
        // Early reflections, darker and quieter than the direct blast.
        var output = (float[])dry.Clone();
        int[] delays = { (int)(.029f * Rate * Jitter(random, .1f)), (int)(.063f * Rate * Jitter(random, .1f)), (int)(.118f * Rate * Jitter(random, .1f)) };
        float[] gains = { .32f, .2f, .11f };
        for (int d = 0; d < delays.Length; d++)
        {
            float low = 0f, cut = Alpha(1400f - d * 350f);
            for (int i = delays[d]; i < n; i++) { low += cut * (dry[i - delays[d]] - low); output[i] += low * gains[d]; }
        }
        return Finish(output, .95f, drive: 1.9f);
    }

    private static float[] Click(System.Random random, float start, float gain, float tone)
    {
        int n = Mathf.CeilToInt((start + .02f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(tone);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += cut * (white - low);
            data[i] = ((white - low) * Mathf.Exp(-t / .0011f) + Mathf.Sin(2f * Mathf.PI * tone * .7f * t) * Mathf.Exp(-t / .0035f) * .5f) * gain;
        }
        return data;
    }

    /// <summary>Metal-on-polymer impact: a short low-passed burst plus a damped knock.</summary>
    private static float[] Clack(System.Random random, float start, float gain, float tone)
    {
        int n = Mathf.CeilToInt((start + .06f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(tone * 2f);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += cut * (white - low);
            data[i] = (low * Mathf.Exp(-t / .009f) * 2.2f + Mathf.Sin(2f * Mathf.PI * tone * t) * Mathf.Exp(-t / .014f) * .45f) * gain;
        }
        return data;
    }

    private static float[] Slide(System.Random random, float start, float duration, float gain)
    {
        int n = Mathf.CeilToInt((start + duration) * Rate); var data = new float[n]; float low = 0f, lower = 0f;
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = (i / (float)Rate - start) / duration, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += Alpha(2500f + 3000f * t) * (white - low); lower += Alpha(700f) * (low - lower);
            data[i] = (low - lower) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * gain;
        }
        return data;
    }

    private static float[] Noise(System.Random random, float start, float decay, float gain, float cutoff)
    {
        int n = Mathf.CeilToInt((start + decay * 6f) * Rate); var data = new float[n]; float low = 0f, cut = Alpha(cutoff);
        for (int i = (int)(start * Rate); i < n; i++)
        {
            float t = i / (float)Rate - start; low += cut * ((float)(random.NextDouble() * 2.0 - 1.0) - low);
            data[i] = low * Mathf.Exp(-t / decay) * gain * 2.4f;
        }
        return data;
    }

    /// <summary>Kill "whump": band-limited noise swelling for 60 ms, then falling away, like a camera shutter through air.</summary>
    private static float[] Whoosh(System.Random random)
    {
        int n = Mathf.CeilToInt(.42f * Rate); var data = new float[n]; float low = 0f, lower = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += Alpha(400f + 2600f * Mathf.Exp(-t / .12f)) * (white - low); lower += Alpha(180f) * (low - lower);
            float envelope = t < .06f ? t / .06f : Mathf.Exp(-(t - .06f) / .09f);
            data[i] = (low - lower) * envelope * .9f;
        }
        return data;
    }

    private static float[] Sine(float frequency, float decay, float gain)
    {
        int n = Mathf.CeilToInt(decay * 7f * Rate); var data = new float[n];
        for (int i = 0; i < n; i++) { float t = i / (float)Rate; data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t / decay) * gain; }
        return data;
    }

    private static float[] Sweep(float from, float to, float decay, float gain)
    {
        int n = Mathf.CeilToInt(decay * 5f * Rate); var data = new float[n]; float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate; phase += 2f * Mathf.PI * (to + (from - to) * Mathf.Exp(-t / (decay * .4f))) / Rate;
            data[i] = Mathf.Sin(phase) * Mathf.Exp(-t / decay) * gain * Mathf.Clamp01(t / .002f);
        }
        return data;
    }

    /// <summary>Inharmonic partials decaying at different rates read as struck metal.</summary>
    private static float[] Bell(float fundamental, float decay, float gain, float ratio2, float ratio3)
    {
        int n = Mathf.CeilToInt(decay * 6f * Rate); var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, w = 2f * Mathf.PI * fundamental * t;
            data[i] = (Mathf.Sin(w) * Mathf.Exp(-t / decay) + .5f * Mathf.Sin(w * ratio2) * Mathf.Exp(-t / (decay * .55f))
                + .3f * Mathf.Sin(w * ratio3) * Mathf.Exp(-t / (decay * .3f))) * gain * Mathf.Clamp01(t / .0008f);
        }
        return data;
    }

    private static float[] Shift(float[] data, float seconds)
    {
        int offset = Mathf.RoundToInt(seconds * Rate); var shifted = new float[data.Length + offset];
        Array.Copy(data, 0, shifted, offset, data.Length); return shifted;
    }

    private static float[] Mix(float seconds, params float[][] layers)
    {
        var data = new float[Mathf.CeilToInt(seconds * Rate)];
        foreach (var layer in layers)
            for (int i = 0; i < Mathf.Min(layer.Length, data.Length); i++) data[i] += layer[i];
        return data;
    }

    /// <summary>Soft saturation, peak normalisation and a short fade so no clip ends on a click.</summary>
    private static float[] Finish(float[] data, float peak = .9f, float drive = 1.2f)
    {
        float max = 0f, norm = (float)Math.Tanh(drive);
        for (int i = 0; i < data.Length; i++) { data[i] = (float)Math.Tanh(data[i] * drive) / norm; max = Mathf.Max(max, Mathf.Abs(data[i])); }
        float scale = max > 0f ? peak / max : 0f;
        int fade = Mathf.Min(data.Length, Rate / 50);
        for (int i = 0; i < data.Length; i++)
        {
            float tail = data.Length - i < fade ? (data.Length - i) / (float)fade : 1f;
            data[i] *= scale * tail;
        }
        return data;
    }

    private static float Alpha(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
    private static float Jitter(System.Random random, float amount) => 1f + (float)(random.NextDouble() * 2.0 - 1.0) * amount;
}
