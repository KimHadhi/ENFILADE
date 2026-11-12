using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Each moment's sound, which now carries the weight its look no longer shouts (the user's restraint, 2026-11-12). The
/// user ruled out a moment's own music ("SFX는 있어도 전용 음악까진 X"), so each is a short stinger, under a second and
/// made here, one per class and a few for moments:
/// - Knight: a low bell and a blade drawn;
/// - Assault: a bugle's three notes over a drum;
/// - Ghost: one high ring of steel and a breath;
/// - Point: a cylinder spun and its hammer;
/// - Détente: two heartbeats and a clear chime;
/// - Schießhund: six clicks and a till's ring;
/// - Support: a radio's click and a crate set down.
/// The moments' own: bounty, reversal, cover, kata, duel, and duelwon (a ricochet and one low toll as a duel's foe falls).
/// A file at Resources/Audio/Moments/&lt;key&gt; (a mod's or a recorded one) plays in a stinger's place. One's own plays at
/// the ear; a friend's plays where they stand, softer with distance. Numbers: Tables/MomentFx.csv, row Sound.
/// </summary>
public static class MomentSound
{
    private const int Rate = 44100;
    private static readonly Dictionary<string, AudioClip> made = new Dictionary<string, AudioClip>();
    private static System.Random noise = new System.Random(4217);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { made.Clear(); noise = new System.Random(4217); }

