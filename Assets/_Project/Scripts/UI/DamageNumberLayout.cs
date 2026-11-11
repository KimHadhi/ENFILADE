using System;
using UnityEngine;

/// <summary>
/// Where damage numbers stand, from Hud.csv's Damage row: pinned to the body part the newest hit landed on, a little
/// off it on the side away from the crosshair, never inside the small box around the crosshair; a running total grows
/// as it climbs. Colours, font and motion stay in Resources/Combat/Feedback (Damage popups).
/// </summary>
[Serializable]
public sealed class DamageNumberLayout
{
    [Tooltip("Half the width and height of the box around the crosshair that numbers stay out of (px at 1080p). " +
        "A number reaching into it moves out sideways on its own side. 0 turns it off.")]
    public Vector2 clear = new Vector2(46f, 30f);
    [Tooltip("How far the number stands off the spot it was hit (px at full size): sideways away from the crosshair; up.")]
    public Vector2 offset = new Vector2(22f, 10f);
    [Tooltip("Seconds the number takes to glide over to where the newest hit landed on the same body.")]
    [Min(0f)] public float glide = .1f;
    [Tooltip("Where on the body's height a hit with no place (a fall) stands: 0 the feet, 1 the top of the head.")]
    [Range(0f, 1f)] public float height = .72f;
    [Tooltip("How much larger a running total grows at its fullest (0.3 = 30%).")]
    [Min(0f)] public float grow = .3f;
    [Tooltip("The running total at which it has grown fully.")]
    [Min(1f)] public float growDamage = 400f;
    [Tooltip("How far a new number slides out from the body as it appears (px).")]
    [Min(0f)] public float slide = 16f;
    [Tooltip("Seconds a running total takes to count up to its new value.")]
    [Min(0f)] public float roll = .12f;
    [Tooltip("Hits closer together than this make one number even when each hit shows its own (a shotgun's pellets).")]
    [Min(0f)] public float sameShot = .05f;
    [Tooltip("Seconds a critical or counter keeps its colour and mark on a running total before it turns back.")]
    [Min(0f)] public float markHold = .4f;
    [Tooltip("Width of the bar on the side facing the target (px). 0 hides it.")]
    [Min(0f)] public float bar = 3f;
    [Tooltip("The most numbers one target shows at once when each hit shows its own; older ones leave early.")]
    [Min(1)] public int column = 4;

    // The push eases in over this many px above and below the box, so a number never snaps sideways.
    private const float Band = 14f;

    /// <summary>
    /// Moves a number (its near edge at x, vertical centre at y, from the crosshair) sideways on its own side until it
    /// clears the box around the crosshair. Only sideways and only outward: there is no ring for numbers to collect on.
    /// </summary>
    public Vector2 ClearOfCrosshair(Vector2 at, int side, float halfHeight)
    {
        if (clear.x <= 0f || clear.y <= 0f) return at;
        float over = Mathf.Abs(at.y) - (clear.y + halfHeight);
        if (over >= Band) return at;
        float outward = side * at.x;
        if (outward >= clear.x) return at;
        float push = over <= 0f ? 1f : 1f - over / Band;
        at.x = side * Mathf.Lerp(outward, clear.x, push);
        return at;
    }
}
