using UnityEngine;

/// <summary>
/// The sounds of a zombie calling the others (ZombieMind's call; the user, 2026-11-12: "한번 들켰다고 바로 모든 놈들이
/// 나한테 오지 말고 들켜도 들킨 놈들 빠르게 싹 다 처치하면 조용히 넘어갈 수 있었으면 좋겠어"): its tell as it draws breath,
/// a breath in through the throat rising into a groan over callTell, and then the roar that wakes whoever hears it.
/// Made here once, so they need no files; every PC plays them from the zombie's mouth (the host's and each copy's).
/// </summary>
public static class ZombieCall
{
    private const int SampleRate = 44100;
    // The clips' own lengths (s); the tell is played faster or slower to fill callTell.
    private const float TellLength = 1.2f, RoarLength = 1.5f;
    private static AudioClip tell, roar;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() { tell = null; roar = null; }

    /// <summary>The tell, filling <paramref name="seconds"/>: a breath drawn in that rises into a groan.</summary>
    public static void Tell(Vector3 mouth, float seconds)
        => FeelAudio.PlayClipAt(TellClip(), mouth, .9f, Mathf.Clamp(TellLength / Mathf.Max(.3f, seconds), .7f, 1.6f), 40f);

    /// <summary>The call itself: a hoarse roar that carries.</summary>
    public static void Roar(Vector3 mouth) => FeelAudio.PlayClipAt(RoarClip(), mouth, 1f, Random.Range(.95f, 1.08f), 70f);

    // Breath through the throat, louder and higher as it fills the chest, turning into a groan that climbs to the roar.
    private static AudioClip TellClip()
    {
        if (tell != null) return tell;
        int total = Mathf.RoundToInt(TellLength * SampleRate);
        var samples = new float[total];
        var random = new System.Random(11);
        float low = 0f, phase = 0f, rasp = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)SampleRate, share = t / TellLength;
            // The breath: noise through a filter that opens as the chest fills.
            low = Mathf.Lerp(low, (float)random.NextDouble() * 2f - 1f, Mathf.Lerp(.05f, .3f, share));
            float breath = low * Mathf.SmoothStep(0f, 1f, share / .35f) * (1f - .6f * Mathf.SmoothStep(.55f, 1f, share));
            // The groan under it from halfway: a throat's buzz climbing in pitch, rough with a growl.
            float pitch = Mathf.Lerp(70f, 150f, Mathf.SmoothStep(.4f, 1f, share));
            phase = Mathf.Repeat(phase + pitch / SampleRate, 1f);
            rasp = Mathf.Lerp(rasp, (float)random.NextDouble() * 2f - 1f, .2f);
            float groan = ((2f * phase - 1f) * .5f + rasp * (.5f + .5f * Mathf.Sin(2f * Mathf.PI * 27f * t)) * .5f)
                * Mathf.SmoothStep(.4f, 1f, share);
            float level = Mathf.Min(1f, (TellLength - t) / .04f);
            samples[i] = Mathf.Clamp((breath * .9f + groan * .7f) * level * .8f, -1f, 1f);
        }
        tell = AudioClip.Create("Zombie Call Tell", total, 1, SampleRate, false);
        tell.SetData(samples, 0);
        return tell;
    }

    // A roar: a buzz that starts high and falls, with a wavering throat and a rough edge, loud from its first moment.
    private static AudioClip RoarClip()
    {
        if (roar != null) return roar;
        const float Attack = .06f, Release = .6f;
        int total = Mathf.RoundToInt(RoarLength * SampleRate);
        var samples = new float[total];
        var random = new System.Random(5);
        float phase = 0f, noise = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)SampleRate;
            float pitch = Mathf.Lerp(185f, 105f, t / RoarLength) * (1f + .05f * Mathf.Sin(2f * Mathf.PI * 7f * t));
            phase = Mathf.Repeat(phase + pitch / SampleRate, 1f);
            noise = Mathf.Lerp(noise, (float)random.NextDouble() * 2f - 1f, .25f);
            float rough = noise * (.55f + .45f * Mathf.Sin(2f * Mathf.PI * 34f * t));
            float level = Mathf.Min(1f, t / Attack, (RoarLength - t) / Release);
            samples[i] = Mathf.Clamp(((2f * phase - 1f) * .5f + rough * .75f) * level * .85f, -1f, 1f);
        }
        roar = AudioClip.Create("Zombie Call Roar", total, 1, SampleRate, false);
        roar.SetData(samples, 0);
        return roar;
    }
}
