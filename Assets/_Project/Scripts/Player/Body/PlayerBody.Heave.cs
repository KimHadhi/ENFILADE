using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The climb up from a hang. Its clip keeps its own rise and travel (Mixamo.clips.csv): a real mantle that pulls up, presses
/// over the hands and steps onto the top. So the body doesn't ride the capsule, which only goes ahead to where the player
/// ends up. The hands hold the body as they do in a hang (PlayerBody.Grip) until the arms have pressed to their longest and
/// start to bend again. The clip then carries on from where the hands left the body, and the last of it settles onto the
/// capsule, feet on its floor and hips over it, so the standing clips take over from there. All of it is measured from the
/// pose, for any clip that climbs by itself and any character.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Min(.01f), Tooltip("Time to stand up from the crouch a climb ends in (s).")]
    private float heaveStandTime = .35f;

    // The arms count as pressing from this share of their length, and have finished once they bend back by this much more.
    private const float ArmsPressing = .8f, ArmsBendBack = .02f;
    // If the arms never get that far the hands still let go here at the latest (share of the move).
    private const float HandsLatest = .8f;
    // The body starts settling onto the capsule this share of the way from the hands letting go to the end of the move.
    private const float SettleFrom = .3f;
    // The last of the way off the climb clip, where what is left of its offset is the standing clip's own sway.
    private const float SettleOutWeight = .2f;

    private Channel heaveClip;
    private bool heaving, heavePressing;
    private float heavePeak, heaveLetGoAt, heaveSettled, standBlendLeft;
    private Vector3 heaveRoot;

    /// <summary>The arms have finished pressing and the hands come off the ledge.</summary>
    internal bool HeaveHandsFree => heaving && !heavePressing;

    private bool HeavePressing => heaving && heavePressing && !dead && state == MovementState.Mantle;

    private void StartHeave()
    {
        heaving = heavePressing = true;
        heavePeak = heaveSettled = 0f;
        heaveLetGoAt = 1f;
        heaveRoot = body.position;
        heaveClip = Find(obstacleClip);
    }

    private void CarryHeave(Vector3 placed, float dt)
    {
        if (!heaving) return;
        var traversal = player.Abilities.Traversal;
        bool climbing = !dead && state == MovementState.Mantle && traversal.Obstacle.Kind == VaultKind.Heave;
        if (dead || hips == null || leftLeg == null || rightLeg == null || (!climbing && (heaveClip == null || heaveClip.Weight <= 0f)))
        {
            heaving = false;
            return;
        }
        float progress = climbing ? traversal.ObstacleProgress : 1f;
        if (!climbing || traversal.Ledge.Surface == null) heavePressing = false;
        if (heavePressing)
        {
            heaveRoot = body.position;
            float press = hands != null ? hands.ArmPress() : 0f;
            if (press >= ArmsPressing) heavePeak = Mathf.Max(heavePeak, press);
            if (progress < HandsLatest && (heavePeak <= 0f || press >= heavePeak - ArmsBendBack)) return;
            heavePressing = false;
            heaveLetGoAt = progress;
        }
        float soles = Mathf.Min(leftLeg.Foot.position.y - leftLeg.Sole, rightLeg.Foot.position.y - rightLeg.Sole) - body.position.y;
        Vector3 settled = placed - Vector3.ProjectOnPlane(hips.position - body.position, Vector3.up);
        settled.y = transform.position.y - soles;
        if (climbing)
        {
            // The capsule is still coming up from below the top, and the feet are already on it.
            settled.y = Mathf.Max(settled.y, heaveRoot.y);
            heaveSettled = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.Lerp(heaveLetGoAt, 1f, SettleFrom), 1f, progress));
        }
        else heaveSettled = Mathf.MoveTowards(heaveSettled, 1f, dt / blendTime);
        body.position = Vector3.Lerp(heaveRoot, settled, heaveSettled);
        if (!climbing && heaveClip != null && heaveClip.Weight < SettleOutWeight)
            body.position = Vector3.Lerp(placed, body.position, heaveClip.Weight / SettleOutWeight);
    }
}
