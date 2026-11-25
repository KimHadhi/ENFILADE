using UnityEditor;
using UnityEngine;

// GunClipRetarget: fewer keys. One part of the class; the rest is in GunClipRetarget.cs and the files beside it.
internal static partial class GunClipRetarget
{
    // How far a dropped key may take a curve from the bake: muscle units (a muscle's whole range is -1..1, so a few
    // hundredths of a degree) and the root's metres (half a millimetre). Retarget Pro and GunClipFingers key every frame
    // of every muscle; the bakes were 875 MB of text in git with 123 clips (MX68_Inspect 24 MB). The same rule thinned the
    // clips already made on 2026-11-25 to about a seventh of their keys.
    private const float KeyTolerance = .0005f;

    /// <summary>Drops the keys each curve of the clip doesn't need: a key goes when the curve between the keys kept on
    /// either side, with their own tangents, passes within KeyTolerance of it and of every point halfway between frames.</summary>
    private static void ThinKeys(AnimationClip clip)
    {
        if (clip == null) return;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var curves = new AnimationCurve[bindings.Length];
        bool changed = false;
        for (int b = 0; b < bindings.Length; b++)
        {
            curves[b] = AnimationUtility.GetEditorCurve(clip, bindings[b]);
            var thinned = Thinned(curves[b]);
            if (thinned == null) continue;
            curves[b] = thinned;
            changed = true;
        }
        if (!changed) return;
        AnimationUtility.SetEditorCurves(clip, bindings, curves);
        EditorUtility.SetDirty(clip);
    }

    // The curve with only the keys it needs; null when it has none to spare or can't be read as plain Hermite segments.
    private static AnimationCurve Thinned(AnimationCurve curve)
    {
        if (curve == null) return null;
        var keys = curve.keys;
        int count = keys.Length;
        if (count <= 2) return null;
        foreach (var key in keys)
            if (key.weightedMode != WeightedMode.None || !float.IsFinite(key.value) || !float.IsFinite(key.inTangent)
                || !float.IsFinite(key.outTangent))
                return null;
        var kept = new System.Collections.Generic.List<Keyframe>(count) { keys[0] };
        int from = 0;
        while (from < count - 1)
        {
            int to = from + 1;
            while (to + 1 < count && Spans(keys, from, to + 1)) to++;
            kept.Add(keys[to]);
            from = to;
        }
        if (kept.Count == count) return null;
        return new AnimationCurve(kept.ToArray()) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
    }

    // Whether the one segment from key a to key b stays within KeyTolerance of the keys between them and of the original
    // curve halfway between each pair of neighbouring keys.
    private static bool Spans(Keyframe[] keys, int a, int b)
    {
        for (int m = a + 1; m < b; m++)
            if (Mathf.Abs(Hermite(keys[a], keys[b], keys[m].time) - keys[m].value) > KeyTolerance) return false;
        for (int m = a; m < b; m++)
        {
            float half = (keys[m].time + keys[m + 1].time) * .5f;
            if (Mathf.Abs(Hermite(keys[a], keys[b], half) - Hermite(keys[m], keys[m + 1], half)) > KeyTolerance) return false;
        }
        return true;
    }

    // An unweighted curve segment at time t, as Unity draws it.
    private static float Hermite(Keyframe from, Keyframe to, float t)
    {
        float span = to.time - from.time;
        if (span <= 0f) return from.value;
        float s = (t - from.time) / span, s2 = s * s, s3 = s2 * s;
        return (2f * s3 - 3f * s2 + 1f) * from.value + (s3 - 2f * s2 + s) * span * from.outTangent
            + (-2f * s3 + 3f * s2) * to.value + (s3 - s2) * span * to.inTangent;
    }
}
