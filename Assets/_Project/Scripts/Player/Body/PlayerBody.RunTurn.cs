using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body's side of a run's turn back (RunTurn): its clip on the turn's own clock, the body's root where the clip's turn has
/// it so the turn ends facing the way the view looked as it began, the spine turning the chest back toward the view from where
/// the clip has it (AimLook: RunTurnFacing), and the walk or run taking the stride from the clip's footfall as it ends
/// (StepFrom). A clip that turns the body in its own pose has the root round where the pose is held from as the turn begins and
/// back under the chest as it ends, and the pose the bones carry over each switch is turned back as much (BodyInertia.Reroot),
/// so the hips don't swing round over it.
/// </summary>
public sealed partial class PlayerBody
{
    // The turn's clip playing (null for none), and which way its chest faced on the last frame (world yaw, degrees).
    private string turnClip;
    private float turnChest;

    /// <summary>Where the stride is (0 the left foot landing, .5 the right): the foot a turn plants (RunTurn).</summary>
    internal float StridePhase => phase;

    /// <summary>Which way the body faces (world yaw, degrees).</summary>
    internal float FacingYaw => legYaw;

    // How far the turn's clip has the chest round against the body's root (degrees), for the spine's turn back to the view.
    private float RunTurnFacing => turnClip != null && player.Actions != null ? player.Actions.RunTurn.PoseFacing : 0f;

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
        if (turnClip == null)
        {
            Restart(channel);
            bodyInertia.Reroot(Mathf.DeltaAngle(legYaw, root));
            turnClip = turn.Clip;
        }
        Drive(turn.Clip, turn.Moment / Mathf.Max(.05f, channel.Length));
        legYaw = root;
        turnChest = turn.ChestYaw;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, root);
        legsPlanted = true;
        return true;
    }

    private void EndRunTurn()
    {
        bodyInertia.Reroot(Mathf.DeltaAngle(legYaw, turnChest));
        legYaw = turnChest;
        yawVelocity = 0f;
        var channel = Find(turnClip);
        turnClip = null;
        bool onFeet = grounded && (state == MovementState.Standing || state == MovementState.Crouching);
        if (onFeet && channel != null && HorizontalSpeed() > walkClipSpeed) StepFrom(channel);
    }
}
