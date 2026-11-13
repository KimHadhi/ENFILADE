using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The room HUD pieces take along the screen's right edge right now, so a piece that comes and goes stands clear of
/// whatever is showing beside it instead of over it. Each piece placed by its own code knew nothing of the others: in
/// play (2026-11-13) the HUD overlap check measured a callout window 107.5 px over the weapon slots, which both sat on
/// the right edge, and later 12.4 px into the hub's card above it. A piece that takes room says so every frame it shows
/// (Take, from its drawn rects: Measure), and how a passing piece gives way to it (Way): room that comes and goes beside
/// the passing piece is stepped past, in from the edge (Reach); room that stays over it is stood under (Ceiling); room
/// that stays under it is kept above (Floor); each by Hud.csv Room.gap, the passing piece easing back once the room is
/// free. Measured flat, in canvas px at 1080p from the screen's right and top edges, in the frame of the HUD plane the
/// rects stand on before it turns (HudPlane groups are centred on their canvas), so pieces on different canvases of the
/// same side compare. Room not taken again since last frame is forgotten.
/// </summary>
public static class HudRoom
{
    /// <summary>How a passing piece gives way to the room a piece takes.</summary>
    public enum Way
    {
        /// <summary>It comes and goes beside the passing piece (weapon slots, kill feed): step in from the edge past it.</summary>
        In,
        /// <summary>It stays over the passing piece (the hub's card): stand under it.</summary>
        Under,
        /// <summary>It stays under the passing piece (the ammo display): keep above it.</summary>
        Over,
    }

    private struct Claim
    {
        public Object Owner;
        public Way Way;
        public float Near, Far, Top, Bottom;
        public int Frame;
    }

    private static readonly List<Claim> claims = new List<Claim>();
    private static readonly Vector3[] corners = new Vector3[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => claims.Clear();

    /// <summary>Room kept between a passing piece and the room it gives way to (px, Hud.csv Room.gap).</summary>
    public static float Gap => Number("gap", 18f);

    /// <summary>How quickly a passing piece steps aside and back (1/s, Hud.csv Room.follow).</summary>
    public static float Follow => Number("follow", 12f);

    /// <summary>
    /// <paramref name="owner"/> shows from <paramref name="near"/> to <paramref name="far"/> px in from the right edge and
    /// from <paramref name="top"/> to <paramref name="bottom"/> px down from the top this frame; passing pieces give way
    /// to it as <paramref name="way"/> says.
    /// </summary>
    public static void Take(Object owner, float near, float far, float top, float bottom, Way way = Way.In)
    {
        var claim = new Claim { Owner = owner, Way = way, Near = near, Far = far, Top = top, Bottom = bottom, Frame = Time.frameCount };
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
    /// How far in from the right edge the room taken beside (Way.In) by others in the band from <paramref name="top"/> to
    /// <paramref name="bottom"/> px down from the top reaches now (px; 0 when nothing shows there).
    /// </summary>
    public static float Reach(float top, float bottom, Object except = null)
    {
        Forget();
        float reach = 0f;
        foreach (var claim in claims)
        {
            if (claim.Way != Way.In || claim.Owner == except || claim.Bottom <= top || claim.Top >= bottom) continue;
            reach = Mathf.Max(reach, claim.Far);
        }
        return reach;
    }

    /// <summary>
    /// How far down from the top the room that stays over (Way.Under) the column from <paramref name="near"/> to
    /// <paramref name="far"/> px in from the right edge reaches now (px; 0 when nothing stands over it).
    /// </summary>
    public static float Ceiling(float near, float far, Object except = null)
    {
        Forget();
        float ceiling = 0f;
        foreach (var claim in claims)
        {
            if (claim.Way != Way.Under || claim.Owner == except || claim.Far <= near || claim.Near >= far) continue;
            ceiling = Mathf.Max(ceiling, claim.Bottom);
        }
        return ceiling;
    }

    /// <summary>
    /// How far down from the top the room that stays under (Way.Over) the column from <paramref name="near"/> to
    /// <paramref name="far"/> px in from the right edge begins now (px; infinity when nothing stands under it).
    /// </summary>
    public static float Floor(float near, float far, Object except = null)
    {
        Forget();
        float floor = float.PositiveInfinity;
        foreach (var claim in claims)
        {
            if (claim.Way != Way.Over || claim.Owner == except || claim.Far <= near || claim.Near >= far) continue;
            floor = Mathf.Min(floor, claim.Top);
        }
        return floor;
    }

    /// <summary>
    /// The flat box <paramref name="rects"/> take together, in px from their canvas's right and top edges: their corners
    /// in the frame of the HUD plane they stand on (their root canvas's where flat). False when there is none.
    /// </summary>
    public static bool Measure(List<RectTransform> rects, out float near, out float far, out float top, out float bottom)
    {
        near = top = float.PositiveInfinity;
        far = bottom = float.NegativeInfinity;
        foreach (var rect in rects) Add(rect, ref near, ref far, ref top, ref bottom);
        return far > near && bottom > top;
    }

    /// <summary>The flat box one <paramref name="rect"/> takes (see the list's Measure).</summary>
    public static bool Measure(RectTransform rect, out float near, out float far, out float top, out float bottom)
    {
        near = top = float.PositiveInfinity;
        far = bottom = float.NegativeInfinity;
        Add(rect, ref near, ref far, ref top, ref bottom);
        return far > near && bottom > top;
    }

    private static void Add(RectTransform rect, ref float near, ref float far, ref float top, ref float bottom)
    {
        if (rect == null) return;
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return;
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

    // Its owner gone, or it has not said so since last frame: the room is free.
    private static void Forget()
    {
        int now = Time.frameCount;
        for (int i = claims.Count - 1; i >= 0; i--)
            if (claims[i].Owner == null || now - claims[i].Frame > 1) claims.RemoveAt(i);
    }

    private static float Number(string field, float fallback) => GameTables.Number(HudCamera.TableName, "Room", field, fallback);
}
