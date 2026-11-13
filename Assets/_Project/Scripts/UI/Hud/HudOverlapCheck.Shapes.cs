using System.Collections.Generic;
using UnityEngine;

// HudOverlapCheck: Shapes. One part of the class; the rest is in HudOverlapCheck.cs and the files beside it.
public sealed partial class HudOverlapCheck
{
    private static Rect Box(Vector2[] shape)
    {
        float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity, xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;
        foreach (var p in shape)
        {
            if (p.x < xMin) xMin = p.x;
            if (p.y < yMin) yMin = p.y;
            if (p.x > xMax) xMax = p.x;
            if (p.y > yMax) yMax = p.y;
        }
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Vector2 Middle(Vector2[] shape)
    {
        if (shape == null || shape.Length == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        foreach (var p in shape) sum += p;
        return sum / shape.Length;
    }

    private static float SignedArea(Vector2[] shape)
    {
        float area = 0f;
        for (int i = 0; i < shape.Length; i++)
        {
            Vector2 a = shape[i], b = shape[(i + 1) % shape.Length];
            area += a.x * b.y - b.x * a.y;
        }
        return area * .5f;
    }

    // How far two convex shapes would have to move apart to clear each other (px); 0 when they do not overlap.
    private static float Depth(Vector2[] a, Vector2[] b)
    {
        float best = float.PositiveInfinity;
        if (!Separates(a, a, b, ref best) || !Separates(b, a, b, ref best)) return 0f;
        return float.IsInfinity(best) ? 0f : best;
    }

    // Tries the edge normals of one shape as separating axes: false when one of them separates the shapes; best is the
    // smallest overlap along any axis tried.
    private static bool Separates(Vector2[] edges, Vector2[] a, Vector2[] b, ref float best)
    {
        for (int i = 0; i < edges.Length; i++)
        {
            Vector2 along = edges[(i + 1) % edges.Length] - edges[i];
            var normal = new Vector2(-along.y, along.x);
            float length = normal.magnitude;
            if (length < 1e-5f) continue;
            normal /= length;
            Project(a, normal, out float aMin, out float aMax);
            Project(b, normal, out float bMin, out float bMax);
            float overlap = Mathf.Min(aMax - bMin, bMax - aMin);
            if (overlap <= 0f) return false;
            if (overlap < best) best = overlap;
        }
        return true;
    }

    private static void Project(Vector2[] shape, Vector2 axis, out float min, out float max)
    {
        min = float.PositiveInfinity;
        max = float.NegativeInfinity;
        foreach (var p in shape)
        {
            float d = Vector2.Dot(p, axis);
            if (d < min) min = d;
            if (d > max) max = d;
        }
    }

    // Sutherland-Hodgman: what of a convex shape lies inside a convex clip shape (either winding); null when nothing.
    private static Vector2[] Clip(Vector2[] subject, Vector2[] clip)
    {
        float winding = Mathf.Sign(SignedArea(clip));
        if (winding == 0f) return null;
        var output = new List<Vector2>(subject);
        for (int e = 0; e < clip.Length && output.Count > 0; e++)
        {
            Vector2 a = clip[e], b = clip[(e + 1) % clip.Length];
            var input = output;
            output = new List<Vector2>(input.Count + 2);
            for (int i = 0; i < input.Count; i++)
            {
                Vector2 p = input[i], q = input[(i + 1) % input.Count];
                bool pIn = Inside(a, b, p, winding), qIn = Inside(a, b, q, winding);
                if (pIn)
                {
                    output.Add(p);
                    if (!qIn) output.Add(Crossing(a, b, p, q));
                }
                else if (qIn) output.Add(Crossing(a, b, p, q));
            }
        }
        return output.Count >= 3 ? output.ToArray() : null;
    }

    private static bool Inside(Vector2 a, Vector2 b, Vector2 p, float winding)
        => winding * ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) >= 0f;

    private static Vector2 Crossing(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
    {
        Vector2 r = q - p, s = b - a;
        float across = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(across) < 1e-6f) return p;
        float t = ((a.x - p.x) * s.y - (a.y - p.y) * s.x) / across;
        return p + r * t;
    }
}
