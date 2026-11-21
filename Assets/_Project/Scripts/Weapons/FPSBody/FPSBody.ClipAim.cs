using UnityEngine;

// FPSBody: the gun aimed while the gun's own aim clips (BIPER Combat Girls) have the arms. One part of the class; the rest
// is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    // Aiming standing, crouched or in a step dodge the aim clips (Combat Girls *_AimIdle, *_AimWalk_*, *_AimJog) have the
    // upper body and arms (PlayerBody.AimsOnClip), and the gun is held as FPSAF's Ads holds it, its eye point on the camera
    // (FPSGun.HoldFromEyes), the camera on the right eye (PlayerBody.Look). FPSAF's recoil layer is out with its upper
    // body, so the kick of a shot (KINEMATION's recoil) goes on here, in the view's frame. Then the hands are put on the
    // gun (PlayerBodyHands, 320).
    private void AimKick(Transform gun, float weight)
    {
        if (weight <= 0f || recoil == null || eyes == null) return;
        Quaternion view = eyes.rotation;
        gun.SetPositionAndRotation(gun.position + view * recoil.OutLoc * weight,
            Quaternion.Slerp(gun.rotation, view * recoil.OutRot * Quaternion.Inverse(view) * gun.rotation, weight));
    }
}
