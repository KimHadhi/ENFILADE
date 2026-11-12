using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which meshes of a character sit on its head, read from the skin's own bone weights: hair, a face mesh, animal ears.
/// The camera sits inside the head, so these show in first person only as the head does (PlayerBody.ApplyHeadVisibility).
/// Names alone can't tell: a BOOTH avatar's face mesh is called "Body" (Mayo, Ichigo) while Kazusa's body is, so a mesh
/// counts as the head's when nearly all of its skin weight hangs from the head bone or below it. Any humanoid works this
/// way, a character a player brings included, with nothing set up per character.
/// </summary>
internal static class HeadMeshes
{
    // Share of a mesh's skin weight on the head and below for the mesh to be the head's.
    private const float HeadShare = .8f;
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
