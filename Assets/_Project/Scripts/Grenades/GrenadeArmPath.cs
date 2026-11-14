using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The left arm's clips for a G throw (the user, 2026-11-12: "드는 모션, 대기 모션, 던지는 모션 내가 블렌더로 수정할 수 있게
/// 만들어서 적용해 봐"). Each throwable's hand motion (a row of Tables/ThrowArms, picked by Tables/Grenades arm) names four
/// clips: the draw (from the gun to the belt and up, the pin pulled at its moment), the hold (the cooking loop, played
/// round while G is held), the throw (the wind-up, the release at its moment, the follow-through and back to the gun) and
/// the tap (Call of Duty's quick throw in one motion; the knife's is its throw). Each clip is a table the Blender throw
/// workspace writes (ArtSource/Grenades, Tools/Blender/Grenades/grenade_tools.py "게임에 보내기"): the grenade's drafted
/// there, the knife's the MW2019 viewmodel of GMod's "[MW] Quick Throwing Knife" mirrored onto the left hand bone for bone.
/// A key says what the arm does, not where a rig's bones are: the wrist and the elbow from the eyes in metres (or, for a
/// motion measured from the shoulder, in arm lengths from it), which way the hand runs (wrist to middle knuckle) and faces
/// (out of the palm), every finger joint as Blender has it (FingerWays: each segment's way in the hand's own frame and its
/// twist, read by SampleFingers) and whether the thing is in the hand; all in the view's axes (x right, y up, z forward);
/// its moments (pin, release) are rows marked so. How far each finger and the thumb are closed (curl, thumb: degrees)
/// stays beside them for a table from before 0.9.61, which has nothing else for its fingers. A clip's cycle is its
/// seconds at its own speed. GrenadeArm fits it to whatever arm a body has. RepairArm reads its loops through Sample too.
/// </summary>
public static class GrenadeArmPath
{
    public const string MotionsTable = "ThrowArms", DefaultMotion = "grenade";

    /// <summary>One moment of the arm, in the view's axes.</summary>
    public struct Key
    {
        public float Cycle;
        /// <summary>The wrist and the elbow: from the eyes in metres for an eye-space motion, else from the shoulder in arm lengths.</summary>
        public Vector3 Wrist, Elbow;
        /// <summary>The hand's run (wrist to middle knuckle) and its palm's facing, unit length.</summary>
        public Vector3 Along, Palm;
        /// <summary>Degrees the four fingers (their mean) and the thumb are closed from straight.</summary>
        public float Curl, Thumb;
        /// <summary>Each finger's own closing (degrees); the mean where a table gives only that.</summary>
        public float CurlIndex, CurlMiddle, CurlRing, CurlLittle;
        /// <summary>The throwable is in this hand.</summary>
        public bool Held;

        /// <summary>A finger's closing: 0 index, 1 middle, 2 ring, 3 little.</summary>
        public float FingerCurl(int finger)
            => finger == 0 ? CurlIndex : finger == 1 ? CurlMiddle : finger == 2 ? CurlRing : CurlLittle;
    }

    /// <summary>
    /// A clip of the arm: its table, its length (s at its own speed: the last key's cycle), and its moments, the pin and
    /// the release (s; below 0: none), from its rows marked pin and release.
    /// </summary>
    public sealed class Clip
    {
        public string Table = "";
        public float Length, PinAt = -1f, ReleaseAt = -1f;
        public bool HasPin => PinAt >= 0f;
        public bool HasRelease => ReleaseAt >= 0f;
        internal readonly List<Key> Keys = new List<Key>();
        // Each key's fingers, in the keys' order; none when a row lacks the finger columns (a table from before 0.9.61).
        internal readonly List<FingerWays> Fingers = new List<FingerWays>();
        /// <summary>Whether the clip carries every finger joint (FingerWays), not only how far each finger closes.</summary>
        public bool HasFingers => Fingers.Count > 0 && Fingers.Count == Keys.Count;
    }

    /// <summary>
    /// A hand motion (a row of Tables/ThrowArms): its four clips by table, how fast the draw (held), the tap and the throw
    /// play (times their own speed), how long one clip blends into the next (Fade, s), whether its wrists and elbows are
    /// measured from the eyes in metres (EyeSpace: a viewmodel's framing) and the clips' own vertical field of view
    /// (ViewFov: the hand is spread out as much as a player's view is wider, so it sits where it sat on screen; 0: never).
    /// </summary>
    public sealed class Motion
    {
        public string Id = DefaultMotion;
        public string DrawTable = "GrenadeDraw", HoldTable = "GrenadeHold", ThrowTable = "GrenadeThrow", TapTable = "GrenadeTap";
        public float DrawSpeed = 1f, TapSpeed = 1f, ThrowSpeed = 1f, Fade = .08f;
        public bool EyeSpace = true;
        public float ViewFov = 58.72f;

