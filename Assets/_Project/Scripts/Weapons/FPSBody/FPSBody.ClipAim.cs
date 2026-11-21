using UnityEngine;

// FPSBody: the gun aimed on the gun's own aim clips (BIPER Combat Girls). One part of the class; the rest is in FPSBody.cs
// and the files beside it.
public sealed partial class FPSBody
{
    // Aiming standing or crouched, the gun's aim clips (Combat Girls *_AimIdle, *_AimWalk_*, *_AimJog) have the arms
    // (PlayerBody.AimsOnClip): the gun goes where the clip's right hand has it, held in that hand as FPSAF holds it
    // (IK WeaponBoneRight, as FollowHand), turned so its sight looks where the view looks, and the camera comes onto that
    // sight line (SightLineEye, PlayerBody.Look): the gun stays where the clip holds it, at the shoulder and cheek, as
    // others see it. FPSAF's Ads brought the sight onto the camera between the eyes instead, and the gun went through the
    // middle of the face (the user, 2026-11-22: "조준하는 것도 이 애니메이션 써. 지금 제 3자가 보기에 내 미간 사이를
    // 뚫어서 총을 위치하게 해서 조준하잖아, 카메라 때문이지만"); so did 0.9.318, which moved the gun across onto the
    // camera ("하나도 안 달라졌어"). Here the gun only takes up what the camera's own easing left (body sway, a wall), so the
    // sight stays on the screen's middle. The kick of a shot (KINEMATION's recoil) goes on top in the view's frame. Then
    // the hands are put on the gun (PlayerBodyHands, 320).
    private void AimOnClip(Transform gun, float weight)
    {
        var sight = held.EyePoint;
        var hand = rig != null ? rig.WeaponBoneRight : null;
        if (weight <= 0f || sight == null || hand == null || eyes == null) return;
        Quaternion view = eyes.rotation;
        Vector3 at = SightLine(gun, sight, hand, view, out Quaternion turn, out Vector3 point);
        // Across the view only, what the camera's easing left between it and the sight line.
        Vector3 ahead = view * Vector3.forward;
        Vector3 off = point - eyes.position;
        at -= off - ahead * Vector3.Dot(off, ahead);
        if (recoil != null)
        {
            at += view * recoil.OutLoc;
            turn = view * recoil.OutRot * Quaternion.Inverse(view) * turn;
        }
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, at, weight), Quaternion.Slerp(gun.rotation, turn, weight));
    }

    /// <summary>Where the camera goes aiming on the aim clips (PlayerBody.Look, before it is placed): the point of the gun's
    /// sight line level with <paramref name="eye"/>, the gun in the clip's right hand turned so its sight looks along
    /// <paramref name="view"/>.</summary>
    internal bool SightLineEye(Vector3 eye, Quaternion view, out Vector3 onSight)
    {
        onSight = eye;
        var sight = held != null ? held.EyePoint : null;
        var hand = rig != null ? rig.WeaponBoneRight : null;
        var gun = rig != null ? rig.IkWeaponBone : null;
        if (!HoldsGun || sight == null || hand == null || gun == null) return false;
        SightLine(gun, sight, hand, view, out _, out Vector3 point);
        Vector3 ahead = view * Vector3.forward;
        onSight = point + ahead * Vector3.Dot(eye - point, ahead);
        return true;
    }

    // The gun in the clip's right hand (its root on IK WeaponBoneRight) turned so its sight looks along the view: where its
    // root goes, how it is turned, and where its sight point is then. The sight hangs under the gun (IK WeaponBone) at a
    // place that doesn't change while it is held.
    private static Vector3 SightLine(Transform gun, Transform sight, Transform hand, Quaternion view, out Quaternion turn, out Vector3 point)
    {
        Quaternion sightTurn = Quaternion.Inverse(gun.rotation) * sight.rotation;
        turn = view * Quaternion.Inverse(sightTurn);
        Vector3 at = hand.position;
        // The sight's offset from the gun's root, turned with the gun.
        point = at + turn * (Quaternion.Inverse(gun.rotation) * (sight.position - gun.position));
        return at;
    }
}
