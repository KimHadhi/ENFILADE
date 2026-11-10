using UnityEngine;

/// <summary>
/// Stacks plates that each lean their own way (a z tilt about their top-left corner, as KitStyle.TopLeft hangs them)
/// down a column, so that wherever two lie over the same stretch they keep a gap: one turning up under one turning down
/// meets it at the right end, and a plain step down left them lying over each other (the title screen's 방 만들기 over
/// 서버 찾기, the records' chapters; user, 2026-11-10 and 11-11). Every plate keeps its own tilt, indent and width.
/// Positions are px right and down from the column's top left.
/// </summary>
public static class KitStack
{
    /// <summary>
    /// Where (px down) the next plate's top-left corner goes so that, across the stretch the two share, it stays at least
    /// <paramref name="gap"/> below this one, measured straight down. Tilts in degrees, positive turning the right end up.
    /// </summary>
    public static float Below(float x, float y, float width, float height, float tilt, float nextX, float nextWidth, float nextTilt, float gap)
    {
        float turn = tilt * Mathf.Deg2Rad, next = nextTilt * Mathf.Deg2Rad;
        float sin = Mathf.Sin(turn), cos = Mathf.Cos(turn), tan = Mathf.Tan(turn), nextTan = Mathf.Tan(next);
        // This plate's bottom edge runs from its bottom-left corner to its bottom-right one, falling by tan a px across.
        float bottomLeft = x + height * sin, bottomRight = bottomLeft + width * cos;
        float from = Mathf.Max(bottomLeft, nextX), to = Mathf.Min(bottomRight, nextX + nextWidth * Mathf.Cos(next));
        if (to < from) from = to = Mathf.Clamp(nextX, bottomLeft, bottomRight);
        // Both edges are straight, so the nearest they come is at one end of the shared stretch.
        return Mathf.Max(Clear(from), Clear(to));

        float Clear(float at) => y + height * cos - (at - bottomLeft) * tan + gap + (at - nextX) * nextTan;
    }

    /// <summary>How far down (px) a plate's lowest corner reaches.</summary>
    public static float Lowest(float y, float width, float height, float tilt)
    {
        float turn = tilt * Mathf.Deg2Rad;
        return y + height * Mathf.Cos(turn) + Mathf.Max(0f, -width * Mathf.Sin(turn));
    }
}
