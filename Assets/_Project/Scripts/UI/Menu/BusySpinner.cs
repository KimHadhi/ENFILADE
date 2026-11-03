using UnityEngine;
using UnityEngine.UI;

/// <summary>A ring of dots with a bright one running round it, for "still looking". Plain images, so it needs no sprite.</summary>
public sealed class BusySpinner
{
    private const int Dots = 8;
    private const float Lap = .9f; // seconds
    private readonly RectTransform root;
    private readonly Image[] dots = new Image[Dots];
    private readonly Color tint;

    public RectTransform Rect => root;

    public BusySpinner(Transform parent, float size, Color tint)
    {
        this.tint = tint;
        root = NewRect("Spinner", parent);
        root.sizeDelta = new Vector2(size, size);
        float dot = size * .22f, radius = (size - dot) * .5f;
        for (int i = 0; i < Dots; i++)
        {
            float angle = i * Mathf.PI * 2f / Dots;
            var rect = NewRect("Dot", root);
            rect.sizeDelta = new Vector2(dot, dot);
            rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
            dots[i] = rect.gameObject.AddComponent<Image>();
            dots[i].raycastTarget = false;
        }
    }

    public void Show(bool on)
    {
        if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }

    public void Tick(float time)
    {
        if (!root.gameObject.activeSelf) return;
        float head = time / Lap * Dots;
        for (int i = 0; i < Dots; i++)
        {
            Color color = tint;
            color.a *= Mathf.Lerp(1f, .15f, Mathf.Repeat(head - i, Dots) / Dots);
            dots[i].color = color;
        }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        return rect;
    }
}
