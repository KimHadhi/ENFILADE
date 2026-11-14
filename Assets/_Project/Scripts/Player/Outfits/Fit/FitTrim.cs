using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// What of a layer shows once other things are worn over it, worked out from the surfaces alone, so any two pieces go
/// together whoever made them and nothing is written per pair. It never pushes a surface about: socks under shoes used
/// to be pressed in along their normals until the nearest shoe surface, which crushed toes against the insole, dragged
/// loose socks into spikes round a shoe's collar and flattened a stocking's foot (the user, 2026-11-14: "어떤 양말과
/// 신발은 호환이 안 돼. 발이 엄청나게 쪼그라드는 문제"). Two plain facts are measured instead:
/// - which of the layer's points can be seen: a point no ray gets away from, every way out stopped by a garment over
///   it, is hidden (a foot inside a shoe, a bra under a shirt);
/// - where the layer passes through a garment over it: its edges that cross that garment's surface.
/// From a start on the layer that is surely where it should be (clear of every garment over it, or seen with no garment
/// right behind it) the layer is walked edge by edge without ever stepping through a garment. What the walk reaches is
/// on the right side: outside the garment, or inside it by its opening (a sock down into a shoe, a collar out over a
/// jacket's). What it cannot reach got there through the garment's wall: a toe out through a shoe's upper, a sole out
/// under a boot, a bra's cup through a tight dress. Triangles wholly unreached or hidden are not drawn; a point just
/// past a crossing, on a triangle still drawn, is pulled back to just short of it along its own edge, never further.
/// So what shows keeps the shape its maker gave it, a loose sock still drapes over the shoe it was made with, and the
/// worst a wrong measure does is leave a piece as it was.
/// One layer against one garment is measured once (Cut) and kept; a whole outfit is their sum (Solve).
/// Plain arrays, no engine objects: the measuring runs on worker threads, and the offline checks run this file as it is.
/// </summary>
internal static class FitTrim
{
    /// <summary>How many ways a point is looked at from, spread evenly round it; those on its outer side count.</summary>
    public const int WayCount = 64;
    // How far the extra looks behind a point lean from straight behind (their sideways share): a point on the rim of
    // what came through a garment faces half away from it.
    private const float WallLean = .6f;

    /// <summary>The ways rays leave a point, the same for every point so what garments stop adds up bit by bit.</summary>
    public static readonly Vector3[] Ways = MakeWays();

