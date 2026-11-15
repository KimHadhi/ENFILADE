#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// FPSBody: In the editor, what the gun did through each action of its own, as numbers (Logs/gun-actions.txt). One part of
// the class; the rest is in FPSBody.cs and the files beside it.
// Whether a drawn gun stands and then slides home was only ever seen by playing and looking (the user, 2026-11-15). This
// writes where the gun was from the eyes at every frame of an action and for a moment after it, so one play of a draw is
// enough for whoever changed the gun's place to read what it did.
public sealed partial class FPSBody
{
    private const string TracePath = "Logs/gun-actions.txt";
    // An action is followed this long after its clips have left the mix (s), and the file keeps this many of the last ones.
    private const float TraceAfter = .6f;
    private const int TraceKept = 24;
    private static readonly List<string> traced = new List<string>();

    private readonly StringBuilder trace = new StringBuilder();
    private bool tracing, traceHadLast;
    private float traceFrom, traceQuietFrom, traceMoveAfter, traceTurnAfter;
    private string traceOf;
    private Vector3 traceLast, traceAtEnd;
    private Quaternion traceTurnAtEnd;
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
        bool playing = hasContact && contactWeight > 0f;
        if (!tracing)
        {
            if (!playing) return;
            tracing = true;
            traceHadLast = false;
            traceFrom = Time.time;
            traceQuietFrom = -1f;
            traceMoveAfter = traceTurnAfter = 0f;
            traceOf = $"{System.DateTime.Now:HH:mm:ss} {(body != null && body.Skin != null ? body.Skin.Id : "?")} {held.name}";
            trace.Clear();
        }
        var gun = rig.IkWeaponBone;
        Vector3 at = eyes.InverseTransformPoint(gun.position) * 100f;
        Quaternion turned = Quaternion.Inverse(eyes.rotation) * gun.rotation;
        float speed = traceHadLast && Time.deltaTime > 0f ? Vector3.Distance(at, traceLast) / Time.deltaTime : 0f;
        traceLast = at;
        traceHadLast = true;
        if (playing)
        {
            traceQuietFrom = -1f;
            traceAtEnd = at;
            traceTurnAtEnd = turned;
        }
        else
        {
            if (traceQuietFrom < 0f) traceQuietFrom = Time.time;
            traceMoveAfter = Mathf.Max(traceMoveAfter, Vector3.Distance(at, traceAtEnd));
            traceTurnAfter = Mathf.Max(traceTurnAfter, Quaternion.Angle(turned, traceTurnAtEnd));
        }
        trace.AppendLine($"   {Time.time - traceFrom:0.000}: {(playing ? tracedKind.ToString() : "-")} {tracedTime:0.000} s, share {contactWeight:0.00}, "
            + $"move {contact.GunMove.magnitude * 100f:0.0} cm; gun ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}) cm from the eyes, {speed:0} cm/s; "
            + $"aim {controller.AimBlend:0.00}, turn onto the point {convergeWeight:0.00}");
        if (playing || Time.time - traceQuietFrom < TraceAfter) return;
        tracing = false;
        traced.Add($"== {traceOf}: in the {TraceAfter:0.0} s after its clips left the mix the gun went at most {traceMoveAfter:0.0} cm and "
            + $"{traceTurnAfter:0.0}° from where the action left it (from the eyes; sway and breathing are in it)\n" + trace);
        if (traced.Count > TraceKept) traced.RemoveRange(0, traced.Count - TraceKept);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TracePath));
            File.WriteAllText(TracePath, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} the held gun through its own actions, newest last (the last "
                + $"{TraceKept}). Each frame: seconds since the action began, the newest clip in the mix and how far into it, the "
                + "actions' share of the upper body, how far the track has the gun from its hold, where the gun is from the eyes "
                + "after everything.\n\n" + string.Join("\n", traced));
        }
        catch (IOException) { }
    }
}
#endif
