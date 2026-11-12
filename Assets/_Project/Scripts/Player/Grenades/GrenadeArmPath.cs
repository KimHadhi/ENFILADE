using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The left arm's way through a G throw, as a path table of keys (the user brought GMod's quick-throw addons as the
/// reference, 2026-11-12). Tables/GrenadeArm is VManip's "thrownade" (Tools/Blender/vmanip_throw_path.py): the hand
/// leaves the gun for the belt, comes up with the throwable, holds it out, swings out to the left and whips it
/// forward, follows through and goes back to the gun. Tables/KnifeArm is the MW2019 throwing knife of "[MW] Quick
/// Throwing Knife" (Tools/Blender/hand_path.py, HandPaths/KnifeArm.json), mirrored onto the left hand: drawn and held
/// low, cocked back to the side and flicked forward. Which path a throwable uses, and the moments and speeds along it,
/// are Tables/ThrowArms (Motion); Tables/Grenades arm picks the row.
/// A key says what the arm does, not where a rig's bones are: the wrist and the elbow from the shoulder in arm lengths,
/// which way the hand runs (wrist to middle knuckle) and faces (out of the palm), how far the fingers and the thumb are
/// closed (degrees from straight) and whether the thing is in the hand; all in the view's axes (x right, y up, z
/// forward). GrenadeArm fits it to whatever arm a body has. A path's cycle is seconds at its clip's own speed.
/// </summary>
public static class GrenadeArmPath
{
    public const string TableName = "GrenadeArm", MotionsTable = "ThrowArms", DefaultMotion = "grenade";

    /// <summary>One moment of the arm, in the view's axes.</summary>
    public struct Key
    {
        public float Cycle;
        /// <summary>The wrist and the elbow from the shoulder, in arm lengths (upper arm plus forearm).</summary>
        public Vector3 Wrist, Elbow;
        /// <summary>The hand's run (wrist to middle knuckle) and its palm's facing, unit length.</summary>
        public Vector3 Along, Palm;
        /// <summary>Degrees the four fingers (their mean) and the thumb are closed from straight.</summary>
        public float Curl, Thumb;
        /// <summary>The throwable is in this hand.</summary>
        public bool Held;
    }

    /// <summary>
    /// A hand motion (a row of Tables/ThrowArms): its path table, how fast a tap's and a hold's draw and the throw go
    /// (times its clip's own speed), and its moments along the path: the pin (below 0: none), the hold, the release,
    /// the way back to the gun, the end. EyeSpace: its wrists and elbows are measured from the eyes in metres (a
    /// viewmodel's framing, the knife's) rather than from the shoulder in arm lengths, and ViewFov is the clip's own
    /// vertical field of view, to put the hand where it sat on screen at the player's own (GrenadeArm).
    /// </summary>
    public sealed class Motion
    {
        public string Id = DefaultMotion, Table = TableName;
        public float TapDraw = 1.6f, HoldDraw = 1.25f, ThrowSpeed = 1.3f, Pin = .3f, Hold = .4f, Release = .61f, Back = .85f, End = .97f;
        public bool EyeSpace;
        public float ViewFov = 50f;
        public bool HasPin => Pin >= 0f;
    }

    private static readonly Dictionary<string, List<Key>> paths = new Dictionary<string, List<Key>>();
    private static readonly Dictionary<string, Motion> motions = new Dictionary<string, Motion>();
    private static readonly Motion fallback = new Motion();
    private static bool motionsLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        paths.Clear();
        motions.Clear();
        motionsLoaded = false;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // A table saved while playing: read again at the next ask.
    private static void Forget()
    {
        paths.Clear();
        motions.Clear();
        motionsLoaded = false;
    }

    /// <summary>A throwable's hand motion: its row's arm (Tables/Grenades), else the grenade's.</summary>
    public static Motion For(Throwable kind)
    {
        var info = Throwables.Of(kind);
        return Of(info != null && !string.IsNullOrEmpty(info.Arm) ? info.Arm : DefaultMotion);
    }

    /// <summary>A hand motion by its row id (Tables/ThrowArms); the grenade's for an unknown one.</summary>
    public static Motion Of(string id)
    {
        LoadMotions();
        if (!string.IsNullOrEmpty(id) && motions.TryGetValue(id, out var motion)) return motion;
        return motions.TryGetValue(DefaultMotion, out var plain) ? plain : fallback;
    }

    /// <summary>Whether a path table has a path at all (two keys or more).</summary>
    public static bool Ready(string table) => Keys(table).Count > 1;

    /// <summary>The grenade's path at this point of its cycle (Tables/GrenadeArm).</summary>
    public static bool Sample(float cycle, out Key key) => Sample(TableName, cycle, out key);

