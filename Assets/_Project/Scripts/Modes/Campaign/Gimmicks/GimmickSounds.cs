using System;
using UnityEngine;

/// <summary>
/// The map gimmicks' sounds, made here as the campaign shutter's siren is (CampaignGate): the project has no recording of a
/// TV's hiss, a breaker, a lift's chime, a door smashed in, gas or a rope snapping, and a few seconds of shaped noise and
/// tones do. Each is made once on first use. Played in the world (GameAudio.PlayAt, or a looping source a switch keeps).
/// </summary>
public static class GimmickSounds
{
    // New cues go at the end: a lure's cue is saved in the map by its number.
    public enum Cue { Static, Clunk, Ding, Smash, Hiss, Unlock, Jingle, Snap, Crash, Whistle, CarAlarm, Fire, Rumble, Beep, Chime, FireBell, Lever, Shutter, Generator }

    private const int Rate = 44100;
    private static readonly AudioClip[] made = new AudioClip[Enum.GetValues(typeof(Cue)).Length];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => Array.Clear(made, 0, made.Length);

    /// <summary>The clip for a cue, made on first use.</summary>
    public static AudioClip Get(Cue cue)
    {
        int i = (int)cue;
        if (made[i] != null) return made[i];
        float[] samples = cue switch
        {
            Cue.Static => Static(),
            Cue.Clunk => Clunk(),
            Cue.Ding => Ding(),
            Cue.Smash => Smash(),
            Cue.Hiss => Hiss(),
            Cue.Unlock => Unlock(),
            Cue.Jingle => Jingle(),
            Cue.Snap => Snap(),
            Cue.Crash => Crash(),
            Cue.CarAlarm => CarAlarm(),
            Cue.Fire => Fire(),
            Cue.Rumble => Rumble(),
            Cue.Beep => Beep(),
            Cue.Chime => Chime(),
            Cue.FireBell => FireBell(),
            Cue.Lever => Lever(),
            Cue.Shutter => Shutter(),
            Cue.Generator => Generator(),
            _ => Whistle()
        };
        var clip = AudioClip.Create("Gimmick " + cue, samples.Length, 1, Rate, false);
        clip.SetData(samples, 0);
        return made[i] = clip;
    }

    /// <summary>Once at a place in the world, heard from near to far (m).</summary>
    public static void PlayAt(Cue cue, Vector3 at, float volume = 1f, float near = 3f, float far = 45f)
        => GameAudio.PlayAt(Get(cue), at, AudioChannel.Combat, volume, near, far);

    /// <summary>A looping source on <paramref name="host"/>, quiet until played (a TV's hiss, gas).</summary>
    public static AudioSource Loop(GameObject host, Cue cue, float near = 2f, float far = 30f)
    {
        var source = host.AddComponent<AudioSource>();
        source.clip = Get(cue);
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = near;
        source.maxDistance = far;
        GameAudio.Bind(source, AudioChannel.Combat);
        return source;
    }

    // ---- The sounds ----

