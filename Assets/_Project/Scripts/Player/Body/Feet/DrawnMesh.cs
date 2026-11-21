using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a look's meshes are drawn as its model stands now, before any clip has posed it: every vertex with the keys as
/// dressing left them, skinned by Unity's own rule (each bone's pose times its bind pose, blended by the weights), in the
/// model's frame and units. The model hasn't been drawn yet when a body is dressed, so nothing can be asked of the GPU.
/// What the soles stand on (DrawnSole) and the heels they stand in (DrawnHeels) are both read from it, so the two always
/// measure the same feet.
/// </summary>
internal static class DrawnMesh
{
    // Reused between reads: the key's moves on one mesh and each bone's skinning.
    private static Vector3[] deltas = Array.Empty<Vector3>();
    private static Matrix4x4[] skin = Array.Empty<Matrix4x4>();

    /// <summary>The mesh a renderer draws: a skinned renderer's own, else its filter's; null when it has none.</summary>
    internal static Mesh MeshOf(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
        return renderer != null && renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
    }

    /// <summary>
    /// Fills <paramref name="points"/> with where each vertex of <paramref name="mesh"/> (the renderer's, readable) is drawn
    /// now, in the frame <paramref name="toModel"/> leads to. A vertex the fit left out of the mesh (a sock's sole that came
    /// out under a boot, OutfitFit.DrawnVertices) isn't drawn: its point is NaN.
    /// </summary>
    internal static void Read(Renderer renderer, Mesh mesh, Matrix4x4 toModel, List<Vector3> points)
    {
        mesh.GetVertices(points);
        int count = points.Count;
        var skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null) AddKeys(skinned, mesh, points);
        var bones = skinned != null ? skinned.bones : null;
        var poses = skinned != null ? mesh.bindposes : null;
        var perVertex = skinned != null ? mesh.GetBonesPerVertex() : default;
        // An unskinned mesh, or a skinned one with no bones, is drawn where its object holds it.
        if (bones == null || bones.Length == 0 || poses.Length == 0 || perVertex.Length != count)
        {
            Matrix4x4 held = toModel * renderer.transform.localToWorldMatrix;
            for (int i = 0; i < count; i++) points[i] = held.MultiplyPoint3x4(points[i]);
        }
        else
        {
            if (skin.Length < bones.Length) skin = new Matrix4x4[bones.Length];
            Matrix4x4 own = toModel * renderer.transform.localToWorldMatrix;
            for (int b = 0; b < bones.Length; b++)
                skin[b] = bones[b] != null && b < poses.Length ? toModel * bones[b].localToWorldMatrix * poses[b] : own;
            var weights = mesh.GetAllBoneWeights();
            int at = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = points[i], sum = Vector3.zero;
                float total = 0f;
                for (int n = perVertex[i]; n > 0; n--, at++)
                {
                    var bone = weights[at];
                    if (bone.boneIndex >= bones.Length) continue;
                    sum += bone.weight * skin[bone.boneIndex].MultiplyPoint3x4(v);
                    total += bone.weight;
                }
                points[i] = total > 0f ? sum / total : new Vector3(float.NaN, float.NaN, float.NaN);
            }
        }
        var drawn = OutfitFit.DrawnVertices(mesh);
        if (drawn == null) return;
        for (int i = 0; i < count && i < drawn.Length; i++)
            if (!drawn[i]) points[i] = new Vector3(float.NaN, float.NaN, float.NaN);
    }

    // Each key the renderer holds, its last frame taken the share of the way the weight goes (BOOTH keys have one frame,
    // at 100).
    private static void AddKeys(SkinnedMeshRenderer renderer, Mesh mesh, List<Vector3> points)
    {
        int count = points.Count;
        for (int k = 0; k < mesh.blendShapeCount; k++)
        {
            float weight = renderer.GetBlendShapeWeight(k);
            int frames = mesh.GetBlendShapeFrameCount(k);
            if (weight == 0f || frames == 0) continue;
            float full = mesh.GetBlendShapeFrameWeight(k, frames - 1);
            if (full == 0f) continue;
            if (deltas.Length != count) deltas = new Vector3[count];
            mesh.GetBlendShapeFrameVertices(k, frames - 1, deltas, null, null);
            float share = weight / full;
            for (int i = 0; i < count; i++) points[i] += deltas[i] * share;
        }
    }
}
