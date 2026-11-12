using UnityEngine;

/// <summary>
/// A humanoid bone of an editor preview body (KINEMATION Retarget Pro's preview copies of our skins), by the transform
/// name its avatar maps it to: a preview's Animator isn't always bound, so its GetBoneTransform can't be trusted.
/// </summary>
internal static class PreviewHumanoid
{
    public static Transform Bone(Transform body, HumanBodyBones bone)
    {
        var animator = body != null ? body.GetComponentInChildren<Animator>() : null;
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
        string humanName = HumanTrait.BoneName[(int)bone];
        foreach (var mapped in animator.avatar.humanDescription.human)
            if (mapped.humanName == humanName) return Find(body, mapped.boneName);
        return null;
    }

    public static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
            if (Find(child, name) is Transform found) return found;
        return null;
    }
}
