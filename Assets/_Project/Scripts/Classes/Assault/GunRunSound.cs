using UnityEngine;

/// <summary>
/// An A-10's gun run's sounds (GunRunFx), made here as the moments' stingers are (MomentSound, ArenaSound), each once:
/// - Hit: a 30 mm round going off, a sharp crack over a low thump and a rattle of dirt;
/// - Brrrt: the gun itself heard from afar, some seventy rounds a second run together into one low tearing note that
///   spins up, holds and stops dead, rolling back off the ground after (the sound comes after the hits, as a real one's
///   does: the rounds outrun it);
/// - Jet: the plane going over, its roar swelling and dying away and its engines' whine falling as it passes.
/// A file at Resources/Audio/Moments/gunrun_hit, gunrun_brrrt or gunrun_jet (a mod's, a recorded one) plays in place of
/// the made one.
/// </summary>
internal static class GunRunSound
{
    private const int Rate = 22050;
    private static AudioClip hit, brrrt, jet;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => hit = brrrt = jet = null;

    public static AudioClip Hit
    {
        get
        {
            if (hit == null) hit = Load("gunrun_hit");
            if (hit == null) hit = Make("Gun Run Hit", HitData());
            return hit;
        }
    }

    public static AudioClip Brrrt
    {
        get
        {
            if (brrrt == null) brrrt = Load("gunrun_brrrt");
            if (brrrt == null) brrrt = Make("Gun Run BRRRT", BrrrtData());
            return brrrt;
        }
    }

    public static AudioClip Jet
    {
        get
        {
            if (jet == null) jet = Load("gunrun_jet");
            if (jet == null) jet = Make("Gun Run Jet", JetData());
            return jet;
        }
    }

    /// <summary>Makes all three now (a run was called: they are wanted within seconds, so not in the frame they play).</summary>
    public static void Prewarm()
    {
        _ = Hit;
        _ = Brrrt;
        _ = Jet;
    }

    private static AudioClip Load(string key) => Resources.Load<AudioClip>("Audio/Moments/" + key);

    private static AudioClip Make(string name, float[] data)
    {
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        int fade = Rate / 40;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] / Mathf.Max(.001f, top) * .9f * Mathf.Min(1f, (data.Length - 1 - i) / (float)fade);
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    // A crack, a thump falling in pitch, and dirt pattering down.
    private static float[] HitData()
    {
        var data = new float[(int)(Rate * .55f)];
        var random = new System.Random(3011);
        float turn = 0f, fine = 0f, coarse = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate, noise = (float)random.NextDouble() * 2f - 1f;
            float crack = t < .005f ? noise * (1f - t / .005f) : 0f;
            turn += 2f * Mathf.PI * Mathf.Lerp(88f, 36f, Mathf.Clamp01(t / .22f)) / Rate;
            float thump = Mathf.Sin(turn) * Mathf.Exp(-t / .11f);
            fine += (noise - fine) * .35f;
            coarse += (noise - coarse) * .05f;
            float dirt = (fine - coarse) * Mathf.Exp(-t / .16f);
            data[i] = crack * .9f + thump * .95f + dirt * .55f;
        }
        return data;
    }

    // The gun: rounds at some seventy a second (spinning up over the first moment), each a burst of noise over a short
    // low knock, run together, dulled by the distance, with the ground's echo rolling after.
    private static float[] BrrrtData()
    {
        const float Burst = 1.15f;
        var dry = new float[(int)(Rate * 1.9f)];
        var random = new System.Random(6511);
        int shotLength = (int)(Rate * .03f);
        float at = 0f;
        while (at < Burst)
        {
            float level = Mathf.Lerp(.55f, 1f, Mathf.Clamp01(at / .08f)) * (.9f + .2f * (float)random.NextDouble());
            int start = (int)(at * Rate);
            for (int k = 0; k < shotLength && start + k < dry.Length; k++)
            {
                float noise = (float)random.NextDouble() * 2f - 1f;
                float knock = Mathf.Sin(2f * Mathf.PI * 105f * k / Rate) * Mathf.Exp(-k / (Rate * .012f));
                dry[start + k] += (noise * Mathf.Exp(-k / (Rate * .004f)) * .55f + knock * .8f) * level;
            }
            // Spinning up: from forty rounds a second to the gun's full rate within the first eighth of a second.
            at += 1f / Mathf.Lerp(40f, 68f, Mathf.Clamp01(at / .12f));
        }
        // A low bed under the burst, the air shaking with it.
        float bed = 0f;
        for (int i = 0; i < dry.Length; i++)
        {
            float t = i / (float)Rate;
            bed += (((float)random.NextDouble() * 2f - 1f) - bed) * .02f;
            float hold = t < Burst ? Mathf.Clamp01(t / .05f) : Mathf.Exp(-(t - Burst) / .12f);
            dry[i] += bed * 3.2f * hold;
        }
        // Far off and heavy: the highs dulled. The echo off the ground rolls after, duller still.
        var data = new float[dry.Length];
        float low = 0f, echoLow = 0f;
        int first = (int)(Rate * .19f), second = (int)(Rate * .43f);
        for (int i = 0; i < data.Length; i++)
        {
            low += (dry[i] - low) * .43f;
            float echo = (i >= first ? dry[i - first] * .38f : 0f) + (i >= second ? dry[i - second] * .2f : 0f);
            echoLow += (echo - echoLow) * .12f;
            data[i] = low + echoLow;
        }
        return data;
    }

    // The plane going over: a roar of noise that swells and opens up as it comes and closes as it goes, a whine whose
    // pitch falls as it passes (some 1.3 s in), and a low hum under them.
    private static float[] JetData()
    {
        const float Seconds = 3.2f, Pass = 1.3f;
        var data = new float[(int)(Rate * Seconds)];
        var random = new System.Random(8123);
        float roar = 0f, turn = 0f, hum = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate, d = t - Pass;
            float close = 1f / (1f + d / .55f * (d / .55f));
            float edge = Mathf.Clamp01(t / .4f) * Mathf.Clamp01((Seconds - t) / .6f);
            float noise = (float)random.NextDouble() * 2f - 1f;
            roar += (noise - roar) * Mathf.Lerp(.05f, .35f, close);
            float pitch = 1900f * (1f - .18f * Tanh(d / .35f));
            turn += 2f * Mathf.PI * pitch / Rate;
            hum += 2f * Mathf.PI * 55f / Rate;
            float whine = (Mathf.Sin(turn) * .12f + Mathf.Sin(turn * 2f) * .05f) * Mathf.Pow(close, 1.5f);
            data[i] = (roar * 2.4f * (.12f + .88f * close) + whine + Mathf.Sin(hum) * .25f * close) * edge;
        }
        return data;
    }

    private static float Tanh(float x)
    {
        float e = Mathf.Exp(2f * Mathf.Clamp(x, -10f, 10f));
        return (e - 1f) / (e + 1f);
    }
}
