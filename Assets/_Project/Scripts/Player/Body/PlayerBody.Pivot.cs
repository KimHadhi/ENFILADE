using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Pivots (0.9.442): the legs planting a foot and pushing off the other way as the drawn body turns back on itself, A to D
/// and back. The capsule turns back at once (the keys have it: 0.9.435), the body drawn over it follows as legs can
/// (PlayerBody.Slack), and the eight ways' cycles mixed across that turn dragged the feet on the ground from one cycle's
/// footing to the other's (the legs' test of 2026-11-25: a reversal had a planted sole faster than 3 m/s in 42% of its
/// frames). A pivot plays on the hips, the spine and the legs (the Root, Body and both legs' parts), right over the moves;
/// the arms, the head and the gun carry on as they were. The hips' turn is the Body part's: with the Root and the legs alone
/// the pivot's legs hung from the hips the cycles under it turned, and its planted foot slid 10 cm as the body pushed off
/// (the pivot trace of 2026-11-25 against the clip bench, which poses the clip alone).
/// - Every clip in the profile named "enf_pivot_..." is one (made in the Blender pivot workspace, ArtSource/Pivots). Nothing is
///   listed: each is measured on this body the first time it could play (ClipMotion: its travel as root motion, the import
///   dropping it from the pose, inPlace 1): which way it comes in and goes out (off the hips: x right, y ahead), how fast,
///   when it stops between, and its travel's speed at every moment.
/// - It begins the frame the keys ask the pivot's way out (within PivotWithin degrees; PlayerMovement.StrideAsked, which turns
///   with the keys, where the stride crosses none 0.07 s later) after asking its way in for PivotKeysHeld at least, the hips
///   not turning round (PivotTurnMost), while the drawn body still goes its way in at a speed it comes in at (PivotSpeeds),
///   near the capsule (PivotWarp.x of the bound), the slack open (SlackShare): seen from outside
///   (a friend's copy, F5). The player's own body seen from its eyes has no slack (0.9.437) and keeps the cycles. It begins
///   where the clip goes its way in as fast as the drawn body does, before its stop.
/// - From then on the clip's own travel carries the drawn body (PlayerBody.Slack.FollowCapsule), at the clip's own pace, so
///   its planted foot stays where it planted it: no guess of how the body will slow (until it carried the body, the clip was
///   timed to the slack's slowing, read off the body, and stood near its stop a tenth of a second early or late). As the
///   slack it leaves nears the bound, the clip goes faster or slower toward the capsule's pace (PivotWarp), its feet with it.
/// - It ends where the clip goes on at most of its way-out speed (PivotOut), handing the stride to the cycles from the
///   clip's own footing (StepAt), or at once when the keys let go or turn away from the way out, the slack it leaves reaches
///   the bound (PivotGapMost), the body leaves its feet or crouches, or anything takes the legs. It blends in and out over
///   pivotBlend, and the switches of the cycles under it never ease the legs over it (BodyInertia).
/// The other PCs' copies pivot the same way from what they see.
/// </summary>
public sealed partial class PlayerBody
{
    // The clips that are pivots, by their names' start: the pivot workspace bakes each from its action enf_pivot_<key>
    // (Tools/Blender/Pivots), into Art/Animations/Pivots/Pivot_<key>.fbx, and the clip inside keeps the action's name.
    private const string PivotPrefix = "enf_pivot_";
    // How far the drawn body's way and the keys' may be off a pivot's way in and out (degrees); the share of its way-out
    // speed where it hands back; the share of the bound the slack it leaves may reach.
    private const float PivotWithin = 35f, PivotOut = .85f, PivotGapMost = .95f;
    // The drawn body's speed its way in, against the pivot's own, for it to begin.
    private static readonly Vector2 PivotSpeeds = new Vector2(.35f, 1.6f);
    // How fast the drawn body's speed comes onto the clip's (m/s²): above what a pivot's own travel changes by (about 27 in
    // the jog's), so it is the clip's after the first frames, and a body come in a little aside of the clip's way doesn't
    // lose that in a frame.
    private const float PivotCatch = 60f;
    // How long the keys must have asked a pivot's way in before they turn to its way out (s): a run that way, not keys
    // turned faster than a pivot takes to plant and push off (a quarter of a second), which a pivot cut off at each key
    // only jerked about (the legs' test of 2026-11-25: A and D every 0.2 s slid more with pivots than without).
    private const float PivotKeysHeld = .25f;
    // Past PivotWarp.x of the slack's bound the clip goes faster or slower (its feet with it, so they stay planted) toward
    // the pace that brings the body back onto the capsule, all of that by PivotWarp.y; never slower or faster than
    // PivotRates. Keys turned faster than a pivot's own pace (A and D every 0.2 s: the capsule never got to a jog's speed
    // between) took the body to the bound in a few frames, and the pivot ended there with its foot half planted.
    private static readonly Vector2 PivotWarp = new Vector2(.4f, .85f), PivotRates = new Vector2(.4f, 2f);
    // A pivot's speed against the drawn body's counts for this many degrees off its ways a doubling (choosing among those
    // that fit: a walk's pivot for a walk, a jog's for a jog).
    private const float PivotSpeedOff = 30f;
    // Turning faster than this (degrees a second) the hips have no one way for a pivot to plant across: a view flicked round
    // brings them round after it (FacingTurnMost), and the keys held swing round them as it does, which read as keys turned
    // back (the legs' test of 2026-11-25: a jog flicked 180 or 90 degrees played pivots and slid twice as much).
    private const float PivotTurnMost = 90f;
    // Samples a second of a pivot's measured travel.
    private const float PivotSampleRate = 60f;

