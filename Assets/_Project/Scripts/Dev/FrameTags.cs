using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Names for what happened in a frame, so a long frame in Logs/hitches.txt says what it was ("brute appears", "zombie
/// made ×6", "corpse laid") and not only which engine markers took the time (HitchProbe reads them). Code marks the
/// moments it owns with Mark; HitchProbe also notes on its own what it can see from outside (zombies coming and going,
/// brutes arriving, corpses, dolls, the song, the first callout feed and alert). The calls compile away outside the
/// editor ([Conditional]), so marks cost nothing in a build. Only the last few frames are kept.
/// </summary>
public static class FrameTags
{
    // Frames of marks kept: HitchProbe reads the frame it reports and the two before it.
    private const int KeptFrames = 6;
    private static readonly List<(int frame, string what)> marks = new List<(int frame, string what)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => marks.Clear();

    /// <summary>Notes that <paramref name="what"/> happened this frame (editor only).</summary>
    [Conditional("UNITY_EDITOR")]
    public static void Mark(string what)
    {
        if (string.IsNullOrEmpty(what)) return;
        int now = Time.frameCount;
        marks.RemoveAll(mark => mark.frame < now - KeptFrames);
        marks.Add((now, what));
    }

    /// <summary>The marks of frames <paramref name="from"/> to <paramref name="to"/>, oldest first.</summary>
    public static void Between(int from, int to, List<(int frame, string what)> into)
    {
        foreach (var mark in marks)
            if (mark.frame >= from && mark.frame <= to) into.Add(mark);
    }
}
