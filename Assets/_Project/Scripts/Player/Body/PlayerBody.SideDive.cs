using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Backlog 135: a dive to the side, made from clips we have because no clip library we have holds one. The body heads
/// the way it dives like any front dive (PlayerBody.Lying), so with the dive going sideways the head already leads it.
/// It flies in the lying pose the landing slide and prone use (bellySlide, "diveslideidle"), not the forward dive's
/// arms-out reach, and is rolled onto its side about the way it flies so the chest faces where the player looked as the
/// dive began, like a goalkeeper's dive. Rolled about the hips so the body stays over the capsule. In with the dive's
/// own fold (MovementDive.PoseProgress); down, it rolls onto its belly over sideDiveSettleTime and lies on the way it went.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Range(0f, 90f), Tooltip("Side dive: how far the body rolls from face down toward facing the view (degrees).")]
    private float sideDiveRoll = 80f;
    [SerializeField, Min(.05f), Tooltip("Side dive: once down, how long the body takes to roll from its side onto its belly (s).")]
    private float sideDiveSettleTime = .3f;
    private float sideDiveWeight;
    private int sideDiveSide;
    private Vector3 sideDiveChest;
    private bool sideDiveLanded;

    /// <summary>This dive is a side dive: the body flies in the slide's lying pose.</summary>
    private bool SideDivePose => !dead && state == MovementState.Diving && player.Actions.Dive.Side != 0;

    private void SideDive(float dt)
    {
        var dive = player.Actions.Dive;
        if (SideDivePose)
        {
            // The chest turns toward where the view was as the dive began, and stays so however the view moves after.
            if (sideDiveSide == 0) sideDiveChest = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            sideDiveSide = dive.Side;
            float target = Mathf.Max(dive.PoseProgress, Mathf.Min(1f, stateTime / Mathf.Max(.05f, blendTime)));
            sideDiveWeight = Mathf.Max(sideDiveWeight, target);
        }
        else
        {
            bool down = sideDiveLanded && !dead && (state == MovementState.HeadFirstSliding || state == MovementState.Proning);
            sideDiveWeight = Mathf.MoveTowards(sideDiveWeight, 0f, dt / (down ? sideDiveSettleTime : blendTime));
        }
        if (sideDiveWeight <= 0f) { sideDiveSide = 0; sideDiveLanded = false; return; }
        if (sideDiveSide == 0 || sideDiveChest.sqrMagnitude < .0001f) return;

        // About the line the body lies on, face down turns toward the chest's way.
        Vector3 along = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (along.sqrMagnitude < .0001f) return;
        Vector3 chest = Vector3.ProjectOnPlane(sideDiveChest, along.normalized);
        if (chest.sqrMagnitude < .0001f) return;
        Vector3 axis = Vector3.Cross(Vector3.down, chest.normalized).normalized;
        Quaternion roll = Quaternion.AngleAxis(sideDiveRoll * Mathf.SmoothStep(0f, 1f, sideDiveWeight), axis);
        Vector3 pivot = hips != null ? hips.position : body.position;
        body.position = pivot + roll * (body.position - pivot);
        body.rotation = roll * body.rotation;
    }
}