    [SerializeField, Tooltip("How long a pivot takes the hips and legs over the cycles, and gives them back (s).")]
    private Vector2 pivotBlend = new Vector2(.06f, .12f);

    private sealed class Pivot
    {
        public string Clip;
        public AnimationClipPlayable Playable;
        public int Input;
        public bool Measured, Usable;
        public ClipMotion Motion;
        // Its way in and out (unit, the body's frame: x right, y ahead), its speeds (m/s), its length, its stop and its end (s).
        public Vector2 WayIn, WayOut;
        public float SpeedIn, SpeedOut, Length, Stop, End;
        // Its travel's speed per sample (the body's frame, m/s), Step apart (s).
        public Vector2[] Speed;
        public float Step;
        // Its moment (s) and its share of the pivots shown: one that ended plays on from where it was as it goes out, so its
        // feet go on as the body does instead of standing still under it.
        public float Time, Weight;
    }

    private readonly List<Pivot> pivots = new List<Pivot>();
    private AnimationMixerPlayable pivotMixer;
    private AvatarMask pivotMask;
    // The pivot playing (null for none), the frame it was last kept, and the layer's weight.
    private Pivot pivotOn;
    private float pivotWeight;
    private int pivotFrame = -10;
    // The way the keys ask (the world's, level, unit; none before any) and how long they have, and the same of the way before.
    private Vector2 pivotKeysWay, pivotKeysWayBefore;
    private float pivotKeysFor, pivotKeysForBefore;

    /// <summary>A pivot shows on the legs this frame (MoveTest counts it).</summary>
    internal bool Pivoting => pivotOn != null && pivotWeight > .5f;
    /// <summary>The pivot playing, by its clip's name (MoveTest tells which); null for none.</summary>
    internal string PivotPlaying => pivotOn?.Clip;

