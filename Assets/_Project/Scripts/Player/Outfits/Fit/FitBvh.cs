using System;
using UnityEngine;

/// <summary>
/// A surface's triangles in a tree of boxes, for the two questions fitting layers of clothes asks of a garment
/// (FitTrim): does a ray leaving the layer under it get out past it, and where does an edge of that layer pass through
/// it. Plain arrays and no engine objects: it is asked from worker threads, and the offline checks run this same file
/// outside the game. A triangle may carry a tag (the bone it follows); a question can then count only some tags.
/// </summary>
internal sealed class FitBvh
{
    private const int LeafSize = 4, StackSize = 64;
    // A hit this near a ray's own start is the surface the ray leaves from; and how far outside a triangle's edges a
    // crossing still counts, so a ray never slips between two triangles that share an edge.
    private const float NearStart = 1e-6f, EdgeSlack = 1e-5f;

    /// <summary>The surface's points, in the space the questions are asked in.</summary>
    public readonly Vector3[] Points;
    /// <summary>Three points per triangle, in the tree's own order.</summary>
    public readonly int[] Corners;
    /// <summary>Each triangle's tag in that order; null when the surface has none.</summary>
    public readonly int[] Tags;
    /// <summary>The box round the whole surface.</summary>
    public readonly Vector3 Min, Max;

    // Per node: its box (min then max), and either where its second child is (the first follows the node itself) with
    // a count of 0, or its first triangle and how many it holds.
    private readonly float[] boxes;
    private readonly int[] first, count;

    public int TriangleCount => Corners.Length / 3;

    /// <param name="points">Kept, not copied.</param>
    /// <param name="corners">Three per triangle.</param>
    /// <param name="tags">One per triangle, or null.</param>
    public FitBvh(Vector3[] points, int[] corners, int[] tags)
    {
        Points = points;
        int triangles = corners.Length / 3;
        var order = new int[triangles];
        var middle = new float[triangles * 3];
        var low = new float[triangles * 3];
        var high = new float[triangles * 3];
        for (int t = 0; t < triangles; t++)
        {
            order[t] = t;
            Vector3 a = points[corners[t * 3]], b = points[corners[t * 3 + 1]], c = points[corners[t * 3 + 2]];
            for (int axis = 0; axis < 3; axis++)
            {
                float least = Mathf.Min(a[axis], Mathf.Min(b[axis], c[axis])), most = Mathf.Max(a[axis], Mathf.Max(b[axis], c[axis]));
                low[t * 3 + axis] = least;
                high[t * 3 + axis] = most;
                middle[t * 3 + axis] = (least + most) * .5f;
            }
        }
        int most2 = Mathf.Max(1, 2 * triangles);
        boxes = new float[most2 * 6];
        first = new int[most2];
        count = new int[most2];
        int nodes = 0;
        if (triangles > 0) Split(order, middle, low, high, 0, triangles, ref nodes);
        else
        {
            nodes = 1;
            count[0] = 0;
            first[0] = 0;
        }
        Corners = new int[triangles * 3];
        Tags = tags != null ? new int[triangles] : null;
        for (int t = 0; t < triangles; t++)
        {
            int from = order[t];
            Corners[t * 3] = corners[from * 3];
            Corners[t * 3 + 1] = corners[from * 3 + 1];
            Corners[t * 3 + 2] = corners[from * 3 + 2];
            if (Tags != null) Tags[t] = tags[from];
        }
        Min = triangles > 0 ? new Vector3(boxes[0], boxes[1], boxes[2]) : Vector3.zero;
        Max = triangles > 0 ? new Vector3(boxes[3], boxes[4], boxes[5]) : Vector3.zero;
    }

