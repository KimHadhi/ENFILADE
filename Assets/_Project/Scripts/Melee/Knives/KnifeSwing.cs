using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One clip of the class knife in the melee slot (MeleeKnife): the right arm drawing it, holding its guard, slashing,
/// slashing back or stabbing. Written by the Blender knife workspace (ArtSource/Knives, Tools/Blender/Knives/knife_tools.py:
/// saving sends what changed), as the throwables workspace writes the G throws: per half frame, the wrist and the elbow from
/// the eyes in game metres, the hand's run (wrist to middle knuckle) and its palm's facing, all in the view's axes (x right,
/// y up, z forward), every finger joint as posed in Blender (FingerWays' columns, index1X ... thumb3Twist: the hand's own
/// frame, so any character's hand wears the same shape; the user, 2026-11-14: "다신 이런 일이 재발하지 않도록 뼈에 새겨라"),
/// how far the four fingers and the thumb close (degrees: what a table from before the joints has instead),
/// whether the knife is in the fist (held: a draw's first frames reach for it), and, for an attack, its phases as the
/// workspace's timeline marks say them: the rows through which the blade is live (hit: from 벰 to 벰 끝, the only time
/// the knife cuts what it passes through) and from where the next action may begin (next: 다음, the recovery's end; the
/// rest of the clip is the arm going home, which a next swing cuts short). A swing is its clip with these phases
/// (SwingTimeline plays them, PlayerMeleeCombat acts on them): until 2026-11-18 the one hit mark was read and never
/// used, the blow landing by a sphere from the eyes for a fixed 0.18 s whatever the arm was doing.
/// ExecutionKnifeArm fits it to any humanoid's right arm, as GrenadeArm fits its clips to the left.
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

    /// <summary>The table it was read from (what the records call it).</summary>
    public string Table { get; private set; }
    /// <summary>The clip's length (s: its last key).</summary>
    public float Length { get; private set; }
    /// <summary>
    /// The swing's phases in the clip (s): the blade is live from ActiveFrom to ActiveTo and the next action may begin at
    /// Next. A clip with no hit rows is no attack (a draw, a guard): nothing is live and the next may begin at once.
    /// </summary>
    public float ActiveFrom { get; private set; }
    public float ActiveTo { get; private set; }
    public float Next { get; private set; }
    /// <summary>Its rows mark a blade's window: an attack's clip.</summary>
    public bool Cuts => ActiveTo > ActiveFrom;
    /// <summary>The phases as a swing's timeline takes them (SwingTimeline).</summary>
    public SwingMarks Marks => new SwingMarks(Length, ActiveFrom, ActiveTo, Next);

    // A table written before the phases (one hit row: the moment the blade met; a mod's, or a clip not sent again since
    // 2026-11-18) is read as live from its start to this long past that moment (s), the next action from its end.
    private const float OldFormatAfter = .1f;

    private readonly List<Key> keys = new List<Key>();
    // Every key's finger joints (FingerWays), when every row has them.
    private readonly List<FingerWays> fingers = new List<FingerWays>();

    /// <summary>Its rows carry every finger joint (FingerWays); else its fingers close by its curl and thumb.</summary>
    public bool HasFingers => fingers.Count > 0 && fingers.Count == keys.Count;
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
        Span(time, out int a, out int b, out float t);
        key = a == b ? keys[a] : Blend(keys[a], keys[b], t);
        if (a != b) key.Cycle = time;
        return true;
    }

    /// <summary>
    /// The finger joints at <paramref name="time"/> seconds into the clip, between its two nearest keys, into
    /// <paramref name="into"/>; false (into.Has false) when its rows carry none, and the fingers close by the key's curls.
    /// </summary>
    public bool SampleFingers(float time, FingerWays into)
    {
        if (into == null) return false;
        into.Has = false;
        if (!HasFingers) return false;
        Span(time, out int a, out int b, out float t);
        FingerWays.Blend(fingers[a], fingers[b], t, into);
        return into.Has;
    }

    // The two keys round a time and how far between them it is (one key twice, held, at the clip's ends).
    private void Span(float time, out int a, out int b, out float t)
    {
        int last = keys.Count - 1;
        t = 0f;
        if (time <= keys[0].Cycle) { a = b = 0; return; }
        if (time >= keys[last].Cycle) { a = b = last; return; }
        int hi = 1;
        while (hi < last && keys[hi].Cycle < time) hi++;
        a = hi - 1;
        b = hi;
        t = Mathf.InverseLerp(keys[a].Cycle, keys[b].Cycle, time);
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
        var clip = new KnifeSwing { Table = table };
        var read = new List<(Key key, FingerWays fingers)>();
        bool everyFinger = true;
        float from = -1f, to = -1f, next = -1f;
        int hits = 0;
        foreach (string id in GameTables.RowIds(table))
        {
            if (!GameTables.TryGetRow(table, id, out var row)) continue;
            float cycle = row.Number("cycle", -1f);
            if (cycle < 0f) continue;
            var key = new Key
            {
                Cycle = cycle,
                Wrist = TableCells.Vector(row, "wrist", new Vector3(.2f, -.2f, .32f)),
                Elbow = TableCells.Vector(row, "elbow", new Vector3(.3f, -.45f, .05f)),
                Along = TableCells.Vector(row, "along", Vector3.forward),
                Palm = TableCells.Vector(row, "palm", Vector3.left),
                Curl = row.Number("curl", 150f),
                Thumb = row.Number("thumb", 80f),
                Held = row.Number("held", 1f) > .5f,
            };
            key.Along = key.Along.sqrMagnitude > 1e-8f ? key.Along.normalized : Vector3.forward;
            key.Palm = key.Palm.sqrMagnitude > 1e-8f ? key.Palm.normalized : Vector3.left;
            var ways = new FingerWays();
            if (!ways.Read(row)) everyFinger = false;
            read.Add((key, ways));
            if (row.Number("hit", 0f) > .5f)
            {
                hits++;
                if (from < 0f || cycle < from) from = cycle;
                if (cycle > to) to = cycle;
            }
            if (row.Number("next", 0f) > .5f && (next < 0f || cycle < next)) next = cycle;
        }
        if (read.Count == 0) return null;
        read.Sort((x, y) => x.key.Cycle.CompareTo(y.key.Cycle));
        foreach (var (key, ways) in read)
        {
            clip.keys.Add(key);
            if (everyFinger) clip.fingers.Add(ways);
        }
        clip.Length = clip.keys[clip.keys.Count - 1].Cycle;
        if (hits == 1)
        {
            to = Mathf.Min(clip.Length, from + OldFormatAfter);
            from = 0f;
        }
        clip.ActiveFrom = hits > 0 ? Mathf.Clamp(from, 0f, clip.Length) : 0f;
        clip.ActiveTo = hits > 0 ? Mathf.Clamp(to, clip.ActiveFrom, clip.Length) : 0f;
        clip.Next = !clip.Cuts ? 0f : next >= 0f ? Mathf.Clamp(next, clip.ActiveTo, clip.Length) : clip.Length;
        return clip;
    }
}
