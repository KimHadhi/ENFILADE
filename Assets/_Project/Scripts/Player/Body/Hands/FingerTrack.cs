using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A body clip's fingers as Blender has them, both hands, key by key: the table beside the clip,
/// Tables/Clips/&lt;clip name&gt;_fingers (time: seconds into the clip at its own speed; the left hand's FingerWays in the
/// columns l…, the right hand's in r…). A humanoid clip can't carry fingers whole: Unity's muscles keep a finger's bend
/// and spread but not a segment's twist, the outer joints' side swing or the thumb's roll, and the hands' own safety
/// passes made for clips retargeted from other skeletons (HandGrip.RelaxThumb, KeepHuman) then replace or clamp what it
/// does carry. Whoever plays such a clip puts the track on after them (PlayerBodyHands.WearFingerWays, or an ArmIK of its
/// own), so the hands are as posed in Blender on any character (the user, 2026-11-14, on the hands of the clips made in
/// Blender: "네, 다 고쳐"). A clip with no table plays as before. Tools/Blender/HandPaths/finger_ways.py writes the tables
/// (track_rows, track_text).
/// </summary>
public sealed class FingerTrack
{
    /// <summary>The table's name is the clip's with this after it.</summary>
    public const string Suffix = "_fingers";

    /// <summary>The column prefixes of the two hands (FingerWays.Columns).</summary>
    public const string Left = "l", Right = "r";

    private readonly List<float> times = new List<float>();
    private readonly List<FingerWays> left = new List<FingerWays>(), right = new List<FingerWays>();

    private static readonly Dictionary<string, FingerTrack> tracks = new Dictionary<string, FingerTrack>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        tracks.Clear();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // A table saved while playing (a clip sent from Blender): read again at the next ask.
    private static void Forget() => tracks.Clear();

    /// <summary>Whether every key has the left hand (else that hand plays as the clip has it).</summary>
    public bool HasLeft => times.Count > 0 && left.Count == times.Count;

    /// <summary>Whether every key has the right hand.</summary>
    public bool HasRight => times.Count > 0 && right.Count == times.Count;

    /// <summary>The finger track of a clip by the clip's name; null when it has none.</summary>
    public static FingerTrack Of(string clip)
    {
        if (string.IsNullOrEmpty(clip)) return null;
        if (tracks.TryGetValue(clip, out var known)) return known;
        var track = Load(clip + Suffix);
        tracks[clip] = track;
        return track;
    }

    /// <summary>
    /// One hand at this time (s at the clip's own speed), between the two keys round it and held at the ends, into
    /// <paramref name="into"/>. False (into without ways) when the track lacks that hand.
    /// </summary>
    public bool Sample(bool isLeft, float time, FingerWays into)
    {
        if (into == null) return false;
        into.Has = false;
        if (!(isLeft ? HasLeft : HasRight)) return false;
        var hand = isLeft ? left : right;
        int last = times.Count - 1;
        if (time <= times[0])
        {
            into.CopyFrom(hand[0]);
            return into.Has;
        }
        if (time >= times[last])
        {
            into.CopyFrom(hand[last]);
            return into.Has;
        }
        int next = 1;
        while (next < last && times[next] < time) next++;
        FingerWays.Blend(hand[next - 1], hand[next], Mathf.InverseLerp(times[next - 1], times[next], time), into);
        return into.Has;
    }

    private static FingerTrack Load(string table)
    {
        var read = new List<(float time, FingerWays left, FingerWays right)>();
        bool everyLeft = true, everyRight = true;
        foreach (string id in GameTables.RowIds(table))
        {
            if (!GameTables.TryGetRow(table, id, out var row)) continue;
            float time = row.Number("time", -1f);
            if (time < 0f) continue;
            var l = new FingerWays();
            var r = new FingerWays();
            if (!l.Read(row, Left)) everyLeft = false;
            if (!r.Read(row, Right)) everyRight = false;
            read.Add((time, l, r));
        }
        if (read.Count == 0 || (!everyLeft && !everyRight)) return null;
        read.Sort((a, b) => a.time.CompareTo(b.time));
        var track = new FingerTrack();
        foreach (var (time, l, r) in read)
        {
            track.times.Add(time);
            if (everyLeft) track.left.Add(l);
            if (everyRight) track.right.Add(r);
        }
        return track;
    }
}