    // A dead channel's hiss under a mains hum, crackling now and then: loops (its ends meet).
    private static float[] Static()
    {
        var random = new System.Random(11);
        int count = Rate * 2;
        var s = new float[count];
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .35f;
            float hum = Mathf.Sin(2f * Mathf.PI * 60f * t) * .06f + Mathf.Sin(2f * Mathf.PI * 120f * t) * .03f;
            float crackle = random.NextDouble() < .0015 ? (float)(random.NextDouble() * 2.0 - 1.0) * .6f : 0f;
            s[i] = (low * .38f + white * .1f + hum + crackle) * .8f;
        }
        return s;
    }

    // A breaker thrown: a hard click over a low thump, then the building's hum coming on.
    private static float[] Clunk()
    {
        var random = new System.Random(3);
        int count = (int)(Rate * .9f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float click = t < .012f ? (float)(random.NextDouble() * 2.0 - 1.0) * (1f - t / .012f) : 0f;
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 18f) * .9f;
            float hum = (Mathf.Sin(2f * Mathf.PI * 120f * t) * .12f + Mathf.Sin(2f * Mathf.PI * 240f * t) * .05f)
                * Mathf.Clamp01((t - .1f) / .3f) * Mathf.Clamp01((.9f - t) / .25f);
            s[i] = (click * .8f + thump + hum) * .8f;
        }
        return s;
    }

    // A lift's chime: two bell tones, high then lower, each ringing out.
    private static float[] Ding()
    {
        int count = (int)(Rate * 1.6f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            s[i] = Bell(t, 1318.5f) + Bell(t - .32f, 1046.5f);
        }
        return s;
    }

    private static float Bell(float t, float pitch)
    {
        if (t < 0f) return 0f;
        float ring = Mathf.Exp(-t * 3.2f), attack = Mathf.Clamp01(t / .004f);
        return (Mathf.Sin(2f * Mathf.PI * pitch * t) + .35f * Mathf.Sin(2f * Mathf.PI * pitch * 2.76f * t) * Mathf.Exp(-t * 6f)) * ring * attack * .42f;
    }

    // A door smashed in: a crack of noise on a deep boom, wood and metal rattling after.
    private static float[] Smash()
    {
        var random = new System.Random(7);
        int count = (int)(Rate * 1.1f);
        var s = new float[count];
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .12f;
            float crack = white * Mathf.Exp(-t * 26f);
            float boom = Mathf.Sin(2f * Mathf.PI * (55f + 40f * Mathf.Exp(-t * 20f)) * t) * Mathf.Exp(-t * 7f);
            float rattle = low * Mathf.Exp(-t * 4f) * (random.NextDouble() < .05 ? 2.5f : .6f);
            s[i] = Mathf.Clamp(crack * .7f + boom * .9f + rattle * .5f, -1f, 1f) * .9f;
        }
        return s;
    }

    // Gas let out of a can: a steady hiss, darker than the TV's: loops.
    private static float[] Hiss()
    {
        var random = new System.Random(5);
        int count = (int)(Rate * 1.5f);
        var s = new float[count];
        float band = 0f, lower = 0f;
        for (int i = 0; i < count; i++)
        {
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            band += (white - band) * .55f;
            lower += (band - lower) * .2f;
            s[i] = (band - lower) * .55f;
        }
        return s;
    }

    // A key turned: two small clicks and a bolt sliding back.
    private static float[] Unlock()
    {
        var random = new System.Random(9);
        int count = (int)(Rate * .45f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float click = Tick(t, .0f, random) + Tick(t, .09f, random) * .8f;
            float slide = t > .2f && t < .36f ? (float)(random.NextDouble() * 2.0 - 1.0) * .18f * Mathf.Sin((t - .2f) / .16f * Mathf.PI) : 0f;
            float clack = Mathf.Sin(2f * Mathf.PI * 900f * t) * Mathf.Exp(-(t - .36f) * 60f) * (t > .36f ? .35f : 0f);
            s[i] = click + slide + clack;
        }
        return s;
    }

    private static float Tick(float t, float at, System.Random random)
    {
        float since = t - at;
        if (since < 0f || since > .02f) return 0f;
        return ((float)(random.NextDouble() * 2.0 - 1.0) * .4f + Mathf.Sin(2f * Mathf.PI * 2400f * since) * .5f) * (1f - since / .02f);
    }

    // Keys on a ring picked up: a few small metal chinks.
    private static float[] Jingle()
    {
        int count = (int)(Rate * .5f);
        var s = new float[count];
        float[] at = { 0f, .07f, .13f, .22f };
        float[] pitch = { 3520f, 4186f, 3136f, 3951f };
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate, sum = 0f;
            for (int k = 0; k < at.Length; k++)
            {
                float since = t - at[k];
                if (since < 0f) continue;
                sum += (Mathf.Sin(2f * Mathf.PI * pitch[k] * since) + .4f * Mathf.Sin(2f * Mathf.PI * pitch[k] * 1.51f * since))
                    * Mathf.Exp(-since * 28f) * .22f;
            }
            s[i] = sum;
        }
        return s;
    }

    // A rope or cable parting: a sharp twang falling in pitch.
    private static float[] Snap()
    {
        var random = new System.Random(13);
        int count = (int)(Rate * .35f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float twang = Mathf.Sin(2f * Mathf.PI * (420f * Mathf.Exp(-t * 9f) + 90f) * t) * Mathf.Exp(-t * 12f);
            float crack = (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 60f);
            s[i] = (twang * .7f + crack * .6f) * .85f;
        }
        return s;
    }

    // A container crashing down: a huge low impact and metal ringing out.
    private static float[] Crash()
    {
        var random = new System.Random(17);
        int count = (int)(Rate * 1.8f);
        var s = new float[count];
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .08f;
            float boom = Mathf.Sin(2f * Mathf.PI * (38f + 60f * Mathf.Exp(-t * 14f)) * t) * Mathf.Exp(-t * 3.5f);
            float ring = (Mathf.Sin(2f * Mathf.PI * 173f * t) + .6f * Mathf.Sin(2f * Mathf.PI * 311f * t) + .3f * Mathf.Sin(2f * Mathf.PI * 529f * t))
                * Mathf.Exp(-t * 2.2f) * .25f;
            s[i] = Mathf.Clamp(boom + ring + low * Mathf.Exp(-t * 5f) * 1.6f + white * Mathf.Exp(-t * 30f) * .5f, -1f, 1f) * .9f;
        }
        return s;
    }

    // A car alarm: a whoop rising, then a fast warble, then two honks: loops (1.8 s).
    private static float[] CarAlarm()
    {
        int count = (int)(Rate * 1.8f);
        var s = new float[count];
        float phase = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate, pitch;
            if (t < .7f) pitch = Mathf.Lerp(650f, 1350f, t / .7f);
            else if (t < 1.3f) pitch = 1000f + 260f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 14f * t));
            else pitch = 520f;
            phase += 2f * Mathf.PI * pitch / Rate;
            float wave = Mathf.Sin(phase) + .35f * Mathf.Sin(3f * phase) + .18f * Mathf.Sin(5f * phase);
            float gate = t < 1.3f ? 1f : (t < 1.48f || (t > 1.56f && t < 1.74f) ? 1f : 0f);
            float edge = Mathf.Min(1f, t / .01f, (1.8f - t) / .01f);
            s[i] = wave * gate * edge * .26f;
        }
        return s;
    }

    // Fire: a low roar under crackles: loops (2 s).
    private static float[] Fire()
    {
        var random = new System.Random(21);
        int count = Rate * 2;
        var s = new float[count];
        float low = 0f, lower = 0f;
        for (int i = 0; i < count; i++)
        {
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .05f;
            lower += (low - lower) * .1f;
            float crackle = random.NextDouble() < .004 ? (float)(random.NextDouble() * 2.0 - 1.0) * .8f : 0f;
            s[i] = (lower * 3.2f + low * .9f + crackle) * .7f;
        }
        return s;
    }

    // Concrete giving way: a long deep rumble with cracks through it.
    private static float[] Rumble()
    {
        var random = new System.Random(23);
        int count = (int)(Rate * 2.4f);
        var s = new float[count];
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .03f;
            float swell = Mathf.Clamp01(t / .2f) * Mathf.Clamp01((2.4f - t) / .9f);
            float crack = random.NextDouble() < .002 ? white * 1.4f : 0f;
            s[i] = Mathf.Clamp(low * 5f * swell + crack * swell + Mathf.Sin(2f * Mathf.PI * 32f * t) * .25f * swell, -1f, 1f) * .9f;
        }
        return s;
    }

    // A metal detector's alarm: a steady electronic tone, two long beeps.
    private static float[] Beep()
    {
        int count = (int)(Rate * 1.4f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float on = (t < .55f || (t > .75f && t < 1.3f)) ? 1f : 0f;
            float edge = Mathf.Min(1f, (t % .75f) / .008f);
            s[i] = (Mathf.Sin(2f * Mathf.PI * 1760f * t) * .7f + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 1760f * t)) * .15f) * on * edge * .3f;
        }
        return s;
    }

    // A public address's chime: three soft tones falling, as before an announcement.
    private static float[] Chime()
    {
        int count = (int)(Rate * 2.2f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            s[i] = Bell(t, 1046.5f) * .8f + Bell(t - .45f, 880f) * .8f + Bell(t - .9f, 698.5f) * .8f;
        }
        return s;
    }

    // A fire alarm's bell: a hammer striking a gong some eighteen times a second, the ring never dying away between
    // strikes: loops (1 s, a whole number of strikes).
    private static float[] FireBell()
    {
        const float Strikes = 18f;
        int count = Rate;
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate, since = t % (1f / Strikes);
            float ring = Mathf.Sin(2f * Mathf.PI * 880f * t) + .55f * Mathf.Sin(2f * Mathf.PI * 2370f * t) + .3f * Mathf.Sin(2f * Mathf.PI * 3510f * t);
            float strike = .55f + .45f * Mathf.Exp(-since * 70f);
            s[i] = ring * strike * .2f;
        }
        return s;
    }

    // A heavy lever pulled down: a ratchet's few clicks and a latch dropping home.
    private static float[] Lever()
    {
        var random = new System.Random(29);
        int count = (int)(Rate * .7f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate, sum = 0f;
            for (int k = 0; k < 4; k++) sum += Tick(t, .06f * k, random) * .7f;
            float home = t > .38f ? (Mathf.Sin(2f * Mathf.PI * 140f * (t - .38f)) * Mathf.Exp(-(t - .38f) * 22f)
                + (float)(random.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-(t - .38f) * 50f) * .5f) : 0f;
            s[i] = Mathf.Clamp(sum + home * .9f, -1f, 1f) * .85f;
        }
        return s;
    }

    // A steel shutter rolling up: a rattling grind rising, ending in a knock.
    private static float[] Shutter()
    {
        var random = new System.Random(31);
        int count = (int)(Rate * 1.8f);
        var s = new float[count];
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .1f;
            float slats = .6f + .4f * Mathf.Sin(2f * Mathf.PI * (22f + 8f * t) * t);
            float roll = low * 2.2f * slats * Mathf.Clamp01(t / .15f) * Mathf.Clamp01((1.5f - t) / .2f);
            float knock = t > 1.5f ? Mathf.Sin(2f * Mathf.PI * 90f * (t - 1.5f)) * Mathf.Exp(-(t - 1.5f) * 16f) : 0f;
            s[i] = Mathf.Clamp(roll + knock * .9f, -1f, 1f) * .75f;
        }
        return s;
    }

    // A portable generator running: a small engine's firing, a putter 25 times a second, over its drone and a breath
    // of exhaust. Low throughout (nothing above 150 Hz but the noise, kept dark): loops (2 s, a whole number of firings
    // and of every tone's turns, so its ends meet).
    private static float[] Generator()
    {
        var random = new System.Random(29);
        int count = Rate * 2;
        var s = new float[count];
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            low += (white - low) * .05f;
            float beat = t * 25f % 1f;
            float putter = Mathf.Exp(-beat * 9f) * Mathf.Sin(2f * Mathf.PI * 75f * t);
            float drone = Mathf.Sin(2f * Mathf.PI * 50f * t) * .35f + Mathf.Sin(2f * Mathf.PI * 100f * t) * .2f
                + Mathf.Sin(2f * Mathf.PI * 150f * t) * .08f;
            s[i] = Mathf.Clamp(putter * .45f + drone * .5f + low * 1.2f, -1f, 1f) * .7f;
        }
        return s;
    }

    // A guard's whistle: a shrill trilled tone, blown twice.
    private static float[] Whistle()
    {
        int count = (int)(Rate * 1.3f);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float blow = t < .5f ? Mathf.Clamp01(t / .03f) * Mathf.Clamp01((.5f - t) / .05f)
                : t > .62f ? Mathf.Clamp01((t - .62f) / .03f) * Mathf.Clamp01((1.3f - t) / .08f) : 0f;
            float trill = 1f + .5f * Mathf.Sin(2f * Mathf.PI * 32f * t);
            float pitch = 2900f + 60f * Mathf.Sin(2f * Mathf.PI * 32f * t);
            s[i] = Mathf.Sin(2f * Mathf.PI * pitch * t) * trill * blow * .3f;
        }
        return s;
    }
}
