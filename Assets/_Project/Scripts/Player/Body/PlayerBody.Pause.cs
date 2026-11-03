/// <summary>
/// The Esc menu stops time, and everything that holds the hands on the gun stops with it (IK runs with time). A running
/// graph would still write the bare clip pose every frame, which dropped the hands out of view, so a pause freezes the
/// body as it stands and play picks up where it left off.
/// </summary>
public sealed partial class PlayerBody
{
    private bool frozen;

    /// <summary>True while paused. Freezes the body on the way in and lets it go on the way out.</summary>
    private bool HoldsForPause()
    {
        bool paused = GamePause.IsPaused;
        if (paused == frozen) return paused;
        frozen = paused;
        // A dead body's graph is already stopped, and coming back to life restarts it.
        if (limp) return paused;
        if (paused)
        {
            if (animator != null)
            {
                animator.writeDefaultValuesOnDisable = false;
                animator.enabled = false;
            }
            if (graph.IsValid()) graph.Stop();
        }
        else
        {
            if (animator != null) animator.enabled = true;
            if (graph.IsValid()) graph.Play();
        }
        return paused;
    }
}
