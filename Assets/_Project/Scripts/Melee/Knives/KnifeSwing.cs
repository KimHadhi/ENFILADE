using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// One clip of the class knife in the melee slot (MeleeKnife): the right arm drawing it, holding its guard, slashing,
/// slashing back or stabbing. Written by the Blender knife workspace (ArtSource/Knives, Tools/Blender/Knives/knife_tools.py:
/// saving sends what changed), as the throwables workspace writes the G throws: per half frame, the wrist and the elbow from
/// the eyes in game metres, the hand's run (wrist to middle knuckle) and its palm's facing, all in the view's axes (x right,
/// y up, z forward), how far the four fingers and the thumb close (degrees), whether the knife is in the fist (held: a
/// draw's first frames reach for it) and the moment the blade meets what it cuts (hit). ExecutionKnifeArm fits it to any
/// humanoid's right arm, as GrenadeArm fits its clips to the left.
/// </summary>
public sealed class KnifeSwing
{
    /// <summary>One moment of the right arm, in the view's axes from the eyes (positions in game metres).</summary>
    public struct Key
    {
        public float Cycle;
        public Vector3 Wrist, Elbow, Along, Palm;
        public float Curl, Thumb;
        public bool Held;
    }

    /// <summary>The clip's length (s: its last key) and its blade's moment (s; below 0: none marked).</summary>
    public float Length { get; private set; }
    public float Hit { get; private set; } = -1f;

    private readonly List<Key> keys = new List<Key>();
    private static readonly Dictionary<string, KnifeSwing> known = new Dictionary<string, KnifeSwing>(System.StringComparer.OrdinalIgnoreCase);
    private static bool listening;

    /// <summary>The clip a table holds, read once (again after the tables are saved); null when it has no keys.</summary>
    public static KnifeSwing Of(string table)
    {
        if (string.IsNullOrWhiteSpace(table)) return null;
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += known.Clear;
        }
        if (known.TryGetValue(table, out var clip)) return clip;
        clip = Read(table.Trim());
        known[table] = clip;
        return clip;
    }

    /// <summary>The arm at <paramref name="time"/> seconds into the clip, between its two nearest keys (held at its ends).</summary>
    public bool Sample(float time, out Key key)
    {
        key = default;
        if (keys.Count == 0) return false;
        if (time <= keys[0].Cycle) { key = keys[0]; return true; }
        int last = keys.Count - 1;
        if (time >= keys[last].Cycle) { key = keys[last]; return true; }
        int hi = 1;
        while (hi < last && keys[hi].Cycle < time) hi++;
        key = Blend(keys[hi - 1], keys[hi], Mathf.InverseLerp(keys[hi - 1].Cycle, keys[hi].Cycle, time));
        key.Cycle = time;
        return true;
    }

    /// <summary>Two moments of the arm mixed (t 0: a, 1: b): positions and closings in line, ways turned between.</summary>
    public static Key Blend(in Key a, in Key b, float t)
    {
        return new Key
        {
            Cycle = Mathf.Lerp(a.Cycle, b.Cycle, t),
            Wrist = Vector3.Lerp(a.Wrist, b.Wrist, t),
            Elbow = Vector3.Lerp(a.Elbow, b.Elbow, t),
            Along = Vector3.Slerp(a.Along, b.Along, t).normalized,
            Palm = Vector3.Slerp(a.Palm, b.Palm, t).normalized,
            Curl = Mathf.Lerp(a.Curl, b.Curl, t),
            Thumb = Mathf.Lerp(a.Thumb, b.Thumb, t),
            Held = t < .5f ? a.Held : b.Held,
        };
    }

    private static KnifeSwing Read(string table)
    {
        var clip = new KnifeSwing();
        foreach (string id in GameTables.RowIds(table))
        {
            if (!GameTables.TryGetRow(table, id, out var row)) continue;
            float cycle = Number(row, "cycle", -1f);
            if (cycle < 0f) continue;
            var key = new Key
            {
                Cycle = cycle,
                Wrist = Vector(row, "wrist", new Vector3(.2f, -.2f, .32f)),
                Elbow = Vector(row, "elbow", new Vector3(.3f, -.45f, .05f)),
                Along = Vector(row, "along", Vector3.forward),
                Palm = Vector(row, "palm", Vector3.left),
                Curl = Number(row, "curl", 150f),
                Thumb = Number(row, "thumb", 80f),
                Held = Number(row, "held", 1f) > .5f,
            };
            key.Along = key.Along.sqrMagnitude > 1e-8f ? key.Along.normalized : Vector3.forward;
            key.Palm = key.Palm.sqrMagnitude > 1e-8f ? key.Palm.normalized : Vector3.left;
            clip.keys.Add(key);
            if (clip.Hit < 0f && Number(row, "hit", 0f) > .5f) clip.Hit = cycle;
        }
        if (clip.keys.Count == 0) return null;
        clip.keys.Sort((x, y) => x.Cycle.CompareTo(y.Cycle));
        clip.Length = clip.keys[clip.keys.Count - 1].Cycle;
        return clip;
    }

    private static Vector3 Vector(IReadOnlyDictionary<string, string> row, string name, Vector3 fallback)
        => new Vector3(Number(row, name + "X", fallback.x), Number(row, name + "Y", fallback.y), Number(row, name + "Z", fallback.z));

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback) => TableCells.Number(row, key, fallback);
}
