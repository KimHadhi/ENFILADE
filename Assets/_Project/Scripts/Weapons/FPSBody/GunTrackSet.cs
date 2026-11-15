using System;
using UnityEngine;

/// <summary>What one track of a gun is the motion of.</summary>
public enum GunTrackKind
{
    /// <summary>The gun's pose clip: how it is held.</summary>
    Hold,
    /// <summary>Its pose with no grip on the handguard, where the gun has one (FPSBodyAssets.Gun.sourcePoseNoGrip).</summary>
    HoldNoGrip,
    Draw,
    Holster,
    Inspect,
    ReloadTactical,
    ReloadEmpty,
    /// <summary>One shell of a shell reload.</summary>
    ReloadShell,
    /// <summary>A shell reload's end.</summary>
    ReloadEnd,
    /// <summary>The action worked by hand after a shot: a bolt, a pump (FPSBodyAssets.Gun.sourceFire).</summary>
    Cycle
}

/// <summary>
/// One hand in one frame of a track: the middle of its palm, the way its fingers point and the way the palm faces, in the
/// gun's frame as the gun is in that frame; and the way its elbow points off the shoulder-to-wrist line, in the frame of the
/// gun as the pose clip holds it (zero where the arm was too straight to tell). As GunArms reads them. And how far the palm
/// is from the gun's surface (m, looked for no further than GunTrack.GapMost): a hand at the gun's surface has hold of the
/// gun wherever on it that is (its grip, the handguard, a magazine in its well, the bolt), one at a pouch has let go, and
/// one on its way between the two is taking hold or letting go (GunHold.Holds).
/// </summary>
[Serializable]
public struct GunTrackHand
{
    public Vector3 palm, fingers, facing, elbow;
    public float gap;
}

/// <summary>
/// One action of a gun as KINEMATION's animators made it, read once from their clip on their own arms and kept as numbers:
/// frame by frame, how far the clip has moved and turned the gun from where the gun's pose clip holds it, where both palms
/// are on the gun, how each finger joint points, and how the clip turns the camera. Everything is in the gun's own frame
/// (the gun model's: KINEMATION's weapon bone turned by the gun's rotationOffset), in metres, so one track fits every
/// body: where a body holds the gun is its hold's to say (GunHold), and its arms reach the palms' places by IK.
/// The frames are the clip's own (frame i at i / frameRate seconds, the last at the clip's end), not a resampling.
/// </summary>
[Serializable]
public sealed class GunTrack
{
    public GunTrackKind kind;
    [Tooltip("KINEMATION's clip it was read from, by name, for the reports.")]
    public string clip;
    [Tooltip("The clip's length (s) and its frames a second.")]
    public float length;
    public float frameRate;
    [Tooltip("The gun from where the pose clip holds it, in that pose's gun frame: its move (m) and its turn.")]
    public Vector3[] gunMove = Array.Empty<Vector3>();
    public Quaternion[] gunTurn = Array.Empty<Quaternion>();
    public GunTrackHand[] right = Array.Empty<GunTrackHand>();
    public GunTrackHand[] left = Array.Empty<GunTrackHand>();
    [Tooltip("Each finger joint's way in its own hand's frame (GunArms.Digits), GunArms.DigitCount to a frame. Empty where the arms have no finger joints to read.")]
    public Vector3[] rightDigits = Array.Empty<Vector3>();
    public Vector3[] leftDigits = Array.Empty<Vector3>();
    [Tooltip("How the clip turns KINEMATION's camera bone from where the pose clip holds it. Identity throughout where the clip has no camera bone.")]
    public Quaternion[] camera = Array.Empty<Quaternion>();

    /// <summary>A palm's gap from the gun's surface is looked for no further than this (m); further off it is kept as this.
    /// No rule asks about a hand further from the gun: one that far has let go of it (GunHold.Rules.TakesHoldWithin).</summary>
    public const float GapMost = .3f;

    /// <summary>How many frames it has; 0 for a track that was never read.</summary>
    public int Frames => gunMove != null ? gunMove.Length : 0;

    /// <summary>When frame <paramref name="frame"/> is (s into the clip).</summary>
    public float TimeOf(int frame) => frameRate > 0f ? Mathf.Min(frame / frameRate, length) : 0f;

