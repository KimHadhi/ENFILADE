using System;
using UnityEngine;

/// <summary>
/// The menus' sounds, played flat on the UI channel (GameAudio: they follow the UI volume and keep playing while the
/// game is paused). A cue is, in order, a clip from the UI sound bank when its folder holds any
/// (Art/Audio/UI/&lt;cue&gt;, e.g. Art/Audio/UI/Buy; volume and pitch in Tables/Sounds, row "UI/&lt;cue&gt;") and otherwise a
/// short sound made here once, so a menu answers the player before any clips exist, as the guns do (FeelAudio).
/// First used by the shop (user, 2026-11-10: nothing told them a buy went through); the HUD's alerts use it too.
/// </summary>
public static class KitSound
{
    /// <summary>Buy: something was paid for. Refuse: it couldn't be. Alert: a teammate went down or died (AlertHUD).</summary>
    public enum Cue { Buy, Refuse, Alert }

    private const string BankId = "UI";
    private const int Rate = 44100;
    private static readonly AudioClip[] made = new AudioClip[3];
    private static AudioSource source;
    private static SoundBank bank;
    private static bool bankLooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        source = null;
        bank = null;
        bankLooked = false;
        Array.Clear(made, 0, made.Length);
    }

    public static void Play(Cue cue, float volume = 1f)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        if (source == null)
        {
            var host = new GameObject("Kit Sound");
            UnityEngine.Object.DontDestroyOnLoad(host);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            GameAudio.Bind(source, AudioChannel.UI);
        }
        if (!bankLooked) { bankLooked = true; bank = SoundBank.Load(BankId); }
        string key = cue.ToString();
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip != null)
        {
            var settings = bank.SettingsOf(key);
            source.pitch = settings.RandomPitch();
            source.PlayOneShot(clip, Mathf.Clamp01(settings.Volume * volume));
            return;
        }
        source.pitch = 1f;
        source.PlayOneShot(Made(cue), Mathf.Clamp01(volume));
    }

    private static AudioClip Made(Cue cue)
    {
        int index = (int)cue;
        if (made[index] != null) return made[index];
        float[] data = cue == Cue.Buy ? Till() : cue == Cue.Alert ? Fall() : Buzz();
        var clip = AudioClip.Create("Kit " + cue, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return made[index] = clip;
    }

    // A till: the drawer's clack, then its bell struck twice a fifth apart, the second ringing out.
    private static float[] Till()
    {
        var data = new float[(int)(Rate * .62f)];
        var random = new System.Random(4217);
        float held = 0f;
        int clack = (int)(Rate * .03f);
        for (int i = 0; i < clack; i++)
        {
            float t = i / (float)Rate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .35f;
            data[i] += held * Mathf.Exp(-t / .006f) * .5f;
        }
        Bell(data, .02f, 1568f, .1f, .34f);
        Bell(data, .11f, 2349f, .22f, .42f);
        return Finish(data, .7f);
    }

    // A struck bell starting at <at> (s): the note, with two partials above it that die sooner.
    private static void Bell(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate, turn = 2f * Mathf.PI * pitch * t;
            float wave = Mathf.Sin(turn) * Mathf.Exp(-t / decay)
                + .45f * Mathf.Sin(turn * 2.76f) * Mathf.Exp(-t / (decay * .55f))
                + .25f * Mathf.Sin(turn * 5.4f) * Mathf.Exp(-t / (decay * .3f));
            data[i] += wave * Mathf.Min(1f, t / .002f) * level;
        }
    }

    // A refusal: two short low blips, dull so they never pass for a hit or a buy.
    private static float[] Buzz()
    {
        const float Blip = .085f, Every = .12f;
        var data = new float[(int)(Rate * (Every + Blip + .03f))];
        int length = (int)(Blip * Rate);
        for (int blip = 0; blip < 2; blip++)
        {
            int from = (int)(blip * Every * Rate);
            for (int i = 0; i < length && from + i < data.Length; i++)
            {
                float t = i / (float)Rate, turn = 2f * Mathf.PI * 165f * t;
                float edge = Mathf.Min(1f, Mathf.Min(t / .004f, (Blip - t) / .012f));
                data[from + i] += (Mathf.Sin(turn) + .5f * Mathf.Sin(turn * 3f) + .22f * Mathf.Sin(turn * 5f)) * edge;
            }
        }
        return Finish(data, .6f);
    }

    // An alert: two round notes, the second a fourth lower and ringing a little longer. Falling, so it reads as bad news,
    // and soft, so it is never taken for a hit.
    private static float[] Fall()
    {
        var data = new float[(int)(Rate * .5f)];
        Note(data, 0f, 880f, .08f, .5f);
        Note(data, .13f, 659f, .15f, .5f);
        return Finish(data, .55f);
    }

    // A plain note starting at <at> (s): the pitch and a little of its octave and twelfth.
    private static void Note(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate, turn = 2f * Mathf.PI * pitch * t;
            data[i] += (Mathf.Sin(turn) + .3f * Mathf.Sin(turn * 2f) + .12f * Mathf.Sin(turn * 3f)) * Mathf.Exp(-t / decay) * Mathf.Min(1f, t / .004f) * level;
        }
    }

    // Brings the loudest sample to <peak> and fades the last few ms so the end doesn't click.
    private static float[] Finish(float[] data, float peak)
    {
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f)
        {
            float gain = peak / top;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }
        int fade = Mathf.Min(data.Length, Rate / 100);
        for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
        return data;
    }
}
