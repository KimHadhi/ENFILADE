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
// player was doing (speed, stance, view pitch, cover), and how far the gun is from where its owners put it: its hold,
// moved by what its action's track asks at that moment.
// The second record (0.9.119) measured that against the hold the body had learnt (FPSGun.HoldFromEyes), which is the gun
// as it stood on its last clean frame, whatever the player was doing then: learnt at a sprint, it put an MKR9 reloaded
// standing still 6 cm "off" all the way through. So the hold a frame is measured against is now the place the gun came to
// rest in right after that very action, in the same stance and with the same view, and only an action with no rest after
// it (cut by a swap) still goes by the learnt hold, marked with a ~. It also could not tell a gun that moved from a camera
// that did, since everything is from the eyes: each frame now says where the eyes are in the body, how far the
// crosshair's point is and how far ahead the muzzle is.
public sealed partial class FPSBody
{
    private const string TracePath = "Logs/gun-actions.txt";
    // An action is followed from this long before it began until this long after its clips have left the mix (s); a block
    // is closed after this long whatever plays (s); the file keeps this many of the last blocks.
    private const float TraceBefore = .3f, TraceAfter = .6f, TraceLongest = 30f;
    private const int TraceKept = 24;
    // Slower than this (m/s), in one stance, with the view's pitch within this (degrees): the player stood still.
    private const float TraceStillSpeed = .1f, TraceStillPitch = 3f;
    // Two frames further apart than this (s) are not one motion: no speed is worked out across them.
    private const float TraceGap = .1f;
    // A frame goes by the rest after its action only if the aim then is within this of the aim at the frame.
    private const float TraceSameAim = .05f;
    private static readonly List<string> traced = new List<string>();

    // One frame of the held gun, as numbers: a line is made of it only when its block is written.
    private struct TraceFrame
    {
        public float at;
        public FPSGun gun;
        // An action's clip is in the mix; else, a gun with no draw clip is coming out on FPSAF's equip motion.
        public bool acting, equipping;
        public GunTrackKind kind;
        // How far into the newest clip (s), the actions' share of the upper body, and how the track moves the gun off its
        // hold at that share (m, the gun's own frame).
        public float clipTime, share;
        public Vector3 move;
        // The gun (the IK weapon bone) from the eyes (cm), its facing from them, how fast it moved since the last frame
        // (cm/s), and how far it is from the learnt hold moved by the track (cm; below 0 before this gun has a hold).
        public Vector3 fromEyes;
        public Quaternion facing;
        public float speed, offLearnt;
        public float aim, converge, framework, carry, cover;
        // Where the eyes are in the body (cm, the player's own frame), how far ahead of them the crosshair's point is taken
        // (m) and the muzzle is (cm).
        public Vector3 eyesInBody;
        public float aimDistance, muzzleAhead;
        // The player: speed over the ground (m/s), stance, the view's pitch (degrees, down-positive).
        public float bodySpeed, pitch;
        public MovementState stance;
    }

