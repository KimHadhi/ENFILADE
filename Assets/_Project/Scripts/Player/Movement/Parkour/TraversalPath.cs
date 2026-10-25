using System.Collections.Generic;
using UnityEngine;

/// <summary>A rounded, arc-length parameterized route. Speed does not reset at each corner.</summary>
public sealed class TraversalPath
{
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<float> distances = new List<float>();
    public float Length { get; private set; }
    public TraversalPath(ObstacleTarget target, ParkourDetector detector, float height)
    {
        if (!target.HasApproach && detector.ClearPath(target.Start, target.End, height))
        { points.Add(target.Start); points.Add(target.End); Measure(); return; }
        if (!target.HasApproach && TryRise(target, detector, height)) { Measure(); return; }
        var controls = new List<Vector3> { target.Start };
        var route = target.HasApproach
            ? new[] { target.Approach, target.RaisedStart, target.RaisedEnd, target.End }
            : new[] { target.RaisedStart, target.RaisedEnd, target.End };
        foreach (Vector3 p in route)
            if (Vector3.Distance(controls[controls.Count - 1], p) > 0.001f) controls.Add(p);
        for (float rounding = 0.9f; ; rounding *= 0.5f)
        {
            if (rounding < 0.01f) rounding = 0f;
            points.Clear(); points.Add(controls[0]);
            for (int i = 1; i < controls.Count - 1; i++)
            {
                Vector3 before = controls[i - 1] - controls[i], after = controls[i + 1] - controls[i];
                float cut = Mathf.Min(rounding, Mathf.Min(before.magnitude, after.magnitude) * 0.4f);
                Vector3 a = controls[i] + before.normalized * cut, b = controls[i] + after.normalized * cut;
                points.Add(a);
                for (int j = 1; j <= 12; j++)
                {
                    float t = j / 12f;
                    points.Add((1f - t) * (1f - t) * a + 2f * (1f - t) * t * controls[i] + t * t * b);
                }
            }
            points.Add(controls[controls.Count - 1]);
            if (IsClear(detector, height) || rounding == 0f) break;
        }
        Measure();
    }
    private bool TryRise(ObstacleTarget target, ParkourDetector detector, float height)
    {
        // Rise only as far as the clearance plane, then move through it. There is
        // no artificial apex or forced landing arc after the obstacle is cleared.
        foreach (float riseFraction in new[] { 0.65f, 0.45f, 0.25f })
        {
            points.Clear();
            for (int i = 0; i <= 48; i++)
            {
                float t = i / 48f;
                Vector3 point = Vector3.Lerp(target.Start, target.End, t);
                float rise = Mathf.Clamp01(t / riseFraction);
                point.y = Mathf.Lerp(target.Start.y, target.End.y, 1f - Mathf.Pow(1f - rise, 3f));
                points.Add(point);
            }
            if (IsClear(detector, height)) return true;
        }
        return false;
    }
    private bool IsClear(ParkourDetector detector, float height)
    {
        for (int i = 1; i < points.Count; i++)
            if (!detector.ClearPath(points[i - 1], points[i], height)) return false;
        return true;
    }
    private void Measure()
    {
        distances.Add(0f);
        for (int i = 1; i < points.Count; i++)
        { Length += Vector3.Distance(points[i - 1], points[i]); distances.Add(Length); }
    }
    public Vector3 Evaluate(float t)
    {
        float distance = Mathf.Clamp01(t) * Length;
        for (int i = 1; i < points.Count; i++)
            if (distance <= distances[i]) return Vector3.Lerp(points[i - 1], points[i],
                Mathf.InverseLerp(distances[i - 1], distances[i], distance));
        return points[points.Count - 1];
    }
}
