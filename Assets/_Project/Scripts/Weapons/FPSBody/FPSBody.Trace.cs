#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

// FPSBody: In the editor, what the gun did through each action of its own, as numbers (Logs/gun-actions.txt). One part of
// the class; the rest is in FPSBody.cs and the files beside it.
// Whether a drawn gun stands and then slides home was only ever seen by playing and looking (the user, 2026-11-15). This
// writes where the gun was from the eyes at every frame of an action, for a moment before it and after it, so one play is
// enough for whoever changed the gun's place to read what it did.
// The first record (0.9.118) could say that the gun moved but not why: a gun 3.8 cm lower after a reload was the player
// crouching or the gun's place going wrong, and the file had nothing to tell them apart. So each frame also says what the
// player was doing (speed, stance, view pitch, cover), and how far the gun is from where its owners put it: this gun's
// hold from the eyes, moved by what its action's track asks at that moment. With the player standing still that
// distance is the gun's sway alone; a gun sliding in from somewhere else shows in it at once.
public sealed partial class FPSBody
{
    private const string TracePath = "Logs/gun-actions.txt";
    // An action is followed from this long before it began until this long after its clips have left the mix (s); a block
    // is closed after this long whatever plays (s); the file keeps this many of the last blocks.
    private const float TraceBefore = .3f, TraceAfter = .6f, TraceLongest = 30f;
    private const int TraceKept = 24;
    // Slower than this (m/s), in one stance, with the view's pitch within this (degrees): the player stood still.
    private const float TraceStillSpeed = .1f, TraceStillPitch = 1f;
    private static readonly List<string> traced = new List<string>();

    // One frame of the held gun, as numbers: a line is made of it only when its block is written.
    private struct TraceFrame
    {
        public float at;
        public FPSGun gun;
        // An action's clip is in the mix; else, a gun with no draw clip is coming out on FPSAF's equip motion.
        public bool acting, equipping;
        public GunTrackKind kind;
        // How far into the newest clip (s), the actions' share of the upper body, how far the track moves the gun off its
        // hold at that share (cm).
        public float clipTime, share, asked;
        // The gun (the IK weapon bone) from the eyes (cm), its facing from them, how fast it moved since the last frame
        // (cm/s), and how far it is from its hold moved by the track (cm; below 0 before this gun has a hold).
        public Vector3 fromEyes;
        public Quaternion facing;
        public float speed, off;
        public float aim, converge, framework, cover;
        // The player: speed over the ground (m/s), stance, the view's pitch (degrees, down-positive).
        public float bodySpeed, pitch;
        public MovementState stance;
    }

    // The frames just before a block (a ring, newest at traceRingAt - 1), and the block being followed.
    private readonly TraceFrame[] traceRing = new TraceFrame[256];
    private int traceRingAt, traceRingCount;
    private readonly List<TraceFrame> traceBlock = new List<TraceFrame>();
    private bool tracing, traceHadLast;
    private float traceFrom, traceQuietFrom;
    private Vector3 traceLast;
    private FPSGun traceLastGun;
    // The newest clip in the mix this frame: its action and how far into it.
    private GunTrackKind tracedKind;
    private float tracedTime;

    partial void TraceClip(GunTrackKind kind, float time)
    {
        tracedKind = kind;
        tracedTime = time;
    }

    // After the animation and everything the body puts on the gun after it (LateUpdate's end).
    partial void TraceAction()
    {
        // Only this PC's own player: a friend's copy would write the same file.
        if (movement != null && (!movement.IsLocal || movement.IsNetworkDriven)) return;
        if (rig == null || eyes == null || held == null) return;
        var frame = TraceNow();
        bool busy = frame.acting || frame.equipping;
        // No gun was held for a while (put away, or the body made anew): the block that was open is over.
        if (tracing && traceBlock.Count > 0 && frame.at - traceBlock[traceBlock.Count - 1].at > TraceAfter)
        {
            tracing = false;
            WriteTrace();
        }
        if (!tracing)
        {
            if (!busy)
            {
                traceRing[traceRingAt] = frame;
                traceRingAt = (traceRingAt + 1) % traceRing.Length;
                traceRingCount = Mathf.Min(traceRingCount + 1, traceRing.Length);
                return;
            }
            tracing = true;
            traceFrom = frame.at;
            traceQuietFrom = -1f;
            traceBlock.Clear();
            // What the gun in the hands was doing just before, oldest first.
            for (int i = traceRingCount; i >= 1; i--)
            {
                var before = traceRing[(traceRingAt - i + traceRing.Length) % traceRing.Length];
                if (frame.at - before.at <= TraceBefore) traceBlock.Add(before);
            }
            traceRingCount = 0;
        }
        traceBlock.Add(frame);
        if (busy) traceQuietFrom = -1f;
        else if (traceQuietFrom < 0f) traceQuietFrom = frame.at;
        bool over = !busy && frame.at - traceQuietFrom >= TraceAfter;
        if (!over && frame.at - traceFrom < TraceLongest) return;
        tracing = false;
        WriteTrace();
    }

