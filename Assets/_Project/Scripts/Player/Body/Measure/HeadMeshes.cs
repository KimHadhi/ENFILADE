using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which meshes of a character sit on its head, read from the skin's own bone weights: hair, a face mesh, animal ears.
/// The camera sits inside the head, so these show in first person only as the head does (PlayerBody.ApplyHeadVisibility).
/// Names alone can't tell: a BOOTH avatar's face mesh is called "Body" (Mayo, Ichigo) while Kazusa's body is, so a mesh
/// counts as the head's when nearly all of its skin weight hangs from the head bone or below it. Any humanoid works this
/// way, a character a player brings included, with nothing set up per character.
/// Of those, the face (IsFace) is told from the hair the same way: in first person the hair is drawn and the face is not.
/// </summary>
internal static class HeadMeshes
{
    // Share of a mesh's skin weight on the head and below for the mesh to be the head's.
    private const float HeadShare = .8f;
    // A head mesh is the face with this share of its skin weight on the eyes or the jaw, or this share on the head bone itself
    // and shape keys of its own.
    private const float FaceBones = .005f, HeadItself = .5f;
    // Each mesh's total skin weight per bone slot, read once: bone weights are the mesh asset's, the bones the instance's.
    private static readonly Dictionary<Mesh, float[]> weights = new Dictionary<Mesh, float[]>();

    /// <summary>Whether <paramref name="skinned"/> hangs from <paramref name="head"/>. A mesh that can't be read never does.</summary>
    internal static bool OnHead(SkinnedMeshRenderer skinned, Transform head)
    {
        var mesh = skinned != null ? skinned.sharedMesh : null;
        if (mesh == null || head == null) return false;
        var perBone = WeightsOf(mesh);
        var bones = skinned.bones;
        if (perBone == null || bones == null) return false;
        float onHead = 0f, total = 0f;
        for (int i = 0; i < perBone.Length && i < bones.Length; i++)
        {
            total += perBone[i];
            if (bones[i] != null && (bones[i] == head || bones[i].IsChildOf(head))) onHead += perBone[i];
        }
        return total > 0f && onHead >= total * HeadShare;
    }

    /// <summary>
    /// Whether a mesh on the head is the face (the eyes, lids, lashes, brows, the mouth) and not hair, ears or a hat: it carries
    /// the eyes or the jaw, or it hangs mostly from the head bone itself and has shape keys (expressions, blinks). Hair hangs
    /// from bones of its own below the head, and a hat has no shape keys. The camera sits at the eyes, so in first person a
    /// face is only ever seen from inside, and the near clip plane can stay small (PlayerBody.firstPersonNear) with nothing of
    /// it to cut. A character whose face is in its body mesh has none: from the eyes its face lies behind or below the view.
    /// </summary>
    internal static bool IsFace(SkinnedMeshRenderer skinned, Animator animator)
    {
        var mesh = skinned != null ? skinned.sharedMesh : null;
        if (mesh == null || animator == null || !animator.isHuman) return false;
        var perBone = WeightsOf(mesh);
        var bones = skinned.bones;
        if (perBone == null || bones == null) return false;
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head), jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
        Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye), right = animator.GetBoneTransform(HumanBodyBones.RightEye);
        float total = 0f, face = 0f, itself = 0f;
        for (int i = 0; i < perBone.Length && i < bones.Length; i++)
        {
            total += perBone[i];
            var bone = bones[i];
            if (bone == null) continue;
            if (bone == left || bone == right || bone == jaw) face += perBone[i];
            else if (bone == head) itself += perBone[i];
        }
        if (total <= 0f) return false;
        return face > total * FaceBones || (mesh.blendShapeCount > 0 && itself >= total * HeadItself);
    }

    private static float[] WeightsOf(Mesh mesh)
    {
        if (weights.TryGetValue(mesh, out var known)) return known;
        float[] sums = null;
        if (mesh.isReadable)
        {
            sums = new float[Mathf.Max(1, mesh.bindposes.Length)];
            var all = mesh.GetAllBoneWeights();
            for (int i = 0; i < all.Length; i++)
                if (all[i].boneIndex >= 0 && all[i].boneIndex < sums.Length) sums[all[i].boneIndex] += all[i].weight;
        }
        weights[mesh] = sums;
        return sums;
    }
}
