using UnityEngine;

/// <summary>
/// The body's fingers on one hand, bent joint by joint from the knuckle out so each points where the same joint of the
/// arms rig's hand points. The hand itself already has the rig hand's frame (ArmIK), so the directions carry over
/// between two different skeletons without knowing either one's bone axes.
/// </summary>
internal sealed class GunRigFingers
{
    public Animator Animator { get; }
    private readonly Transform[,] bones = new Transform[5, 3];
    // Each joint's direction to the next joint, in the joint's own frame (the last joint uses its tip bone when it has one).
    private readonly Vector3[,] axes = new Vector3[5, 3];

    private GunRigFingers(Animator animator) => Animator = animator;

    public static GunRigFingers Create(Animator animator, bool left)
    {
        if (animator == null || !animator.isHuman) return null;
        var hand = new GunRigFingers(animator);
        var first = left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;
        for (int f = 0; f < 5; f++)
        {
            for (int j = 0; j < 3; j++) hand.bones[f, j] = animator.GetBoneTransform((HumanBodyBones)((int)first + f * 3 + j));
            for (int j = 0; j < 3; j++)
            {
                var bone = hand.bones[f, j];
                if (bone == null) continue;
                Transform next = j < 2 ? hand.bones[f, j + 1] : bone.childCount > 0 ? bone.GetChild(0) : null;
                Vector3 local = next != null ? bone.InverseTransformDirection(next.position - bone.position) : Vector3.zero;
                hand.axes[f, j] = local.sqrMagnitude > 1e-10f ? local.normalized : j > 0 ? hand.axes[f, j - 1] : Vector3.zero;
            }
        }
        return hand;
    }

    /// <summary>Bends the fingers <paramref name="weight"/> of the way toward the rig hand's.</summary>
    public void Follow(GunRigArm arm, float weight)
    {
        if (arm == null || weight <= 0f) return;
        for (int f = 0; f < 5; f++)
            for (int j = 0; j < 3; j++)
            {
                var bone = bones[f, j];
                if (bone == null || axes[f, j] == Vector3.zero || !arm.Direction(f, j, out Vector3 wanted)) continue;
                Vector3 current = bone.TransformDirection(axes[f, j]);
                Vector3 target = Vector3.Slerp(current, wanted.normalized, Mathf.Clamp01(weight));
                bone.rotation = Quaternion.FromToRotation(current, target) * bone.rotation;
            }
    }
}
