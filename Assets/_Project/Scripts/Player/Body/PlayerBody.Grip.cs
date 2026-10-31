using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Keeps the hands on what they hold. After the clip has posed the body, the whole body moves so the wrists rest there:
/// the hands stay put and the body hangs and swings below them, whatever the clip or character.
/// - On a ledge, only height and distance from the wall; sideways the body stays under the camera.
/// - On a ladder, distance from the rungs and centre between the rails; the clip climbs.
/// - On a zipline, onto the cable; the ride carries the body along it.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Ledge grip")]
    [SerializeField, Tooltip("Where the wrists rest while hanging: above the ledge top, and in from its face (m).")]
    private Vector2 ledgeGrip = new Vector2(.05f, .05f);

    private Transform leftHand, rightHand;
    private Vector3 gripOffset;
    private float gripWeight;

    // Eases in and out with the clip crossfade, so catching and letting go never snap the body.
    private void Grip(float dt)
    {
        bool holding = !dead && (state == MovementState.LedgeHang || state == MovementState.Ladder || state == MovementState.Zipline);
        gripWeight = Mathf.MoveTowards(gripWeight, holding ? 1f : 0f, dt / blendTime);
        if (gripWeight <= 0f || leftHand == null || rightHand == null) return;
        if (holding) MeasureGrip((leftHand.position + rightHand.position) * .5f);
        body.position += gripOffset * gripWeight;
    }

    private void MeasureGrip(Vector3 hands)
    {
        var fixtures = player.Abilities.Fixtures;
        switch (state)
        {
            case MovementState.LedgeHang:
                var ledge = player.Abilities.Traversal.Ledge;
                if (ledge.Surface == null) return;
                Transform support = ledge.Surface.transform;
                Vector3 outward = support.TransformDirection(ledge.LocalNormal);
                outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
                Vector3 grip = support.TransformPoint(ledge.LocalEdge) + Vector3.up * ledgeGrip.x - outward * ledgeGrip.y;
                Vector3 error = grip - hands;
                gripOffset = Vector3.up * error.y + outward * Vector3.Dot(error, outward);
                break;
            case MovementState.Ladder:
                if (fixtures.Ladder != null) gripOffset = Vector3.ProjectOnPlane(fixtures.Ladder.transform.position - hands, Vector3.up);
                break;
            case MovementState.Zipline:
                gripOffset = Vector3.ProjectOnPlane(fixtures.ZiplineFrom - hands, fixtures.ZiplineDirection);
                break;
        }
    }
}
