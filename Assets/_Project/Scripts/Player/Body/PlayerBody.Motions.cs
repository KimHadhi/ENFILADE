using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The clips' own motion (ClipMotion), for the movement that goes as they do and the body that plays them: how far a move
/// goes, at what pace, and when its body is down, up or bursts off. Read once as the body is made, so nothing is read in the
/// middle of a move. The movement asks by the clip's name (PlayerMovement.MotionOf).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Min(.1f), Tooltip("How far the game goes for one of a clip's own metres: a clip is a performer's, whose legs are "
        + "longer for their height than the characters'. The one number between the clips and the game's speeds and distances.")]
    private float clipMetres = .93f;

    private readonly Dictionary<string, ClipMotion> motions = new Dictionary<string, ClipMotion>(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a clip's own body does; null for a clip this body does not have.</summary>
    internal ClipMotion MotionOf(string clip)
    {
        if (string.IsNullOrEmpty(clip) || profile == null || animator == null) return null;
        if (motions.TryGetValue(clip, out var known)) return known;
        var motion = ClipMotion.Of(profile.Find(clip), animator);
        if (motion != null) motions[clip] = motion;
        return motion;
    }

    // From Build, the clips made ready: those the body goes by the motion of.
    private void ReadMotions()
    {
        foreach (string clip in WayClips()) MotionOf(clip);
        foreach (string clip in new[] { crouchDown, standUp, heavyLand, rollLand, rollLandLow }) MotionOf(clip);
        // The pivots too: each is played on the body to be read, and read at the first pivot of a play all of them went in
        // one frame (PlayerBody.Pivot).
        foreach (var pivot in pivots) Measure(pivot);
    }
}