    /// <summary>
    /// The moment <paramref name="time"/> seconds into the clip, between its frames: the gun's move and turn, both hands, and
    /// the finger ways into the buffers given (each GunArms.DigitCount long; the moment's arrays are those buffers, or null
    /// where the track has no fingers or no buffer was given). False for an empty track.
    /// </summary>
    internal bool Sample(float time, Vector3[] rightBuffer, Vector3[] leftBuffer, out GunContact moment, out Quaternion cameraTurn)
    {
        moment = default;
        cameraTurn = Quaternion.identity;
        int frames = Frames;
        if (frames == 0 || gunTurn.Length != frames || right.Length != frames || left.Length != frames) return false;
        Between(time, out int a, out int b, out float t);
        moment.GunMove = Vector3.Lerp(gunMove[a], gunMove[b], t);
        moment.GunTurn = Quaternion.Slerp(gunTurn[a], gunTurn[b], t);
        Mix(right[a], right[b], t, out moment.RightPalm, out moment.RightFingers, out moment.RightFacing, out moment.RightElbow);
        Mix(left[a], left[b], t, out moment.LeftPalm, out moment.LeftFingers, out moment.LeftFacing, out moment.LeftElbow);
        moment.RightGap = Mathf.Lerp(right[a].gap, right[b].gap, t);
        moment.LeftGap = Mathf.Lerp(left[a].gap, left[b].gap, t);
        moment.RightDigits = Ways(rightDigits, frames, a, b, t, rightBuffer);
        moment.LeftDigits = Ways(leftDigits, frames, a, b, t, leftBuffer);
        if (camera != null && camera.Length == frames) cameraTurn = Quaternion.Slerp(camera[a], camera[b], t);
        return true;
    }

    // The two frames a time lies between and how far from the first to the second (0-1). The last frame is at the clip's
    // end, which can be less than a whole frame after the one before it.
    private void Between(float time, out int a, out int b, out float t)
    {
        int last = Frames - 1;
        a = b = 0;
        t = 0f;
        if (last <= 0 || frameRate <= 0f) return;
        time = Mathf.Clamp(time, 0f, length);
        a = Mathf.Clamp((int)(time * frameRate), 0, last);
        b = Mathf.Min(a + 1, last);
        float from = TimeOf(a), to = TimeOf(b);
        t = to > from ? Mathf.Clamp01((time - from) / (to - from)) : 0f;
    }

    private static void Mix(in GunTrackHand a, in GunTrackHand b, float t, out Vector3 palm, out Vector3 fingers, out Vector3 facing, out Vector3 elbow)
    {
        palm = Vector3.Lerp(a.palm, b.palm, t);
        fingers = Vector3.Slerp(a.fingers, b.fingers, t);
        facing = Vector3.Slerp(a.facing, b.facing, t);
        // An elbow that couldn't be told in one of the two frames takes the other's.
        bool hasA = a.elbow.sqrMagnitude > .5f, hasB = b.elbow.sqrMagnitude > .5f;
        elbow = hasA && hasB ? Vector3.Slerp(a.elbow, b.elbow, t) : hasA ? a.elbow : hasB ? b.elbow : Vector3.zero;
    }

    private static Vector3[] Ways(Vector3[] all, int frames, int a, int b, float t, Vector3[] buffer)
    {
        const int count = GunArms.DigitCount;
        if (buffer == null || buffer.Length < count || all == null || all.Length != frames * count) return null;
        int fromA = a * count, fromB = b * count;
        for (int i = 0; i < count; i++) buffer[i] = Vector3.Slerp(all[fromA + i], all[fromB + i], t);
        return buffer;
    }
}

/// <summary>
/// Every action of one gun as numbers (GunTrack): made in the editor from KINEMATION's own clips (GunTrackBake), read by the
/// game. The game never samples KINEMATION's clips itself: what a clip does to the gun and where it has the hands is this
/// asset's to say, the same on every PC, in the editor and in a build.
/// Kept as binary: it is rows of sampled numbers, nothing to read or merge as text.
/// </summary>
[PreferBinarySerialization]
public sealed class GunTrackSet : ScriptableObject
{
    public WeaponData weapon;
    [Tooltip("Where the gun's pose clip holds the gun on KINEMATION's arms, in the arms' own space: what every track's move is from.")]
    public Vector3 posePosition;
    public Quaternion poseRotation = Quaternion.identity;
    [Tooltip("The pose clip holds the gun in both hands (the support palm is within reach of the gun): its clips can be followed.")]
    public bool holds;
    public GunTrack[] tracks = Array.Empty<GunTrack>();

    /// <summary>The gun's track of that kind; null when the gun has no such action.</summary>
    public GunTrack Track(GunTrackKind kind)
    {
        foreach (var track in tracks)
            if (track != null && track.kind == kind && track.Frames > 0) return track;
        return null;
    }
}
