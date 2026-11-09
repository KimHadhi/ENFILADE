using UnityEngine;

/// <summary>A row of stripes sliding sideways without end (one stripe's spacing, then back), like a loading belt.</summary>
[DisallowMultipleComponent]
public sealed class KitStripes : MonoBehaviour
{
    /// <summary>The gap from one stripe to the next (px); the row loops after moving this far.</summary>
    public const float Spacing = 38f;
    private const float Speed = 26f;

    private void Update()
    {
        var rect = (RectTransform)transform;
        rect.anchoredPosition = new Vector2(Mathf.Repeat(Time.unscaledTime * Speed, Spacing), 0f);
    }
}
