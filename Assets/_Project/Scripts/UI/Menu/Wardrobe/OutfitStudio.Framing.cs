using UnityEngine;

// OutfitStudio: Framing. One part of the class; the rest is in OutfitStudio.cs and the files beside it.
public sealed partial class OutfitStudio
{
    /// <summary>
    /// Points the lens at the standing piece as <paramref name="framing"/> says: round it from its front, leaning in the
    /// picture, the whole of it (or of the part it holds, PartOf) fitted in a picture of this <paramref name="aspect"/>,
    /// then the lens narrowed by the zoom and the piece moved (along the picture's own sides).
    /// </summary>
    public void Frame(OutfitFraming framing, float aspect = 1f)
    {
        int part = Resolve(framing.part);
        if (part != framedPart) Select(part);
        if (framed.Count == 0) return;
        aspect = Mathf.Max(.1f, aspect);
        float baseYaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
        var upright = Quaternion.Euler(Mathf.Clamp(OutfitPictureLook.AutoPitch + framing.pitch, -85f, 85f), baseYaw + framing.yaw, 0f);
        // The lens turned about its own axis: turned so (+ about its forward axis, its top towards its left) the piece
        // leans clockwise in the picture. The fit below is in the turned view, so the leaning piece still fits whole.
        var view = upright * Quaternion.Euler(0f, 0f, OutfitPictureLook.AutoTilt + framing.roll);
        var into = Quaternion.Inverse(view);
        float fill = OutfitPictureLook.AutoFill;
        float tanV = Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad), tanH = tanV * aspect;
        // Fitted round the box's middle, then once more round the middle of what that showed, so it sits centred.
        Vector2 shift = Vector2.zero;
        float distance = .1f, nearest = 0f, farthest = 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            distance = .05f;
            foreach (var point in framed)
            {
                Vector3 seen = into * (point - middle);
                float need = Mathf.Max(Mathf.Abs(seen.x - shift.x) / (tanH * fill), Mathf.Abs(seen.y - shift.y) / (tanV * fill)) - seen.z;
                if (need > distance) distance = need;
            }
            float left = float.MaxValue, right = float.MinValue, low = float.MaxValue, high = float.MinValue;
            nearest = float.MaxValue;
            farthest = float.MinValue;
            foreach (var point in framed)
            {
                Vector3 seen = into * (point - middle);
                float depth = distance + seen.z;
                nearest = Mathf.Min(nearest, depth);
                farthest = Mathf.Max(farthest, depth);
                if (depth <= .001f) continue;
                float across = (seen.x - shift.x) / depth, up = (seen.y - shift.y) / depth;
                left = Mathf.Min(left, across);
                right = Mathf.Max(right, across);
                low = Mathf.Min(low, up);
                high = Mathf.Max(high, up);
            }
            if (left <= right) shift += new Vector2((left + right) * .5f * distance, (low + high) * .5f * distance);
        }
        // The zoom narrows the lens rather than moving in, so the camera never stands inside a skirt or a coat.
        float zoom = Mathf.Clamp(framing.zoom, .05f, 20f);
        float zoomedV = tanV / zoom, zoomedH = tanH / zoom;
        lens.fieldOfView = 2f * Mathf.Atan(zoomedV) * Mathf.Rad2Deg;
        lens.aspect = aspect;
        Vector3 aim = middle + view * new Vector3(shift.x - framing.x * distance * zoomedH, shift.y - framing.y * distance * zoomedV, 0f);
        lens.transform.SetPositionAndRotation(aim - view * Vector3.forward * distance, view);
        lens.nearClipPlane = Mathf.Max(.005f, nearest * .5f);
        lens.farClipPlane = Mathf.Max(lens.nearClipPlane + .1f, farthest * 1.5f + .5f);
        // The lights keep to the upright view: the lean turns the photograph, not the light on the piece.
        foreach (var (light, turn) in rig) light.transform.rotation = upright * Quaternion.Inverse(MenuView) * turn;
    }
}
