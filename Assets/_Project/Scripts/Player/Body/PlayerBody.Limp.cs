using UnityEngine;

/// <summary>
/// A dead body lies as physics leaves it (PlayerRagdoll). No clip, IK or look touches its bones until they come back:
/// switched off rather than hidden, because a hidden body left running costs as much as a live one.
/// </summary>
public sealed partial class PlayerBody
{
    private bool limp;

    internal bool IsLimp => limp;

    /// <summary>Hands the body over just as it stands. Coming back to life takes it again by itself.</summary>
    internal void GoLimp()
    {
        limp = dead = true;
        feet?.LetGo();
        hands.OrNull()?.LetGo();
        if (animator != null)
        {
            animator.writeDefaultValuesOnDisable = false;
            animator.enabled = false;
        }
    }

    // They come back somewhere else, so the body starts from a plain stand, not from what it was doing when it died.
    private bool StaysLimp()
    {
        if (!limp || player.IsDead) return limp;
        limp = false;
        foreach (var channel in channels) channel.Weight = channel.Target = 0f;
        var rest = Find(idle);
        if (rest != null) rest.Weight = rest.Target = 1f;
        transition = null;
        playingOut = false;
        headAlignment = new Vector3(0f, -soleLift, -backOffset);
        if (animator != null) animator.enabled = true;
        // Before FPS Animation Framework notices the Animator is back (FPSAnimator rebuilds from its first output).
        ReattachGraph();
        return false;
    }
}
