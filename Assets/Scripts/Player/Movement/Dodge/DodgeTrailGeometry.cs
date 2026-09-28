using UnityEngine;

// The swept capsule is a vertical quad expanded by the body radius. Measuring
// distance to that quad avoids gaps between sampled capsules, including diagonal dodges.
internal static class DodgeTrailGeometry
{
    internal static float SegmentQuadDistanceSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        => Mathf.Min(SegmentTriangle(p, q, a, b, c), SegmentTriangle(p, q, a, c, d));

    private static float SegmentTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        float denom = Vector3.Dot(normal, q - p);
        if (Mathf.Abs(denom) > 0.000001f)
        {
            float t = Vector3.Dot(normal, a - p) / denom;
            if (t >= 0f && t <= 1f && PointTriangle(p + (q - p) * t, a, b, c) < 0.0000001f) return 0f;
        }
        float result = Mathf.Min(PointTriangle(p, a, b, c), PointTriangle(q, a, b, c));
        result = Mathf.Min(result, Segments(p, q, a, b));
        result = Mathf.Min(result, Segments(p, q, b, c));
        return Mathf.Min(result, Segments(p, q, c, a));
    }
    private static float PointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        if (Vector3.Cross(ab, ac).sqrMagnitude < 0.00000001f)
            return Mathf.Min(Segments(p, p, a, b), Mathf.Min(Segments(p, p, b, c), Segments(p, p, c, a)));
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (p - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (p - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).sqrMagnitude;
        float inverse = 1f / (va + vb + vc);
        return (p - (a + ab * (vb * inverse) + ac * (vc * inverse))).sqrMagnitude;
    }
    private static float Segments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = d1.sqrMagnitude, e = d2.sqrMagnitude, f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 0.0000001f && e <= 0.0000001f) return r.sqrMagnitude;
        if (a <= 0.0000001f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 0.0000001f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > 0.0000001f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        return (p1 + d1 * s - p2 - d2 * t).sqrMagnitude;
    }
}
