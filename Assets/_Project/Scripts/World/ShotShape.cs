using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shape a shot meets, for a prop whose collider is a plain box (a car, a van, a truck).
/// Two things are asked of every solid thing on a map: what stops a body, and what stops a bullet. One box used to
/// answer both (the user, 2026-11-18: "차량 오브젝트 히트박스 과대 설정: 차량 판정이 실제 외형보다 커서 허공을 쏴도
/// 총알이 막히는 문제"): a car's box is as wide, long and tall as the car at its widest, longest and tallest (and
/// raised to 2.05 m where a zombie could not reach its roof), so a shot over the bonnet or past a tapered corner
/// stopped in the air. Now they are answered apart, in one place for every prop:
/// - A body, a zombie's walk mesh and everything else that asks the physics still meet the box. A simple hull is what
///   walking wants: no snagging on a wing mirror, a top a zombie can be laid a path onto.
/// - A shot (AttackTrace, and through it the gun's own look ahead) that reaches the box asks this for the shape that is
///   seen: the model's own triangles. It goes on through the box where it meets none, and where it meets one the hit is
///   on the car's skin, so the spark and the hole are too.
/// The builder puts this on the box of every kind whose Tables/MapArt row says shots = mesh, with the meshes of the
/// model it stood there and where each lies in the box (their place is kept as numbers: at play, static batching
/// swaps a model's own mesh for the map's combined one). Nothing is baked per model: the triangles are read from the
/// mesh the first time a shot comes, and kept for every copy of that mesh. A mesh that cannot be read (its import has
/// Read/Write off; the builder switches it on) leaves the box as it was: never worse than before.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShotShape : MonoBehaviour
{
    // A leaf holds about this many triangles; a ray is tried against a leaf's box before its triangles.
    private const int LeafSize = 24;
    // A shot is looked for from this far before where it met the box (m): the skin may stand a hair proud of the box.
    private const float Proud = .05f;

    [SerializeField, Tooltip("The seen model's meshes (its first level of detail).")]
    private Mesh[] meshes = new Mesh[0];
    [SerializeField, Tooltip("Each mesh's place: from its own space into this box's.")]
    private Matrix4x4[] places = new Matrix4x4[0];

    // A mesh's triangles, sorted into leaves; one for every mesh, shared by all its copies.
    private sealed class Soup
    {
        public Vector3[] Corners;
        public Bounds[] Leaves;
    }

    private static readonly Dictionary<Mesh, Soup> soups = new Dictionary<Mesh, Soup>();
    private static readonly List<Vector3> corners = new List<Vector3>();
    private static readonly List<int> indices = new List<int>();

    private Soup[] parts;
    private Matrix4x4[] toPart;
    private float reach;
    private bool ready, usable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => soups.Clear();

    /// <summary>This collider's shots are judged by a seen shape.</summary>
    public static bool Has(Collider collider)
        => collider != null && collider.TryGetComponent(out ShotShape shape) && shape.Usable;

    /// <summary>
    /// A shot from <paramref name="origin"/> along <paramref name="direction"/> (a unit vector) reached a collider at
    /// <paramref name="hit"/>. True when it stops there: the collider has no seen shape (any other collider), or the shot
    /// meets the shape, and then the hit is moved onto the shape (its point, distance and normal). False when the
    /// collider has a shape and the shot misses it: it goes on through the box.
    /// </summary>
    public static bool Meets(ref RaycastHit hit, Vector3 origin, Vector3 direction)
    {
        if (hit.collider == null || !hit.collider.TryGetComponent(out ShotShape shape) || !shape.Usable) return true;
        // A distance along the ray is metres only for a unit direction, whatever the caller handed the physics.
        direction.Normalize();
        if (!shape.Cast(origin, direction, Mathf.Max(0f, hit.distance - Proud), hit.distance + shape.reach, out float distance, out Vector3 normal))
            return false;
        hit.distance = distance;
        hit.point = origin + direction * distance;
        hit.normal = normal;
        return true;
    }

    private bool Usable
    {
        get
        {
            if (!ready) Ready();
            return usable;
        }
    }

    private void Ready()
    {
        ready = true;
        usable = false;
        int count = Mathf.Min(meshes != null ? meshes.Length : 0, places != null ? places.Length : 0);
        parts = new Soup[count];
        toPart = new Matrix4x4[count];
        for (int i = 0; i < count; i++)
        {
            parts[i] = SoupOf(meshes[i]);
            if (parts[i] == null) continue;
            toPart[i] = places[i].inverse;
            usable = true;
        }
        // No shot goes farther inside the box than its diagonal.
        reach = TryGetComponent(out Collider box) ? box.bounds.size.magnitude : 0f;
    }

    /// <summary>
    /// The nearest place between <paramref name="near"/> and <paramref name="far"/> metres along the ray where it meets
    /// the seen shape, from either side of a face. Also what the builder's report fires at every shape it makes.
    /// </summary>
    public bool Cast(Vector3 origin, Vector3 direction, float near, float far, out float distance, out Vector3 normal)
    {
        distance = far;
        normal = -direction;
        if (!Usable) return false;
        bool met = false;
        int metPart = 0;
        Vector3 metAlong = default, metAcross = default;
        // Into the box's own space, then each mesh's. The direction is carried as a vector, not made a unit again, so
        // a distance along it is the same number in every space.
        Vector3 boxFrom = transform.InverseTransformPoint(origin), boxWay = transform.InverseTransformVector(direction);
        for (int p = 0; p < parts.Length; p++)
        {
            var soup = parts[p];
            if (soup == null) continue;
            Vector3 from = toPart[p].MultiplyPoint3x4(boxFrom), way = toPart[p].MultiplyVector(boxWay);
            var inverse = new Vector3(Safe(way.x), Safe(way.y), Safe(way.z));
            for (int leaf = 0; leaf < soup.Leaves.Length; leaf++)
            {
                if (!Enters(soup.Leaves[leaf], from, inverse, near, distance)) continue;
                int end = Mathf.Min(soup.Corners.Length, (leaf + 1) * LeafSize * 3);
                for (int i = leaf * LeafSize * 3; i < end; i += 3)
                {
                    Vector3 a = soup.Corners[i], ab = soup.Corners[i + 1] - a, ac = soup.Corners[i + 2] - a;
                    // Möller-Trumbore, both faces.
                    Vector3 h = Vector3.Cross(way, ac);
                    float det = Vector3.Dot(ab, h);
                    if (det > -1e-9f && det < 1e-9f) continue;
                    float f = 1f / det;
                    Vector3 s = from - a;
                    float u = f * Vector3.Dot(s, h);
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(s, ab);
                    float v = f * Vector3.Dot(way, q);
                    if (v < 0f || u + v > 1f) continue;
                    float t = f * Vector3.Dot(ac, q);
                    if (t < near || t >= distance) continue;
                    distance = t;
                    met = true;
                    metPart = p;
                    metAlong = ab;
                    metAcross = ac;
                }
            }
        }
        if (!met) return false;
        // The face's normal from its two edges as they lie in the world (the box is a cube scaled to the prop's size,
        // so a normal carried through it as a direction would lean), turned to face the shot.
        Vector3 face = Vector3.Cross(transform.TransformVector(places[metPart].MultiplyVector(metAlong)),
            transform.TransformVector(places[metPart].MultiplyVector(metAcross))).normalized;
        if (face.sqrMagnitude > .5f) normal = Vector3.Dot(face, direction) > 0f ? -face : face;
        return true;
    }

    private static float Safe(float along) => Mathf.Abs(along) < 1e-9f ? float.PositiveInfinity : 1f / along;

    // The ray passes through the box between near and far (the slab test; `inverse` is one over the direction).
    private static bool Enters(Bounds box, Vector3 from, Vector3 inverse, float near, float far)
    {
        Vector3 low = box.min, high = box.max;
        for (int axis = 0; axis < 3; axis++)
        {
            float one = inverse[axis];
            if (float.IsInfinity(one))
            {
                if (from[axis] < low[axis] || from[axis] > high[axis]) return false;
                continue;
            }
            float a = (low[axis] - from[axis]) * one, b = (high[axis] - from[axis]) * one;
            if (a > b) (a, b) = (b, a);
            if (a > near) near = a;
            if (b < far) far = b;
            if (near > far) return false;
        }
        return true;
    }

    // A mesh's triangles in leaves of LeafSize, each leaf a tight box: halved over and over along its longer side by
    // the middle triangle. Null for a mesh that can't be read.
    private static Soup SoupOf(Mesh mesh)
    {
        if (mesh == null) return null;
        if (soups.TryGetValue(mesh, out var known)) return known;
        Soup soup = null;
        if (mesh.isReadable)
        {
            mesh.GetVertices(corners);
            var triangles = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(indices, s);
                triangles.AddRange(indices);
            }
            int count = triangles.Count / 3;
            if (count > 0)
            {
                var order = new int[count];
                var middles = new Vector3[count];
                for (int t = 0; t < count; t++)
                {
                    order[t] = t;
                    middles[t] = (corners[triangles[t * 3]] + corners[triangles[t * 3 + 1]] + corners[triangles[t * 3 + 2]]) / 3f;
                }
                Halve(order, middles, 0, count);
                soup = new Soup { Corners = new Vector3[count * 3], Leaves = new Bounds[(count + LeafSize - 1) / LeafSize] };
                for (int t = 0; t < count; t++)
                {
                    int leaf = t / LeafSize;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 corner = corners[triangles[order[t] * 3 + k]];
                        soup.Corners[t * 3 + k] = corner;
                        if (t % LeafSize == 0 && k == 0) soup.Leaves[leaf] = new Bounds(corner, Vector3.zero);
                        else soup.Leaves[leaf].Encapsulate(corner);
                    }
                }
            }
            corners.Clear();
            indices.Clear();
        }
        soups[mesh] = soup;
        return soup;
    }

    // Sorts order[from..to) so that every run of LeafSize is a compact clump: the stretch is split at a leaf boundary
    // near its middle along the longer side of its triangles' middles, and each half again.
    private static void Halve(int[] order, Vector3[] middles, int from, int to)
    {
        int count = to - from;
        if (count <= LeafSize) return;
        var box = new Bounds(middles[order[from]], Vector3.zero);
        for (int i = from + 1; i < to; i++) box.Encapsulate(middles[order[i]]);
        Vector3 size = box.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        var keys = new float[count];
        var items = new int[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = order[from + i];
            keys[i] = middles[items[i]][axis];
        }
        System.Array.Sort(keys, items);
        System.Array.Copy(items, 0, order, from, count);
        // Split on a leaf boundary, so the leaves of the two halves never share a triangle run.
        int half = Mathf.Max(LeafSize, (count / 2 + LeafSize - 1) / LeafSize * LeafSize);
        if (half >= count) return;
        Halve(order, middles, from, from + half);
        Halve(order, middles, from + half, to);
    }
}
