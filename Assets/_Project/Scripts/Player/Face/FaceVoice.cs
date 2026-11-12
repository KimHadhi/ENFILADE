using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a voice clip moves a mouth (CharacterFace's talking), read once per clip from its own samples: every 1/60 s how
/// loud it is (how far the mouth opens) and, syllable by syllable, which of the five vowels the mouth makes. A hiss or a
/// bright sound (many zero crossings) reads as i or e, a dark full one as a, o or u, so the mouth opens on the syllables
/// the line really has. Read from the clip, not from what the speaker's AudioSource puts out, so a friend shouting far
/// off moves their mouth as much as one beside you, and every PC moves it the same. A clip whose samples can't be read
/// (streamed, or compressed in memory, which hands back silence) has no track, and the face flaps the mouth while it
/// plays instead.
/// </summary>
internal static class FaceVoice
{
    /// <summary>One sample of the track per this many seconds.</summary>
    internal const float Step = 1f / 60f;
    // Frames read from a clip at a time (the buffer is kept, so reading allocates only the track itself).
    private const int ChunkFrames = 4096;
    // A syllable starts where the loudness climbs past Rise after falling under Fall (0-1 of the clip's loud part).
    private const float Rise = .35f, Fall = .2f;

    internal sealed class Track
    {
        /// <summary>How open, 0-255, and which vowel (0 a, 1 i, 2 u, 3 e, 4 o), per Step.</summary>
        public byte[] Open, Vowel;

        /// <summary>The mouth at <paramref name="time"/> seconds into the clip.</summary>
        public void Sample(float time, out float open, out int vowel)
        {
            int at = Mathf.Clamp((int)(time / Step), 0, Open.Length - 1);
            open = Open[at] / 255f;
            vowel = Vowel[at];
        }
    }

    private static readonly Dictionary<AudioClip, Track> tracks = new Dictionary<AudioClip, Track>();
    private static float[] chunk;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => tracks.Clear();

    /// <summary>The clip's track, read the first time it is asked for; null when its samples can't be read.</summary>
    internal static Track Of(AudioClip clip)
    {
        if (clip == null) return null;
        if (tracks.TryGetValue(clip, out var track)) return track;
        // Not loaded yet (a clip loading in the background): asked again next time rather than remembered as unreadable.
        if (clip.loadState != AudioDataLoadState.Loaded && clip.loadType != AudioClipLoadType.Streaming) return null;
        track = Read(clip);
        tracks[clip] = track;
        return track;
    }

    private static Track Read(AudioClip clip)
    {
        if (clip.loadType == AudioClipLoadType.Streaming) return null;
        int channels = Mathf.Max(1, clip.channels), frames = clip.samples, rate = clip.frequency;
        if (frames <= 0 || rate <= 0) return null;
        int window = Mathf.Max(16, Mathf.RoundToInt(rate * Step));
        int count = (frames + window - 1) / window;
        var loud = new float[count];
        var bright = new float[count];
        if (chunk == null || chunk.Length < ChunkFrames * channels) chunk = new float[ChunkFrames * channels];
        int index = 0, inWindow = 0, crossings = 0;
        double sum = 0d;
        float last = 0f;
        for (int start = 0; start < frames; start += ChunkFrames)
        {
            // GetData fills the whole buffer and wraps round past the clip's end: only the first frames-start count.
            if (!clip.GetData(chunk, start)) return null;
            int take = Mathf.Min(ChunkFrames, frames - start);
            for (int f = 0; f < take; f++)
            {
                float mixed = 0f;
                for (int c = 0; c < channels; c++) mixed += chunk[f * channels + c];
                mixed /= channels;
                sum += mixed * mixed;
                if ((mixed >= 0f) != (last >= 0f)) crossings++;
                last = mixed;
                if (++inWindow < window) continue;
                loud[index] = Mathf.Sqrt((float)(sum / inWindow));
                bright[index] = crossings / (float)inWindow;
                index++;
                inWindow = crossings = 0;
                sum = 0d;
            }
        }
        if (inWindow > 0 && index < count)
        {
            loud[index] = Mathf.Sqrt((float)(sum / inWindow));
            bright[index] = crossings / (float)inWindow;
        }
        float peak = 0f;
        foreach (float value in loud) peak = Mathf.Max(peak, value);
        // All silence: a compressed clip reads as zeros. Better the flapping than a shut mouth over a voice.
        if (peak < 1e-4f) return null;
        // The loud part of speech, not its single loudest instant, opens the mouth wide.
        float full = peak * .75f;
        var track = new Track { Open = new byte[count], Vowel = new byte[count] };
        for (int i = 0; i < count; i++) track.Open[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(loud[i] / full) * 255f);
        // Syllable by syllable: one vowel for each, from how bright it sounds at its loudest.
        int syllable = -1, from = 0;
        bool quiet = true;
        byte vowel = 0;
        for (int i = 0; i <= count; i++)
        {
            float open = i < count ? loud[i] / full : 0f;
            bool starts = i < count && quiet && open >= Rise;
            if (i < count && open < Fall) quiet = true;
            if (!starts && i < count) continue;
            // The syllable before this one (from..i) takes its vowel; the first start only opens one.
            if (syllable >= 0)
            {
                int loudest = from;
                for (int j = from; j < i; j++)
                    if (loud[j] > loud[loudest]) loudest = j;
                vowel = VowelOf(bright[loudest], syllable);
                for (int j = from; j < i; j++) track.Vowel[j] = vowel;
            }
            else
                for (int j = 0; j < i && j < count; j++) track.Vowel[j] = 0;
            if (i >= count) break;
            syllable++;
            from = i;
            quiet = false;
        }
        return track;
    }

    // Bright (a hiss, an "i") to dark (an "o"), the syllable's number taking turns between neighbours so a run of the
    // same brightness doesn't hold one mouth.
    private static byte VowelOf(float crossings, int syllable)
    {
        if (crossings > .16f) return 1;
        if (crossings > .09f) return (byte)(syllable % 2 == 0 ? 3 : 1);
        if (crossings > .05f) return (byte)(syllable % 2 == 0 ? 0 : 3);
        switch (syllable % 3)
        {
            case 0: return 4;
            case 1: return 0;
            default: return 2;
        }
    }
}
