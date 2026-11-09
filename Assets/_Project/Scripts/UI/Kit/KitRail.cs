using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The rail down the side of a menu column: a faint line with a bright pink marker that glides to whichever of the
/// column's buttons the pointer is on, stretching to its height (a spring, so it overshoots a little and settles), and
/// stays on the last one. The menus' "line of fire".
/// </summary>
[DisallowMultipleComponent]
public sealed class KitRail : MonoBehaviour
{
    private const float Stiffness = 340f, Damping = 26f, MarkerWidth = 4f;

    private RectTransform column, marker;
    private float y, height, ySpeed, heightSpeed, goalY, goalHeight;
    private bool placed;

    /// <summary>
    /// A rail on <paramref name="parent"/> at <paramref name="x"/> (px from its left), running from
    /// <paramref name="top"/> to <paramref name="bottom"/> (px down), following the hovered direct children of <paramref name="followed"/>.
    /// </summary>
    public static KitRail Make(RectTransform parent, RectTransform followed, float x, float top, float bottom)
    {
        var line = KitStyle.Bar("Rail", parent, new Vector2(x, top), new Vector2(1f, bottom - top), KitStyle.Faint).rectTransform;
        var rail = line.gameObject.AddComponent<KitRail>();
        rail.column = followed;
        rail.marker = KitStyle.TopLeft("Marker", line, new Vector2(-(MarkerWidth - 1f) * .5f, 0f), new Vector2(MarkerWidth, 0f));
        KitStyle.Fill(rail.marker, KitStyle.Accent);
        var glow = KitStyle.Stretch("Glow", rail.marker);
        glow.sizeDelta = new Vector2(22f, 22f);
        var soft = glow.gameObject.AddComponent<RawImage>();
        soft.texture = KitShapes.Glow;
        soft.color = KitStyle.With(KitStyle.Accent, .35f);
        soft.raycastTarget = false;
        KitHover.Entered += rail.Follow;
        return rail;
    }

    /// <summary>Puts the marker on <paramref name="target"/> (one of the column's buttons) at once, or glides there.</summary>
    public void Point(RectTransform target, bool now)
    {
        if (target == null) return;
        var rail = (RectTransform)transform;
        // Both hang from their parents' top left; the rail's own top is where it starts.
        goalY = -target.anchoredPosition.y + rail.anchoredPosition.y;
        goalHeight = target.sizeDelta.y;
        if (!now && placed) return;
        y = goalY;
        height = goalHeight;
        ySpeed = heightSpeed = 0f;
        placed = true;
        Apply();
    }

    private void Follow(RectTransform hovered)
    {
        if (hovered == null || column == null || hovered.parent != column) return;
        // Only what lies along the rail (not, say, a card further down the column).
        var rail = (RectTransform)transform;
        float top = -hovered.anchoredPosition.y + rail.anchoredPosition.y;
        if (top < -1f || top + hovered.sizeDelta.y > rail.sizeDelta.y + 1f) return;
        Point(hovered, false);
    }

    private void OnDestroy() => KitHover.Entered -= Follow;

    private void Update()
    {
        if (!placed) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        ySpeed += ((goalY - y) * Stiffness - ySpeed * Damping) * dt;
        heightSpeed += ((goalHeight - height) * Stiffness - heightSpeed * Damping) * dt;
        y += ySpeed * dt;
        height += heightSpeed * dt;
        Apply();
    }

    private void Apply()
    {
        if (marker == null) return;
        marker.anchoredPosition = new Vector2(marker.anchoredPosition.x, -y);
        marker.sizeDelta = new Vector2(MarkerWidth, Mathf.Max(0f, height));
    }
}
