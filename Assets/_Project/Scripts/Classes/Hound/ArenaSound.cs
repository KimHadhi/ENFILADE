using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// A Schießhund's showdown's sounds (ArenaFx), made here as the moments' stingers are (MomentSound): the pull, a rising
/// rush of air into a low thump; the coming back, a thump and air let out; and the crowd's roar for a hound that comes
/// back (the user: "복귀할 땐 환호성과 함께"), a crowd's body of noise swelling and dying away with cries rising and falling
/// through it, scattered claps and two whistles. The roar is made on another thread the moment a showdown begins
/// (Prewarm), so nothing is made in the frame it is wanted. A file at Resources/Audio/Moments/showdown, showdown_back or
/// cheer (a mod's, or a recorded crowd) plays in place of the made one. One's own plays at the ear; a friend's where it
/// happens, softer with distance. Numbers: Tables/ClassAbilities Schiesshund.arenaCheer (the roar's loudness).
/// </summary>
internal static class ArenaSound
{
    private const int Rate = 22050;
    private static AudioClip pull, back, cheer;
    private static Task<float[]> cheering;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pull = back = cheer = null;
        cheering = null;
    }

    public static void Pull(Vector3 at, bool own) => Play(PullClip, at, own, .9f);

    public static void Back(Vector3 at, bool own, float volume) => Play(BackClip, at, own, volume);

    public static void Cheer(Vector3 at, bool own) => Play(CheerClip(), at, own, Mathf.Clamp01(HoundArena.Number("arenaCheer", .8f)));

    /// <summary>Starts making the roar, on another thread, unless a recorded one is there or it is made already.</summary>
    public static void Prewarm()
    {
        if (cheer != null || cheering != null) return;
        cheer = Resources.Load<AudioClip>("Audio/Moments/cheer");
        if (cheer == null) cheering = Task.Run(() => CheerData(5310));
    }

    private static void Play(AudioClip clip, Vector3 at, bool own, float volume)
    {
        if (clip == null || !Application.isPlaying) return;
        if (own)
        {
            var listener = GameAudio.Listener;
            Vector3 ear = listener != null ? listener.transform.position : Camera.main != null ? Camera.main.transform.position : at;
            GameAudio.PlayAt(clip, ear, AudioChannel.Combat, volume, 50f, 60f);
        }
        else GameAudio.PlayAt(clip, at, AudioChannel.Combat, volume * .85f, 5f, 70f);
    }

    private static AudioClip PullClip
    {
        get
        {
            if (pull != null) return pull;
            pull = Resources.Load<AudioClip>("Audio/Moments/showdown");
            if (pull != null) return pull;
            var data = new float[(int)(Rate * .8f)];
            var random = new System.Random(2903);
            Rush(data, random, 0f, .36f, .5f, 350f, 4200f);
            Thump(data, .34f, .32f, 115f, 42f, .9f);
            return pull = Make("Showdown Pull", data);
        }
    }

    private static AudioClip BackClip
    {
        get
        {
            if (back != null) return back;
            back = Resources.Load<AudioClip>("Audio/Moments/showdown_back");
            if (back != null) return back;
            var data = new float[(int)(Rate * .6f)];
            var random = new System.Random(4411);
            Thump(data, 0f, .3f, 95f, 40f, .8f);
            Air(data, random, 0f, .3f, .32f, 900f, 5200f);
            return back = Make("Showdown Back", data);
        }
    }

    private static AudioClip CheerClip()
    {
        if (cheer != null) return cheer;
        Prewarm();
        if (cheer != null || cheering == null) return cheer;
        float[] data = null;
        // Begun when the showdown did, so long done by its end; a moment's wait at most if it was quicker than that.
        try { data = cheering.Result; }
        catch (System.Exception) { data = null; }
        cheering = null;
        return data != null ? cheer = Make("Showdown Cheer", data) : null;
    }

    // ---- Making them (plain arithmetic only: the roar is made off the main thread) ----

    private static float[] CheerData(int seed)
    {
        var random = new System.Random(seed);
        const float Seconds = 2.8f;
        int count = (int)(Seconds * Rate);
        var data = new float[count];
        // The crowd's body: noise in three bands, swelling in, held, dying away, its loudness wavering as voices come and go.
        var roar = new float[count];
        Band(roar, random, 280f, 900f, .6f);
        Band(roar, random, 800f, 2100f, .38f);
        Band(roar, random, 2100f, 4500f, .14f);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float swell = t < .14f ? t / .14f * .75f : t < .5f ? Mathf.Lerp(.75f, 1f, (t - .14f) / .36f)
                : t < 1.4f ? 1f : Mathf.Max(0f, 1f - (t - 1.4f) / (Seconds - 1.4f));
            float waver = 1f + .12f * Mathf.Sin(t * 19.5f) + .08f * Mathf.Sin(t * 33.3f + 1f) + .06f * Mathf.Sin(t * 48.4f + 2f);
            data[i] = roar[i] * swell * swell * waver;
        }
        // Cries rising and falling through it.
        for (int v = 0; v < 12; v++)
        {
            float start = Next(random) * .5f, length = .6f + Next(random) * .9f, f0 = 210f + Next(random) * 330f;
            float rise = .15f + Next(random) * .3f, wobble = Next(random) * 6.28f;
            int from = (int)(start * Rate), n = (int)(length * Rate);
            double phase = 0;
            for (int i = 0; i < n && from + i < count; i++)
            {
                float t = i / (float)Rate, k = t / length;
                float f = f0 * (1f + rise * Mathf.Sin(k * Mathf.PI)) * (1f + .025f * Mathf.Sin(t * 37.7f + wobble));
                phase += f / Rate;
                float p = (float)(phase - System.Math.Floor(phase)) * Mathf.PI * 2f;
                float s = Mathf.Sin(p) + .5f * Mathf.Sin(2f * p) + .25f * Mathf.Sin(3f * p);
                data[from + i] += s * .045f * Mathf.Min(1f, t / .06f) * Mathf.Pow(1f - k, 1.5f);
            }
        }
        // Claps scattered through it: short dry cracks.
        for (int c = 0; c < 80; c++)
        {
            float at = .2f + Next(random) * 2.1f, gain = .1f + Next(random) * .2f;
            int from = (int)(at * Rate), n = (int)(.012f * Rate);
            float previous = 0f, high = 0f;
            for (int i = 0; i < n && from + i < count; i++)
            {
                float raw = Next(random) * 2f - 1f;
                high = .6f * (high + raw - previous);
                previous = raw;
                data[from + i] += high * gain * Mathf.Exp(-i / (Rate * .0025f));
            }
        }
        // Two whistles over it.
        for (int w = 0; w < 2; w++)
        {
            float start = .35f + w * .5f + Next(random) * .2f, length = .45f;
            int from = (int)(start * Rate), n = (int)(length * Rate);
            double phase = 0;
            for (int i = 0; i < n && from + i < count; i++)
            {
                float t = i / (float)Rate, k = t / length;
                float f = (2300f + w * 250f) * (1f + .25f * Mathf.Sin(k * Mathf.PI)) * (1f + .01f * Mathf.Sin(t * 40f));
                phase += f / Rate;
                data[from + i] += Mathf.Sin((float)(phase - System.Math.Floor(phase)) * Mathf.PI * 2f) * .05f
                    * Mathf.Min(1f, t / .03f) * Mathf.Min(1f, (1f - k) / .2f);
            }
        }
        return data;
    }

    private static float Next(System.Random random) => (float)random.NextDouble();

    // Noise kept between two frequencies (one-pole filters).
    private static void Band(float[] data, System.Random random, float low, float high, float gain)
    {
        float aLow = 1f - Mathf.Exp(-2f * Mathf.PI * high / Rate), aHigh = Mathf.Exp(-2f * Mathf.PI * low / Rate);
        float lowState = 0f, highState = 0f, previous = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float raw = Next(random) * 2f - 1f;
            lowState += aLow * (raw - lowState);
            highState = aHigh * (highState + lowState - previous);
            previous = lowState;
            data[i] += highState * gain * 2f;
        }
    }

    // Noise whose top rises through it, swelling as it goes: air rushing in.
    private static void Rush(float[] data, System.Random random, float start, float seconds, float gain, float low, float high)
    {
        int from = (int)(start * Rate), count = (int)(seconds * Rate);
        float aHigh = Mathf.Exp(-2f * Mathf.PI * low / Rate), lowState = 0f, highState = 0f, previous = 0f;
        for (int i = 0; i < count && from + i < data.Length; i++)
        {
            float k = i / (float)count, aLow = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Lerp(low * 2f, high, k * k) / Rate);
            float raw = Next(random) * 2f - 1f;
            lowState += aLow * (raw - lowState);
            highState = aHigh * (highState + lowState - previous);
            previous = lowState;
            data[from + i] += highState * gain * 2f * k * k * Mathf.Min(1f, (count - i) / (Rate * .01f));
        }
    }

    // Noise kept between two frequencies, dying away: air let out.
    private static void Air(float[] data, System.Random random, float start, float seconds, float gain, float low, float high)
    {
        int from = (int)(start * Rate), count = (int)(seconds * Rate);
        float aLow = 1f - Mathf.Exp(-2f * Mathf.PI * high / Rate), aHigh = Mathf.Exp(-2f * Mathf.PI * low / Rate);
        float lowState = 0f, highState = 0f, previous = 0f;
        for (int i = 0; i < count && from + i < data.Length; i++)
        {
            float k = i / (float)count, raw = Next(random) * 2f - 1f;
            lowState += aLow * (raw - lowState);
            highState = aHigh * (highState + lowState - previous);
            previous = lowState;
            data[from + i] += highState * gain * 2f * Mathf.Min(1f, i / (Rate * .004f)) * Mathf.Exp(-k * 5f);
        }
    }

    // A low note sliding down, struck at once and dying away: a thump.
    private static void Thump(float[] data, float start, float seconds, float f0, float f1, float gain)
    {
        int from = (int)(start * Rate), count = (int)(seconds * Rate);
        double phase = 0;
        for (int i = 0; i < count && from + i < data.Length; i++)
        {
            float t = i / (float)Rate, k = t / seconds;
            phase += f0 * Mathf.Pow(f1 / f0, k) / Rate;
            data[from + i] += Mathf.Sin((float)(phase - System.Math.Floor(phase)) * Mathf.PI * 2f) * gain * Mathf.Min(1f, t / .003f) * Mathf.Exp(-k * 4.5f);
        }
    }

    // Loud enough and no louder, the silence after it cut, its tail faded so it never clicks.
    private static AudioClip Make(string name, float[] data)
    {
        float top = 0f;
        int last = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float a = Mathf.Abs(data[i]);
            if (a > top) top = a;
            if (a > 1e-4f) last = i;
        }
        if (top <= 0f) return null;
        int length = Mathf.Min(data.Length, last + Rate / 50), fade = Rate / 60;
        var trimmed = new float[length];
        for (int i = 0; i < length; i++) trimmed[i] = data[i] / top * .88f * Mathf.Min(1f, (length - 1 - i) / (float)fade);
        var clip = AudioClip.Create(name, length, 1, Rate, false);
        clip.SetData(trimmed, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
