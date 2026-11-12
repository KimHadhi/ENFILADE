using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Measures, from a walk or run cycle on a body, how it moves: how fast its feet carry the body and which way, how many
/// strides it holds and where the left foot comes down. The clip is sampled on the body itself (its pose put back
/// afterwards, as ObstacleClipContacts does) in the Animator's space, x right, y up, z ahead, and read like this:
/// - a foot is down while its ankle or its toes are within a few hundredths of a leg of their lowest;
/// - while down it moves back under the hips as fast as the clip carries the body (the cycles are in place), so the
///   clip's speed is the median of that, along the way the down feet move back from (its travel);
/// - the left foot's touchdowns count the strides and set where a stride starts, so cycles that blend put the same foot
///   down at the same moment, whatever their length or where their file starts.
/// The table's playback speeds before this were guesses and set to the movement's speeds (Body.runClipSpeed 4.9 for a
/// run whose feet carry Mayo at 3.0 m/s), so the feet skated 30-60% of the speed (2026-11-12, measured in Blender: see
/// Docs/PlayerBodyGuide.md). Nothing here is written for one clip or one character: any humanoid cycle on any humanoid
/// measures itself, so a modder's character or clip runs at its own stride. One clip on one model is measured once and kept.
/// </summary>
internal static class GaitClips
{
    /// <summary>Samples a second.</summary>
    private const float SampleRate = 60f;
    // A foot is down within this share of the leg's length of its lowest ankle or toes, and lifts again past twice that.
    private const float AnkleDown = .04f, ToesDown = .025f;
    // One foot comes down at most every this many seconds even sprinting: closer touchdowns are one landing (heel, then toes).
    private const float SameFootGap = .25f;

    /// <summary>How one cycle moves on one model.</summary>
    internal sealed class Gait
    {
        /// <summary>The clip's length (s).</summary>
        public float Length;
        /// <summary>Strides (the left and then the right foot down) in the clip.</summary>
        public int Cycles = 1;
        /// <summary>Share of the clip where the left foot first comes down.</summary>
        public float LeftDown;
        /// <summary>The way the clip travels in the Animator's frame (x right, y ahead), unit; zero for one that stands.</summary>
        public Vector2 Way;
        /// <summary>How fast the down feet carry the body, in the Animator's own units a second (its scale makes it m/s).</summary>
        public float Speed;
        /// <summary>Hip joint to ankle, in the Animator's own units.</summary>
        public float Leg;

        public bool Moves => Speed > 1e-3f && Way.sqrMagnitude > .5f;
        /// <summary>One stride's time at the clip's own pace (s).</summary>
        public float Stride => Length / Mathf.Max(1, Cycles);

        /// <summary>The share of the clip <paramref name="phase"/> (0 to 1, the left foot down at 0) into stride
        /// <paramref name="stride"/>; the clip's strides take turns.</summary>
        public float ShareAt(int stride, float phase)
        {
            int within = ((stride % Cycles) + Cycles) % Cycles;
            return Mathf.Repeat(LeftDown + (within + phase) / Cycles, 1f);
        }
    }

    private static readonly Dictionary<(Object model, AnimationClip clip), Gait> measured = new();

    /// <summary><paramref name="clip"/>'s gait on <paramref name="animator"/>'s body, made from <paramref name="model"/>; null
    /// for a body that is not humanoid.</summary>
    internal static Gait For(Object model, AnimationClip clip, Animator animator)
    {
        if (clip == null || animator == null || !animator.isHuman) return null;
        if (measured.TryGetValue((model, clip), out var known)) return known;
        var made = Measure(clip, animator);
        measured[(model, clip)] = made;
        return made;
    }

