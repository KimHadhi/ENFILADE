using UnityEngine;

// GunClipRetarget: The support hand. One part of the class; the rest is in GunClipRetarget.cs and the files beside it.
internal static partial class GunClipRetarget
{
    // How the hands hold the gun as the body is placed: how far the farther wrist is from where KINEMATION's hold puts it on
    // the gun (m in game; Retarget Pro's FPS feature leaves a hand that can't reach short of its place), and how straight the
    // support arm is. Measured against the hold itself, not the gun bone: a gun whose bone is ahead of the support hand
    // (the Herrington's, at its pump) read as reaching when its hands fell short.
    private struct Placement
    {
        public float miss, extension;
        public bool Reaches => miss <= StanceReachSlack && extension <= StanceArmStraight;
    }

    private static Placement Measure(Transform body, Transform weapon, Transform source, Transform sourceWeapon, float fit)
    {
        var shoulder = PreviewHumanoid.Bone(body, HumanBodyBones.LeftUpperArm);
        var elbow = PreviewHumanoid.Bone(body, HumanBodyBones.LeftLowerArm);
        var wrist = PreviewHumanoid.Bone(body, HumanBodyBones.LeftHand);
        if (shoulder == null || elbow == null || wrist == null) return new Placement { miss = 0f, extension = 0f };
        float arm = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
        return new Placement
        {
            miss = Mathf.Max(Miss(wrist, FindLike(source, "hand_l")), Miss(PreviewHumanoid.Bone(body, HumanBodyBones.RightHand), FindLike(source, "hand_r"))),
            extension = arm > 1e-5f ? Vector3.Distance(shoulder.position, wrist.position) / arm : 0f
        };

        // Where the FPS feature asks this wrist to be: KINEMATION's hand on its gun, carried onto ours at the bake's size.
        float Miss(Transform hand, Transform sourceHand)
        {
            if (hand == null || sourceHand == null) return 0f;
            Vector3 wanted = weapon.position + weapon.rotation * (Quaternion.Inverse(sourceWeapon.rotation) * (sourceHand.position - sourceWeapon.position))
                / Mathf.Max(.01f, fit);
            return Vector3.Distance(hand.position, wanted) * fit;
        }
    }
}
