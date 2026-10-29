using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Keeps the hands on the ledge while hanging. After the clip has posed the body, the whole body moves so the wrists
/// rest on the ledge top: the hands stay put and the body hangs and swings below them, whatever the clip or character.
/// Only height and distance from the wall are corrected; sideways the body stays under the camera.
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
    private void GripLedge(float dt)
    {
        bool hanging = state == MovementState.LedgeHang && !dead;
        gripWeight = Mathf.MoveTowards(gripWeight, hanging ? 1f : 0f, dt / blendTime);
        if (gripWeight <= 0f || leftHand == null || rightHand == null) return;
        var ledge = player.Abilities.Traversal.Ledge;
        if (hanging && ledge.Surface != null)
        {
            Transform support = ledge.Surface.transform;
            Vector3 outward = support.TransformDirection(ledge.LocalNormal);
            outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            Vector3 grip = support.TransformPoint(ledge.LocalEdge) + Vector3.up * ledgeGrip.x - outward * ledgeGrip.y;
            Vector3 error = grip - (leftHand.position + rightHand.position) * .5f;
            gripOffset = Vector3.up * error.y + outward * Vector3.Dot(error, outward);
        }
        body.position += gripOffset * gripWeight;
    }
}
