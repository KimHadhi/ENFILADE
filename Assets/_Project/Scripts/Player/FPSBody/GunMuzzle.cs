using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds a gun's muzzle from its shape, since KINEMATION's guns have no muzzle bone: the middle of the gun's front face
/// along its sight line (the aim point's forward), from the meshes in their idle pose. Read/Write meshes are measured
/// vertex by vertex; without one, only the bounds are known, and the muzzle goes on the sight line at the front.
/// The back face is found the same way: the end of the stock, which the bodies hold against the shoulder (GunClipRetarget).
/// </summary>
public static class GunMuzzle
{
    private static readonly List<Vector3> Points = new();
    private static readonly List<Vector3> Vertices = new();

    /// <summary>The muzzle in <paramref name="gun"/>'s space; <paramref name="frontDepth"/> (m) is how deep the front face is taken.</summary>
    public static void Measure(Transform gun, Transform aimPoint, float frontDepth, out Vector3 position, out Quaternion rotation)
    {
        Vector3 axis = (aimPoint != null ? aimPoint.forward : gun.forward).normalized;
        Vector3 up = aimPoint != null ? aimPoint.up : gun.up;
        Vector3 muzzle = Face(gun, aimPoint, axis, frontDepth);
        position = gun.InverseTransformPoint(muzzle);
        rotation = Quaternion.Inverse(gun.rotation) * Quaternion.LookRotation(axis, up);
    }

    /// <summary>
    /// The middle of the gun's back face (a stock's butt) and the way the gun points, in <paramref name="gun"/>'s space;
    /// <paramref name="backDepth"/> (m) is how deep the back face is taken.
    /// </summary>
    public static void Back(Transform gun, Transform aimPoint, float backDepth, out Vector3 position, out Vector3 forward)
    {
        Vector3 axis = (aimPoint != null ? aimPoint.forward : gun.forward).normalized;
        position = gun.InverseTransformPoint(Face(gun, aimPoint, -axis, backDepth));
        forward = gun.InverseTransformDirection(axis);
    }

    // The middle of the gun's face furthest along axis (world space).
    private static Vector3 Face(Transform gun, Transform aimPoint, Vector3 axis, float depth)
    {
        Points.Clear();
        bool vertices = false;
        foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true)) vertices |= Collect(renderer, Points);
        Vector3 muzzle = aimPoint != null ? aimPoint.position : gun.position;
        if (Points.Count > 0)
        {
            float front = float.NegativeInfinity;
            foreach (var point in Points) front = Mathf.Max(front, Vector3.Dot(point, axis));
            if (vertices)
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (var point in Points)
                    if (Vector3.Dot(point, axis) >= front - depth) { sum += point; count++; }
                muzzle = sum / count;
            }
            // Bounds alone would centre it on the whole gun's height: keep it on the sight line instead.
            else muzzle += axis * (front - Vector3.Dot(muzzle, axis));
        }
        return muzzle;
    }

    // Adds the renderer's vertices (true) or, when its mesh can't be read, its bounds' corners (false).
    private static bool Collect(Renderer renderer, List<Vector3> points)
    {
        if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
        {
            if (!skinned.sharedMesh.isReadable)
            {
                AddCorners(skinned.localBounds, skinned.rootBone != null ? skinned.rootBone : skinned.transform, points);
                return false;
            }
            var baked = new Mesh();
            skinned.BakeMesh(baked, true);
            baked.GetVertices(Vertices);
            // Baked with the renderer's scale, in its position and rotation.
            var toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            foreach (var vertex in Vertices) points.Add(toWorld.MultiplyPoint3x4(vertex));
            // The editor measures guns too (GunClipRetarget), where only DestroyImmediate may be called.
            if (Application.isPlaying) Object.Destroy(baked);
            else Object.DestroyImmediate(baked);
            return true;
        }
        if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
        {
            if (!filter.sharedMesh.isReadable)
            {
                AddCorners(filter.sharedMesh.bounds, renderer.transform, points);
                return false;
            }
            filter.sharedMesh.GetVertices(Vertices);
            foreach (var vertex in Vertices) points.Add(renderer.transform.TransformPoint(vertex));
            return true;
        }
        return false;
    }

    private static void AddCorners(Bounds bounds, Transform space, List<Vector3> points)
    {
        Vector3 c = bounds.center, e = bounds.extents;
        for (int i = 0; i < 8; i++)
            points.Add(space.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
    }
}
