using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mesh surgery for a bought scene used as a map's building (GreyboxBuilder.PackScene; the user, 2026-11-14: "공항 실내는
/// 그 그대로 쓰면 되지 않음 내가 산 에셋??"). The Airport Terminal pack's terminal is a set made to be seen from inside: no
/// door leads out of it, and most of its walls have one face, the inner one. Cut takes a box out of a mesh (a doorway
/// through a wall and its collision): every triangle the box touches is clipped by the box's six planes and only what
/// lies outside the box is kept, its UVs, normals and colours carried along, so the wall around the hole looks as it
/// did. Flipped turns a mesh round (the outer face of a wall made with only its inner one).
/// </summary>
public static class MeshCut
{
    // A distance this small from a plane counts as on it (m).
    private const float OnPlane = 1e-5f;

    // The mesh's per-vertex data, as many channels as it has, with the box-space positions the cut measures against;
    // a vertex made on an edge is appended to every list.
    private sealed class Data
    {
        public readonly List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>(), InBox = new List<Vector3>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
        public readonly List<Vector2> Uv = new List<Vector2>(), Uv2 = new List<Vector2>();
        public readonly List<Color> Colours = new List<Color>();
        // An edge already split by a plane: the vertex made there (the same one for both triangles beside the edge).
        public readonly Dictionary<(int, int, int), int> Splits = new Dictionary<(int, int, int), int>();

        public int Between(int a, int b, float t, int plane)
        {
            // Keyed and measured from the lower index, so both triangles sharing the edge get the same vertex.
            if (a > b) { (a, b) = (b, a); t = 1f - t; }
            if (Splits.TryGetValue((a, b, plane), out int known)) return known;
            Positions.Add(Vector3.Lerp(Positions[a], Positions[b], t));
            InBox.Add(Vector3.Lerp(InBox[a], InBox[b], t));
            if (Normals.Count > 0) Normals.Add(Vector3.Lerp(Normals[a], Normals[b], t).normalized);
            if (Tangents.Count > 0) Tangents.Add(Vector4.Lerp(Tangents[a], Tangents[b], t));
            if (Uv.Count > 0) Uv.Add(Vector2.Lerp(Uv[a], Uv[b], t));
            if (Uv2.Count > 0) Uv2.Add(Vector2.Lerp(Uv2[a], Uv2[b], t));
            if (Colours.Count > 0) Colours.Add(Color.Lerp(Colours[a], Colours[b], t));
            int made = Positions.Count - 1;
            Splits[(a, b, plane)] = made;
            return made;
        }
    }

    /// <summary>Whether the mesh's vertices can be read here (an imported model needs Read/Write on).</summary>
    public static bool Readable(Mesh mesh) => mesh != null && mesh.isReadable && mesh.vertexCount > 0;

