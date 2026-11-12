using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// An execution's knife stroke: a path table both arms follow while a player executes or takes down an enemy with their
/// class's knife (ClassKnives' motion column names it; Tables/KnifeStab is the one drawn so far, written by
/// Tools/Blender/Knives/knife_stab.py). A key says what an arm does, not where a rig's bones are, as RepairArm's loops do:
/// the wrist and an elbow pole from that arm's own shoulder in that arm's lengths, the hand's run (wrist to middle knuckle)
/// and its palm's facing, all in the view's axes (x right, y up, z forward); how far the fingers and the thumb close
/// (degrees); how much of the way the wrist is drawn to the victim (aim: the right hand over its neck, the left on its
/// head), and how much of the arm the stroke has at all (weight, so it eases in from the body's own pose and back out).
/// Columns r... are the right arm (the knife's), l... the left (the grab); held says the knife is in the right fist. Its
/// moments are the first rows marked ready (the drawn knife waits here while the player dashes in), strike (the blade
/// goes in: the execution's impact), out (pulled clear) and sheath (put away). ExecutionKnifeArm fits it to any humanoid.
/// </summary>
public sealed class KnifeStroke
{
    public const string DefaultTable = "KnifeStab";

    /// <summary>One arm at one moment, in the view's axes from its own shoulder (positions in that arm's lengths).</summary>
    public struct Arm
    {
        public Vector3 Wrist, Elbow, Along, Palm;
        public float Curl, Thumb, Aim, Weight;
    }

    /// <summary>Both arms at one moment of the stroke (Cycle: seconds into it).</summary>
    public struct Key
    {
        public float Cycle;
        public Arm Right, Left;
        public bool Held;
    }

    /// <summary>The stroke's length (s: its last key) and its moments (s).</summary>
    public float Length { get; private set; }
    public float Ready { get; private set; }
    public float Strike { get; private set; }
    public float Out { get; private set; }
    public float Sheath { get; private set; }

    private readonly List<Key> keys = new List<Key>();
    private static readonly Dictionary<string, KnifeStroke> known = new Dictionary<string, KnifeStroke>(System.StringComparer.OrdinalIgnoreCase);
    private static bool listening;

