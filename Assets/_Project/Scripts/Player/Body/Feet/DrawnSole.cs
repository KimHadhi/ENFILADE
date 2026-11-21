using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the soles of what a character draws are against its model's floor: the model's root, where every clip and the
/// feet on the ground (FootPlanting) put the soles. BOOTH avatars are made standing on the heels their own shoes have, and
/// other shoes or none don't reach that floor: Mayo's soles float 1 cm over it in her own pumps, 4.5 cm in the Sailor
/// Maiden loafers, 4.1 cm in the White Board boots and 5.8 cm barefoot, while Ichigo's sink 7 mm into it in her own shoes
/// and 13 mm in the Sera Yura ones (measured from the FBX files at their own size, 2026-11-12; the game draws Mayo 1.41
/// and Ichigo 1.53 times as tall; the user asked whether heels make the body float or sink). The lowest point a look draws is measured from its meshes as the model stands before any clip, its
/// keys as dressing left them (DrawnMesh), so the body can be stood on its real soles: any character in any shoes, a
/// modder's too.
/// </summary>
internal static class DrawnSole
{
    // Reused between measures: where one mesh's vertices are drawn.
    private static readonly List<Vector3> points = new List<Vector3>();
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
            lowest = Mathf.Min(lowest, Lowest(renderer, toModel, bottom));
        }
        return float.IsInfinity(lowest) ? 0f : lowest;
    }

    /// <summary>The lowest corner of a world box, in the model's frame.</summary>
    internal static float BoxBottom(Bounds box, Matrix4x4 toModel)
    {
        float least = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? box.min.x : box.max.x, (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z);
            least = Mathf.Min(least, toModel.MultiplyPoint3x4(corner).y);
        }
        return least;
    }

    // One renderer's lowest drawn point. A mesh that can't be read counts by its box.
    private static float Lowest(Renderer renderer, Matrix4x4 toModel, float box)
    {
        var mesh = DrawnMesh.MeshOf(renderer);
        if (mesh == null) return float.PositiveInfinity;
        if (!mesh.isReadable) return box;
        DrawnMesh.Read(renderer, mesh, toModel, points);
        float lowest = float.PositiveInfinity;
        // NaN, a vertex the fit left out, never wins a comparison.
        foreach (var point in points)
            if (point.y < lowest) lowest = point.y;
        return lowest;
    }
}
