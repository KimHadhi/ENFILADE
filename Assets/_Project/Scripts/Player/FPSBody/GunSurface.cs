using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A gun's real outer surface for the hands to touch: its own mesh triangles as it stands at rest, kept in the gun's
/// frame (its position and turn, in world metres), in a grid of small cells so a finger's test reads only the triangles
/// beside it. A palm rests on it and the fingers close round it (PlayerBodyHands.GunFingers). A bar standing in for the
/// gun is not where the gun is, so fingers either sank in or were thrown open (user, 2026-11-08).
/// </summary>
internal sealed class GunSurface
{
    private const float Cell = .02f;
    private readonly Transform gun;
    private readonly Vector3[] corners;   // three per triangle
    private readonly Vector3[] normals;   // one per triangle, outward as the mesh is wound
    private readonly Dictionary<Vector3Int, List<int>> cells = new();
    private readonly List<int> found = new();
    private readonly int[] stamp;
    private int pass;

    public Transform Gun => gun;
    public bool Empty => normals.Length == 0;

    private GunSurface(Transform gun, List<Vector3> points)
    {
        this.gun = gun;
        corners = points.ToArray();
        int count = corners.Length / 3;
        normals = new Vector3[count];
        stamp = new int[count];
        for (int t = 0; t < count; t++)
        {
            Vector3 a = corners[t * 3], b = corners[t * 3 + 1], c = corners[t * 3 + 2];
            normals[t] = Vector3.Cross(b - a, c - a).normalized;
            Vector3Int low = CellOf(Vector3.Min(a, Vector3.Min(b, c))), high = CellOf(Vector3.Max(a, Vector3.Max(b, c)));
            for (int x = low.x; x <= high.x; x++)
                for (int y = low.y; y <= high.y; y++)
                    for (int z = low.z; z <= high.z; z++)
                    {
                        var key = new Vector3Int(x, y, z);
                        if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                        list.Add(t);
                    }
        }
    }

    /// <summary>The surface of every mesh under <paramref name="gun"/>, as it stands now. Empty if no mesh can be read.</summary>
    public static GunSurface Of(Transform gun)
    {
        var points = new List<Vector3>();
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Quaternion toGun = Quaternion.Inverse(gun.rotation);
        bool unreadable = false;
        foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            Matrix4x4 toWorld;
            bool baked = false;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                if (!skinned.sharedMesh.isReadable) { unreadable = true; continue; }
                mesh = new Mesh();
                skinned.BakeMesh(mesh, true);
                baked = true;
                toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                if (!filter.sharedMesh.isReadable) { unreadable = true; continue; }
                mesh = filter.sharedMesh;
                toWorld = renderer.transform.localToWorldMatrix;
            }
            else continue;
            mesh.GetVertices(vertices);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(triangles, sub);
                foreach (int index in triangles) points.Add(toGun * (toWorld.MultiplyPoint3x4(vertices[index]) - gun.position));
            }
            if (baked) Object.Destroy(mesh);
        }
        if (unreadable)
            Debug.LogWarning($"[GunSurface] {gun.name}: a mesh has Read/Write off, so the hands can't touch that part of the gun.", gun);
        return new GunSurface(gun, points);
    }

    private Vector3 Local(Vector3 world) => Quaternion.Inverse(gun.rotation) * (world - gun.position);
    private static Vector3Int CellOf(Vector3 p) => new(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

    private void Gather(Vector3 min, Vector3 max)
    {
        found.Clear();
        if (++pass == int.MaxValue) { System.Array.Clear(stamp, 0, stamp.Length); pass = 1; }
        Vector3Int low = CellOf(min), high = CellOf(max);
        for (int x = low.x; x <= high.x; x++)
            for (int y = low.y; y <= high.y; y++)
                for (int z = low.z; z <= high.z; z++)
                {
                    if (!cells.TryGetValue(new Vector3Int(x, y, z), out var list)) continue;
                    foreach (int t in list)
                        if (stamp[t] != pass) { stamp[t] = pass; found.Add(t); }
                }
    }

    /// <summary>True when the segment (world) passes no nearer the gun's surface than <paramref name="padding"/>.</summary>
    public bool Clear(Vector3 start, Vector3 end, float padding)
    {
        if (Empty) return true;
        Vector3 a = Local(start), b = Local(end);
        Vector3 pad = Vector3.one * padding;
        Gather(Vector3.Min(a, b) - pad, Vector3.Max(a, b) + pad);
        float limit = padding * padding;
        bool line = (b - a).sqrMagnitude > 1e-12f;
        foreach (int t in found)
        {
            Vector3 p0 = corners[t * 3], p1 = corners[t * 3 + 1], p2 = corners[t * 3 + 2];
            if (line && Crosses(a, b, p0, p1, p2, out _)) return false;
            // Apart, a segment is nearest a triangle at one of its ends or along one of the triangle's edges.
            if ((ClosestOnTriangle(a, p0, p1, p2) - a).sqrMagnitude < limit) return false;
            if (!line) continue;
            if ((ClosestOnTriangle(b, p0, p1, p2) - b).sqrMagnitude < limit) return false;
            if (SegmentsSqr(a, b, p0, p1) < limit || SegmentsSqr(a, b, p1, p2) < limit || SegmentsSqr(a, b, p2, p0) < limit) return false;
        }
        return true;
    }

    /// <summary>
    /// The first face of the gun turned toward a ray (world) within <paramref name="length"/>: how far along it. Faces turned
    /// away are passed through, so a ray starting inside one part still finds the face the hand meets.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float length, out float distance)
    {
        distance = length;
        if (Empty) return false;
        Vector3 a = Local(origin), d = Quaternion.Inverse(gun.rotation) * direction.normalized, b = a + d * length;
        Gather(Vector3.Min(a, b), Vector3.Max(a, b));
        bool hit = false;
        foreach (int t in found)
        {
            if (Vector3.Dot(normals[t], d) >= 0f) continue;
            if (!Crosses(a, b, corners[t * 3], corners[t * 3 + 1], corners[t * 3 + 2], out float at)) continue;
            if (at * length < distance) { distance = at * length; hit = true; }
        }
        return hit;
    }

    // Segment a-b through triangle p0-p1-p2 (Möller-Trumbore), with the crossing's share of the way along it.
    private static bool Crosses(Vector3 a, Vector3 b, Vector3 p0, Vector3 p1, Vector3 p2, out float at)
    {
        at = 0f;
        Vector3 dir = b - a, e1 = p1 - p0, e2 = p2 - p0, h = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, h);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        Vector3 s = a - p0;
        float u = Vector3.Dot(s, h) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(dir, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        at = Vector3.Dot(e2, q) * inv;
        return at >= 0f && at <= 1f;
    }

    // Ericson, Real-Time Collision Detection 5.1.5.
    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    // Ericson 5.1.9: squared distance between segments p1-q1 and p2-q2.
    private static float SegmentsSqr(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-12f && e <= 1e-12f) return r.sqrMagnitude;
        if (a <= 1e-12f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-12f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > 1e-12f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        return (p1 + d1 * s - (p2 + d2 * t)).sqrMagnitude;
    }
}
