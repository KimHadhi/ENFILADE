using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body's side of a run's turn back (RunTurn): its clip on the turn's own clock, the body's root coming in where the body
/// faced and turning as the clip's root turns (with the turn's small bend toward the view), the spine turning the chest back
/// toward the view from where the clip has it (AimLook: RunTurnFacing), and the walk or run taking the stride from the clip's
/// footfall as it ends (StepFrom), the chest then back over the root: nothing jumps. The root's turn is the clip's own motion,
/// so it is no jump to be eased away (rootByClip, PlayerBody.Update). Until 0.9.412 the root went under the chest at the end
/// at once, eased away as a jump, and every frame of the turn faster than 5 degrees was taken for one too.
/// </summary>
public sealed partial class PlayerBody
{
    // The turn's clip playing (null for none), and the turn it plays for (RunTurn.Serial).
    private string turnClip;
    private int turnSerial = -1;
    // The last turn's clip as it goes out of the pose, and the way its pose had the chest against the root as it ended (degrees).
    private string turnLeaving;
    private float turnLeavingFacing;

    /// <summary>Where the stride is (0 the left foot landing, .5 the right): the foot a turn plants (RunTurn).</summary>
    internal float StridePhase => phase;

    /// <summary>Which way the body faces (world yaw, degrees).</summary>
    internal float FacingYaw => legYaw;

    // How far the turn's clip has the chest round against the body's root (degrees), for the spine's turn back to the view.
    // Which way the turn's clip has the chest against the body's root, by as much as the clip shows (its share of the pose),
    // so it comes in and goes out with the clip's crossfade; the last turn's as it ended while its clip goes out. Until
    // 0.9.402 it was all of the clip's from the frame the turn began, with the clip a tenth of the pose, and the spine, the
    // neck and the head turned back by it in a frame (the head 31 to 38 degrees: the pops record of 2026-11-24 20:3x); until
    // 0.9.420 none from the frame the turn ended, its clip still nine tenths of the pose, and the chest and shoulders turned
    // 24 to 26 degrees in that frame (the pops record of 2026-11-25 05:00).
    private float RunTurnFacing
    {
        get
        {
            if (player.Actions == null) return 0f;
            string clip = turnClip ?? turnLeaving;
            var channel = clip != null ? Find(clip) : null;
            if (channel == null) return 0f;
            float shows = shownTotal > .0001f ? Mathf.Clamp01(channel.Weight / shownTotal) : 1f;
            return (turnClip != null ? player.Actions.RunTurn.PoseFacing : turnLeavingFacing) * shows;
        }
    }

    // Before the pose is chosen: a turn that is over, or that the body has left (a jump, a slide), hands the body back.
    private void KeepRunTurn()
    {
        if (turnClip == null) return;
        var turn = player.Actions != null ? player.Actions.RunTurn : null;
        if (turn != null && turn.Turning && turn.Clip == turnClip && state == MovementState.Standing && grounded) return;
        EndRunTurn();
    }

    // Standing on the ground: true while a turn has the body.
    private bool RunTurnPose()
    {
        var turn = player.Actions != null ? player.Actions.RunTurn : null;
        if (turn == null || !turn.Turning) return false;
        var channel = Find(turn.Clip);
        if (channel == null || channel.Synced) return false;
        float root = turn.RootYaw;
        // A new turn (on the same clip too) starts its clip again; from its second frame on the root turns by the clip (its
        // first comes in where the body faced, and anything else there is a jump the bones ease: PlayerBody.Update).
        if (turnSerial != turn.Serial)
        {
            Restart(channel);
            turnClip = turn.Clip;
            turnSerial = turn.Serial;
        }
        else rootByClip = true;
        Drive(turn.Clip, turn.Moment / Mathf.Max(.05f, channel.Length));
        legYaw = root;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, root);
        legsPlanted = true;
        return true;
    }

    private void EndRunTurn()
    {
        yawVelocity = 0f;
        var channel = Find(turnClip);
        turnLeaving = turnClip;
        turnLeavingFacing = player.Actions != null ? player.Actions.RunTurn.PoseFacing : 0f;
        turnClip = null;
        bool onFeet = grounded && (state == MovementState.Standing || state == MovementState.Crouching);
        if (onFeet && channel != null && HorizontalSpeed() > walkClipSpeed) StepFrom(channel);
    }
}
