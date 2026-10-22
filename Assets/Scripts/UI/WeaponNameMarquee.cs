using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Clips long slot labels and scrolls right first, pausing at each end for reading.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
public sealed class WeaponNameMarquee : MonoBehaviour
{
    [SerializeField, HideInInspector] private RectTransform viewport;
    private TMP_Text label;
    private string previousText;
    private float previousWidth = -1f, elapsed;
    public float Overflow { get; private set; }
    public float ReadDuration { get; private set; }

    public void Refresh(string content, bool selected, float dt, float speed, float pause, bool restart)
    {
        if (label == null) label = GetComponent<TMP_Text>();
        EnsureViewport();
        float width = Mathf.Max(1f, viewport.rect.width);
        bool changed = content != previousText || Mathf.Abs(width - previousWidth) > .1f;
        if (changed)
        {
            label.text = content;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            float preferred = Mathf.Ceil(label.GetPreferredValues(content, Mathf.Infinity, Mathf.Infinity).x) + 2f;
            Overflow = selected ? Mathf.Max(0f, preferred - width) : 0f;
            label.rectTransform.sizeDelta = new Vector2(width + Overflow, viewport.rect.height);
            label.alignment = Overflow > 0f ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.BottomRight;
            previousText = content; previousWidth = width;
        }
        elapsed = changed || restart ? 0f : elapsed + Mathf.Max(0f, dt);
        speed = Mathf.Max(1f, speed); pause = Mathf.Max(0f, pause);
        ReadDuration = Overflow > 0f ? Overflow / speed + 2f * pause : 0f;
        float offset = 0f;
        if (Overflow > 0f)
        {
            float travel = Overflow / speed;
            float t = Mathf.Repeat(elapsed, 2f * (travel + pause));
            if (t < pause) offset = -Overflow;
            else if (t < pause + travel) offset = -Overflow + (t - pause) * speed;
            else if (t < 2f * pause + travel) offset = 0f;
            else offset = -(t - 2f * pause - travel) * speed;
        }
        label.rectTransform.anchoredPosition = new Vector2(offset, 0f);
    }

    private void EnsureViewport()
    {
        if (viewport != null) return;
        var rect = label.rectTransform;
        viewport = new GameObject("Weapon name viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        viewport.gameObject.layer = gameObject.layer;
        viewport.SetParent(rect.parent, false); viewport.SetSiblingIndex(rect.GetSiblingIndex());
        viewport.anchorMin = rect.anchorMin; viewport.anchorMax = rect.anchorMax; viewport.pivot = rect.pivot;
        viewport.anchoredPosition = rect.anchoredPosition; viewport.sizeDelta = rect.sizeDelta;
        viewport.localRotation = rect.localRotation; viewport.localScale = rect.localScale;
        rect.SetParent(viewport, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
        rect.anchoredPosition = Vector2.zero;
    }
}
