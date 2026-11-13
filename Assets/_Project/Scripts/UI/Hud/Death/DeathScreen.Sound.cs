using UnityEngine;

// DeathScreen: The sound. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    private void Sting()
    {
        if (look.sting <= 0f) return;
        if (voice == null)
        {
            var host = new GameObject("Death Sting");
            host.transform.SetParent(transform, false);
            voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            GameAudio.Bind(voice, AudioChannel.UI);
        }
        if (!bankLooked) { bankLooked = true; bank = SoundBank.Load(BankId); }
        var clip = bank != null ? bank.Pick(BankKey) : null;
        voice.Stop();
        voice.clip = clip != null ? clip : DeathSting.Clip;
        voice.pitch = 1f;
        GameAudio.SetGain(voice, Mathf.Clamp01(look.sting));
        voice.Play();
    }

    // Coming back cuts the sting almost at once, with a tiny ramp so it doesn't click (the addon's .06 s).
    private void Cut(float now)
    {
        if (voice == null) { cutting = false; return; }
        float f = 1f - Mathf.Clamp01((now - cutAt) / Mathf.Max(.01f, look.cutSeconds));
        GameAudio.SetGain(voice, Mathf.Clamp01(look.sting) * f);
        if (f > 0f) return;
        voice.Stop();
        cutting = false;
    }

    // The world's sound muffled on the listener (the UI channel bypasses it, so the sting stays clear). The low-pass is
    // the one SlowMotionSound uses: while the game is also slowed, the lower cutoff of the two holds.
    private void Muffle(float depth)
    {
        if (depth <= .001f || look.muffle <= 0f)
        {
            if (!muffling) return;
            muffling = false;
            if (muffle != null && SkullSlowMotion.Depth <= .001f) muffle.enabled = false;
            return;
        }
        if (muffle == null) muffle = Filter();
        if (muffle == null) return;
        muffling = true;
        muffle.enabled = true;
        // Heard pitch is logarithmic, so the cutoff falls evenly in octaves.
        float hertz = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(Mathf.Clamp(look.muffle, 100f, OpenCutoff)), depth));
        muffle.cutoffFrequency = SkullSlowMotion.Depth > .001f ? Mathf.Min(muffle.cutoffFrequency, hertz) : hertz;
    }

    // The listener's low-pass, put on ahead switched off (adding it mid-sound can click), as SlowMotionSound does.
    private static AudioLowPassFilter Filter()
    {
        var listener = GameAudio.Listener;
        if (listener == null) return null;
        if (listener.TryGetComponent(out AudioLowPassFilter filter)) return filter;
        filter = listener.gameObject.AddComponent<AudioLowPassFilter>();
        filter.cutoffFrequency = OpenCutoff;
        filter.lowpassResonanceQ = 1f;
        filter.enabled = false;
        return filter;
    }
}
