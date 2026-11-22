using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// What a clip's own body does, read off the clip itself: how far it has gone over the ground and how high it is at each
/// moment, and from those when it bursts off, is lowest, begins to get up and is up. The one place the game takes a move's
/// way, pace and moments from, so a move is its animation and nothing about it is written down anywhere else (the user,
/// 2026-11-22: "걍 애니메이션 하나만 있으면 용량도 적고 버그도 더 적은 거 아닌감"). The clip is played once on a body, its root
/// motion on, the same way in the editor and in a build, and kept; the way is the clip's own (every body's alike), so a
/// move goes as far whoever makes it.
/// Until 0.9.352 the way was baked off the clips' curves into a table in the editor (GaitMoves.csv, GaitMoveBake), and the
/// moments and lengths were numbers of the tables', each measured once and typed.
/// </summary>
public sealed class ClipMotion
{
    // Samples a second.
    private const float SampleRate = 60f;
    // A clip's pace is read over this long either side of its moment (s): its body swings to and fro over its feet, and the
    // player goes as it goes on the whole.
    private const float PaceOver = .08f;
    // Its body is down this near its lowest; of its way back up it begins to rise here, has its legs under it here, is on its
    // feet here and up here (shares of the height between its lowest and where it begins or ends).
    private const float DownShare = .25f, RisesShare = .1f, LegsShare = .25f, FeetShare = .75f, UpShare = .9f;

    /// <summary>How far the game goes for one of the clip's own metres (Body.clipMetres): a clip is a performer's, whose legs
    /// are longer for their height than the game's characters'.</summary>
    internal static float Metres = .93f;

    private static readonly Dictionary<AnimationClip, ClipMotion> read = new Dictionary<AnimationClip, ClipMotion>();

    // Along the clip at even moments: how far it has gone (its own metres) and how high its body is (share of the way between
    // its lowest and its highest end).
    private readonly float[] gone, height;

    /// <summary>The clip's length (s).</summary>
    public float Length { get; }
    /// <summary>How far it goes (m).</summary>
    public float Distance => gone[gone.Length - 1] * Metres;
    /// <summary>The moments its body bursts off and is done bursting (s): where it goes at least half as fast as it ever does.</summary>
    public float BurstFrom { get; }
    public float BurstTo { get; }
    /// <summary>The moment its body is down, and is lowest (s).</summary>
    public float Down { get; }
    public float Lowest { get; }
    /// <summary>The moments on from its lowest its body begins to get up, has its legs under it, is on its feet and is up (s).</summary>
    public float Rises { get; }
    public float LegsUnder { get; }
    public float OnFeet { get; }
    public float Up { get; }

    private ClipMotion(float length, float[] gone, float[] height)
    {
        Length = length;
        this.gone = gone;
        this.height = height;
        int last = gone.Length - 1, peak = 1, low = 0;
        for (int i = 1; i <= last; i++)
        {
            if (gone[i] - gone[i - 1] > gone[peak] - gone[peak - 1]) peak = i;
            if (height[i] < height[low]) low = i;
        }
        float half = (gone[peak] - gone[peak - 1]) * .5f;
        int from = peak, to = peak;
        while (from > 1 && gone[from - 1] - gone[from - 2] >= half) from--;
        while (to < last && gone[to + 1] - gone[to] >= half) to++;
        BurstFrom = length * (from - 1) / last;
        BurstTo = length * to / last;
        Lowest = length * low / last;
        int down = low;
        while (down > 0 && height[down - 1] <= DownShare) down--;
        Down = length * down / last;
        Rises = RiseAt(RisesShare);
        LegsUnder = RiseAt(LegsShare);
        OnFeet = RiseAt(FeetShare);
        Up = RiseAt(UpShare);
    }

    /// <summary>How far it has gone at its moment (m).</summary>
    public float GoneAt(float time) => At(gone, time) * Metres;

    /// <summary>How fast it goes at its moment (m/s), over a moment either side.</summary>
    public float PaceAt(float time)
    {
        float from = Mathf.Clamp(time - PaceOver, 0f, Length), to = Mathf.Clamp(time + PaceOver, 0f, Length);
        return to - from > 1e-3f ? (GoneAt(to) - GoneAt(from)) / (to - from) : 0f;
    }

    /// <summary>How high its body is at its moment: 0 as low as it ever is, 1 as high as it begins or ends.</summary>
    public float HeightAt(float time) => At(height, time);