    // From BuildLayers: the layer of the hips, the spine and the legs, and a playable for each pivot clip, held where it is set.
    private void BuildPivots()
    {
        pivotMask = Mask(AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        layers.SetLayerMaskFromAvatarMask(PivotLayer, pivotMask);
        pivotMixer = AnimationMixerPlayable.Create(graph, 0);
        graph.Connect(pivotMixer, 0, layers, PivotLayer);
        layers.SetInputWeight(PivotLayer, 0f);
        foreach (var clip in profile.Named(PivotPrefix))
        {
            var playable = ClipPlayable(clip);
            playable.SetSpeed(0);
            int input = pivotMixer.GetInputCount();
            pivotMixer.SetInputCount(input + 1);
            graph.Connect(playable, 0, pivotMixer, input);
            pivots.Add(new Pivot { Clip = clip.name, Playable = playable, Input = input, Length = Mathf.Max(.01f, clip.length) });
        }
    }

    private void TeardownPivots()
    {
        pivots.Clear();
        pivotOn = null;
        pivotWeight = 0f;
        if (pivotMask != null) Destroy(pivotMask);
    }

    // A pivot measured on this body: its way in and out, its speeds, its stop, its end and its travel's speed throughout.
    // False for one that doesn't turn back on itself (or a body that can't read it yet: asked again).
    private bool Measure(Pivot p)
    {
        if (p.Measured) return p.Usable;
        p.Motion = MotionOf(p.Clip);
        if (p.Motion == null) return false;
        p.Measured = true;
        int count = Mathf.Max(8, Mathf.CeilToInt(p.Length * PivotSampleRate));
        p.Step = p.Length / count;
        var way = new Vector2[count + 1];
        for (int i = 0; i <= count; i++) way[i] = p.Motion.WayAt(i * p.Step);
        p.Speed = new Vector2[count + 1];
        for (int i = 0; i <= count; i++)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(count, i + 1);
            p.Speed[i] = (way[b] - way[a]) / ((b - a) * p.Step);
        }
        // Its way in over its first tenth of a second, its way out over its last tenth and a half.
        int first = Mathf.Max(1, Mathf.RoundToInt(.1f / p.Step)), last = Mathf.Max(1, Mathf.RoundToInt(.15f / p.Step));
        Vector2 inV = (way[first] - way[0]) / (first * p.Step), outV = (way[count] - way[count - last]) / (last * p.Step);
        p.SpeedIn = inV.magnitude;
        p.SpeedOut = outV.magnitude;
        if (p.SpeedIn < .3f || p.SpeedOut < .3f || Vector2.Angle(inV, outV) < 60f) return p.Usable = false;
        p.WayIn = inV / p.SpeedIn;
        p.WayOut = outV / p.SpeedOut;
        // Its stop: where its pace its way in first falls to none, or (a cut aside, whose way in only comes to none as it
        // goes off the other way) where that pace is least, past its first fifth.
        p.Stop = -1f;
        int least = count / 5;
        for (int i = 1; i <= count && p.Stop < 0f; i++)
        {
            float before = Vector2.Dot(p.Speed[i - 1], p.WayIn), now = Vector2.Dot(p.Speed[i], p.WayIn);
            if (before > 0f && now <= 0f) p.Stop = (i - 1 + before / Mathf.Max(1e-6f, before - now)) * p.Step;
            if (i >= count / 5 && now < Vector2.Dot(p.Speed[least], p.WayIn)) least = i;
        }
        if (p.Stop < 0f) p.Stop = least * p.Step;
        if (p.Stop <= 0f) return p.Usable = false;
        // Its end: after its stop, where it goes on its way out at PivotOut of its speed out.
        p.End = p.Length;
        for (int i = Mathf.CeilToInt(p.Stop / p.Step); i <= count; i++)
            if (Vector2.Dot(p.Speed[i], p.WayOut) >= PivotOut * p.SpeedOut) { p.End = i * p.Step; break; }
        return p.Usable = true;
    }

    // The clip's travel speed at a moment (the body's frame, m/s).
    private static Vector2 PivotSpeedAt(Pivot p, float time)
    {
        float at = Mathf.Clamp(time / p.Step, 0f, p.Speed.Length - 1);
        int i = Mathf.Min(Mathf.FloorToInt(at), p.Speed.Length - 2);
        return Vector2.Lerp(p.Speed[i], p.Speed[i + 1], at - i);
    }

