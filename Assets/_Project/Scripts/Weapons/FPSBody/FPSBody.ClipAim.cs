using UnityEngine;

// FPSBody: the gun aimed on the gun's own aim clips (BIPER Combat Girls). One part of the class; the rest is in FPSBody.cs
// and the files beside it.
public sealed partial class FPSBody
{
    // Aiming standing or crouched, the gun's aim clips (Combat Girls *_AimIdle, *_AimWalk_*, *_AimJog) have the arms
    // (PlayerBody.AimsOnClip): the gun goes where the clip's right hand has it, held in that hand as FPSAF holds it
    // (IK WeaponBoneRight, as FollowHand), turned so its sight looks where the view looks, and moved across only so far
    // that the sight's line runs through the eyes. The camera stays on the eyes and the gun stays where the hand has it,
    // ahead of the face. FPSAF's Ads brought the sight onto the eyes instead, so the gun went through the head between
    // the brows as others saw it (the user, 2026-11-22: "조준하는 것도 이 애니메이션 써. 지금 제 3자가 보기에 내 미간
    // 사이를 뚫어서 총을 위치하게 해서 조준하잖아, 카메라 때문이지만"). The kick of a shot (KINEMATION's recoil) goes
    // on top in the view's frame. Then the hands are put on the gun (PlayerBodyHands, 320).
    private void AimOnClip(Transform gun, float weight)
    {
        var sight = held.EyePoint;
        var hand = rig != null ? rig.WeaponBoneRight : null;
        if (weight <= 0f || sight == null || hand == null || eyes == null) return;
        // The sight as the gun carries it.
        Vector3 sightAt = gun.InverseTransformPoint(sight.position);
        Quaternion sightTurn = Quaternion.Inverse(gun.rotation) * sight.rotation;
        Quaternion view = eyes.rotation;
        Quaternion turn = view * Quaternion.Inverse(sightTurn);
        Vector3 at = hand.position;
        // Across the view only: what lies along it is how far ahead of the eyes the hand holds the gun.
        Vector3 ahead = view * Vector3.forward;
        Vector3 off = at + turn * sightAt - eyes.position;
        at -= off - ahead * Vector3.Dot(off, ahead);
        if (recoil != null)
        {
            at += view * recoil.OutLoc;
            turn = view * recoil.OutRot * Quaternion.Inverse(view) * turn;
        }
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, at, weight), Quaternion.Slerp(gun.rotation, turn, weight));
    }
}
