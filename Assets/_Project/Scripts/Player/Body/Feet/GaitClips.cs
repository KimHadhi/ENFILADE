using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Measures, from a walk or run cycle on a body, how it moves: how fast its feet carry the body and which way, how many
/// strides it holds and where the left foot comes down. The clip is sampled on the body itself (its pose put back
/// afterwards, as ObstacleClipContacts does) in the Animator's space, x right, y up, z ahead, and read like this:
/// - a foot is down while its ankle or its toes are within a few hundredths of a leg of their lowest;
/// - while down it moves back under the hips as fast as the clip carries the body (the cycles are in place), so the
///   clip's speed is the median of that, along the way the down feet move back from (its travel), at the point of the sole
///   that is on the ground (the heel or the toes); a foot that only hangs low carries nothing and is left out (CarryShare);
/// - the left foot's touchdowns count the strides and set where a stride starts, so cycles that blend put the same foot
///   down at the same moment, whatever their length or where their file starts.
/// The table's playback speeds before this were guesses and set to the movement's speeds (Body.runClipSpeed 4.9 for a
/// run whose feet carry Mayo at 3.0 m/s), so the feet skated 30-60% of the speed (2026-11-12, measured in Blender: see
/// Docs/Guides/PlayerBodyGuide.md). Nothing here is written for one clip or one character: any humanoid cycle on any humanoid
/// measures itself, so a modder's character or clip runs at its own stride. One clip on one model is measured once and kept.
/// </summary>
internal static class GaitClips
{
    /// <summary>Samples a second.</summary>
    private const float SampleRate = 60f;
    // A foot is down within this share of the leg's length of its lowest ankle or toes, and lifts again past twice that.
    private const float AnkleDown = .04f, ToesDown = .025f;
    // A down foot carries the body while it moves back at least this share as fast as the fastest down feet do.
    private const float CarryShare = .5f;
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
        // Each foot's turn in the Animator's space, to carry its heel (Heel).
        var turns = new[] { new Quaternion[count], new Quaternion[count] };
        float leg = 0f;

