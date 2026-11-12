using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The room HUD pieces take along the screen's right edge right now, so a piece that comes and goes stands clear of
/// whatever is showing beside it instead of over it. Each piece placed by its own code knew nothing of the others: in
/// play (2026-11-13) the HUD overlap check measured a callout window 107.5 px over the weapon slots, which both sat on
/// the right edge. A piece that takes room says so every frame it shows (Take, from its drawn rects: Measure); a passing
/// piece asks how far in from the edge the room in its band is taken (Reach) and steps in past it by Hud.csv Room.gap,
/// stepping back out once the room is free. Measured flat, in canvas px at 1080p from the screen's right and top edges,
/// in the frame of the HUD plane the rects stand on before it turns (HudPlane groups are centred on their canvas), so
/// pieces on different canvases of the same side compare. Room not taken again since last frame is forgotten.
/// </summary>
public static class HudRoom
{
    private struct Claim
    {
        public Object Owner;
        public float Near, Far, Top, Bottom;
        public int Frame;
    }

    private static readonly List<Claim> claims = new List<Claim>();
    private static readonly Vector3[] corners = new Vector3[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => claims.Clear();

    /// <summary>Room kept between a passing piece and the room it steps past (px, Hud.csv Room.gap).</summary>
    public static float Gap => Number("gap", 18f);

    /// <summary>How quickly a passing piece steps aside and back (1/s, Hud.csv Room.follow).</summary>
    public static float Follow => Number("follow", 12f);

    /// <summary>
    /// <paramref name="owner"/> shows from <paramref name="near"/> to <paramref name="far"/> px in from the right edge and
    /// from <paramref name="top"/> to <paramref name="bottom"/> px down from the top this frame.
    /// </summary>
    public static void Take(Object owner, float near, float far, float top, float bottom)
    {
        var claim = new Claim { Owner = owner, Near = near, Far = far, Top = top, Bottom = bottom, Frame = Time.frameCount };
        for (int i = 0; i < claims.Count; i++)
            if (claims[i].Owner == owner) { claims[i] = claim; return; }
        claims.Add(claim);
    }

    /// <summary><paramref name="owner"/> shows nothing now.</summary>
    public static void Free(Object owner)
    {
        for (int i = claims.Count - 1; i >= 0; i--)
            if (claims[i].Owner == owner || claims[i].Owner == null) claims.RemoveAt(i);
    }

    /// <summary>
    /// How far in from the right edge the room taken by others in the band from <paramref name="top"/> to
    /// <paramref name="bottom"/> px down from the top reaches now (px; 0 when nothing shows there).
    /// </summary>
    public static float Reach(float top, float bottom, Object except = null)
    {
        float reach = 0f;
        int now = Time.frameCount;
        for (int i = claims.Count - 1; i >= 0; i--)
        {
            var claim = claims[i];
            // Its owner gone, or it has not said so since last frame: the room is free.
            if (claim.Owner == null || now - claim.Frame > 1) { claims.RemoveAt(i); continue; }
            if (claim.Owner == except || claim.Bottom <= top || claim.Top >= bottom) continue;
            reach = Mathf.Max(reach, claim.Far);
        }
        return reach;
    }

    /// <summary>
    /// The flat box <paramref name="rects"/> take together, in px from their canvas's right and top edges: their corners
    /// in the frame of the HUD plane they stand on (their root canvas's where flat). False when there is none.
    /// </summary>
    public static bool Measure(List<RectTransform> rects, out float near, out float far, out float top, out float bottom)
    {
        near = top = float.PositiveInfinity;
        far = bottom = float.NegativeInfinity;
        foreach (var rect in rects)
        {
            if (rect == null) continue;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) continue;
            var root = (RectTransform)canvas.rootCanvas.transform;
            var plane = rect.GetComponentInParent<HudPlane>();
            var frame = plane != null ? (Transform)plane.transform : root;
            Vector2 half = root.rect.size * .5f;
            rect.GetLocalCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = frame.InverseTransformPoint(rect.TransformPoint(corners[i]));
                float fromRight = half.x - p.x, fromTop = half.y - p.y;
                near = Mathf.Min(near, fromRight);
                far = Mathf.Max(far, fromRight);
                top = Mathf.Min(top, fromTop);
                bottom = Mathf.Max(bottom, fromTop);
            }
        }
        return far > near && bottom > top;
    }

    private static float Number(string field, float fallback)
    {
        if (GameTables.TryGetRow(HudCamera.TableName, "Room", out var row) && row.TryGetValue(field, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return value;
        return fallback;
    }
}