    private TraceFrame TraceNow()
    {
        var gun = rig.IkWeaponBone;
        float aim = controller.AimBlend;
        bool acting = hasContact && contactWeight > 0f;
        var frame = new TraceFrame
        {
            at = Time.time,
            gun = held,
            acting = acting,
            equipping = !acting && !held.HasDraw && Time.time - drawnAt < drawSettle,
            kind = tracedKind,
            clipTime = tracedTime,
            share = acting ? contactWeight : 0f,
            asked = acting ? contact.GunMove.magnitude * contactWeight * 100f : 0f,
            fromEyes = eyes.InverseTransformPoint(gun.position) * 100f,
            facing = Quaternion.Inverse(eyes.rotation) * gun.rotation,
            off = -1f,
            aim = aim,
            converge = convergeWeight,
            framework = Framework,
            cover = cover,
            bodySpeed = movement != null ? Vector3.ProjectOnPlane(movement.ActualVelocity, Vector3.up).magnitude : 0f,
            pitch = lastPitch,
            stance = movement != null ? movement.CurrentState : MovementState.Standing,
        };
        // Where the gun's owners have it: its hold from the eyes, and on it what its action's track asks now, in the
        // hold's own frame (GunActionLayer moves the gun so).
        if (held.HoldFromEyes(aim, out KTransform hold))
        {
            Vector3 owned = hold.position + (acting ? hold.rotation * (contact.GunMove * contactWeight) : Vector3.zero);
            frame.off = Vector3.Distance(frame.fromEyes, owned * 100f);
        }
        frame.speed = traceHadLast && traceLastGun == held && Time.deltaTime > 0f ? Vector3.Distance(frame.fromEyes, traceLast) / Time.deltaTime : 0f;
        traceLast = frame.fromEyes;
        traceLastGun = held;
        traceHadLast = true;
        return frame;
    }

