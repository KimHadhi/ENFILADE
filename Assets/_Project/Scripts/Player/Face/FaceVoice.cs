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
        /// <summary>Which syllable of the clip each Step is in (0 before the first starts), and how many it has.</summary>
        public ushort[] Syllable;
        public int Syllables;

        /// <summary>The mouth at <paramref name="time"/> seconds into the clip, and the syllable it is in.</summary>
        public void Sample(float time, out float open, out int vowel, out int syllable)
        {
            int at = Mathf.Clamp((int)(time / Step), 0, Open.Length - 1);
            open = Open[at] / 255f;
            vowel = Vowel[at];
            syllable = Syllable[at];
        }

        public void Sample(float time, out float open, out int vowel) => Sample(time, out open, out vowel, out _);
    }

    // Each kana from ぁ (U+3041) to ゖ (U+3096), its vowel (a i u e o) or '-' for one that adds no syllable of its own
    // (っ, ん); a small ゃゅょ (y) and ゎ (w) change the syllable before it instead. Katakana is the same 0x60 up.
    private const string KanaVowels = "aaiiuueeooaaiiuueeooaaiiuueeooaaii-uueeooaiueoaaaiiiuuueeeoooaiueoyayuyoaiueowaieo-uae";
    // Hangul's 21 vowels (medial jamo) in Unicode order, as the five mouth shapes: ㅏ a, ㅐ e, ㅑ a, ㅒ e, ㅓ o, ㅔ e,
    // ㅕ o, ㅖ e, ㅗ o, ㅘ a, ㅙ e, ㅚ e, ㅛ o, ㅜ u, ㅝ o, ㅞ e, ㅟ i, ㅠ u, ㅡ u, ㅢ i, ㅣ i.
    private const string HangulVowels = "aeaeoeoeoaeeouoeiuuii";

    /// <summary>
    /// The vowels of a line as it would be said, syllable by syllable (Korean by each syllable's vowel, Japanese kana by
    /// their row, other letters by their vowel letters), into <paramref name="into"/>; returns how many. A modder's voice
    /// pack, a line without a voice at all, or a clip whose samples can't be read still moves the mouth through the
    /// line's own vowels (like Skyrim's Fuz Ro D-oh, which makes silent lines' mouths move by their text).
    /// </summary>
    internal static int VowelsOf(string text, byte[] into)
    {
        if (string.IsNullOrEmpty(text) || into == null) return 0;
        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (count >= into.Length) break;
            int vowel = -1;
            if (c >= '가' && c <= '힣') vowel = Code(HangulVowels[(c - 0xAC00) / 28 % 21]);
            else if ((c >= 'ぁ' && c <= 'ゖ') || (c >= 'ァ' && c <= 'ヶ'))
            {
                char v = KanaVowels[(c >= 'ァ' ? c - 0x60 : c) - 0x3041];
                if (v == 'y' || v == 'w')
                {
                    // きゃ is one syllable, "kya": the small kana sets the vowel of the one before it.
                    if (count > 0) into[count - 1] = Code(v == 'w' ? 'a' : SmallYa(c));
                    continue;
                }
                if (v != '-') vowel = Code(v);
            }
            else if (c == 'ー' && count > 0) vowel = into[count - 1];
            else
            {
                char lower = char.ToLowerInvariant(c);
                bool letter = lower == 'a' || lower == 'e' || lower == 'i' || lower == 'o' || lower == 'u' || lower == 'y';
                // A run of vowel letters ("ea", "ou") is one syllable.
                if (letter && !inWord) vowel = Code(lower == 'y' ? 'i' : lower);
                inWord = letter;
                if (vowel < 0) continue;
            }
            if (vowel >= 0) into[count++] = (byte)vowel;
        }
        return count;
    }

    // The vowel a small ゃ/ゅ/ょ (or ャ/ュ/ョ) gives the syllable it joins.
    private static char SmallYa(char c)
    {
        int at = (c >= 'ァ' ? c - 0x60 : c) - 0x3041;
        // ゃ (0x3083) a, ゅ (0x3085) u, ょ (0x3087) o.
        return at == 0x42 ? 'a' : at == 0x44 ? 'u' : 'o';
    }

    private static byte Code(char vowel)
    {
        switch (vowel)
        {
            case 'i': return 1;
            case 'u': return 2;
            case 'e': return 3;
            case 'o': return 4;
            default: return 0;
        }
    }

    /// <summary>
    /// How long a line without a voice is "said" (its mouth moves and its face stays): its syllables at a talking pace
    /// (FaceLife.csv Talk.syllable seconds each, Talk.pad more), within Talk.shortest and Talk.longest.
    /// </summary>
    internal static float Seconds(int syllables)
    {
        float each = Mathf.Max(.02f, FaceTables.Number("Talk.syllable", .14f));
        float said = syllables * each + FaceTables.Number("Talk.pad", .2f);
        return Mathf.Clamp(said, FaceTables.Number("Talk.shortest", .6f), Mathf.Max(.6f, FaceTables.Number("Talk.longest", 4f)));
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
        var track = new Track { Open = new byte[count], Vowel = new byte[count], Syllable = new ushort[count] };
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
                for (int j = from; j < i; j++)
                {
                    track.Vowel[j] = vowel;
                    track.Syllable[j] = (ushort)Mathf.Min(syllable, ushort.MaxValue);
                }
            }
            else
                for (int j = 0; j < i && j < count; j++) track.Vowel[j] = 0;
            if (i >= count)
            {
                track.Syllables = syllable + 1;
                break;
            }
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
