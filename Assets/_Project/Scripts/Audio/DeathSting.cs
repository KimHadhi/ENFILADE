using UnityEngine;

/// <summary>
/// The death screen's sting (DeathScreen), made here once a run, after the mood of the MW3 Death Screen addon the user
/// pointed to (GMod workshop 3833150063): its "whistle" is a low muffled roar that fades while a thin tone rings over it.
/// None of the addon's sound is used (it is ripped from Call of Duty); this is our own, in five parts:
/// - the fall: a thud whose pitch drops, and the crack of the blow;
/// - the world rushing away: noise through a low-pass that closes, as the muffled mix around it does;
/// - underneath, a low rumble that holds and then dies away;
/// - the ringing in the ears: a thin 2.35 kHz tone with a slow waver, a second one just out of tune beating against it;
/// - two last heartbeats, the second pair slower and fainter.
/// Mono, 2.9 s, on the UI channel so the world's muffling (DeathScreen's low-pass) leaves it clear. A clip in the UI
/// bank under "Death" (Art/Audio/UI/Death, Sounds.csv row UI/Death) plays instead when there is one, as KitSound's cues do.
/// Tools/Audio has no script for it: it is made at run time, like the menus' sounds.
/// </summary>
public static class DeathSting
{
    private const int Rate = 44100;
    private const float Length = 2.9f;
    private static AudioClip made;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => made = null;

    /// <summary>The sting, made the first time it is asked for (DeathScreen.Prepare asks behind the loading screen).</summary>
    public static AudioClip Clip
    {
        get
        {
            if (made != null) return made;
            float[] data = Make();
            made = AudioClip.Create("Death Sting", data.Length, 1, Rate, false);
            made.SetData(data, 0);
            made.hideFlags = HideFlags.HideAndDontSave;
            return made;
        }
    }

    private static float[] Make()
    {
        int count = (int)(Rate * Length);
        var data = new float[count];
        var random = new System.Random(5167);
        // The heartbeats: when (s) and how loud, a pair close together and then a slower, fainter pair.
        float[] beatAt = { .82f, 1.02f, 1.92f, 2.17f }, beatLevel = { .34f, .24f, .17f, .11f };
        var beatPhase = new double[beatAt.Length];
        double thudPhase = 0d, ringPhase = 0d, ringPhase2 = 0d;
        float crack = 0f, rush = 0f, brown = 0f, rumble = 0f, peak = 0f;
        float rumbleCut = Cut(90f);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate, white = (float)random.NextDouble() * 2f - 1f, s = 0f;
            // The fall.
            if (t < .6f)
            {
                thudPhase += 2d * Mathf.PI * (42f + 78f * Mathf.Exp(-t / .06f)) / Rate;
                s += (float)System.Math.Sin(thudPhase) * Mathf.Exp(-t / .14f) * .55f;
            }
            if (t < .06f)
            {
                crack += (white - crack) * .35f;
                s += crack * Mathf.Exp(-t / .012f) * .45f;
            }
            // The world rushing away.
            rush += (white - rush) * Cut(180f + 2400f * Mathf.Exp(-t / .28f));
            s += rush * Mathf.Min(1f, t / .025f) * Mathf.Exp(-t / .55f) * .3f;
            // The rumble under it.
            brown = brown * .996f + white * .04f;
            rumble += (brown - rumble) * rumbleCut;
            s += rumble * Ease.Smooth(t / .2f) * (t < 1.1f ? 1f : Mathf.Exp(-(t - 1.1f) / .6f)) * .32f;
            // The ringing.
            if (t > .16f)
            {
                float since = t - .16f;
                float level = Ease.Smooth(since / .55f) * Mathf.Exp(-Mathf.Max(0f, since - .7f) / .75f);
                double pitch = 2350d * (1d + .0025d * System.Math.Sin(2d * Mathf.PI * 4.6d * since));
                ringPhase += 2d * Mathf.PI * pitch / Rate;
                ringPhase2 += 2d * Mathf.PI * pitch * 1.0068d / Rate;
                s += (float)(System.Math.Sin(ringPhase) + .35d * System.Math.Sin(ringPhase2)) * level * .06f;
            }
            // The heartbeats.
            for (int b = 0; b < beatAt.Length; b++)
            {
                float since = t - beatAt[b];
                if (since < 0f || since >= .35f) continue;
                beatPhase[b] += 2d * Mathf.PI * (38f + 30f * Mathf.Exp(-since / .03f)) / Rate;
                float wave = (float)(System.Math.Sin(beatPhase[b]) + .35d * System.Math.Sin(2d * beatPhase[b]));
                s += wave * Mathf.Exp(-since / .075f) * beatLevel[b] * Ease.Smooth(since / .004f);
            }
            data[i] = s;
            peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        // Up to a clean peak, eased in over 2 ms and out over the last .3 s.
        float gain = .9f / Mathf.Max(.0001f, peak);
        int easeIn = (int)(Rate * .002f), easeOut = (int)(Rate * .3f);
        for (int i = 0; i < count; i++)
        {
            float g = gain;
            if (i < easeIn) g *= i / (float)easeIn;
            if (i > count - easeOut) g *= (count - i) / (float)easeOut;
            data[i] *= g;
        }
        return data;
    }

    // A one-pole low-pass's step for a cutoff (Hz).
    private static float Cut(float hertz) => 1f - Mathf.Exp(-2f * Mathf.PI * hertz / Rate);
}
