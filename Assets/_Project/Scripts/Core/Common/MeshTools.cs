using UnityEngine;

/// <summary>Small readings of meshes that the outfit, gore and gun code all need.</summary>
public static class MeshTools
{
    /// <summary>The mesh a renderer draws: a skinned renderer's own, else its mesh filter's; null without one.</summary>
    public static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    /// <summary>A local box as the axis-aligned box round it in the space the matrix leads to (all eight corners).</summary>
    public static Bounds BoxOf(Bounds local, Matrix4x4 matrix)
    {
        var box = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            box.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
        return box;
    }

    /// <summary>The bone a vertex follows most; -1 when it follows none.</summary>
    public static int Strongest(in BoneWeight weight)
    {
        int index = weight.boneIndex0;
        float best = weight.weight0;
        if (weight.weight1 > best) { best = weight.weight1; index = weight.boneIndex1; }
        if (weight.weight2 > best) { best = weight.weight2; index = weight.boneIndex2; }
        if (weight.weight3 > best) index = weight.boneIndex3;
        return best > 0f ? index : -1;
    }
}
