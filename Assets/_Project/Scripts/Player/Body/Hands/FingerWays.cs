using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One hand's fingers as a clip made in Blender has them, in terms any humanoid hand can wear: for each finger segment
/// (index, middle, ring, little, thumb; each its base, middle and tip: HandGrip's order) the way it points in the hand's
/// own frame, as GunArms.Digits reads a gun clip's fingers (x the way the fingers run, wrist to middle knuckle; y the way
/// the palm faces; z = x × y), and how far it is turned about its own length past the least turn that points it so from
/// the hand's rest (degrees, Unity's sense about that way). HandGrip.WearWays puts them on a hand, so every joint the user
/// poses in Blender, its bend, its spread and its turn about itself, holds on any character's hand, a mod's included,
/// whatever its bones' axes. Before 0.9.61 the Blender hand workspaces sent the fingers as one closing number each (curl,
/// thumb) and the game bent every finger alike from them, so a finger shaped in Blender never reached the game (the user,
/// 2026-11-14: "단순하게 만드느라 손가락이 지금까지 그럼 계속 고장나 나를 한 달 동안 괴롭혔던 거고 … 다신 이런 일이 재발하지
/// 않도록 뼈에 새겨라").
/// A table carries them as four columns per segment, &lt;prefix&gt;&lt;finger&gt;&lt;joint&gt;X, Y, Z and Twist (index1X ...
/// thumb3Twist; a prefix tells two hands of one table apart). Tools/Blender/HandPaths/finger_ways.py measures them on a
/// posed rig and writes those columns; every Blender hand workspace's export uses it. A table without them (one from
/// before, a mod's) reads as no ways (Has false), and its reader closes the fingers by its curls as before.
/// </summary>
public sealed class FingerWays
{
    /// <summary>Finger segments per hand: five fingers, three each (GunArms.DigitCount).</summary>
    public const int Count = 15;

    /// <summary>The fingers' names in the columns, in HandGrip's order.</summary>
    public static readonly string[] Fingers = { "index", "middle", "ring", "little", "thumb" };

    /// <summary>Each segment's way in the hand's own frame (unit), in Fingers' order, base to tip: [finger * 3 + joint].</summary>
    public readonly Vector3[] Ways = new Vector3[Count];

    /// <summary>Each segment's turn about its own length past the least turn from the hand's rest (degrees).</summary>
    public readonly float[] Twists = new float[Count];

    /// <summary>Whether every segment has a way (false for a table without these columns).</summary>
    public bool Has;

    // The columns per prefix, made once: four per segment (X, Y, Z, Twist).
    private static readonly Dictionary<string, string[]> columns = new Dictionary<string, string[]>();

    /// <summary>The 60 column names for a prefix ("" for a table of one hand), segment by segment: X, Y, Z, Twist.</summary>
    public static string[] Columns(string prefix)
    {
        prefix ??= "";
        if (columns.TryGetValue(prefix, out var names)) return names;
        names = new string[Count * 4];
        for (int finger = 0; finger < 5; finger++)
            for (int joint = 0; joint < 3; joint++)
            {
                string segment = prefix + Fingers[finger] + (joint + 1);
                int at = (finger * 3 + joint) * 4;
                names[at] = segment + "X";
                names[at + 1] = segment + "Y";
                names[at + 2] = segment + "Z";
                names[at + 3] = segment + "Twist";
            }
        columns[prefix] = names;
        return names;
    }

    /// <summary>
    /// Reads a row's ways (the columns with <paramref name="prefix"/>). True when every segment has one; otherwise Has is
    /// false (a table from before these columns, a row missing some): a hand is never part ways, part closing numbers.
    /// </summary>
    public bool Read(IReadOnlyDictionary<string, string> row, string prefix = "")
    {
        Has = false;
        if (row == null) return false;
        var names = Columns(prefix);
        for (int i = 0; i < Count; i++)
        {
            int at = i * 4;
            if (!row.ContainsKey(names[at])) return false;
            var way = new Vector3(row.Number(names[at], 0f), row.Number(names[at + 1], 0f), row.Number(names[at + 2], 0f));
            if (way.sqrMagnitude < 1e-6f) return false;
            Ways[i] = way.normalized;
            Twists[i] = row.Number(names[at + 3], 0f);
        }
        Has = true;
        return true;
    }

    /// <summary>Becomes a copy of <paramref name="other"/> (none, or one without ways: no ways).</summary>
    public void CopyFrom(FingerWays other)
    {
        if (other == null || !other.Has)
        {
            Has = false;
            return;
        }
        if (other == this) return;
        System.Array.Copy(other.Ways, Ways, Count);
        System.Array.Copy(other.Twists, Twists, Count);
        Has = true;
    }

    /// <summary>
    /// Between two poses, into <paramref name="into"/> (which may be either of them): each segment's way turned the short
    /// way from one to the other and its twist the short way round, as a clip's other channels blend between its keys.
    /// Where only one of them has ways (a clip from before them beside one with them), the nearer one is taken whole.
    /// </summary>
    public static void Blend(FingerWays a, FingerWays b, float t, FingerWays into)
    {
        if (into == null) return;
        if (a == null || !a.Has || b == null || !b.Has)
        {
            into.CopyFrom(t < .5f ? a : b);
            return;
        }
        for (int i = 0; i < Count; i++)
        {
            into.Ways[i] = Vector3.Slerp(a.Ways[i], b.Ways[i], t);
            into.Twists[i] = Mathf.LerpAngle(a.Twists[i], b.Twists[i], t);
        }
        into.Has = true;
    }
}
