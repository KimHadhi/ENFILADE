using UnityEngine;

// FPSBody: the gun aimed while the gun's own aim clips (BIPER Combat Girls) have the arms. One part of the class; the rest
// is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    // Aiming standing, crouched or in a step dodge the aim clips (Combat Girls *_AimIdle, *_AimWalk_*, *_AimJog) have the
    // upper body and arms (PlayerBody.AimsOnClip), and the gun is held with its eye point on the camera
    // (FPSGun.HoldFromEyes), the camera on the right eye (PlayerBody.Look). FPSAF's layers are out with its upper body, so
    // what they did to the gun at the hip goes on here, in the same order and measure:
    // - the gun's own action worked after a shot (a bolt, a pump: its track's move, aimedCycleMove of it, FeedAction), as
    //   GunActionLayer moves it, so the hand that works it (its contact, PlayerBodyHands) works it on the gun;
    // - the kick of a shot (KINEMATION's recoil animation), gunKick of it as the Additive layer's alpha has it at the hip,
    //   in the view's frame. Taken whole it kicked 1.67 times as hard as at the hip (0.9.320).
    // Then the hands are put on the gun (PlayerBodyHands, 320).
    private void AimOnClip(Transform gun, float weight)
    {
        if (weight <= 0f) return;
        float acted = hasContact ? contactWeight * weight : 0f;
        if (acted > 0f)
        {
            Quaternion facing = gun.rotation;
            gun.SetPositionAndRotation(gun.position + facing * (contact.GunMove * acted),
                facing * Quaternion.Slerp(Quaternion.identity, contact.GunTurn, acted));
        }
        if (recoil == null || eyes == null) return;
        float kick = weight * gunKick;
        Quaternion view = eyes.rotation;
        gun.SetPositionAndRotation(gun.position + view * recoil.OutLoc * kick,
            Quaternion.Slerp(gun.rotation, view * recoil.OutRot * Quaternion.Inverse(view) * gun.rotation, kick));
    }
}