        // The body is sampled where it stands and put back as it was: its own graph poses it again before it is drawn.
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = new (Vector3 position, Quaternion rotation, Vector3 scale)[bones.Length];
        for (int i = 0; i < bones.Length; i++) poses[i] = (bones[i].localPosition, bones[i].localRotation, bones[i].localScale);
        try
        {
            for (int i = 0; i < count; i++)
            {
                ClipSampler.Sample(clip, animator.gameObject, i * step);
                hip[i] = root.InverseTransformPoint(hips.position);
                feet[0][i] = root.InverseTransformPoint(leftFoot.position);
                feet[1][i] = root.InverseTransformPoint(rightFoot.position);
                if (toes[0] != null) toes[0][i] = root.InverseTransformPoint(leftToes.position);
                if (toes[1] != null) toes[1][i] = root.InverseTransformPoint(rightToes.position);
                turns[0][i] = Quaternion.Inverse(root.rotation) * leftFoot.rotation;
                turns[1][i] = Quaternion.Inverse(root.rotation) * rightFoot.rotation;
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
        var heels = new[] { Heel(feet[0], toes[0], turns[0]), Heel(feet[1], toes[1], turns[1]) };

        // How each down foot moves against the hips, from the samples either side (the loop wraps), at the point of its sole
        // that is on the ground: the heel or the toes, whichever is lower (the ankle for a body without toe bones). The ankle
        // alone is no point of the sole: as a heel comes down and rolls, and as it rises round the planted toes, the ankle
        // moves over the ground, and walks, jogs and runs read 15-25% under their root's travel even on BIPER's own body (walk
        // 1.32 against 1.54 m/s, a back step landing on its toes 1.66 against 1.67; 2026-11-21, ClipBench). This is what this
        // body's feet do: the stride the game stretches to its speed (FootPlanting.Stride), so a body whose legs are another
        // length for its hips than the performer's still keeps its feet where they come down. 0.9.253 took the root's travel
        // for it instead, which is the performer's: on the game's bodies the soles go 20-35% slower than that (Ichigo's walk
        // 1.24 against 1.57 m/s, Mayo's run 3.78 against 5.14), so their strides were never stretched to the speed and the
        // feet slid under them (the user: "굉장히 얼음장 위를 걷는 느낌"). The game's speeds are the clips' own (ClipMotion), which
        // these feet come within a tenth of.
        var moving = new List<Vector2>();
        for (int f = 0; f < 2; f++)
            for (int i = 0; i < count; i++)
            {
                if (!down[f][i]) continue;
                int before = (i + count - 1) % count, after = (i + 1) % count;
                var point = heels[f] == null ? feet[f] : heels[f][i].y <= toes[f][i].y ? heels[f] : toes[f];
                Vector3 from = point[before] - hip[before], to = point[after] - hip[after];
                moving.Add(new Vector2(to.x - from.x, to.z - from.z) / (2f * step));
            }
        if (Along(moving, leg, out Vector2 way))
        {
            // Only the feet that carry: a foot that hangs a moment just over the floor counts as down by its height but
            // moves little or the other way, and a crouched back step has more of that than of standing: BIPER's
            // CrouchWalk_L135 read 0.4 m/s for feet that go 2.2 (its root 2.3), so the game played it a fifth faster and its
            // strides a third longer than it was made (0.9.350, measured foot by foot in Blender).
            List<Vector2> carry = moving;
            for (int pass = 0; pass < 2; pass++)
            {
                float least = CarryShare * Speeds(moving, way)[(int)(moving.Count * .9f)];
                Vector2 toward = way;
                carry = moving.FindAll(v => -Vector2.Dot(v, toward) >= least);
                if (!Along(carry, leg, out way)) way = toward;
            }
            var back = Speeds(carry, way);
            gait.Way = way;
            // The median: on BIPER's own body it reads within 8% of the root's travel at every walk and crouch and within 10%
            // in the jogs and sprints, whose feet roll over the toes (2026-11-22, ClipBench).
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

    // The way these feet carry the body: against their mean. False where they go nowhere together.
    private static bool Along(List<Vector2> moving, float leg, out Vector2 way)
    {
        Vector2 sum = Vector2.zero;
        foreach (var v in moving) sum += v;
        bool goes = moving.Count > 0 && sum.magnitude / moving.Count > 1e-3f * leg;
        way = goes ? -sum.normalized : Vector2.zero;
        return goes;
    }

    // How fast each goes back along the way, slowest first.
    private static List<float> Speeds(List<Vector2> moving, Vector2 way)
    {
        var back = new List<float>(moving.Count);
        foreach (var v in moving) back.Add(-Vector2.Dot(v, way));
        back.Sort();
        return back;
    }

    // A foot's heel through the clip: the point of the sole under the ankle, found where the foot lies flattest in the clip
    // itself (ankle and toes both nearest their lowest) and carried with the foot's turn. Null for a body without toes.
    private static Vector3[] Heel(Vector3[] ankle, Vector3[] toes, Quaternion[] turns)
    {
        if (toes == null) return null;
        int count = ankle.Length;
        float lowestAnkle = float.PositiveInfinity, lowestToes = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            lowestAnkle = Mathf.Min(lowestAnkle, ankle[i].y);
            lowestToes = Mathf.Min(lowestToes, toes[i].y);
        }
        int flat = 0;
        float flattest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            float off = ankle[i].y - lowestAnkle + toes[i].y - lowestToes;
            if (off < flattest) { flattest = off; flat = i; }
        }
        Vector3 sole = new Vector3(ankle[flat].x, toes[flat].y, ankle[flat].z);
        Vector3 local = Quaternion.Inverse(turns[flat]) * (sole - ankle[flat]);
        var heel = new Vector3[count];
        for (int i = 0; i < count; i++) heel[i] = ankle[i] + turns[i] * local;
        return heel;
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
