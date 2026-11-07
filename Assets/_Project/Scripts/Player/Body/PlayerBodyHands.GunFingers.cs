using UnityEngine;

/// <summary>
/// The hands on the gun's real surface (GunSurface, the gun's own mesh triangles): a palm that FPSAF left inside the gun
/// is pushed back out along the way it faces, until its skin rests on the gun, and then each finger closes round the gun
/// joint by joint only until it meets it (HandGrip, this body's own phalanges and skin). A bar standing in for the gun
/// wasn't where the gun is, so fingers sank in or were thrown open (user, 2026-11-08). The trigger finger keeps
/// KINEMATION's place. Each hand rests on the gun as far as it is on its grip (FPSBody.GripHold): a reload's hand on the
/// magazine or the bolt lets go as it leaves and takes hold again as it comes back, instead of the whole reload and the
/// draw playing with the hands off the gun (user, 2026-11-08).
/// </summary>
public sealed partial class PlayerBodyHands
{
    [SerializeField, Range(0f, 1f), Tooltip("How far the hands rest on and close round the gun's real surface (1), "
        + "instead of KINEMATION's hand and finger places alone (0).")]
    private float gunFingerWrap = 1f;

    private readonly float[] gunWrap = new float[2];
    private readonly float[] gunHold = { 1f, 1f };
    private readonly GunSurface[] gunSurfaces = new GunSurface[2];
    private readonly Quaternion[] triggerFinger = new Quaternion[3];

    private void CurlGunFingers(ArmIK arm, bool isLeft, float dt)
    {
        int side = isLeft ? 0 : 1;
        var gunBody = FPSBody.Holding(weapons);
        var surface = gunFingerWrap > 0f && gunBody != null ? gunBody.HeldSurface : null;
        if (surface != null) gunSurfaces[side] = surface;
        gunWrap[side] = Mathf.MoveTowards(gunWrap[side], surface != null ? 1f : 0f, dt / blendTime);
        // How far this hand is on its grip, kept through the ease out once the gun is gone.
        if (surface != null) gunHold[side] = gunBody.GripHold(isLeft);
        surface = gunSurfaces[side];
        float weight = gunWrap[side] * gunHold[side] * weights[(int)Hold.Gun] * (isLeft ? 1f - leftOffGun : 1f) * gunFingerWrap;
        if (weight <= 0f || surface == null || surface.Gun == null) return;

        PalmOutOfGun(arm, surface, weight);

        // The trigger finger stays where KINEMATION's clip put it.
        Transform[] index = null;
        if (!isLeft && boundAnimator != null)
        {
            index = new[]
            {
                boundAnimator.GetBoneTransform(HumanBodyBones.RightIndexProximal),
                boundAnimator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate),
                boundAnimator.GetBoneTransform(HumanBodyBones.RightIndexDistal)
            };
            for (int i = 0; i < 3; i++) if (index[i] != null) triggerFinger[i] = index[i].localRotation;
        }
        arm.GripSurface(surface, fingerSurfaceClearance, weight);
        if (index != null)
            for (int i = 0; i < 3; i++) if (index[i] != null) index[i].localRotation = triggerFinger[i];
    }

    // Six points on this palm's skin (heel and knuckle rows, both edges and the middle), each looked for from behind the
    // hand along the way the palm faces: where the gun's face comes before the skin, the hand is in the gun by that much,
    // and the whole hand goes back out by the deepest. Lengths are this hand's own (palm length, width, depth).
    private static void PalmOutOfGun(ArmIK arm, GunSurface surface, float weight)
    {
        Vector3 facing = arm.PalmFacing.normalized, forward = arm.FingersForward.normalized;
        Vector3 across = Vector3.Cross(facing, forward).normalized;
        Vector3 skin = arm.PalmPosition + facing * arm.PalmDepth;
        float back = arm.PalmLength;
        float depth = 0f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 point = skin + forward * (arm.PalmLength * (i < 3 ? -.25f : .35f)) + across * (arm.HalfPalmWidth * .8f * (i % 3 - 1));
            if (surface.Raycast(point - facing * back, facing, back, out float distance))
                depth = Mathf.Max(depth, back - distance);
        }
        if (depth <= 0f) return;
        depth = Mathf.Min(depth, arm.PalmLength * .5f);
        Transform hand = arm.Hand;
        arm.Solve(hand.position - facing * depth, hand.rotation, arm.Elbow, weight, 1f, 0f);
    }
}
