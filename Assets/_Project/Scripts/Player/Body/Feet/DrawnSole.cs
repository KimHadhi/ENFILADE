using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the soles of what a character draws are against its model's floor: the model's root, where every clip and the
/// feet on the ground (FootPlanting) put the soles. BOOTH avatars are made standing on the heels their own shoes have, and
/// other shoes or none don't reach that floor: Mayo's soles float 1 cm over it in her own pumps, 4.5 cm in the Sailor
/// Maiden loafers, 4.1 cm in the White Board boots and 5.8 cm barefoot, while Ichigo's sink 7 mm into it in her own shoes
/// and 13 mm in the Sera Yura ones (measured from the FBX files at their own size, 2026-11-12; the game draws Mayo 1.41
/// and Ichigo 1.53 times as tall; the user asked whether heels make the body float or sink). The lowest point a look draws is measured from its meshes as the model stands before any clip, its
/// keys as dressing left them, so the body can be stood on its real soles: any character in any shoes, a modder's too.
/// </summary>
internal static class DrawnSole
{
    // Reused between measures: vertices of one mesh, the key's moves on them, and each bone's skinning.
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static Vector3[] deltas = Array.Empty<Vector3>();
    private static Matrix4x4[] skin = Array.Empty<Matrix4x4>();
    // A renderer whose box starts this far (model units) over the lowest point found yet can't go lower.
    private const float Clear = .1f;

    /// <summary>
    /// The height, in <paramref name="model"/>'s own frame and units, of the lowest point it draws as it stands now: over
    /// its floor when its soles float, under it when they sink. Measure it before any clip has posed the model. Renderers
    /// in <paramref name="ignored"/> (a piece drawn only on its way off) don't count. Zero when nothing could be read.
    /// </summary>
    internal static float Of(Transform model, ICollection<Renderer> ignored = null)
    {
        if (model == null) return 0f;
        var renderers = new List<(float Bottom, Renderer Renderer)>();
        Matrix4x4 toModel = model.worldToLocalMatrix;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (ignored != null && ignored.Contains(renderer))) continue;
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            renderers.Add((BoxBottom(renderer.bounds, toModel), renderer));
        }
        // Lowest boxes first: once a sole is found, whatever starts well above it is passed over unread.
        renderers.Sort((a, b) => a.Bottom.CompareTo(b.Bottom));
        float lowest = float.PositiveInfinity;
        foreach (var (bottom, renderer) in renderers)
        {
            if (bottom > lowest + Clear) break;
            lowest = Mathf.Min(lowest, renderer is SkinnedMeshRenderer skinned ? Skinned(skinned, toModel, bottom) : Plain(renderer, toModel, bottom));
        }
        return float.IsInfinity(lowest) ? 0f : lowest;
    }

    // The lowest corner of a world box, in the model's frame.
    private static float BoxBottom(Bounds box, Matrix4x4 toModel)
    {
        float least = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? box.min.x : box.max.x, (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z);
            least = Mathf.Min(least, toModel.MultiplyPoint3x4(corner).y);
        }
        return least;
    }

    // A skinned mesh as its bones hold it now, with its keys: Unity's own skinning (each bone's pose times its bind pose,
    // blended by the weights), worked out here because the model hasn't been drawn yet. What can't be read counts by its box.
    private static float Skinned(SkinnedMeshRenderer renderer, Matrix4x4 toModel, float box)
    {
        var mesh = renderer.sharedMesh;
        if (mesh == null) return float.PositiveInfinity;
        if (!mesh.isReadable) return box;
        mesh.GetVertices(vertices);
        int count = vertices.Count;
        for (int k = 0; k < mesh.blendShapeCount; k++)
        {
            float weight = renderer.GetBlendShapeWeight(k);
            int frames = mesh.GetBlendShapeFrameCount(k);
            if (weight == 0f || frames == 0) continue;
            // The key's last frame, taken the share of the way the weight goes (BOOTH keys have one frame, at 100).
            float full = mesh.GetBlendShapeFrameWeight(k, frames - 1);
            if (full == 0f) continue;
            if (deltas.Length != count) deltas = new Vector3[count];
            mesh.GetBlendShapeFrameVertices(k, frames - 1, deltas, null, null);
            float share = weight / full;
            for (int i = 0; i < count; i++) vertices[i] += deltas[i] * share;
        }
        var bones = renderer.bones;
        var poses = mesh.bindposes;
        var perVertex = mesh.GetBonesPerVertex();
        if (bones.Length == 0 || poses.Length == 0 || perVertex.Length != count) return Plain(renderer.transform, toModel);
        if (skin.Length < bones.Length) skin = new Matrix4x4[bones.Length];
        Matrix4x4 own = toModel * renderer.transform.localToWorldMatrix;
        for (int b = 0; b < bones.Length; b++)
            skin[b] = bones[b] != null && b < poses.Length ? toModel * bones[b].localToWorldMatrix * poses[b] : own;
        var weights = mesh.GetAllBoneWeights();
        float lowest = float.PositiveInfinity;
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 v = vertices[i];
            float y = 0f, total = 0f;
            for (int n = perVertex[i]; n > 0; n--, at++)
            {
                var bone = weights[at];
                if (bone.boneIndex >= bones.Length) continue;
                ref var m = ref skin[bone.boneIndex];
                y += bone.weight * (m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13);
                total += bone.weight;
            }
            if (total > 0f) lowest = Mathf.Min(lowest, y / total);
        }
        return lowest;
    }

    // An unskinned mesh, or a skinned one with no bones, where its object holds it.
    private static float Plain(Renderer renderer, Matrix4x4 toModel, float box)
    {
        var mesh = renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        if (mesh == null) return float.PositiveInfinity;
        if (!mesh.isReadable) return box;
        mesh.GetVertices(vertices);
        return Plain(renderer.transform, toModel);
    }

    private static float Plain(Transform holder, Matrix4x4 toModel)
    {
        Matrix4x4 m = toModel * holder.localToWorldMatrix;
        float lowest = float.PositiveInfinity;
        foreach (var v in vertices) lowest = Mathf.Min(lowest, m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13);
        return lowest;
    }
}
