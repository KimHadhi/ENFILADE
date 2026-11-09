using UnityEngine;
using UnityEngine.UI;

/// <summary>Three ▶ marching to the right, brightest moving along, as on the user's menu photo's main button.</summary>
[DisallowMultipleComponent]
public sealed class KitChevrons : MonoBehaviour
{
    private readonly RawImage[] arrows = new RawImage[3];

    public void Build(Color ink, float size)
    {
        var rect = (RectTransform)transform;
        for (int i = 0; i < arrows.Length; i++)
        {
            arrows[i] = KitButtons.IconImage(rect, "Play", size, ink);
            arrows[i].rectTransform.anchorMin = arrows[i].rectTransform.anchorMax = new Vector2(0f, .5f);
            arrows[i].rectTransform.anchoredPosition = new Vector2(size * (.6f + i * .85f), 0f);
        }
    }

    private void Update()
    {
        float t = Time.unscaledTime * 3.2f;
        for (int i = 0; i < arrows.Length; i++)
        {
            if (arrows[i] == null) continue;
            var color = arrows[i].color;
            color.a = .3f + .7f * Mathf.Max(0f, Mathf.Sin(t - i * .9f));
            arrows[i].color = color;
        }
    }
}