    /// <summary>A path's arm at this point of its cycle, between the two keys around it. False when there is no path.</summary>
    public static bool Sample(string table, float cycle, out Key key)
    {
        var keys = Keys(table);
        key = default;
        if (keys.Count < 2) return false;
        if (cycle <= keys[0].Cycle) { key = keys[0]; return true; }
        int last = keys.Count - 1;
        if (cycle >= keys[last].Cycle) { key = keys[last]; return true; }
        int next = 1;
        while (next < last && keys[next].Cycle < cycle) next++;
        Key a = keys[next - 1], b = keys[next];
        float t = Mathf.InverseLerp(a.Cycle, b.Cycle, cycle);
        key.Cycle = cycle;
        key.Wrist = Vector3.Lerp(a.Wrist, b.Wrist, t);
        key.Elbow = Vector3.Lerp(a.Elbow, b.Elbow, t);
        // The hand turns the short way between its two facings, kept square to its run.
        Quaternion turn = Quaternion.Slerp(Quaternion.LookRotation(a.Along, a.Palm), Quaternion.LookRotation(b.Along, b.Palm), t);
        key.Along = turn * Vector3.forward;
        key.Palm = turn * Vector3.up;
        key.Curl = Mathf.Lerp(a.Curl, b.Curl, t);
        key.Thumb = Mathf.Lerp(a.Thumb, b.Thumb, t);
        // In the hand from the key that has it to the key that hasn't, half way: the grab and the release are cuts.
        key.Held = t < .5f ? a.Held : b.Held;
        return true;
    }

    private static List<Key> Keys(string table)
    {
        if (string.IsNullOrEmpty(table)) table = TableName;
        if (paths.TryGetValue(table, out var keys)) return keys;
        keys = new List<Key>();
        paths[table] = keys;
        foreach (string id in GameTables.RowIds(table))
        {
            if (!GameTables.TryGetRow(table, id, out var row)) continue;
            var key = new Key
            {
                Cycle = Number(row, "cycle", -1f),
                Wrist = Vector(row, "wrist"),
                Elbow = Vector(row, "elbow"),
                Along = Vector(row, "along"),
                Palm = Vector(row, "palm"),
                Curl = Number(row, "curl", 0f),
                Thumb = Number(row, "thumb", 0f),
                Held = Number(row, "held", 0f) > .5f,
            };
            if (key.Cycle < 0f || key.Along.sqrMagnitude < 1e-6f || key.Palm.sqrMagnitude < 1e-6f) continue;
            // Square the facing to the run, so a rounded cell never skews the hand.
            key.Along.Normalize();
            key.Palm = Vector3.ProjectOnPlane(key.Palm, key.Along);
            if (key.Palm.sqrMagnitude < 1e-6f) continue;
            key.Palm.Normalize();
            keys.Add(key);
        }
        keys.Sort((a, b) => a.Cycle.CompareTo(b.Cycle));
        return keys;
    }

    private static void LoadMotions()
    {
        if (motionsLoaded) return;
        motionsLoaded = true;
        motions.Clear();
        foreach (string id in GameTables.RowIds(MotionsTable))
        {
            if (!GameTables.TryGetRow(MotionsTable, id, out var row)) continue;
            var motion = new Motion { Id = id.Trim() };
            if (row.TryGetValue("table", out string table) && !string.IsNullOrWhiteSpace(table)) motion.Table = table.Trim();
            motion.TapDraw = Mathf.Max(.05f, Number(row, "tapDraw", motion.TapDraw));
            motion.HoldDraw = Mathf.Max(.05f, Number(row, "holdDraw", motion.HoldDraw));
            motion.ThrowSpeed = Mathf.Max(.05f, Number(row, "throwSpeed", motion.ThrowSpeed));
            motion.Pin = Number(row, "pin", motion.Pin);
            motion.Hold = Mathf.Max(0f, Number(row, "hold", motion.Hold));
            motion.Release = Mathf.Max(motion.Hold, Number(row, "release", motion.Release));
            motion.Back = Mathf.Max(motion.Release, Number(row, "back", motion.Back));
            motion.End = Mathf.Max(motion.Back, Number(row, "end", motion.End));
            motion.EyeSpace = row.TryGetValue("space", out string space) && string.Equals(space.Trim(), "eye", System.StringComparison.OrdinalIgnoreCase);
            motion.ViewFov = Mathf.Clamp(Number(row, "viewFov", motion.ViewFov), 10f, 150f);
            motions[motion.Id] = motion;
        }
    }

    private static Vector3 Vector(IReadOnlyDictionary<string, string> row, string name)
        => new Vector3(Number(row, name + "X", 0f), Number(row, name + "Y", 0f), Number(row, name + "Z", 0f));

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallbackValue)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallbackValue;
}