    /// <summary>
    /// A copy of <paramref name="source"/> with what lies inside <paramref name="box"/> taken out; the box is in the space
    /// <paramref name="toBox"/> takes the mesh's own vertices to. Null when the box touches none of its triangles (the
    /// source can stay as it is) or the mesh can't be read.
    /// </summary>
    public static Mesh Cut(Mesh source, Matrix4x4 toBox, Bounds box)
    {
        if (!Readable(source)) return null;
        var data = Read(source);
        for (int i = 0; i < data.Positions.Count; i++) data.InBox.Add(toBox.MultiplyPoint3x4(data.Positions[i]));
        var submeshes = new List<List<int>>();
        bool touched = false;
        var outside = new List<int>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var kept = new List<int>();
            submeshes.Add(kept);
            var indices = source.GetIndices(s);
            if (source.GetTopology(s) != MeshTopology.Triangles) { kept.AddRange(indices); continue; }
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (!Touches(box, data.InBox[a], data.InBox[b], data.InBox[c])) { kept.Add(a); kept.Add(b); kept.Add(c); continue; }
                touched = true;
                // The triangle is clipped by each of the box's planes in turn: what lies beyond a plane is outside the box
                // and kept; the rest goes on to the next plane, and what is left after all six is inside and dropped.
                var rest = new List<int> { a, b, c };
                for (int plane = 0; plane < 6 && rest.Count >= 3; plane++)
                {
                    var inner = new List<int>();
                    outside.Clear();
                    Split(data, rest, plane, box, outside, inner);
                    Fan(data, outside, kept);
                    rest = inner;
                }
            }
        }
        if (!touched) return null;
        // A mesh cut again keeps one mark of it in its name.
        return Write(source, data, submeshes, source.name.EndsWith(" (잘라 냄)") ? source.name : source.name + " (잘라 냄)");
    }

    /// <summary>
    /// <paramref name="source"/> turned round: every triangle faces the other way (its normals too), for a wall's other
    /// side. With <paramref name="oneMaterial"/> its parts become one, for a single material.
    /// </summary>
    public static Mesh Flipped(Mesh source, bool oneMaterial)
    {
        if (!Readable(source)) return null;
        var data = Read(source);
        for (int i = 0; i < data.Normals.Count; i++) data.Normals[i] = -data.Normals[i];
        for (int i = 0; i < data.Tangents.Count; i++)
        {
            var t = data.Tangents[i];
            data.Tangents[i] = new Vector4(-t.x, -t.y, -t.z, t.w);
        }
        var submeshes = new List<List<int>>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var indices = source.GetIndices(s);
            var turned = new List<int>(indices.Length);
            if (source.GetTopology(s) == MeshTopology.Triangles)
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    turned.Add(indices[i]);
                    turned.Add(indices[i + 2]);
                    turned.Add(indices[i + 1]);
                }
            if (oneMaterial && submeshes.Count > 0) submeshes[0].AddRange(turned);
            else submeshes.Add(turned);
        }
        return Write(source, data, submeshes, source.name + " (바깥)");
    }

    private static Data Read(Mesh source)
    {
        var data = new Data();
        source.GetVertices(data.Positions);
        source.GetNormals(data.Normals);
        source.GetTangents(data.Tangents);
        source.GetUVs(0, data.Uv);
        source.GetUVs(1, data.Uv2);
        source.GetColors(data.Colours);
        return data;
    }

    private static Mesh Write(Mesh source, Data data, List<List<int>> submeshes, string name)
    {
        var mesh = new Mesh { name = name };
        if (data.Positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(data.Positions);
        if (data.Normals.Count == data.Positions.Count) mesh.SetNormals(data.Normals);
        if (data.Tangents.Count == data.Positions.Count) mesh.SetTangents(data.Tangents);
        if (data.Uv.Count == data.Positions.Count) mesh.SetUVs(0, data.Uv);
        if (data.Uv2.Count == data.Positions.Count) mesh.SetUVs(1, data.Uv2);
        if (data.Colours.Count == data.Positions.Count) mesh.SetColors(data.Colours);
        mesh.subMeshCount = submeshes.Count;
        for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s, false);
        mesh.RecalculateBounds();
        if (data.Normals.Count != data.Positions.Count) mesh.RecalculateNormals();
        return mesh;
    }

    // Whether the triangle's extent meets the box at all (a cheap test before the clipping).
    private static bool Touches(Bounds box, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
        return min.x < box.max.x && max.x > box.min.x && min.y < box.max.y && max.y > box.min.y && min.z < box.max.z && max.z > box.min.z;
    }

    // How far a point is beyond one of the box's planes (positive outside the box): 0 and 1 the far and near x, 2 and 3
    // y, 4 and 5 z.
    private static float Beyond(Vector3 p, int plane, Bounds box)
    {
        switch (plane)
        {
            case 0: return p.x - box.max.x;
            case 1: return box.min.x - p.x;
            case 2: return p.y - box.max.y;
            case 3: return box.min.y - p.y;
            case 4: return p.z - box.max.z;
            default: return box.min.z - p.z;
        }
    }

    // One plane's cut of a convex polygon (Sutherland–Hodgman both ways): the part beyond it and the part within it,
    // sharing the vertices made where the plane crosses its edges; a vertex on the plane goes in both.
    private static void Split(Data data, List<int> polygon, int plane, Bounds box, List<int> beyond, List<int> within)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            int a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            float da = Beyond(data.InBox[a], plane, box), db = Beyond(data.InBox[b], plane, box);
            if (da >= -OnPlane) beyond.Add(a);
            if (da <= OnPlane) within.Add(a);
            if ((da > OnPlane && db < -OnPlane) || (da < -OnPlane && db > OnPlane))
            {
                int made = data.Between(a, b, da / (da - db), plane);
                beyond.Add(made);
                within.Add(made);
            }
        }
    }

    // A convex polygon as triangles round its first corner, leaving out slivers with no area.
    private static void Fan(Data data, List<int> polygon, List<int> into)
    {
        for (int i = 1; i + 1 < polygon.Count; i++)
        {
            Vector3 p0 = data.Positions[polygon[0]], p1 = data.Positions[polygon[i]], p2 = data.Positions[polygon[i + 1]];
            if (Vector3.Cross(p1 - p0, p2 - p0).sqrMagnitude < 1e-12f) continue;
            into.Add(polygon[0]);
            into.Add(polygon[i]);
            into.Add(polygon[i + 1]);
        }
    }
}