    /// <summary>The stroke a table holds, read once (again after the tables are saved); null when it has no keys.</summary>
    public static KnifeStroke Of(string table)
    {
        if (string.IsNullOrWhiteSpace(table)) table = DefaultTable;
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += known.Clear;
        }
        if (known.TryGetValue(table, out var stroke)) return stroke;
        stroke = Read(table);
        known[table] = stroke;
        return stroke;
    }

    /// <summary>The arms at <paramref name="time"/> seconds into the stroke, between its two nearest keys.</summary>
    public bool Sample(float time, out Key key)
    {
        key = default;
        if (keys.Count == 0) return false;
        if (time <= keys[0].Cycle) { key = keys[0]; return true; }
        int last = keys.Count - 1;
        if (time >= keys[last].Cycle) { key = keys[last]; return true; }
        int hi = 1;
        while (hi < last && keys[hi].Cycle < time) hi++;
        var a = keys[hi - 1];
        var b = keys[hi];
        float t = Mathf.InverseLerp(a.Cycle, b.Cycle, time);
        key = Blend(a, b, t);
        key.Cycle = time;
        return true;
    }

    /// <summary>Two moments of the arms mixed (t 0: a, 1: b): positions and closings in line, ways turned between.</summary>
    public static Key Blend(in Key a, in Key b, float t)
    {
        return new Key
        {
            Cycle = Mathf.Lerp(a.Cycle, b.Cycle, t),
            Right = Blend(a.Right, b.Right, t),
            Left = Blend(a.Left, b.Left, t),
            Held = t < .5f ? a.Held : b.Held,
        };
    }

    private static Arm Blend(in Arm a, in Arm b, float t)
    {
        return new Arm
        {
            Wrist = Vector3.Lerp(a.Wrist, b.Wrist, t),
            Elbow = Vector3.Lerp(a.Elbow, b.Elbow, t),
            Along = Vector3.Slerp(a.Along, b.Along, t).normalized,
            Palm = Vector3.Slerp(a.Palm, b.Palm, t).normalized,
            Curl = Mathf.Lerp(a.Curl, b.Curl, t),
            Thumb = Mathf.Lerp(a.Thumb, b.Thumb, t),
            Aim = Mathf.Lerp(a.Aim, b.Aim, t),
            Weight = Mathf.Lerp(a.Weight, b.Weight, t),
        };
    }

    private static KnifeStroke Read(string table)
    {
        var stroke = new KnifeStroke();
        float ready = -1f, strike = -1f, out_ = -1f, sheath = -1f;
        foreach (string id in GameTables.RowIds(table))
        {
            if (!GameTables.TryGetRow(table, id, out var row)) continue;
            float cycle = Number(row, "cycle", -1f);
            if (cycle < 0f) continue;
            var key = new Key
            {
                Cycle = cycle,
                Right = ArmOf(row, "r"),
                Left = ArmOf(row, "l"),
                Held = Number(row, "held", 0f) > .5f,
            };
            stroke.keys.Add(key);
            string mark = row.TryGetValue("mark", out string text) ? text.Trim() : "";
            if (mark.Length == 0) continue;
            if (ready < 0f && Is(mark, "ready")) ready = cycle;
            else if (strike < 0f && Is(mark, "strike")) strike = cycle;
            else if (out_ < 0f && Is(mark, "out")) out_ = cycle;
            else if (sheath < 0f && Is(mark, "sheath")) sheath = cycle;
        }
        if (stroke.keys.Count == 0) return null;
        stroke.keys.Sort((x, y) => x.Cycle.CompareTo(y.Cycle));
        stroke.Length = stroke.keys[stroke.keys.Count - 1].Cycle;
        // A table without its marks still plays: the strike in the middle, ready before it, out and sheath after.
        stroke.Strike = strike >= 0f ? strike : stroke.Length * .4f;
        stroke.Ready = ready >= 0f ? Mathf.Min(ready, stroke.Strike) : stroke.Strike * .5f;
        stroke.Out = out_ >= 0f ? Mathf.Max(out_, stroke.Strike) : Mathf.Lerp(stroke.Strike, stroke.Length, .4f);
        stroke.Sheath = sheath >= 0f ? Mathf.Max(sheath, stroke.Out) : Mathf.Lerp(stroke.Out, stroke.Length, .7f);
        return stroke;
    }

    private static Arm ArmOf(IReadOnlyDictionary<string, string> row, string side)
    {
        var arm = new Arm
        {
            Wrist = Vector(row, side + "wrist", new Vector3(0f, -.9f, .1f)),
            Elbow = Vector(row, side + "elbow", new Vector3(0f, -.5f, -.1f)),
            Along = Vector(row, side + "along", Vector3.down),
            Palm = Vector(row, side + "palm", side == "r" ? Vector3.left : Vector3.right),
            Curl = Number(row, side + "curl", 30f),
            Thumb = Number(row, side + "thumb", 30f),
            Aim = Mathf.Clamp01(Number(row, side + "aim", 0f)),
            Weight = Mathf.Clamp01(Number(row, side + "weight", 0f)),
        };
        arm.Along = arm.Along.sqrMagnitude > 1e-8f ? arm.Along.normalized : Vector3.down;
        arm.Palm = arm.Palm.sqrMagnitude > 1e-8f ? arm.Palm.normalized : Vector3.left;
        return arm;
    }

    private static Vector3 Vector(IReadOnlyDictionary<string, string> row, string name, Vector3 fallback)
        => new Vector3(Number(row, name + "X", fallback.x), Number(row, name + "Y", fallback.y), Number(row, name + "Z", fallback.z));

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read : fallback;

    private static bool Is(string mark, string name) => string.Equals(mark, name, System.StringComparison.OrdinalIgnoreCase);
}
