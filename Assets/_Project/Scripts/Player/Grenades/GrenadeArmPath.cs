using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The left arm's way through a quick throw, as GMod's "[VManip] Quick Grenades" plays it (the user brought it as the
/// reference, 2026-11-12): Tables/GrenadeArm, one row a key, sampled from VManip's own "thrownade" hand animation by
/// Tools/Blender/vmanip_throw_path.py. The hand leaves the gun for the belt, comes up with the throwable, holds it out
/// (VManip's hold point), swings out to the left and whips it forward, follows through and goes back to the gun.
/// A key says what the arm does, not where a rig's bones are: the wrist and the elbow from the shoulder in arm lengths,
/// which way the hand runs (wrist to middle knuckle) and faces (out of the palm), how far the fingers and the thumb are
/// closed (degrees from straight) and whether the throwable is in the hand; all in the view's axes (x right, y up, z
/// forward). GrenadeArm fits it to whatever arm a body has. The cycle runs 0 to 1 at VManip's one cycle a second;
/// the moments in it (the grab, the pin, the hold, the release, the way back) are GrenadeRules'.
/// </summary>
public static class GrenadeArmPath
{
    public const string TableName = "GrenadeArm";

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

    private static readonly List<Key> keys = new List<Key>();
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        keys.Clear();
        loaded = false;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // A table saved while playing: read again at the next ask.
    private static void Forget() => loaded = false;

    /// <summary>Whether there is a path at all (the table has two keys or more).</summary>
    public static bool Ready
    {
        get
        {
            Load();
            return keys.Count > 1;
        }
    }

    /// <summary>The arm at this point of the cycle, between the two keys around it. False when there is no path.</summary>
    public static bool Sample(float cycle, out Key key)
    {
        Load();
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

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        keys.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
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
    }

    private static Vector3 Vector(IReadOnlyDictionary<string, string> row, string name)
        => new Vector3(Number(row, name + "X", 0f), Number(row, name + "Y", 0f), Number(row, name + "Z", 0f));

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}