    // The frames just before a block (a ring, newest at traceRingAt - 1), and the block being followed.
    private readonly TraceFrame[] traceRing = new TraceFrame[256];
    private int traceRingAt, traceRingCount;
    private readonly List<TraceFrame> traceBlock = new List<TraceFrame>();
    private bool tracing, traceHadLast;
    private float traceFrom, traceQuietFrom, traceLastAt;
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
            move = acting ? contact.GunMove * contactWeight : Vector3.zero,
            fromEyes = eyes.InverseTransformPoint(gun.position) * 100f,
            facing = Quaternion.Inverse(eyes.rotation) * gun.rotation,
            offLearnt = -1f,
            aim = aim,
            converge = convergeWeight,
            framework = Framework,
            carry = BodyCarries,
            cover = cover,
            eyesInBody = transform.InverseTransformPoint(eyes.position) * 100f,
            aimDistance = held.Converge != null ? held.Converge.distance : 0f,
            muzzleAhead = Vector3.Dot(gun.TransformPoint(held.MuzzlePosition) - eyes.position, eyes.forward) * 100f,
            bodySpeed = movement != null ? Vector3.ProjectOnPlane(movement.ActualVelocity, Vector3.up).magnitude : 0f,
            pitch = lastPitch,
            stance = movement != null ? movement.CurrentState : MovementState.Standing,
        };
        // Against the hold the body has learnt for this gun, for an action with no rest after it (WriteTrace).
        if (held.HoldFromEyes(aim, out KTransform hold))
            frame.offLearnt = Vector3.Distance(frame.fromEyes, (hold.position + hold.rotation * frame.move) * 100f);
        bool follows = traceHadLast && traceLastGun == held && frame.at - traceLastAt < TraceGap && Time.deltaTime > 0f;
        frame.speed = follows ? Vector3.Distance(frame.fromEyes, traceLast) / Time.deltaTime : 0f;
        traceLast = frame.fromEyes;
        traceLastGun = held;
        traceLastAt = frame.at;
        traceHadLast = true;
        return frame;
    }

    // For each frame an action had the gun, the frame the gun came to rest in right after that action: the first one of
    // the same gun with nothing in the mix. -1 for an action cut by a swap, by the block's end or by another aim.
    private int[] RestsAfter()
    {
        var rests = new int[traceBlock.Count];
        int rest = -1;
        for (int i = traceBlock.Count - 1; i >= 0; i--)
        {
            var frame = traceBlock[i];
            bool busy = frame.acting || frame.equipping;
            if (rest >= 0 && traceBlock[rest].gun != frame.gun) rest = -1;
            if (!busy) rest = i;
            rests[i] = busy && rest >= 0 && Mathf.Abs(traceBlock[rest].aim - frame.aim) <= TraceSameAim ? rest : -1;
        }
        return rests;
    }

    private void WriteTrace()
    {
        if (traceBlock.Count == 0) return;
        var rests = RestsAfter();
        var lines = new StringBuilder();
        var guns = new StringBuilder();
        FPSGun lastGun = null;
        // The first frame an action or a draw had the gun, the worst the gun was off its owners' place while one had it
        // (by the rest after it, and by the learnt hold where there was none), where its last action left it and how far it
        // went from there after, and what the player did meanwhile.
        int first = -1;
        float firstOff = -1f, worst = -1f, worstAt = 0f, worstLearnt = -1f, worstLearntAt = 0f, moveAfter = 0f, turnAfter = 0f;
        bool firstByRest = false;
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
            string off = "";
            if (busy)
            {
                float byRest = -1f;
                if (rests[i] >= 0)
                {
                    var rest = traceBlock[rests[i]];
                    byRest = Vector3.Distance(frame.fromEyes, rest.fromEyes + rest.facing * (frame.move * 100f));
                    if (byRest > worst) { worst = byRest; worstAt = frame.at - traceFrom; }
                    off = $", {byRest:0.0} cm off its rest and track";
                }
                else if (frame.offLearnt >= 0f)
                {
                    if (frame.offLearnt > worstLearnt) { worstLearnt = frame.offLearnt; worstLearntAt = frame.at - traceFrom; }
                    off = $", ~{frame.offLearnt:0.0} cm off its learnt hold and track";
                }
                if (first < 0)
                {
                    first = i;
                    firstByRest = byRest >= 0f;
                    firstOff = firstByRest ? byRest : frame.offLearnt;
                }
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
            lines.AppendLine($"   {frame.at - traceFrom:0.000}: {what}, share {frame.share:0.00}, track {frame.move.magnitude * 100f:0.0} cm; "
                + $"gun ({frame.fromEyes.x:0.0}, {frame.fromEyes.y:0.0}, {frame.fromEyes.z:0.0}) cm from the eyes, {frame.speed:0} cm/s{off}; "
                + $"aim {frame.aim:0.00}, on the point {frame.converge:0.00}, FPSAF {frame.framework:0.00}, body carries {frame.carry:0.00}, cover {frame.cover:0.00}; "
                + $"point {frame.aimDistance:0.00} m, muzzle {frame.muzzleAhead:0} cm ahead, eyes ({frame.eyesInBody.x:0.0}, {frame.eyesInBody.y:0.0}, {frame.eyesInBody.z:0.0}) in the body; "
                + $"body {frame.bodySpeed:0.0} m/s {frame.stance}, pitch {frame.pitch:0}°");
        }

        bool still = fastest < TraceStillSpeed && oneStance && pitchHigh - pitchLow < TraceStillPitch;
        var head = new StringBuilder();
        head.AppendLine($"== {System.DateTime.Now:HH:mm:ss} {(body != null && body.Skin != null ? body.Skin.Id : "?")} {guns}");
        if (first >= 0)
        {
            var began = traceBlock[first];
            string clip = began.acting
                ? $"{began.kind} {began.clipTime:0.000} s at share {began.share:0.00}, its track asks {began.move.magnitude * 100f:0.0} cm"
                : "no clip (FPSAF's equip motion)";
            string stood = firstOff < 0f ? "this gun has no hold yet"
                : firstByRest ? $"the gun stood {firstOff:0.0} cm from where its rest after the action and its track have it"
                : $"the gun stood ~{firstOff:0.0} cm from where its learnt hold and its track have it (no rest after this action)";
            head.AppendLine($"   first frame: {clip}; {stood}; FPSAF {began.framework:0.00}, body carries {began.carry:0.00}");
        }
        if (worst >= 0f) head.AppendLine($"   while an action had it the gun was at most {worst:0.0} cm from where its rest after that action and its track have it (at {worstAt:0.000} s)");
        if (worstLearnt >= 0f) head.AppendLine($"   in an action with no rest after it, at most ~{worstLearnt:0.0} cm from its learnt hold and track (at {worstLearntAt:0.000} s)");
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
                + "its speed, and how far that is from the place the gun came to rest in right after this action moved by the track (a gun "
                + "that follows its owners alone is off by its sway and by what the player did meanwhile; ~ and \"learnt hold\" for an "
                + "action with no rest after it, which goes by the hold the body last learnt, in whatever stance that was); the aim, the "
                + "turn onto the crosshair's point, FPSAF's share of the upper body (the hands on the gun), how much the body rather "
                + "than the view carries the gun, the wall's hold; how far ahead the crosshair's point is taken and the muzzle is, where "
                + "the eyes are in the body (a camera that jumps shows here, not in the gun); what the player did.\n\n"
                + string.Join("\n", traced));
        }
        catch (IOException) { }
    }
}
#endif
