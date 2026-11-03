using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A skirt's rest shape as a ring round the body: for each chain of bones, how far out it hangs at each height. A hand the
/// clip puts inside the skirt can be moved out onto it (PlayerBodyHands.Skirt). Made from the bones' rest places relative
/// to the bone the skirt hangs from, so it rides the hips and comes out the same on every PC, simulating or not.
/// </summary>
internal sealed class SkirtShell
{
    // Within this share of a chain's height from its waist or its hem the shell thins away, so a hand slides off the
    // skirt's edge instead of jumping at it.
    private const float EdgeFade = .15f;

    private readonly Transform anchor;
    // In the anchor's space: the middle of the waist, the way the skirt hangs, and two directions across it for angles.
    private readonly Vector3 centre, axis, across, aside;
    private readonly float[] angles;
    private readonly int[] order;
    private readonly float[][] heights, radii;

    internal SkirtShell(List<Transform> roots)
    {
        anchor = roots[0].parent;
        var chains = new List<Vector3[]>();
        foreach (var root in roots)
        {
            var chain = new List<Vector3>();
            for (var bone = root; bone != null; bone = bone.childCount > 0 ? bone.GetChild(0) : null)
                chain.Add(anchor.InverseTransformPoint(bone.position));
            chains.Add(chain.ToArray());
        }
        Vector3 deep = Vector3.zero;
        int count = 0;
        foreach (var chain in chains)
        {
            centre += chain[0];
            foreach (var point in chain) { deep += point; count++; }
        }
        centre /= chains.Count;
        axis = (deep / count - centre).normalized;
        if (axis == Vector3.zero) axis = anchor.InverseTransformDirection(Vector3.down).normalized;
        across = Vector3.ProjectOnPlane(chains[0][chains[0].Length - 1] - centre, axis).normalized;
        aside = Vector3.Cross(axis, across);

        angles = new float[chains.Count];
        heights = new float[chains.Count][];
        radii = new float[chains.Count][];
        for (int k = 0; k < chains.Count; k++)
        {
            var chain = chains[k];
            heights[k] = new float[chain.Length];
            radii[k] = new float[chain.Length];
            Vector3 outward = Vector3.zero;
            for (int j = 0; j < chain.Length; j++)
            {
                Vector3 off = chain[j] - centre;
                heights[k][j] = Vector3.Dot(off, axis);
                Vector3 radial = off - axis * heights[k][j];
                radii[k][j] = radial.magnitude;
                outward += radial;
            }
            angles[k] = Angle(outward);
        }
        order = new int[chains.Count];
        for (int k = 0; k < order.Length; k++) order[k] = k;
        System.Array.Sort(order, (a, b) => angles[a].CompareTo(angles[b]));
    }

    private float Angle(Vector3 radial) => Mathf.Atan2(Vector3.Dot(radial, aside), Vector3.Dot(radial, across));

    /// <summary>
    /// How far a point inside the skirt must move straight out from the body to sit this far (metres) outside its rest
    /// shape (world). Zero for a point already out there, above the waist or below the hem.
    /// </summary>
    internal Vector3 Out(Vector3 point, float clearance)
    {
        float scale = Mathf.Abs(anchor.lossyScale.x);
        if (scale <= 0f || order.Length < 2) return Vector3.zero;
        Vector3 local = anchor.InverseTransformPoint(point) - centre;
        float height = Vector3.Dot(local, axis);
        Vector3 radial = local - axis * height;
        float distance = radial.magnitude;
        if (distance < 1e-5f) return Vector3.zero;

        // The chains either side of the point, going round.
        float angle = Angle(radial);
        int after = 0;
        while (after < order.Length && angles[order[after]] < angle) after++;
        int a = order[(after + order.Length - 1) % order.Length], b = order[after % order.Length];
        float from = angles[a], to = angles[b], at = angle;
        if (to <= from) to += 2f * Mathf.PI;
        if (at < from) at += 2f * Mathf.PI;
        float share = Mathf.Clamp01((at - from) / (to - from));
        if (!Hangs(a, height, out float radiusA, out float coverA) || !Hangs(b, height, out float radiusB, out float coverB))
            return Vector3.zero;

        float push = (Mathf.Lerp(radiusA, radiusB, share) + clearance / scale - distance) * Mathf.Lerp(coverA, coverB, share);
        return push > 0f ? anchor.TransformVector(radial / distance * push) : Vector3.zero;
    }

    // How far out the chain hangs at this height, and how far that height is from its waist and hem (0 at an edge, 1 well inside).
    private bool Hangs(int chain, float height, out float radius, out float cover)
    {
        radius = cover = 0f;
        float[] h = heights[chain], r = radii[chain];
        float span = Mathf.Abs(h[h.Length - 1] - h[0]);
        for (int j = 0; j + 1 < h.Length; j++)
        {
            if (Mathf.Abs(h[j + 1] - h[j]) < 1e-6f || height < Mathf.Min(h[j], h[j + 1]) || height > Mathf.Max(h[j], h[j + 1])) continue;
            radius = Mathf.Lerp(r[j], r[j + 1], (height - h[j]) / (h[j + 1] - h[j]));
            float along = span > 0f ? Mathf.Abs(height - h[0]) / span : 0f;
            cover = Mathf.Clamp01(Mathf.Min(along, 1f - along) / EdgeFade);
            return true;
        }
        return false;
    }
}
