using UnityEngine;

/// <summary>
/// Dual pistols (AkimboGuns, FPSBody.Akimbo). FPSAF holds the single gun in both hands; then FPSBody sets that gun apart
/// to the right and puts a copy where the left hand will hold it. Here the right hand goes back onto its gun where it now
/// is (as FPSAF had it on the gun), and the left hand onto the copy, held the way the right hand holds its own as seen
/// across the guns' sight lines: the middle of its palm, the way its fingers point and its palm faces, and its elbow. As far
/// as the left hand has its gun (FPSBody.AkimboHold, which comes and goes with a reload clip, a draw and a vault's plant),
/// and where the arm comes short, the gun stays in the hand (FPSBody.KeepLeftGunInHand). Its fingers take the shooting
/// hand's shape (AkimboFingers, from GunFingers).
/// </summary>
public sealed partial class PlayerBodyHands
{
    private void HoldAkimbo()
    {
        var gunBody = FPSBody.Holding(weapons);
        if (gunBody == null || left == null || right == null || !gunBody.AkimboHands(out float hold, out bool apart)) return;
        float onGun = weights[(int)Hold.Gun];
        // The right hand where its gun now is: FPSAF put it on the gun before the gun was set apart.
        if (apart && onGun > 0f && gunBody.HandOnGun(false, out Vector3 wrist, out Quaternion turn))
            right.Solve(wrist, turn, right.Elbow, onGun, 1f, 0f);
        float weight = hold * onGun * (1f - leftOffGun);
        if (weight <= 0f) return;
        Vector3 palm = gunBody.AkimboAcross(right.PalmPosition);
        Vector3 fingers = gunBody.AkimboAcrossWay(right.FingersForward);
        Vector3 facing = gunBody.AkimboAcrossWay(right.PalmFacing);
        Quaternion rotation = left.HandRotation(fingers, facing);
        Vector3 target = left.WristAtPalm(palm, rotation);
        left.Solve(target, rotation, gunBody.AkimboAcross(right.Elbow), weight, 1f, shoulderReach);
        gunBody.KeepLeftGunInHand(target, rotation, left.Hand.position, left.Hand.rotation);
    }

    // The left hand on a dual pistol's left gun wears the shooting hand's shape (the single gun's own, GunGrips "/R", or
    // Trigger), from the support hand's Cup as it takes the gun up. False when it holds no such gun.
    private bool AkimboFingers(ArmIK arm, FPSBody gunBody, float weight)
    {
        float hold = gunBody != null ? gunBody.AkimboHold : 0f;
        if (hold <= 0f) return false;
        var custom = gunCustom[1];
        if (custom != null) arm.WearShape(custom, HandGrip.Shape.Cup, hold, weight);
        else arm.WearShape(HandGrip.Shape.Trigger, HandGrip.Shape.Cup, hold, weight);
        return true;
    }
}