    // One node over order[from..to): a leaf when few are left, else halved at the middle one along its longest side.
    private int Split(int[] order, float[] middle, float[] low, float[] high, int from, int to, ref int nodes)
    {
        int node = nodes++;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        float midMinX = float.PositiveInfinity, midMinY = float.PositiveInfinity, midMinZ = float.PositiveInfinity;
        float midMaxX = float.NegativeInfinity, midMaxY = float.NegativeInfinity, midMaxZ = float.NegativeInfinity;
        for (int i = from; i < to; i++)
        {
            int t = order[i] * 3;
            if (low[t] < minX) minX = low[t];
            if (low[t + 1] < minY) minY = low[t + 1];
            if (low[t + 2] < minZ) minZ = low[t + 2];
            if (high[t] > maxX) maxX = high[t];
            if (high[t + 1] > maxY) maxY = high[t + 1];
            if (high[t + 2] > maxZ) maxZ = high[t + 2];
            if (middle[t] < midMinX) midMinX = middle[t];
            if (middle[t + 1] < midMinY) midMinY = middle[t + 1];
            if (middle[t + 2] < midMinZ) midMinZ = middle[t + 2];
            if (middle[t] > midMaxX) midMaxX = middle[t];
            if (middle[t + 1] > midMaxY) midMaxY = middle[t + 1];
            if (middle[t + 2] > midMaxZ) midMaxZ = middle[t + 2];
        }
        int at = node * 6;
        boxes[at] = minX;
        boxes[at + 1] = minY;
        boxes[at + 2] = minZ;
        boxes[at + 3] = maxX;
        boxes[at + 4] = maxY;
        boxes[at + 5] = maxZ;
        float spanX = midMaxX - midMinX, spanY = midMaxY - midMinY, spanZ = midMaxZ - midMinZ;
        int axis = spanX >= spanY && spanX >= spanZ ? 0 : spanY >= spanZ ? 1 : 2;
        float span = axis == 0 ? spanX : axis == 1 ? spanY : spanZ;
        // Triangles whose middles all coincide can't be told apart by any side: they stay together.
        if (to - from <= LeafSize || !(span > 0f))
        {
            first[node] = from;
            count[node] = to - from;
            return node;
        }
        int half = (from + to) / 2;
        Select(order, middle, axis, from, to - 1, half);
        count[node] = 0;
        Split(order, middle, low, high, from, half, ref nodes);
        first[node] = Split(order, middle, low, high, half, to, ref nodes);
        return node;
    }

    // Puts the wanted-th smallest middle (along the axis) at its place in order[from..to], the smaller before it.
    private static void Select(int[] order, float[] middle, int axis, int from, int to, int wanted)
    {
        while (from < to)
        {
            float pivot = middle[order[(from + to) >> 1] * 3 + axis];
            int i = from, j = to;
            while (i <= j)
            {
                while (middle[order[i] * 3 + axis] < pivot) i++;
                while (middle[order[j] * 3 + axis] > pivot) j--;
                if (i > j) break;
                (order[i], order[j]) = (order[j], order[i]);
                i++;
                j--;
            }
            if (wanted <= j) to = j;
            else if (wanted >= i) from = i;
            else return;
        }
    }

