using UnityEngine;
using UnityEngine.Playables;

public sealed partial class PlayerBody
{
    // Eases a copy's legs in rather than jumping them to a new place in the stride.
    private const float StepCatchUp = 6f;

    private float reportedStep;

    /// <summary>How fast this body's clips play: a room's slow motion runs per character, so game time can't do it.</summary>
    internal void SetPace(float pace)
    {
        // Every root: with FPS Animation Framework on the graph (FPSBody) the clips feed its mixer, and its own layers and
        // animations must slow down with them.
        if (!graph.IsValid()) return;
        for (int i = 0; i < graph.GetRootPlayableCount(); i++) graph.GetRootPlayable(i).SetSpeed(pace);
    }

    /// <summary>Where the stride is: the player's real one when read, the one their PC reported when a copy is told.</summary>
    internal byte Step
    {
        get => (byte)(phase * 256f);
        set => reportedStep = value / 256f;
    }

    // The way round the cycle that's shortest, so a copy a little ahead waits rather than running a whole stride on.
    private void FollowStep(float dt)
    {
        float behind = Mathf.Repeat(reportedStep - phase + .5f, 1f) - .5f;
        phase = Mathf.Repeat(phase + behind * (1f - Mathf.Exp(-StepCatchUp * dt)), 1f);
    }
}