        public Clip HoldClip => ClipOf(HoldTable);
        public Clip ThrowClip => ClipOf(ThrowTable);
        /// <summary>The tap's own clip; null when it has none (a tap then runs the draw at the tap's speed and throws).</summary>
        public Clip TapClip => string.IsNullOrEmpty(TapTable) ? null : ClipOf(TapTable);
        /// <summary>The tap is the throw itself (the knife's): it starts from the throw's beginning, not from where the draw got.</summary>
        public bool TapIsThrow => string.Equals(TapTable, ThrowTable, System.StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
    private static readonly Dictionary<string, Motion> motions = new Dictionary<string, Motion>();
    private static readonly Motion fallback = new Motion();
    private static bool motionsLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        clips.Clear();
        motions.Clear();
        motionsLoaded = false;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // A table saved while playing (a clip sent from Blender): read again at the next ask.
    private static void Forget()
    {
        clips.Clear();
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

    /// <summary>A clip by its table; null when the table has fewer than two keys.</summary>
    public static Clip ClipOf(string table)
    {
        if (string.IsNullOrEmpty(table)) return null;
        var clip = Load(table);
        return clip.Keys.Count > 1 ? clip : null;
    }

    /// <summary>A clip's arm at this time (s at its own speed), between the two keys round it, held at its ends. False when there is no clip.</summary>
    public static bool Sample(string table, float cycle, out Key key)
    {
        key = default;
        var clip = ClipOf(table);
        if (clip == null) return false;
        Span(clip, cycle, out int a, out int b, out float t);
        if (a == b) { key = clip.Keys[a]; return true; }
        key = Blend(clip.Keys[a], clip.Keys[b], t);
        key.Cycle = cycle;
        return true;
    }

    /// <summary>
    /// A clip's fingers at this time (s at its own speed), between the same two keys as Sample, into
    /// <paramref name="into"/>. False (into without ways) when the clip has none: a table from before 0.9.61, whose arm
    /// closes the fingers by the key's curls.
    /// </summary>
    public static bool SampleFingers(string table, float cycle, FingerWays into)
    {
        if (into == null) return false;
        into.Has = false;
        var clip = ClipOf(table);
        if (clip == null || !clip.HasFingers) return false;
        Span(clip, cycle, out int a, out int b, out float t);
        FingerWays.Blend(clip.Fingers[a], clip.Fingers[b], t, into);
        return into.Has;
    }

    // The two keys round a time and how far between them it is (one key twice, held, at the clip's ends).
    private static void Span(Clip clip, float cycle, out int a, out int b, out float t)
    {
        var keys = clip.Keys;
        int last = keys.Count - 1;
        t = 0f;
        if (cycle <= keys[0].Cycle) { a = b = 0; return; }
        if (cycle >= keys[last].Cycle) { a = b = last; return; }
        int next = 1;
        while (next < last && keys[next].Cycle < cycle) next++;
        a = next - 1;
        b = next;
        t = Mathf.InverseLerp(keys[a].Cycle, keys[b].Cycle, cycle);
    }

    /// <summary>
    /// Between two moments: the wrist and the elbow and the fingers in a straight blend, the hand turned the short way
    /// between its two facings (kept square to its run); in the hand from the one that has it to the one that hasn't, half
    /// way (the grab and the release are cuts).
    /// </summary>
    public static Key Blend(in Key a, in Key b, float t)
    {
        var key = new Key
        {
            Cycle = Mathf.Lerp(a.Cycle, b.Cycle, t),
            Wrist = Vector3.Lerp(a.Wrist, b.Wrist, t),
            Elbow = Vector3.Lerp(a.Elbow, b.Elbow, t),
            Curl = Mathf.Lerp(a.Curl, b.Curl, t),
            Thumb = Mathf.Lerp(a.Thumb, b.Thumb, t),
            CurlIndex = Mathf.Lerp(a.CurlIndex, b.CurlIndex, t),
            CurlMiddle = Mathf.Lerp(a.CurlMiddle, b.CurlMiddle, t),
            CurlRing = Mathf.Lerp(a.CurlRing, b.CurlRing, t),
            CurlLittle = Mathf.Lerp(a.CurlLittle, b.CurlLittle, t),
            Held = t < .5f ? a.Held : b.Held,
        };
        Quaternion turn = Quaternion.Slerp(Quaternion.LookRotation(a.Along, a.Palm), Quaternion.LookRotation(b.Along, b.Palm), t);
        key.Along = turn * Vector3.forward;
        key.Palm = turn * Vector3.up;
        return key;
    }

    private static Clip Load(string table)
    {
        if (clips.TryGetValue(table, out var clip)) return clip;
        clip = new Clip { Table = table };
        clips[table] = clip;
        float pin = -1f, release = -1f;
        // Each key with its fingers, sorted together; the clip keeps the fingers only if every key has them.
        var read = new List<(Key key, FingerWays fingers)>();
        bool everyFinger = true;
        foreach (string id in GameTables.RowIds(table))
        {
            if (!GameTables.TryGetRow(table, id, out var row)) continue;
            var key = new Key
            {
                Cycle = row.Number("cycle", -1f),
                Wrist = Vector(row, "wrist"),
                Elbow = Vector(row, "elbow"),
                Along = Vector(row, "along"),
                Palm = Vector(row, "palm"),
                Curl = row.Number("curl", 0f),
                Thumb = row.Number("thumb", 0f),
                Held = row.Number("held", 0f) > .5f,
            };
            if (key.Cycle < 0f || key.Along.sqrMagnitude < 1e-6f || key.Palm.sqrMagnitude < 1e-6f) continue;
            // A table from before each finger had its own (the shove workspace's loops, a modder's) closes all four alike.
            key.CurlIndex = row.Number("curlIndex", key.Curl);
            key.CurlMiddle = row.Number("curlMiddle", key.Curl);
            key.CurlRing = row.Number("curlRing", key.Curl);
            key.CurlLittle = row.Number("curlLittle", key.Curl);
            // Square the facing to the run, so a rounded cell never skews the hand.
            key.Along.Normalize();
            key.Palm = Vector3.ProjectOnPlane(key.Palm, key.Along);
            if (key.Palm.sqrMagnitude < 1e-6f) continue;
            key.Palm.Normalize();
            if (pin < 0f && row.Number("pin", 0f) > .5f) pin = key.Cycle;
            if (release < 0f && row.Number("release", 0f) > .5f) release = key.Cycle;
            var fingers = new FingerWays();
            if (!fingers.Read(row)) everyFinger = false;
            read.Add((key, fingers));
        }
        read.Sort((a, b) => a.key.Cycle.CompareTo(b.key.Cycle));
        foreach (var (key, fingers) in read)
        {
            clip.Keys.Add(key);
            if (everyFinger) clip.Fingers.Add(fingers);
        }
        clip.Length = clip.Keys.Count > 0 ? clip.Keys[clip.Keys.Count - 1].Cycle : 0f;
        clip.PinAt = pin;
        clip.ReleaseAt = release;
        return clip;
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
            motion.DrawTable = row.Text("draw", motion.DrawTable);
            motion.HoldTable = row.Text("hold", motion.HoldTable);
            motion.ThrowTable = row.Text("throw", motion.ThrowTable);
            // An empty tap cell is no tap clip of its own (the draw runs at the tap's speed into the throw).
            motion.TapTable = row.TryGetValue("tap", out string tap) ? tap.Trim() : motion.TapTable;
            motion.DrawSpeed = Mathf.Max(.05f, row.Number("drawSpeed", motion.DrawSpeed));
            motion.TapSpeed = Mathf.Max(.05f, row.Number("tapSpeed", motion.TapSpeed));
            motion.ThrowSpeed = Mathf.Max(.05f, row.Number("throwSpeed", motion.ThrowSpeed));
            motion.Fade = Mathf.Max(0f, row.Number("fade", motion.Fade));
            motion.EyeSpace = !row.TryGetValue("space", out string space) || string.IsNullOrWhiteSpace(space)
                || string.Equals(space.Trim(), "eye", System.StringComparison.OrdinalIgnoreCase);
            float fov = row.Number("viewFov", motion.ViewFov);
            motion.ViewFov = fov > 0f ? Mathf.Clamp(fov, 10f, 150f) : 0f;
            motions[motion.Id] = motion;
        }
    }

    private static Vector3 Vector(IReadOnlyDictionary<string, string> row, string name)
        => new Vector3(row.Number(name + "X", 0f), row.Number(name + "Y", 0f), row.Number(name + "Z", 0f));
}
