using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health as ten cells, each a tenth of the maximum, filled from the left: green, yellow under 40%, red under 25%, as in
/// Left 4 Dead. The cell being used up shows how much of it is left. Teammates' marks and the team panel share it.
/// </summary>
public sealed class HealthSegments
{
    public const int Count = 10;
    private const float Gap = 2f;
    private static readonly Color Green = new Color(.35f, .88f, .4f), Amber = new Color(1f, .82f, .25f), Blood = new Color(1f, .27f, .22f),
        Empty = new Color(0f, 0f, 0f, .55f);
    private readonly RectTransform root;
    private readonly Image[] cells = new Image[Count];

    public RectTransform Rect => root;
    public float Width { get; }

    public HealthSegments(Transform parent, Vector2 cell)
    {
        root = FeelUI.Rect("Health Cells", parent);
        Width = Count * cell.x + (Count - 1) * Gap;
        root.sizeDelta = new Vector2(Width, cell.y);
        for (int i = 0; i < Count; i++)
        {
            var rect = FeelUI.Rect("Cell", root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(i * (cell.x + Gap), 0f);
            rect.sizeDelta = cell;
            cells[i] = rect.gameObject.AddComponent<Image>();
            cells[i].raycastTarget = false;
        }
    }

    public static Color ColorFor(float share) => share < .25f ? Blood : share < .4f ? Amber : Green;
    public static Color Downed => Blood;

    public void Show(float share, Color color)
    {
        share = Mathf.Clamp01(share);
        for (int i = 0; i < Count; i++)
        {
            float left = Mathf.Clamp01(share * Count - i);
            cells[i].color = left <= 0f ? Empty : left >= 1f ? color : new Color(color.r, color.g, color.b, Mathf.Lerp(.3f, .85f, left));
        }
    }

    public void SetActive(bool on)
    {
        if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }
}
