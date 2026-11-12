using UnityEngine;

/// <summary>
/// The support hand on a grip the gun wears (GunAttachments, picked in the customize menu): the arm bends until the
/// middle of the palm is where KINEMATION's Tactical Shooter Pack puts its own hand on that grip, the fingers pointing
/// and the palm facing as its hand does there (FPSGun.LeftGrip, from the grip's LeftHand, made into directions any hand
/// takes). The fingers then close round the grip's own mesh like the rest of the gun (GunFingers). It lets go while the
/// hands are busy (a reload, a draw, a look over the gun) and comes back over GripEase. The grip sits no further back
/// than KINEMATION's support hand holds the gun (GunAttachments.SeatGrips), and the elbow bends to the side theirs does
/// (FPSBody.ElbowWay), as for the plain hold, so the arm reaches out to it as theirs reaches the handguard.
/// </summary>
public sealed partial class PlayerBodyHands
{
    // How long the hand takes to go onto the grip or come off it (s, 0 to full).
    private const float GripEase = .2f;
    private float gripOn;

    private void HoldGunGrip(ArmIK arm, float dt)
    {
        var gunBody = FPSBody.Holding(weapons);
        bool wanted = gunBody != null && !gunBody.ReloadingGun && gunBody.LeftGrip(out _, out _, out _);
        gripOn = Mathf.MoveTowards(gripOn, wanted ? 1f : 0f, dt / GripEase);
        float weight = gripOn * weights[(int)Hold.Gun] * (1f - leftOffGun) * OffHandKept;
        if (arm == null || weight <= 0f || gunBody == null || !gunBody.LeftGrip(out Vector3 palm, out Vector3 fingers, out Vector3 facing)) return;
        Quaternion rotation = arm.HandRotation(fingers, facing);
        // The elbow toward KINEMATION's way (FPSBody.ElbowWay), else the side it bends to now.
        Vector3 hint = gunBody.ElbowWay(true, out Vector3 way) ? arm.Upper.position + (palm - arm.Upper.position) * .5f + way * .3f : arm.Elbow;
        arm.Solve(arm.WristAtPalm(palm, rotation), rotation, hint, weight, 1f, 0f);
    }

    /// <summary>How far the support hand is on a grip the gun wears (0-1), for GunFingers to close it round.</summary>
    private float OnAttachedGrip => gripOn;
}