    /// <summary>The first moment its body is this high, going the way it goes from its start to its end (s); for a clip that goes
    /// from one posture to another.</summary>
    public float MomentAt(float share)
    {
        int last = height.Length - 1;
        bool rises = height[last] > height[0];
        for (int i = 1; i <= last; i++)
        {
            if (rises ? height[i] < share : height[i] > share) continue;
            float into = Mathf.Abs(height[i] - height[i - 1]) > 1e-5f ? Mathf.InverseLerp(height[i - 1], height[i], share) : 1f;
            return Length * (i - 1 + into) / last;
        }
        return Length;
    }

    // The moment after its lowest its body has come this share of the way up again (s); its length where it never does.
    private float RiseAt(float share)
    {
        int last = height.Length - 1;
        for (int i = Mathf.RoundToInt(Lowest / Length * last); i <= last; i++)
            if (height[i] >= share) return Length * i / last;
        return Length;
    }

    private float At(float[] values, float time)
    {
        float at = Mathf.Clamp01(time / Length) * (values.Length - 1);
        int low = Mathf.Min(values.Length - 2, Mathf.FloorToInt(at));
        return Mathf.Lerp(values[low], values[low + 1], at - low);
    }

    /// <summary>How fast a walk or run cycle's own body goes over the ground (m/s).</summary>
    internal static float CycleSpeed(AnimationClip clip)
    {
        Vector3 average = clip.averageSpeed;
        return new Vector2(average.x, average.z).magnitude * Metres;
    }

    /// <summary><paramref name="clip"/>'s motion, read on <paramref name="animator"/>'s body the first time it is asked for;
    /// null for a clip or a body that is not humanoid, or a body that plays nothing now (it is asked again).</summary>
    internal static ClipMotion Of(AnimationClip clip, Animator animator)
    {
        if (clip == null) return null;
        if (read.TryGetValue(clip, out var known)) return known;
        if (animator == null || !animator.isHuman || !clip.isHumanMotion || clip.length <= 0f) return null;
        var motion = Read(clip, animator);
        // A body switched off plays nothing: a clip that travels and went nowhere there has not been read.
        if (motion.Distance < CycleSpeed(clip) * clip.length * .5f) return null;
        return read[clip] = motion;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => read.Clear();

    private static ClipMotion Read(AnimationClip clip, Animator animator)
    {
        Transform root = animator.transform, hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        int count = Mathf.Max(8, Mathf.CeilToInt(clip.length * SampleRate));
        float step = clip.length / count;
        // The clip's own metres: the root's motion goes with the body's size.
        float own = 1f / Mathf.Max(1e-4f, animator.humanScale * Mathf.Abs(root.lossyScale.y));
        var gone = new float[count + 1];
        var height = new float[count + 1];

        // Played where the body stands and put back as it was: its own graph poses it again before it is drawn.
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = new (Vector3 position, Quaternion rotation, Vector3 scale)[bones.Length];
        for (int i = 0; i < bones.Length; i++) poses[i] = (bones[i].localPosition, bones[i].localRotation, bones[i].localScale);
        bool rootMotion = animator.applyRootMotion;
        var culling = animator.cullingMode;
        animator.applyRootMotion = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var graph = PlayableGraph.Create("Clip motion (" + clip.name + ")");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Motion", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            output.SetSourcePlayable(playable);
            // Twice, so no clip event fires.
            playable.SetTime(0d);
            playable.SetTime(0d);
            graph.Evaluate(0f);
            for (int i = 0; i <= count; i++)
            {
                if (i > 0)
                {
                    graph.Evaluate(step);
                    Vector3 went = Vector3.ProjectOnPlane(animator.deltaPosition, Vector3.up);
                    gone[i] = gone[i - 1] + went.magnitude * own;
                }
                height[i] = Vector3.Dot(hips.position - root.position, root.up) * own;
            }
        }
        finally
        {
            graph.Destroy();
            animator.applyRootMotion = rootMotion;
            animator.cullingMode = culling;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].position, poses[i].rotation);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localScale = poses[i].scale;
        }
        float lowest = float.PositiveInfinity, end = Mathf.Max(height[0], height[count]);
        foreach (float h in height) lowest = Mathf.Min(lowest, h);
        float dip = Mathf.Max(1e-3f, end - lowest);
        for (int i = 0; i <= count; i++) height[i] = (height[i] - lowest) / dip;
        return new ClipMotion(clip.length, gone, height);
    }
}
