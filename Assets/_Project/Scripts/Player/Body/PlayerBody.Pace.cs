using UnityEngine.Playables;

public sealed partial class PlayerBody
{
    /// <summary>How fast this body's clips play: a room's slow motion runs per character, so game time can't do it.</summary>
    internal void SetPace(float pace)
    {
        if (graph.IsValid() && graph.GetRootPlayableCount() > 0) graph.GetRootPlayable(0).SetSpeed(pace);
    }
}