    private void WriteTrace()
    {
        if (traceBlock.Count == 0) return;
        var lines = new StringBuilder();
        var guns = new StringBuilder();
        FPSGun lastGun = null;
        // The first frame an action or a draw had the gun, the worst the gun was off its owners' place while one had it,
        // where its last action left it and how far it went from there after, and what the player did meanwhile.
        int first = -1;
        float worst = -1f, worstAt = 0f, moveAfter = 0f, turnAfter = 0f;
        float slowest = float.MaxValue, fastest = 0f, pitchLow = float.MaxValue, pitchHigh = float.MinValue;
        bool hasEnd = false, oneStance = true;
        Vector3 endAt = default;
        Quaternion endFacing = Quaternion.identity;
        var stances = new StringBuilder();
        MovementState lastStance = default;
        for (int i = 0; i < traceBlock.Count; i++)
        {
            var frame = traceBlock[i];
            bool busy = frame.acting || frame.equipping;
            if (frame.gun != lastGun)
            {
                string name = frame.gun != null ? frame.gun.name : "?";
                if (lastGun != null) lines.AppendLine($"   -- {name} in the hands from here");
                guns.Append(guns.Length > 0 ? " → " : "").Append(name);
                lastGun = frame.gun;
                // Another gun: what the last one's action left has nothing to say about this one.
                hasEnd = false;
            }
            if (busy)
            {
                if (first < 0) first = i;
                if (frame.off > worst) { worst = frame.off; worstAt = frame.at - traceFrom; }
                hasEnd = true;
                endAt = frame.fromEyes;
                endFacing = frame.facing;
            }
            else if (hasEnd)
            {
                moveAfter = Mathf.Max(moveAfter, Vector3.Distance(frame.fromEyes, endAt));
                turnAfter = Mathf.Max(turnAfter, Quaternion.Angle(frame.facing, endFacing));
            }
            slowest = Mathf.Min(slowest, frame.bodySpeed);
            fastest = Mathf.Max(fastest, frame.bodySpeed);
            pitchLow = Mathf.Min(pitchLow, frame.pitch);
            pitchHigh = Mathf.Max(pitchHigh, frame.pitch);
            if (i == 0 || frame.stance != lastStance)
            {
                if (i > 0) oneStance = false;
                stances.Append(stances.Length > 0 ? " → " : "").Append(frame.stance);
                lastStance = frame.stance;
            }
            string what = frame.acting ? $"{frame.kind} {frame.clipTime:0.000} s" : frame.equipping ? "Equip" : "-";
            string off = frame.off >= 0f ? $"{frame.off:0.0} cm off its hold and track" : "no hold yet";
            lines.AppendLine($"   {frame.at - traceFrom:0.000}: {what}, share {frame.share:0.00}, track {frame.asked:0.0} cm; "
                + $"gun ({frame.fromEyes.x:0.0}, {frame.fromEyes.y:0.0}, {frame.fromEyes.z:0.0}) cm from the eyes, {frame.speed:0} cm/s, {off}; "
                + $"aim {frame.aim:0.00}, on the point {frame.converge:0.00}, FPSAF {frame.framework:0.00}, cover {frame.cover:0.00}; "
                + $"body {frame.bodySpeed:0.0} m/s {frame.stance}, pitch {frame.pitch:0}°");
        }

        bool still = fastest < TraceStillSpeed && oneStance && pitchHigh - pitchLow < TraceStillPitch;
        var head = new StringBuilder();
        head.AppendLine($"== {System.DateTime.Now:HH:mm:ss} {(body != null && body.Skin != null ? body.Skin.Id : "?")} {guns}");
        if (first >= 0)
        {
            var began = traceBlock[first];
            string clip = began.acting
                ? $"{began.kind} {began.clipTime:0.000} s at share {began.share:0.00}, its track asks {began.asked:0.0} cm"
                : "no clip (FPSAF's equip motion)";
            string stood = began.off >= 0f
                ? $"the gun stood {began.off:0.0} cm from where its hold and its track have it"
                : "this gun has no hold yet";
            head.AppendLine($"   first frame: {clip}; {stood}");
        }
        if (worst >= 0f) head.AppendLine($"   while an action had it the gun was at most {worst:0.0} cm from where its hold and its track have it (at {worstAt:0.000} s)");
        head.AppendLine($"   in the quiet after an action (up to {TraceAfter:0.0} s) it went at most {moveAfter:0.0} cm and {turnAfter:0.0}° from where the action left it");
        head.AppendLine($"   the player {(still ? "stood still" : "moved")}: {slowest:0.0} to {fastest:0.0} m/s, {stances}, view pitch {pitchLow:0} to {pitchHigh:0}°"
            + (still ? "" : " (the gun's sway, bob and the stance's own place are in the numbers)"));

        traced.Add(head.ToString() + lines);
        if (traced.Count > TraceKept) traced.RemoveRange(0, traced.Count - TraceKept);
        traceBlock.Clear();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TracePath));
            File.WriteAllText(TracePath, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} the held gun through its own actions, newest last (the last "
                + $"{TraceKept}). Each block is one run of actions, from {TraceBefore:0.0} s before it to {TraceAfter:0.0} s after. Each frame: "
                + "seconds since the first action began (below 0: before it), the newest clip in the mix and how far into it, the actions' "
                + "share of the upper body, how far their track moves the gun off its hold; where the gun is from the eyes after everything, "
                + "its speed, and how far that is from this gun's hold from the eyes moved by the track (a gun that follows its owners alone "
                + "is off by its sway only; until a gun has been held clean once on this body its hold is the computed one, a little "
                + "ahead); the aim, the turn onto the crosshair's point, FPSAF's share of the upper body, the wall's hold; what the player "
                + "did.\n\n" + string.Join("\n", traced));
        }
        catch (IOException) { }
    }
}
#endif
