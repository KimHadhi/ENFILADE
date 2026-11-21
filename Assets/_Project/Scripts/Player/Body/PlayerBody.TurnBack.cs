using UnityEngine;

/// <summary>
/// Turning round on the run (MovementTurnBack, 0.9.295): the turn's clip on its own clock from the moment the turn began,
/// the same on every PC. The clip turns the body itself (its turn is kept in the pose, BiperRunner.clips.csv inPlaceYaw 0),
/// starting from the way the body ran; the view may have swung further or less far than the clip turns, so the body is
/// faced on by the difference as the clip goes, and it ends facing the view. The movement goes along the clip's root the
/// same way, so the feet stay where they come down.
/// </summary>
public sealed partial class PlayerBody
{
    private string turnBackClip;

    // From ChoosePose, standing on the ground: true while a turn round plays.
    private bool TurnBackPose()
    {
        var turn = player.Actions != null ? player.Actions.TurnBack : null;
        if (turn == null || !turn.Active || state != PlayerMovement.MovementState.Standing || !(Find(turn.Clip) is Channel channel)
            || channel.Length <= 0f)
        {
            turnBackClip = null;
            return false;
        }
        if (turn.Clip != turnBackClip)
        {
            Restart(channel);
            turnBackClip = turn.Clip;
        }
        float time = Mathf.Max(0f, Time.time - turn.StartedAt);
        Drive(turn.Clip, time / channel.Length);
        // The body faces the way it ran, turned on by what the view's turn adds to the clip's own: with the clip's turn on
        // top, it faces the way the movement goes, the view's at the end.
        float facing = turn.FromYaw + turn.ClipTurnAt(time) * (turn.Stretch - 1f);
        legYaw = facing;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, facing);
        legsPlanted = true;
        turning = 0;
        keepsPlace = false;
        return true;
    }
}