    private static Vector3[] MakeWays()
    {
        var ways = new Vector3[WayCount];
        float turn = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < WayCount; i++)
        {
            float y = 1f - 2f * (i + .5f) / WayCount;
            float round = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            ways[i] = new Vector3(Mathf.Cos(turn * i) * round, y, Mathf.Sin(turn * i) * round);
        }
        return ways;
    }

    /// <summary>The numbers of a fit (Tables/OutfitFit).</summary>
    public struct Rules
    {
        /// <summary>A point this far outside a garment's box is still measured against it (m).</summary>
        public float Near;
        /// <summary>A point with a garment this near behind it never starts the walk (m).</summary>
        public float Behind;
        /// <summary>A point pulled back stops this far short of the crossing (m).</summary>
        public float Inset;
        /// <summary>A hidden point on a triangle still drawn goes this far in under the garment (m); 0 leaves it.</summary>
        public float Dive;
        /// <summary>
        /// A point lying on the outside of a garment no deeper than this (m) is tucked in under it: a sock a few
        /// millimetres wider than a shoe's rim goes into the shoe. 0 tucks nothing.
        /// </summary>
        public float Tuck;
        /// <summary>
        /// A patch of cloth lying on a garment is tucked only when at least this share of it lies that shallow: one that
        /// mostly stands well clear was made to drape (loose socks down over the shoes they came with) and is left alone.
        /// </summary>
        public float TuckShare;
        /// <summary>How far round from straight out a ray may leave a point: the least cosine with its facing.</summary>
        public float Facing;
        /// <summary>
        /// How many rings of hidden points beside the seen ones stay drawn. Seen almost edge-on, a triangle between
        /// three hidden points can still show a sliver beside the seen ones (from under a skirt, 2026-11-14's check).
        /// </summary>
        public int Margin;
        /// <summary>
        /// A garment hides only what it lies on: a point with the garment straight over it within this (m). A skirt
        /// hanging a hand's width off the thighs stops every ray from them and still hides nothing: from below they show.
        /// </summary>
        public float On;
    }

    /// <summary>One layer measured against one garment over it.</summary>
    public sealed class Cut
    {
        /// <summary>The layer's points within reach of the garment's box.</summary>
        public int[] Near;
        /// <summary>For each of them, the ways out the garment stops (a bit per Ways); null when it hides nothing.</summary>
        public ulong[] Stopped;
        /// <summary>For each of them, whether the garment lies on it (straight over it, close); null when it hides nothing.</summary>
        public bool[] On;
        /// <summary>For each of them, whether the garment lies right behind it.</summary>
        public bool[] Behind;
        /// <summary>For each of them, how far straight behind it the garment lies (m); -1 when it isn't within reach there.</summary>
        public float[] Depth;
        /// <summary>The layer's edges that pass through the garment, and where along each (shares from its first point).</summary>
        public int[] Edges;
        public float[] First, Last;
    }

    /// <summary>What the outfit leaves of one layer.</summary>
    public sealed class Result
    {
        /// <summary>For each of the mesh's triangles, whether it is no longer drawn.</summary>
        public bool[] Gone;
        public int GoneCount;
        /// <summary>The points moved, and where to.</summary>
        public int[] Moved;
        public Vector3[] MovedTo;
        /// <summary>How many points no ray leaves, and how many the walk didn't reach.</summary>
        public int Hidden, Unreached;
        public bool Any => GoneCount > 0 || Moved.Length > 0;
    }

    /// <summary>
    /// Measures a layer against one garment worn over it; null when no point of it comes near the garment.
    /// </summary>
    /// <param name="points">The layer's points as it is worn (keys on), in the space the garment's tree is in.</param>
    /// <param name="normals">Which way each faces (FitShape.Normals).</param>
    /// <param name="tags">For each point the bone it follows most; null with <paramref name="allow"/> null.</param>
    /// <param name="allow">
    /// By a point's tag, which of the garment's tags move with it (a sock's foot with a shoe's, never a thigh with a
    /// skirt's): only those triangles hide the point or cut its edges. Null counts all; a null row counts none.
    /// </param>
    /// <param name="hides">Whether the garment can hide what is under it at all (a sheer one, or one fitted to a face, can't).</param>
    public static Cut Measure(FitShape shape, Vector3[] points, Vector3[] normals, int[] tags, FitBvh garment, bool[][] allow,
        bool hides, in Rules rules)
    {
        if (garment.TriangleCount == 0) return null;
        Vector3 low = garment.Min - Vector3.one * rules.Near, high = garment.Max + Vector3.one * rules.Near;
        var inside = new bool[shape.Count];
        var near = new List<int>();
        for (int p = 0; p < shape.Count; p++)
        {
            Vector3 at = points[p];
            if (at.x < low.x || at.y < low.y || at.z < low.z || at.x > high.x || at.y > high.y || at.z > high.z) continue;
            inside[p] = true;
            near.Add(p);
        }
        if (near.Count == 0) return null;
        var cut = new Cut
        {
            Near = near.ToArray(), Behind = new bool[near.Count], Depth = new float[near.Count], Stopped = hides ? new ulong[near.Count] : null,
            On = hides ? new bool[near.Count] : null
        };
        for (int i = 0; i < cut.Depth.Length; i++) cut.Depth[i] = -1f;
        float facing = rules.Facing, behind = rules.Behind, tuck = rules.Tuck, on = rules.On;
        Parallel.For(0, cut.Near.Length, i =>
        {
            int p = cut.Near[i];
            Vector3 normal = normals[p];
            if (normal == Vector3.zero) return;
            bool[] counted = null;
            if (allow != null)
            {
                int tag = tags[p];
                counted = tag >= 0 && tag < allow.Length ? allow[tag] : null;
                if (counted == null) return;
            }
            Vector3 at = points[p];
            if (cut.Stopped != null)
            {
                cut.On[i] = garment.Blocks(at, normal, on, counted);
                ulong stopped = 0;
                for (int w = 0; w < WayCount; w++)
                    if (Vector3.Dot(normal, Ways[w]) > facing && garment.Blocks(at, Ways[w], float.PositiveInfinity, counted))
                        stopped |= 1UL << w;
                cut.Stopped[i] = stopped;
            }
            // Straight behind, and leaning four ways round it.
            Vector3 back = -normal;
            float depth = garment.Nearest(at, back, Mathf.Max(behind, tuck), counted);
            cut.Depth[i] = depth;
            bool found = depth >= 0f && depth <= behind;
            if (!found)
            {
                Vector3 side = Mathf.Abs(normal.y) < .9f ? Vector3.Cross(normal, Vector3.up).normalized : Vector3.Cross(normal, Vector3.right).normalized;
                Vector3 other = Vector3.Cross(normal, side);
                found = garment.Blocks(at, (back + side * WallLean).normalized, behind, counted)
                    || garment.Blocks(at, (back - side * WallLean).normalized, behind, counted)
                    || garment.Blocks(at, (back + other * WallLean).normalized, behind, counted)
                    || garment.Blocks(at, (back - other * WallLean).normalized, behind, counted);
            }
            cut.Behind[i] = found;
        });
        int edgeCount = shape.EdgeCount;
        var first = new float[edgeCount];
        var last = new float[edgeCount];
        var crossed = new bool[edgeCount];
        Parallel.For(0, edgeCount, e =>
        {
            int a = shape.Edges[e * 2], b = shape.Edges[e * 2 + 1];
            if (!inside[a] && !inside[b]) return;
            bool[] forA = null, forB = null;
            if (allow != null)
            {
                int tagA = tags[a], tagB = tags[b];
                forA = tagA >= 0 && tagA < allow.Length ? allow[tagA] : null;
                forB = tagB >= 0 && tagB < allow.Length ? allow[tagB] : null;
                if (forA == null && forB == null) return;
            }
            float least = float.PositiveInfinity, most = float.NegativeInfinity;
            bool any = false;
            if (allow == null || forA != null)
                any |= garment.Crossings(points[a], points[b], forA, out least, out most) > 0;
            // An edge between two bones is cut by what moves with either.
            if (allow != null && forB != null && forB != forA
                && garment.Crossings(points[a], points[b], forB, out float least2, out float most2) > 0)
            {
                least = any ? Mathf.Min(least, least2) : least2;
                most = any ? Mathf.Max(most, most2) : most2;
                any = true;
            }
            if (!any) return;
            crossed[e] = true;
            first[e] = least;
            last[e] = most;
        });
        var edges = new List<int>();
        for (int e = 0; e < edgeCount; e++)
            if (crossed[e]) edges.Add(e);
        cut.Edges = edges.ToArray();
        cut.First = new float[cut.Edges.Length];
        cut.Last = new float[cut.Edges.Length];
        for (int i = 0; i < cut.Edges.Length; i++)
        {
            cut.First[i] = first[cut.Edges[i]];
            cut.Last[i] = last[cut.Edges[i]];
        }
        return cut;
    }

    /// <summary>What an outfit leaves of a layer: every garment over it, each measured (Measure), together.</summary>
    public static Result Solve(FitShape shape, Vector3[] points, Vector3[] normals, IReadOnlyList<Cut> cuts, in Rules rules)
    {
        int count = shape.Count, edgeCount = shape.EdgeCount;
        var near = new bool[count];
        var behind = new bool[count];
        var depth = new float[count];
        var on = new bool[count];
        var stopped = new ulong[count];
        var crossed = new bool[edgeCount];
        var first = new float[edgeCount];
        var last = new float[edgeCount];
        bool anyCrossed = false, anyStopped = false;
        foreach (var cut in cuts)
        {
            if (cut == null) continue;
            for (int i = 0; i < cut.Near.Length; i++)
            {
                int p = cut.Near[i];
                near[p] = true;
                if (cut.Behind[i]) behind[p] = true;
                // The nearest garment behind the point is the one it lies on.
                if (cut.Depth[i] >= 0f && (depth[p] <= 0f || cut.Depth[i] < depth[p])) depth[p] = cut.Depth[i];
                if (cut.Stopped != null && cut.Stopped[i] != 0)
                {
                    stopped[p] |= cut.Stopped[i];
                    if (cut.On[i]) on[p] = anyStopped = true;
                }
            }
            for (int i = 0; i < cut.Edges.Length; i++)
            {
                int e = cut.Edges[i];
                if (!crossed[e])
                {
                    crossed[e] = true;
                    first[e] = cut.First[i];
                    last[e] = cut.Last[i];
                    anyCrossed = true;
                }
                else
                {
                    if (cut.First[i] < first[e]) first[e] = cut.First[i];
                    if (cut.Last[i] > last[e]) last[e] = cut.Last[i];
                }
            }
        }
        var result = new Result { Gone = new bool[shape.TriangleCount], Moved = Array.Empty<int>(), MovedTo = Array.Empty<Vector3>() };
        if (!anyCrossed && !anyStopped) return result;

        // Hidden: something lies on the point, and every way out on its outer side is stopped.
        var hidden = new bool[count];
        if (anyStopped)
            for (int p = 0; p < count; p++)
            {
                if (!on[p]) continue;
                Vector3 normal = normals[p];
                ulong outer = 0;
                for (int w = 0; w < WayCount; w++)
                    if (Vector3.Dot(normal, Ways[w]) > rules.Facing) outer |= 1UL << w;
                if (outer != 0 && (stopped[p] & outer) == outer)
                {
                    hidden[p] = true;
                    result.Hidden++;
                }
            }

        // The walk: from every point surely where it should be, over every edge that passes through no garment.
        var reached = new bool[count];
        var line = new Queue<int>();
        for (int p = 0; p < count; p++)
            if (!near[p] || (!hidden[p] && !behind[p]))
            {
                reached[p] = true;
                line.Enqueue(p);
            }
        Walk(shape, crossed, reached, line);
        // A piece of the layer that touches no crossing at all never went through anything (a ribbon lying against a
        // shoe has the shoe behind it everywhere, and nowhere to start from): it is where its maker put it.
        if (anyCrossed)
        {
            var seen = new bool[count];
            var piece = new List<int>();
            for (int p = 0; p < count; p++)
            {
                if (reached[p] || seen[p]) continue;
                piece.Clear();
                bool cutOff = false;
                seen[p] = true;
                line.Enqueue(p);
                while (line.Count > 0)
                {
                    int at = line.Dequeue();
                    piece.Add(at);
                    for (int k = shape.AroundStart[at]; k < shape.AroundStart[at + 1]; k++)
                    {
                        if (crossed[shape.AroundEdge[k]])
                        {
                            cutOff = true;
                            continue;
                        }
                        int next = shape.AroundPoint[k];
                        if (reached[next] || seen[next]) continue;
                        seen[next] = true;
                        line.Enqueue(next);
                    }
                }
                if (!cutOff)
                    foreach (int at in piece) reached[at] = true;
            }
        }
        else
        {
            for (int p = 0; p < count; p++) reached[p] = true;
        }

        // What goes: everything that came through a garment, right up to where it came through; and what is hidden, but
        // not the rings of hidden points beside the seen ones (Rules.Margin).
        var gone = new bool[count];
        for (int p = 0; p < count; p++)
        {
            if (!reached[p]) result.Unreached++;
            gone[p] = !reached[p] || hidden[p];
        }
        var beside = new List<int>();
        for (int ring = 0; ring < rules.Margin; ring++)
        {
            beside.Clear();
            for (int p = 0; p < count; p++)
            {
                if (!gone[p] || !reached[p]) continue;
                for (int k = shape.AroundStart[p]; k < shape.AroundStart[p + 1]; k++)
                    if (!gone[shape.AroundPoint[k]])
                    {
                        beside.Add(p);
                        break;
                    }
            }
            foreach (int p in beside) gone[p] = false;
        }
        var drawn = new bool[count];
        for (int t = 0; t < result.Gone.Length; t++)
        {
            int a = shape.Corners[t * 3], b = shape.Corners[t * 3 + 1], c = shape.Corners[t * 3 + 2];
            if (gone[a] && gone[b] && gone[c])
            {
                result.Gone[t] = true;
                result.GoneCount++;
            }
            else drawn[a] = drawn[b] = drawn[c] = true;
        }

        // Points just past a crossing, on triangles still drawn: back along each crossed edge to just short of it.
        var sum = new Vector3[count];
        var shares = new int[count];
        if (anyCrossed)
            for (int e = 0; e < edgeCount; e++)
            {
                if (!crossed[e]) continue;
                int a = shape.Edges[e * 2], b = shape.Edges[e * 2 + 1];
                if (reached[a] == reached[b]) continue;
                int kept = reached[a] ? a : b, past = reached[a] ? b : a;
                if (!drawn[past]) continue;
                float share = reached[a] ? first[e] : 1f - last[e];
                Vector3 along = points[past] - points[kept];
                float length = along.magnitude;
                if (length < 1e-9f) continue;
                share = Mathf.Clamp01(share - rules.Inset / length);
                sum[past] += points[kept] + along * share;
                shares[past]++;
            }
        // Cloth lying on the outside of a garment, patch by patch: a patch that mostly lies shallow is a misfit, to tuck in.
        var tucked = new bool[count];
        if (rules.Tuck > 0f)
        {
            var seen = new bool[count];
            var patch = new List<int>();
            for (int p = 0; p < count; p++)
            {
                if (seen[p] || !Lies(p, depth, reached, hidden)) continue;
                patch.Clear();
                int shallow = 0;
                seen[p] = true;
                line.Enqueue(p);
                while (line.Count > 0)
                {
                    int at = line.Dequeue();
                    patch.Add(at);
                    if (depth[at] <= rules.Tuck) shallow++;
                    for (int k = shape.AroundStart[at]; k < shape.AroundStart[at + 1]; k++)
                    {
                        int next = shape.AroundPoint[k];
                        if (seen[next] || !Lies(next, depth, reached, hidden)) continue;
                        seen[next] = true;
                        line.Enqueue(next);
                    }
                }
                if (shallow < patch.Count * rules.TuckShare) continue;
                foreach (int at in patch)
                    if (depth[at] <= rules.Tuck) tucked[at] = true;
            }
        }
        var moved = new List<int>();
        var movedTo = new List<Vector3>();
        for (int p = 0; p < count; p++)
        {
            if (shares[p] > 0)
            {
                moved.Add(p);
                movedTo.Add(sum[p] / shares[p]);
            }
            else if (tucked[p] && drawn[p])
            {
                // Lying on the outside of a garment, a little way out: in under it, as a shoe holds a sock.
                moved.Add(p);
                movedTo.Add(points[p] - normals[p] * (depth[p] + rules.Inset));
            }
            else if (rules.Dive > 0f && hidden[p] && reached[p] && drawn[p] && normals[p] != Vector3.zero)
            {
                // Under the garment and unseen, but a corner of something seen: further in, so the seen part dips
                // under the garment's edge as skin does under a stocking's band, and never shimmers through it.
                moved.Add(p);
                movedTo.Add(points[p] - normals[p] * rules.Dive);
            }
        }
        result.Moved = moved.ToArray();
        result.MovedTo = movedTo.ToArray();
        return result;
    }

    // Whether a point lies on the outside of a garment: seen, where it should be, and the garment straight behind it.
    private static bool Lies(int point, float[] depth, bool[] reached, bool[] hidden) => depth[point] > 0f && reached[point] && !hidden[point];

    private static void Walk(FitShape shape, bool[] crossed, bool[] reached, Queue<int> line)
    {
        while (line.Count > 0)
        {
            int at = line.Dequeue();
            for (int k = shape.AroundStart[at]; k < shape.AroundStart[at + 1]; k++)
            {
                if (crossed[shape.AroundEdge[k]]) continue;
                int next = shape.AroundPoint[k];
                if (reached[next]) continue;
                reached[next] = true;
                line.Enqueue(next);
            }
        }
    }
}
