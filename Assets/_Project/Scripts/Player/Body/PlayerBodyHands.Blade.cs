using UnityEngine;

/// <summary>
/// Both hands on the katana's handle (KatanaView), as a sword is held: the right under the guard, the left at the
/// pommel, each palm onto the handle from its own side, the fingers round it toward the edge and the thumbs toward the
/// blade. The fingers close round the handle's real thickness the way they close round a swing bar (HandGrip.Bar).
/// The view moves the handle along the move's arc; the arms only follow it.
/// </summary>
public sealed partial class PlayerBodyHands
{
    private KatanaView blade;

    private bool HoldsBlade => blade != null && blade.IsActive;

    private bool BladeTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        if (blade == null || !blade.Grip(isLeft, out Vector3 handle, out Vector3 fingers, out Vector3 palm)) return false;
        rotation = arm.HandRotation(fingers, palm);
        Vector3 palmPoint = handle - palm * (blade.HandleRadius + arm.PalmDepth + ladderPalmClearance);
        position = arm.WristAtPalm(palmPoint, rotation);
        // The elbows down and a little out, as with the fists.
        Vector3 side = transform.right * (isLeft ? -1f : 1f);
        hint = arm.Upper.position + side * .2f - transform.forward * .05f - transform.up * .35f;
        return true;
    }

    private void CurlBladeFingers(ArmIK arm)
    {
        float weight = weights[(int)Hold.Blade];
        if (weight <= 0f || blade == null) return;
        arm.GripBar(blade.HandleRadius, ladderPalmClearance, fingerSurfaceClearance, weight, 1f);
    }
}
