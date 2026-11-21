using UnityEngine;

// DeathScreen: The sound. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    // The addon's whistle (Art/Audio/UI/Death/Whistle.mp3, 4.2 s: a muffled roar with a ring over it), played once on
    // death. It comes from the UI sound bank like every menu sound, so another file dropped in that folder replaces it
    // and Sounds.csv's UI/Death row scales it.
    private void Whistle()
    {
        if (look.whistle <= 0f) return;
        var clip = WhistleClip();
        if (clip == null) return;
        if (voice == null)
        {
            var host = new GameObject("Death Whistle");
            host.transform.SetParent(transform, false);
            voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            GameAudio.Bind(voice, AudioChannel.UI);
        }
        voice.Stop();
        voice.clip = clip;
        voice.pitch = 1f;
        GameAudio.SetGain(voice, WhistleGain());
        voice.Play();
    }

    private AudioClip WhistleClip()
    {
        if (!bankLooked) { bankLooked = true; bank = SoundBank.Load(BankId); }
        var clip = bank != null ? bank.Pick(BankKey) : null;
        // Read ahead of the first death (Prepare), so it never waits on the file.
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        return clip;
    }

    private float WhistleGain() => Mathf.Clamp01(look.whistle * (bank != null ? bank.SettingsOf(BankKey).Volume : 1f));

    // Coming back cuts the whistle almost at once, with a tiny ramp so it doesn't click (the addon's .06 s).
    private void Cut(float now)
    {
        if (voice == null) { cutting = false; return; }
        float f = 1f - Mathf.Clamp01((now - cutAt) / Mathf.Max(.01f, look.cutSeconds));
        GameAudio.SetGain(voice, WhistleGain() * f);
        if (f > 0f) return;
        voice.Stop();
        cutting = false;
    }

    // The world's sound muffled on the listener (the UI channel bypasses it, so the whistle stays clear). The low-pass is
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