    /// <summary>
    /// Plays <paramref name="key"/> (a class id, or bounty, reversal, cover, kata, duel). With <paramref name="at"/> it
    /// plays there in the world (a friend's), else at this PC's ear.
    /// </summary>
    public static void Play(string key, Vector3? at = null, float volume = 1f)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(key)) return;
        var clip = Clip(key.ToLowerInvariant());
        if (clip == null) return;
        float level = Mathf.Clamp01(volume * MomentLook.Number("Sound", "volume", .9f));
        if (at.HasValue)
            GameAudio.PlayAt(clip, at.Value, AudioChannel.Combat, level * MomentLook.Number("Sound", "friend", .75f),
                MomentLook.Number("Sound", "near", 4f), MomentLook.Number("Sound", "far", 60f));
        else
        {
            var listener = GameAudio.Listener;
            Vector3 ear = listener != null ? listener.transform.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            GameAudio.PlayAt(clip, ear, AudioChannel.Combat, level, 50f, 60f);
        }
    }

    private static AudioClip Clip(string key)
    {
        if (made.TryGetValue(key, out var clip) && clip != null) return clip;
        clip = Resources.Load<AudioClip>("Audio/Moments/" + key);
        if (clip == null) clip = Make(key);
        if (clip != null) made[key] = clip;
        return clip;
    }

    // ---- The stingers ----

    private static AudioClip Make(string key)
    {
        var data = new float[(int)(Rate * 1.5f)];
        switch (key)
        {
            case PlayerClass.Knight:
                Tone(data, 0f, 1.4f, 98f, 96f, .5f, Wave.Sine);
                Tone(data, 0f, 1.1f, 196f, 194f, .22f, Wave.Sine);
                Tone(data, 0f, .9f, 587f, 580f, .07f, Wave.Triangle);
                Tone(data, 0f, .7f, 1318f, 1300f, .05f, Wave.Sine);
                Noise(data, .02f, .35f, .16f, 3000f, 9000f);
                break;
            case PlayerClass.Assault:
                float[] notes = { 392f, 523f, 659f };
                for (int i = 0; i < notes.Length; i++)
                {
                    Tone(data, i * .11f, i == 2 ? .44f : .16f, notes[i], notes[i], .1f, Wave.Saw);
                    Tone(data, i * .11f, .1f, notes[i] * 2f, notes[i] * 2f, .025f, Wave.Square);
                }
                Tone(data, 0f, .3f, 80f, 48f, .6f, Wave.Sine);
                Noise(data, 0f, .12f, .28f, 40f, 400f);
                break;
            case PlayerClass.Ghost:
                Tone(data, 0f, .5f, 2400f, 2380f, .2f, Wave.Sine);
                Tone(data, 0f, .4f, 3610f, 3600f, .09f, Wave.Sine);
                Noise(data, .05f, .5f, .05f, 60f, 900f);
                break;
            case PlayerClass.Point:
                for (int i = 0; i < 9; i++) Noise(data, i * .026f, .02f, .22f, 2500f, 12000f);
                Noise(data, .3f, .04f, .45f, 1400f, 2200f);
                Tone(data, .3f, .06f, 140f, 90f, .18f, Wave.Square);
                break;
            case PlayerClass.Detente:
                Tone(data, 0f, .18f, 62f, 46f, .8f, Wave.Sine);
                Tone(data, .2f, .16f, 58f, 42f, .6f, Wave.Sine);
                Tone(data, .32f, .9f, 880f, 878f, .11f, Wave.Sine);
                Tone(data, .32f, .8f, 1320f, 1318f, .05f, Wave.Sine);
                break;
            case PlayerClass.Schiesshund:
                for (int i = 0; i < 6; i++) Noise(data, i * .06f, .025f, .3f, 2000f, 3200f);
                Tone(data, .38f, .6f, 1760f, 1760f, .12f, Wave.Sine);
                Tone(data, .38f, .5f, 2637f, 2637f, .07f, Wave.Sine);
                break;
            case PlayerClass.Support:
                Noise(data, 0f, .05f, .22f, 1100f, 1900f);
                Tone(data, .05f, .08f, 1000f, 1000f, .09f, Wave.Sine);
                Tone(data, .16f, .06f, 1000f, 1000f, .07f, Wave.Sine);
                Tone(data, .25f, .3f, 90f, 44f, .7f, Wave.Sine);
                Noise(data, .25f, .15f, .28f, 40f, 300f);
                break;
            case "bounty":
                Tone(data, 0f, .7f, 1760f, 1760f, .15f, Wave.Sine);
                Tone(data, .06f, .6f, 2637f, 2637f, .09f, Wave.Sine);
                Noise(data, 0f, .08f, .14f, 5000f, 14000f);
                for (int i = 0; i < 4; i++) Noise(data, .12f + i * .05f, .03f, .09f, 5000f, 7000f);
                Tone(data, 0f, .35f, 110f, 55f, .45f, Wave.Sine);
                break;
            case "reversal":
                Noise(data, 0f, .26f, .3f, 600f, 3000f, true);
                Tone(data, .14f, .22f, 120f, 50f, .7f, Wave.Sine);
                break;
            case "cover":
                Tone(data, 0f, .55f, 880f, 870f, .12f, Wave.Sine);
                Tone(data, 0f, .45f, 1327f, 1320f, .08f, Wave.Sine);
                Tone(data, 0f, .35f, 2093f, 2080f, .05f, Wave.Triangle);
                Noise(data, 0f, .1f, .3f, 2000f, 9000f);
                Tone(data, 0f, .25f, 140f, 70f, .5f, Wave.Sine);
                break;
            case "kata":
                for (int i = 0; i < 5; i++) Noise(data, i * .03f, .018f, .2f, 2600f, 11000f);
                Tone(data, .16f, .32f, 2200f, 2190f, .12f, Wave.Sine);
                break;
            case "duel":
                Tone(data, 0f, .12f, 660f, 660f, .14f, Wave.Square);
                Tone(data, .12f, .3f, 990f, 990f, .12f, Wave.Square);
                Tone(data, 0f, .6f, 110f, 104f, .35f, Wave.Sine);
                break;
            case "duelwon":
                // A ricochet's whine falling away, then one low toll: the duel is over.
                Noise(data, 0f, .05f, .3f, 3000f, 12000f);
                Tone(data, 0f, .3f, 2600f, 900f, .08f, Wave.Sine);
                Tone(data, .16f, 1.1f, 82f, 80f, .55f, Wave.Sine);
                Tone(data, .16f, .9f, 164f, 162f, .2f, Wave.Sine);
                Tone(data, .16f, .7f, 493f, 490f, .06f, Wave.Triangle);
                break;
            default:
                return null;
        }
        float top = 0f;
        int last = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float a = Mathf.Abs(data[i]);
            if (a > top) top = a;
            if (a > 1e-4f) last = i;
        }
        if (top <= 0f) return null;
        int length = Mathf.Min(data.Length, last + Rate / 50);
        var trimmed = new float[length];
        int fade = Rate / 60;
        for (int i = 0; i < length; i++) trimmed[i] = data[i] / top * .9f * Mathf.Min(1f, (length - 1 - i) / (float)fade);
        var clip = AudioClip.Create("Moment " + key, length, 1, Rate, false);
        clip.SetData(trimmed, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    private enum Wave { Sine, Saw, Square, Triangle }

    // A note from f0 sliding to f1 over its length, struck at once and dying away.
    private static void Tone(float[] data, float start, float seconds, float f0, float f1, float gain, Wave wave)
    {
        int from = Mathf.Max(0, (int)(start * Rate)), count = (int)(seconds * Rate);
        double phase = 0;
        for (int i = 0; i < count && from + i < data.Length; i++)
        {
            float t = i / (float)Rate, k = t / seconds;
            float frequency = f0 * Mathf.Pow(f1 / f0, k);
            phase += frequency / Rate;
            float p = (float)(phase - System.Math.Floor(phase));
            float s = wave switch
            {
                Wave.Saw => p * 2f - 1f,
                Wave.Square => p < .5f ? .7f : -.7f,
                Wave.Triangle => 1f - 4f * Mathf.Abs(p - .5f),
                _ => Mathf.Sin(p * Mathf.PI * 2f),
            };
            float envelope = Mathf.Min(1f, t / .004f) * Mathf.Exp(-k * 4.5f);
            data[from + i] += s * gain * envelope;
        }
    }

    // A burst of noise kept between two frequencies (one-pole filters), dying away; a sweep rises through it.
    private static void Noise(float[] data, float start, float seconds, float gain, float low, float high, bool sweep = false)
    {
        int from = Mathf.Max(0, (int)(start * Rate)), count = (int)(seconds * Rate);
        float lowState = 0f, highState = 0f, previous = 0f;
        for (int i = 0; i < count && from + i < data.Length; i++)
        {
            float t = i / (float)Rate, k = t / seconds;
            float top = sweep ? Mathf.Lerp(low, high, k) : high;
            float raw = (float)(noise.NextDouble() * 2.0 - 1.0);
            // Low-pass below `top`, then high-pass above `low`.
            float aLow = 1f - Mathf.Exp(-2f * Mathf.PI * top / Rate);
            lowState += aLow * (raw - lowState);
            float aHigh = Mathf.Exp(-2f * Mathf.PI * (sweep ? low * .5f : low) / Rate);
            highState = aHigh * (highState + lowState - previous);
            previous = lowState;
            float envelope = Mathf.Min(1f, t / .002f) * Mathf.Exp(-k * 5f);
            data[from + i] += highState * gain * 2f * envelope;
        }
    }
}