    private static Gait Measure(AnimationClip clip, Animator animator)
    {
        Transform root = animator.transform;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        var rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
        if (hips == null || upper == null || lower == null || leftFoot == null || rightFoot == null) return null;

        // One loop's samples, the end left out: a cycle's last moment is its first again.
        float length = Mathf.Max(.01f, clip.length);
        int count = Mathf.Max(8, Mathf.CeilToInt(length * SampleRate));
        float step = length / count;
        var hip = new Vector3[count];
        var feet = new[] { new Vector3[count], new Vector3[count] };
        var toes = new[] { leftToes != null ? new Vector3[count] : null, rightToes != null ? new Vector3[count] : null };
        float leg = 0f;

        // The body is sampled where it stands and put back as it was: its own graph poses it again before it is drawn.
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = new (Vector3 position, Quaternion rotation, Vector3 scale)[bones.Length];
        for (int i = 0; i < bones.Length; i++) poses[i] = (bones[i].localPosition, bones[i].localRotation, bones[i].localScale);
        try
        {
            for (int i = 0; i < count; i++)
            {
                clip.SampleAnimation(animator.gameObject, i * step);
                hip[i] = root.InverseTransformPoint(hips.position);
                feet[0][i] = root.InverseTransformPoint(leftFoot.position);
                feet[1][i] = root.InverseTransformPoint(rightFoot.position);
                if (toes[0] != null) toes[0][i] = root.InverseTransformPoint(leftToes.position);
                if (toes[1] != null) toes[1][i] = root.InverseTransformPoint(rightToes.position);
                if (i == 0)
                    leg = Vector3.Distance(root.InverseTransformPoint(upper.position), root.InverseTransformPoint(lower.position))
                        + Vector3.Distance(root.InverseTransformPoint(lower.position), feet[0][0]);
            }
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].position, poses[i].rotation);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localScale = poses[i].scale;
        }
        if (leg <= 1e-4f) return null;

        var gait = new Gait { Length = length, Leg = leg };
        var down = new[] { Down(feet[0], toes[0], leg), Down(feet[1], toes[1], leg) };

        // How each down foot moves against the hips, from the samples either side (the loop wraps).
        var moving = new List<Vector2>();
        Vector2 sum = Vector2.zero;
        for (int f = 0; f < 2; f++)
            for (int i = 0; i < count; i++)
            {
                if (!down[f][i]) continue;
                int before = (i + count - 1) % count, after = (i + 1) % count;
                Vector3 from = feet[f][before] - hip[before], to = feet[f][after] - hip[after];
                var v = new Vector2(to.x - from.x, to.z - from.z) / (2f * step);
                moving.Add(v);
                sum += v;
            }
        if (moving.Count > 0 && sum.magnitude / moving.Count > 1e-3f * leg)
        {
            gait.Way = -(sum / moving.Count).normalized;
            var back = new List<float>(moving.Count);
            foreach (var v in moving) back.Add(-Vector2.Dot(v, gait.Way));
            back.Sort();
            gait.Speed = Mathf.Max(0f, back[back.Count / 2]);
        }

        // Where each stride starts: the left foot at its furthest along the travel, ahead of the hips, which is the moment
        // just before it comes down in any gait (ahead running, behind backing up, out to the side strafing) and, unlike a
        // height line, never missed by a foot that barely lifts (a slow back step). Closer peaks are one reach.
        var touchdowns = new List<int>();
        int gap = Mathf.Max(1, Mathf.RoundToInt(SameFootGap / step));
        if (gait.Moves)
        {
            var reach = new float[count];
            float most = float.NegativeInfinity, least = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector3 off = feet[0][i] - hip[i];
                reach[i] = off.x * gait.Way.x + off.z * gait.Way.y;
                most = Mathf.Max(most, reach[i]);
                least = Mathf.Min(least, reach[i]);
            }
            // A peak counts if it reaches the upper fifth of the swing (a stumble's wobble mid-swing doesn't).
            float line = most - (most - least) * .2f;
            for (int i = 0; i < count; i++)
            {
                float here = reach[i];
                if (here < line || here < reach[(i + count - 1) % count] || here <= reach[(i + 1) % count]) continue;
                if (touchdowns.Count > 0 && i - touchdowns[touchdowns.Count - 1] < gap)
                {
                    if (here > reach[touchdowns[touchdowns.Count - 1]]) touchdowns[touchdowns.Count - 1] = i;
                    continue;
                }
                touchdowns.Add(i);
            }
            if (touchdowns.Count > 1 && touchdowns[0] + count - touchdowns[touchdowns.Count - 1] < gap)
                touchdowns.RemoveAt(reach[touchdowns[0]] >= reach[touchdowns[touchdowns.Count - 1]] ? touchdowns.Count - 1 : 0);
        }
        else
        {
            // Standing cycles: where the left foot comes down after being up, if it ever does.
            for (int i = 0; i < count; i++)
                if (down[0][i] && !down[0][(i + count - 1) % count]
                    && (touchdowns.Count == 0 || i - touchdowns[touchdowns.Count - 1] >= gap))
                    touchdowns.Add(i);
        }
        gait.Cycles = Mathf.Max(1, touchdowns.Count);
        gait.LeftDown = touchdowns.Count > 0 ? touchdowns[0] / (float)count : 0f;
        return gait;
    }

    // Down while the ankle or the toes are near their lowest; up again only past twice that, so a millimetre's wobble at the
    // line is not a second step. Twice round the loop, so the first samples know whether the foot was already down.
    private static bool[] Down(Vector3[] ankle, Vector3[] toes, float leg)
    {
        int count = ankle.Length;
        float lowestAnkle = float.PositiveInfinity, lowestToes = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            lowestAnkle = Mathf.Min(lowestAnkle, ankle[i].y);
            if (toes != null) lowestToes = Mathf.Min(lowestToes, toes[i].y);
        }
        var down = new bool[count];
        bool now = false;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < count; i++)
            {
                float a = (ankle[i].y - lowestAnkle) / leg, t = toes != null ? (toes[i].y - lowestToes) / leg : float.PositiveInfinity;
                now = now ? a < AnkleDown * 2f || t < ToesDown * 2f : a < AnkleDown || t < ToesDown;
                down[i] = now;
            }
        return down;
    }
}
