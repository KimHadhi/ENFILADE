using System;
using System.Collections.Generic;
using UnityEngine;

// OutfitStudio: Parts well apart. One part of the class; the rest is in OutfitStudio.cs and the files beside it.
public sealed partial class OutfitStudio
{
    // A piece made as parts far apart, a pair of gloves on outstretched hands (83 cm between them), fitted whole left each
    // glove a sliver of its picture. The points are cut at the widest empty gap along the picture's across, up or depth
    // wherever the gap is wide (WardrobePictures.csv Auto.splitGap, m) or wide next to the parts' own widths that way
    // (Auto.splitRatio), again inside each side, and the sides are the parts; a side with almost none of the points
    // (Auto.noise) is a stray, framed never. Shoes, socks and stockings (2-5 cm apart) stay a pair (the user's
    // outfits measured 2026-11-12).
    private void SplitParts()
    {
        parts.Clear();
        autoPart = 0;
        int count = points.Count;
        if (count == 0) return;
        if (order.Length < count) order = new int[count];
        if (along.Length < count) along = new float[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Vector3 across = Vector3.Cross(front, Vector3.up);
        across = across.sqrMagnitude > 1e-6f ? across.normalized : Vector3.right;
        Vector3[] sides = { across, Vector3.up, front };
        float wide = OutfitPictureLook.AutoSplitGap, ratio = OutfitPictureLook.AutoSplitRatio;
        var leaves = new List<(int start, int size)>();
        var pending = new Stack<(int start, int size, int depth)>();
        pending.Push((0, count, 0));
        while (pending.Count > 0)
        {
            var (start, size, depth) = pending.Pop();
            if (depth < MostSplits && size > 1 && WidestGap(start, size, sides, wide, ratio, out Vector3 side, out float cut))
            {
                int split = Partition(start, size, side, cut);
                if (split > start && split < start + size)
                {
                    pending.Push((split, start + size - split, depth + 1));
                    pending.Push((start, split - start, depth + 1));
                    continue;
                }
            }
            leaves.Add((start, size));
        }
        int least = Mathf.Max(1, Mathf.CeilToInt(count * OutfitPictureLook.AutoNoise));
        int most = 0;
        foreach (var (_, size) in leaves) most = Mathf.Max(most, size);
        foreach (var (start, size) in leaves)
        {
            if (size < least && size < most) continue;
            float sum = 0f;
            for (int i = start; i < start + size; i++) sum += Vector3.Dot(points[order[i]], across);
            parts.Add(new Part(start, size, sum / size));
        }
        parts.Sort((a, b) => a.Across.CompareTo(b.Across));
        if (parts.Count < 2) return;
        // Alone by default: the biggest part, or of parts alike (a pair) the one on the picture's right, where the plate
        // shows it clear of its edge.
        int biggest = 0;
        foreach (var part in parts) biggest = Mathf.Max(biggest, part.Count);
        for (int i = parts.Count - 1; i >= 0; i--)
            if (parts[i].Count >= biggest * .85f)
            {
                autoPart = i + 1;
                break;
            }
    }

    // The widest empty gap of the points order[start..start+size) along one of the sides, where it is wide enough to part
    // them: the side and where to cut. Of more than one, the widest next to the parts' own widths.
    private bool WidestGap(int start, int size, Vector3[] sides, float wide, float ratio, out Vector3 side, out float cut)
    {
        side = Vector3.zero;
        cut = 0f;
        float best = 0f;
        foreach (var direction in sides)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < size; i++)
            {
                float value = Vector3.Dot(points[order[start + i]], direction);
                along[i] = value;
                low = Mathf.Min(low, value);
                high = Mathf.Max(high, value);
            }
            float span = high - low;
            if (span < 1e-5f) continue;
            Array.Clear(filled, 0, GapBins);
            for (int i = 0; i < size; i++) filled[Mathf.Min(GapBins - 1, (int)((along[i] - low) / span * (GapBins - 1)))] = true;
            int run = 0, runStart = 0, longest = 0, longestStart = 0;
            for (int bin = 0; bin < GapBins; bin++)
            {
                if (!filled[bin])
                {
                    if (run == 0) runStart = bin;
                    run++;
                    continue;
                }
                if (run > longest)
                {
                    longest = run;
                    longestStart = runStart;
                }
                run = 0;
            }
            if (longest == 0) continue;
            float at = low + (longestStart + longest * .5f) / (GapBins - 1) * span;
            float leftHigh = float.MinValue, rightLow = float.MaxValue;
            for (int i = 0; i < size; i++)
            {
                if (along[i] < at) leftHigh = Mathf.Max(leftHigh, along[i]);
                else rightLow = Mathf.Min(rightLow, along[i]);
            }
            if (leftHigh == float.MinValue || rightLow == float.MaxValue) continue;
            float gap = rightLow - leftHigh, width = Mathf.Max(leftHigh - low, high - rightLow);
            if (gap < wide && gap < ratio * width) continue;
            float score = gap / Mathf.Max(1e-4f, width);
            if (score <= best) continue;
            best = score;
            side = direction;
            cut = at;
        }
        return best > 0f;
    }

    // Puts the points of order[start..start+size) below the cut along the side first; where the rest begins.
    private int Partition(int start, int size, Vector3 side, float cut)
    {
        int low = start, high = start + size - 1;
        while (low <= high)
        {
            if (Vector3.Dot(points[order[low]], side) < cut) low++;
            else
            {
                (order[low], order[high]) = (order[high], order[low]);
                high--;
            }
        }
        return low;
    }

    // A framing's part as it stands: 0 the whole piece, else 1 from the picture's left.
    private int Resolve(int part)
    {
        if (parts.Count < 2 || part < 0) return 0;
        return part == 0 ? autoPart : Mathf.Min(part, parts.Count);
    }

    // The points the lens frames: one part, or all of them (strays left out), and their middle.
    private void Select(int part)
    {
        framed.Clear();
        if (part >= 1 && part <= parts.Count) Gather(parts[part - 1]);
        else foreach (var each in parts) Gather(each);
        if (framed.Count == 0) framed.AddRange(points);
        var box = new Bounds(framed.Count > 0 ? framed[0] : root.position, Vector3.zero);
        foreach (var point in framed) box.Encapsulate(point);
        middle = box.center;
        framedPart = part;
    }

    private void Gather(Part part)
    {
        for (int i = part.Start; i < part.Start + part.Count; i++) framed.Add(points[order[i]]);
    }
}