    /// <summary>
    /// Whether anything of the surface lies on the way from a point: within <paramref name="reach"/> of it along
    /// <paramref name="way"/> (any length: reach counts in its lengths). Either side of a triangle stops the way.
    /// </summary>
    /// <param name="allow">By tag, which triangles count; null for all.</param>
    public bool Blocks(Vector3 from, Vector3 way, float reach, bool[] allow)
    {
        if (count.Length == 0 || Corners.Length == 0) return false;
        float invX = 1f / way.x, invY = 1f / way.y, invZ = 1f / way.z;
        Span<int> stack = stackalloc int[StackSize];
        int top = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            int node = stack[--top];
            if (!MeetsBox(node, from, invX, invY, invZ, reach)) continue;
            int held = count[node];
            if (held == 0)
            {
                if (top + 2 > StackSize) continue;
                stack[top++] = first[node];
                stack[top++] = node + 1;
                continue;
            }
            int start = first[node];
            for (int t = start; t < start + held; t++)
            {
                if (allow != null && !Allowed(allow, Tags[t])) continue;
                float distance = Meets(from, way, Points[Corners[t * 3]], Points[Corners[t * 3 + 1]], Points[Corners[t * 3 + 2]]);
                if (distance > NearStart && distance <= reach) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// How far along <paramref name="way"/> (one unit long) the surface first lies, within <paramref name="reach"/>;
    /// -1 when it doesn't.
    /// </summary>
    public float Nearest(Vector3 from, Vector3 way, float reach, bool[] allow)
    {
        if (count.Length == 0 || Corners.Length == 0) return -1f;
        float invX = 1f / way.x, invY = 1f / way.y, invZ = 1f / way.z;
        Span<int> stack = stackalloc int[StackSize];
        int top = 0;
        float best = reach;
        bool found = false;
        stack[top++] = 0;
        while (top > 0)
        {
            int node = stack[--top];
            if (!MeetsBox(node, from, invX, invY, invZ, best)) continue;
            int held = count[node];
            if (held == 0)
            {
                if (top + 2 > StackSize) continue;
                stack[top++] = first[node];
                stack[top++] = node + 1;
                continue;
            }
            int start = first[node];
            for (int t = start; t < start + held; t++)
            {
                if (allow != null && !Allowed(allow, Tags[t])) continue;
                float distance = Meets(from, way, Points[Corners[t * 3]], Points[Corners[t * 3 + 1]], Points[Corners[t * 3 + 2]]);
                if (!(distance > NearStart) || distance > best) continue;
                best = distance;
                found = true;
            }
        }
        return found ? best : -1f;
    }

    /// <summary>
    /// Where the way from <paramref name="from"/> to <paramref name="to"/> passes through the surface: how many times,
    /// and the first and the last place as shares of the way (0 at from, 1 at to). The ends themselves don't count.
    /// </summary>
    public int Crossings(Vector3 from, Vector3 to, bool[] allow, out float firstAt, out float lastAt)
    {
        firstAt = float.PositiveInfinity;
        lastAt = float.NegativeInfinity;
        if (count.Length == 0 || Corners.Length == 0) return 0;
        Vector3 way = to - from;
        float invX = 1f / way.x, invY = 1f / way.y, invZ = 1f / way.z;
        Span<int> stack = stackalloc int[StackSize];
        int top = 0, found = 0;
        stack[top++] = 0;
        while (top > 0)
        {
            int node = stack[--top];
            if (!MeetsBox(node, from, invX, invY, invZ, 1f)) continue;
            int held = count[node];
            if (held == 0)
            {
                if (top + 2 > StackSize) continue;
                stack[top++] = first[node];
                stack[top++] = node + 1;
                continue;
            }
            int start = first[node];
            for (int t = start; t < start + held; t++)
            {
                if (allow != null && !Allowed(allow, Tags[t])) continue;
                float share = Meets(from, way, Points[Corners[t * 3]], Points[Corners[t * 3 + 1]], Points[Corners[t * 3 + 2]]);
                if (!(share > NearStart) || !(share < 1f - NearStart)) continue;
                found++;
                if (share < firstAt) firstAt = share;
                if (share > lastAt) lastAt = share;
            }
        }
        return found;
    }

    private static bool Allowed(bool[] allow, int tag) => tag >= 0 && tag < allow.Length && allow[tag];

    // Whether the way meets the node's box within reach. A way that runs along a side of the box (a zero part, whose
    // inverse is infinite) gives NaN there, which no comparison takes: that side then never rules the box out.
    private bool MeetsBox(int node, Vector3 from, float invX, float invY, float invZ, float reach)
    {
        int at = node * 6;
        float near = 0f, far = reach;
        float a = (boxes[at] - from.x) * invX, b = (boxes[at + 3] - from.x) * invX;
        if (a > b) (a, b) = (b, a);
        if (a > near) near = a;
        if (b < far) far = b;
        if (near > far) return false;
        a = (boxes[at + 1] - from.y) * invY;
        b = (boxes[at + 4] - from.y) * invY;
        if (a > b) (a, b) = (b, a);
        if (a > near) near = a;
        if (b < far) far = b;
        if (near > far) return false;
        a = (boxes[at + 2] - from.z) * invZ;
        b = (boxes[at + 5] - from.z) * invZ;
        if (a > b) (a, b) = (b, a);
        if (a > near) near = a;
        if (b < far) far = b;
        return !(near > far);
    }

    // How far along the way it meets the triangle, in the way's own lengths (Möller-Trumbore, either side); -1 for none.
    private static float Meets(Vector3 from, Vector3 way, Vector3 a, Vector3 b, Vector3 c)
    {
        float abX = b.x - a.x, abY = b.y - a.y, abZ = b.z - a.z;
        float acX = c.x - a.x, acY = c.y - a.y, acZ = c.z - a.z;
        float pX = way.y * acZ - way.z * acY, pY = way.z * acX - way.x * acZ, pZ = way.x * acY - way.y * acX;
        float det = abX * pX + abY * pY + abZ * pZ;
        if (det > -1e-18f && det < 1e-18f) return -1f;
        float inverse = 1f / det;
        float sX = from.x - a.x, sY = from.y - a.y, sZ = from.z - a.z;
        float u = (sX * pX + sY * pY + sZ * pZ) * inverse;
        if (u < -EdgeSlack || u > 1f + EdgeSlack) return -1f;
        float qX = sY * abZ - sZ * abY, qY = sZ * abX - sX * abZ, qZ = sX * abY - sY * abX;
        float v = (way.x * qX + way.y * qY + way.z * qZ) * inverse;
        if (v < -EdgeSlack || u + v > 1f + EdgeSlack) return -1f;
        return (acX * qX + acY * qY + acZ * qZ) * inverse;
    }
}
