using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A layer being fitted (skin, underwear, a sock) as its points and what joins them. A mesh splits a vertex into copies
/// along its seams, which stand in one place: here they are one point, so whatever walks the surface crosses a seam as
/// it would any edge, and the copies move as one. Read once per mesh from its vertices as made and its triangles; where
/// the points stand at any moment (keys on) is handed to FitTrim beside it. Plain arrays, no engine objects.
/// </summary>
internal sealed class FitShape
{
    // Copies of one vertex agree to a hundredth of a millimetre as made.
    private const float WeldScale = 1e5f;

    /// <summary>For each of the mesh's vertices, its point.</summary>
    public readonly int[] Weld;
    /// <summary>How many points.</summary>
    public readonly int Count;
    /// <summary>For each point, one of the mesh's vertices that stand there.</summary>
    public readonly int[] Sample;
    /// <summary>Three points per triangle of the mesh, in the mesh's own order.</summary>
    public readonly int[] Corners;
    /// <summary>Two points per edge, each edge once.</summary>
    public readonly int[] Edges;
    // For each point, its edges: those from AroundStart[point] up to AroundStart[point + 1], the point at the other end
    // and the edge itself.
    public readonly int[] AroundStart, AroundPoint, AroundEdge;

    public int EdgeCount => Edges.Length / 2;
    public int TriangleCount => Corners.Length / 3;

    /// <param name="made">The mesh's vertices as made, in its own space.</param>
    /// <param name="triangles">Three vertices per triangle.</param>
    public FitShape(Vector3[] made, int[] triangles)
    {
        Weld = new int[made.Length];
        var spots = new Dictionary<(int, int, int), int>(made.Length);
        var sample = new List<int>(made.Length);
        for (int i = 0; i < made.Length; i++)
        {
            var key = (Mathf.RoundToInt(made[i].x * WeldScale), Mathf.RoundToInt(made[i].y * WeldScale), Mathf.RoundToInt(made[i].z * WeldScale));
            if (!spots.TryGetValue(key, out int point))
            {
                spots[key] = point = sample.Count;
                sample.Add(i);
            }
            Weld[i] = point;
        }
        Count = sample.Count;
        Sample = sample.ToArray();
        Corners = new int[triangles.Length - triangles.Length % 3];
        for (int i = 0; i < Corners.Length; i++) Corners[i] = Weld[triangles[i]];
        var known = new HashSet<long>();
        var edges = new List<int>(Corners.Length);
        for (int t = 0; t + 2 < Corners.Length; t += 3)
        {
            Join(Corners[t], Corners[t + 1], known, edges);
            Join(Corners[t + 1], Corners[t + 2], known, edges);
            Join(Corners[t + 2], Corners[t], known, edges);
        }
        Edges = edges.ToArray();
        AroundStart = new int[Count + 1];
        for (int e = 0; e < Edges.Length; e++) AroundStart[Edges[e] + 1]++;
        for (int p = 0; p < Count; p++) AroundStart[p + 1] += AroundStart[p];
        AroundPoint = new int[Edges.Length];
        AroundEdge = new int[Edges.Length];
        var next = new int[Count];
        for (int e = 0; e + 1 < Edges.Length; e += 2)
        {
            int a = Edges[e], b = Edges[e + 1];
            int at = AroundStart[a] + next[a]++;
            AroundPoint[at] = b;
            AroundEdge[at] = e / 2;
            at = AroundStart[b] + next[b]++;
            AroundPoint[at] = a;
            AroundEdge[at] = e / 2;
        }
    }

    private void Join(int a, int b, HashSet<long> known, List<int> edges)
    {
        if (a == b) return;
        long key = a < b ? (long)a * Count + b : (long)b * Count + a;
        if (!known.Add(key)) return;
        edges.Add(a);
        edges.Add(b);
    }

    /// <summary>The points where these vertices (the mesh's, placed and with its keys on) put them.</summary>
    public Vector3[] Points(Vector3[] vertices)
    {
        var points = new Vector3[Count];
        for (int p = 0; p < Count; p++) points[p] = vertices[Sample[p]];
        return points;
    }

    /// <summary>
    /// Which way each point faces: the sum of its triangles' faces, weighed by their size, from the triangles' winding
    /// (Unity's: clockwise seen from outside). Worked out from the shape, not read from the mesh, whose normals a toon
    /// model's maker bends for its shading. Zero where the faces round a point cancel (a sheet with both sides drawn).
    /// </summary>
    public Vector3[] Normals(Vector3[] points)
    {
        var normals = new Vector3[Count];
        for (int t = 0; t + 2 < Corners.Length; t += 3)
        {
            int a = Corners[t], b = Corners[t + 1], c = Corners[t + 2];
            Vector3 face = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }
        for (int p = 0; p < Count; p++)
        {
            float length = normals[p].magnitude;
            normals[p] = length > 1e-12f ? normals[p] / length : Vector3.zero;
        }
        return normals;
    }
}
