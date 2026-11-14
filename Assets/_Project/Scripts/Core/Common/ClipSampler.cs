using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Poses a model as a clip has it at a moment, the same in the editor and in a player build: the one way the game samples
/// a clip to measure something (a gun's hold on KINEMATION's arms, the arms' reach, the eyes' frame, the first-person
/// camera bone, reload moments, gait, a vault's hand spots, a ledge catch, a magazine's rounds).
/// AnimationClip.SampleAnimation writes a Mecanim clip's curves in the editor only, where the clip's source curves are
/// at hand; a player build keeps only what an Animator plays, and there it left the model as it stood: every gun's hands
/// fell back to the remade clips in the user's build ("KINEMATION's arms didn't take … pose clip", Player.log,
/// 2026-11-14: "원래 잘 고쳐졌던 총들이 왜 손 모양이 갑자기 다 이상하게 변한 거지??"). So in a build the clip is evaluated
/// through a PlayableGraph on the model's Animator: its own (a character, whose callers put its bones back after), made
/// and let go per sample with its root motion held off meanwhile; or for a bare copy, one added without an avatar (its
/// clips are generic, bound by path from the model's root as SampleAnimation binds them), its graph kept for the next
/// sample of the same copy. Time is set twice so no clip event fires. In the editor it stays SampleAnimation, which
/// everything was measured and tuned with.
/// </summary>
public static class ClipSampler
{
#if !UNITY_EDITOR
    // The bare copy sampled last in a build, the Animator added to it and its graph, kept for the next sample of it (a
    // clip read frame by frame, a gun's contacts) and let go when another model is sampled. A build's alone: the editor
    // samples with SampleAnimation and keeps nothing, so its compile leaves them out (there copyRoot was only ever set,
    // never read, and the compiler warned of it).
    private static GameObject copyRoot;
    private static Animator copyAnimator;
    private static PlayableGraph copyGraph;
    private static AnimationPlayableOutput copyOutput;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Release();

    /// <summary>Poses <paramref name="root"/> and everything under it as <paramref name="clip"/> has them <paramref name="time"/> seconds in.</summary>
    public static void Sample(AnimationClip clip, GameObject root, float time)
    {
        if (clip == null || root == null) return;
#if UNITY_EDITOR
        clip.SampleAnimation(root, time);
#else
        var own = root.GetComponent<Animator>();
        if (own != null && own != copyAnimator)
        {
            bool rootMotion = own.applyRootMotion;
            var culling = own.cullingMode;
            own.applyRootMotion = false;
            own.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("Clip sample (" + root.name + ")");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Sample", own);
            Evaluate(graph, output, clip, time);
            graph.Destroy();
            own.applyRootMotion = rootMotion;
            own.cullingMode = culling;
            return;
        }
        if (root != copyRoot || copyAnimator == null || !copyGraph.IsValid())
        {
            Release();
            copyRoot = root;
            copyAnimator = root.AddComponent<Animator>();
            copyAnimator.avatar = null;
            copyAnimator.applyRootMotion = false;
            copyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            copyGraph = PlayableGraph.Create("Clip sample (" + root.name + ")");
            copyGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            copyOutput = AnimationPlayableOutput.Create(copyGraph, "Sample", copyAnimator);
        }
        Evaluate(copyGraph, copyOutput, clip, time);
#endif
    }

    /// <summary>Lets go of the graph kept for the last bare copy sampled (and of the Animator added to it); the editor keeps none.</summary>
    public static void Release()
    {
#if !UNITY_EDITOR
        if (copyGraph.IsValid()) copyGraph.Destroy();
        if (copyAnimator != null) Object.Destroy(copyAnimator);
        copyRoot = null;
        copyAnimator = null;
#endif
    }

    private static void Evaluate(PlayableGraph graph, AnimationPlayableOutput output, AnimationClip clip, float time)
    {
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        playable.SetApplyPlayableIK(false);
        playable.SetTime(time);
        playable.SetTime(time);
        output.SetSourcePlayable(playable);
        graph.Evaluate(0f);
        playable.Destroy();
    }
}