    // The moment before its stop where the clip goes its way in as fast as the drawn body does (m/s); its fastest moment
    // there for a body faster than it.
    private static float PivotFrom(Pivot p, float along)
    {
        int stop = Mathf.Min(p.Speed.Length - 1, Mathf.FloorToInt(p.Stop / p.Step));
        int fastest = 0;
        float most = float.MinValue;
        for (int i = 0; i <= stop; i++)
        {
            float pace = Vector2.Dot(p.Speed[i], p.WayIn);
            if (pace >= most) { most = pace; fastest = i; }
        }
        for (int i = stop; i > fastest; i--)
        {
            float before = Vector2.Dot(p.Speed[i - 1], p.WayIn);
            if (before >= along) return (i - 1 + Mathf.InverseLerp(before, Vector2.Dot(p.Speed[i], p.WayIn), along)) * p.Step;
        }
        return fastest * p.Step;
    }

    // From FollowCapsule, first in the frame: a pivot playing goes on by the frame's time (warped to keep within the
    // slack's bound), and its travel (world, level, m/s) carries the drawn body. capsule: the capsule's speed this frame.
    private bool PivotCarries(float dt, Vector3 capsule, out Vector3 carry)
    {
        carry = Vector3.zero;
        var on = pivotOn;
        if (on == null) return false;
        Quaternion legs = Quaternion.Euler(0f, legYaw, 0f);
        Vector2 now = PivotSpeedAt(on, on.Time);
        Vector3 travel = legs * new Vector3(now.x, 0f, now.y);
        float rate = 1f, limit = Mathf.Max(SlackNone, slackLimit);
        float pull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PivotWarp.x * limit, PivotWarp.y * limit, slackShown.magnitude));
        if (pull > 0f && travel.sqrMagnitude > .25f)
        {
            // The pace whose travel goes as the body should to close on the capsule (as the slack closes: slackClose).
            Vector3 wanted = capsule - slackShown / Mathf.Max(.01f, slackClose);
            rate = Mathf.Lerp(1f, Mathf.Clamp(Vector3.Dot(wanted, travel) / travel.sqrMagnitude, PivotRates.x, PivotRates.y), pull);
        }
        // Stopped over its planted foot the clip carries the body nowhere, and the capsule, turned back at once, goes on away:
        // it hurries through that.
        else if (pull > 0f) rate = Mathf.Lerp(1f, PivotRates.y, pull);
        on.Time = Mathf.Min(on.Time + dt * rate, on.Length);
        Vector2 speed = PivotSpeedAt(on, on.Time) * rate;
        carry = legs * new Vector3(speed.x, 0f, speed.y);
        return true;
    }

    // From the standing pose, once the legs have their stride this frame (Locomotion): begins a pivot where the keys turn the
    // drawn body back on itself, and ends the one playing.
    private void KeepPivot(bool crouched, float dt)
    {
        if (pivots.Count == 0) return;
        Quaternion back = Quaternion.Inverse(Quaternion.Euler(0f, legYaw, 0f));
        Vector3 d = back * stride, a = back * Vector3.ProjectOnPlane(legsAsked ? player.StrideAsked : player.ActualVelocity, Vector3.up);
        Vector2 drawnHere = new Vector2(d.x, d.z), askedHere = new Vector2(a.x, a.z);
        // Which way the keys ask in the world and since when; letting go of them changes nothing here (a pivot needs them on).
        Vector3 asked = legsAsked ? player.StrideAsked : player.ActualVelocity;
        if (askedHere.magnitude >= .1f)
        {
            Vector2 way = new Vector2(asked.x, asked.z).normalized;
            if (pivotKeysWay == Vector2.zero || Vector2.Angle(way, pivotKeysWay) > PivotWithin)
            {
                pivotKeysWayBefore = pivotKeysWay;
                pivotKeysForBefore = pivotKeysFor;
                pivotKeysWay = way;
                pivotKeysFor = 0f;
            }
            pivotKeysFor += dt;
        }
        bool can = !crouched && grounded && state == MovementState.Standing && !dead && !gunRuns && !kicking && SlackShare > .5f;
        if (pivotOn == null)
        {
            // Not with the body already far off the capsule (the bound would end it before its foot was down), nor with the
            // hips turning round.
            if (!can || askedHere.magnitude < .1f || pivotKeysForBefore < PivotKeysHeld
                || slackShown.magnitude > PivotWarp.x * slackLimit || Mathf.Abs(yawVelocity) > PivotTurnMost) return;
            Vector3 b = back * new Vector3(pivotKeysWayBefore.x, 0f, pivotKeysWayBefore.y);
            Vector2 keysBefore = new Vector2(b.x, b.z);
            Pivot best = null;
            float bestOff = float.MaxValue;
            foreach (var p in pivots)
            {
                if (!Measure(p)) continue;
                float along = Vector2.Dot(drawnHere, p.WayIn);
                if (along < PivotSpeeds.x * p.SpeedIn || along > PivotSpeeds.y * p.SpeedIn) continue;
                float offIn = Vector2.Angle(drawnHere, p.WayIn), offOut = Vector2.Angle(askedHere, p.WayOut);
                if (offIn > PivotWithin || offOut > PivotWithin || Vector2.Angle(keysBefore, p.WayIn) > PivotWithin) continue;
                float off = offIn + offOut + PivotSpeedOff * Mathf.Abs(Mathf.Log(along / p.SpeedIn, 2f));
                if (off >= bestOff) continue;
                best = p;
                bestOff = off;
            }
            if (best == null) return;
            pivotOn = best;
            best.Time = PivotFrom(best, Vector2.Dot(drawnHere, best.WayIn));
            // Out of no pivot it comes in alone; out of one going out (keys turned back again within it) beside it.
            if (pivotWeight <= 0f) foreach (var p in pivots) p.Weight = p == best ? 1f : 0f;
            pivotFrame = Time.frameCount;
            return;
        }
        var on = pivotOn;
        bool keysOff = askedHere.magnitude < .1f || Vector2.Dot(askedHere, on.WayOut) <= 0f;
        if (!can || keysOff || slackShown.magnitude >= PivotGapMost * slackLimit || on.Time >= on.End)
        {
            if (on.Motion != null && on.Motion.StepAt(on.Time, out float place)) phase = place;
            pivotOn = null;
            return;
        }
        pivotFrame = Time.frameCount;
    }

    // From BlendLayers: the pivots' weight on the hips and legs, each one's share and moment.
    private void BlendPivot(float dt)
    {
        if (pivotMixer.IsValid() == false) return;
        // A pivot not kept this frame (the pose went elsewhere: a turn, the air) gives the legs back.
        if (pivotOn != null && pivotFrame != Time.frameCount) pivotOn = null;
        bool on = pivotOn != null;
        pivotWeight = Mathf.MoveTowards(pivotWeight, on ? 1f : 0f, dt / Mathf.Max(.01f, on ? pivotBlend.x : pivotBlend.y));
        layers.SetInputWeight(PivotLayer, pivotWeight);
        float total = 0f;
        foreach (var p in pivots)
        {
            // The one playing comes in over the others; with none playing they go out with the layer as they were.
            if (on) p.Weight = Mathf.MoveTowards(p.Weight, p == pivotOn ? 1f : 0f, dt / Mathf.Max(.01f, pivotBlend.x));
            if (pivotWeight <= 0f) p.Weight = 0f;
            // One going out plays on (the one playing goes on with the body it carries: PivotCarries).
            if (p != pivotOn && p.Weight > 0f) p.Time = Mathf.Min(p.Time + dt, p.Length);
            total += p.Weight;
        }
        if (total <= 0f) return;
        foreach (var p in pivots)
        {
            pivotMixer.SetInputWeight(p.Input, p.Weight / total);
            if (p.Weight > 0f) p.Playable.SetTime(p.Time);
        }
    }
}
