using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps the slot type fixed; long weapon names scroll right, hold, then restart at the left.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
public sealed class WeaponNameMarquee : MonoBehaviour
{
    [SerializeField, HideInInspector] private RectTransform viewport;
    [SerializeField, HideInInspector] private TMP_Text categoryLabel;
    [SerializeField, HideInInspector] private float fullWidth = -1f;
    private TMP_Text label;
    private string previousText;
    private float previousWidth = -1f, elapsed;
    private bool previousSelected;
    public float Overflow { get; private set; }
    public float ReadDuration { get; private set; }
    public TMP_Text CategoryLabel => categoryLabel;

    public void Refresh(string content, bool selected, float dt, float speed, float pause, bool restart, string category)
    {
        if (label == null) label = GetComponent<TMP_Text>();
        EnsureViewport();
        LayoutCategory(category, selected);
        float width = Mathf.Max(1f, viewport.rect.width);
        bool changed = content != previousText || selected != previousSelected || Mathf.Abs(width - previousWidth) > .1f;
        if (changed)
        {
            label.text = content;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            float preferred = Mathf.Ceil(label.GetPreferredValues(content, Mathf.Infinity, Mathf.Infinity).x) + 2f;
            Overflow = selected ? Mathf.Max(0f, preferred - width) : 0f;
            label.rectTransform.sizeDelta = new Vector2(width + Overflow, viewport.rect.height);
            label.alignment = TextAlignmentOptions.BottomLeft;
            previousText = content; previousWidth = width; previousSelected = selected;
        }
        elapsed = changed || restart ? 0f : elapsed + Mathf.Max(0f, dt);
        speed = Mathf.Max(1f, speed); pause = Mathf.Max(0f, pause);
        ReadDuration = Overflow > 0f ? Overflow / speed + pause : 0f;
        float offset = 0f;
        if (Overflow > 0f)
        {
            float travel = Overflow / speed;
            float t = Mathf.Repeat(elapsed, travel + pause);
            offset = t < travel ? -Overflow + t * speed : 0f;
        }
        label.rectTransform.anchoredPosition = new Vector2(offset, 0f);
    }

    private void LayoutCategory(string category, bool selected)
    {
        if (fullWidth < 0f) fullWidth = viewport.rect.width;
        if (categoryLabel == null)
        {
            categoryLabel = new GameObject("Slot type", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            categoryLabel.gameObject.layer = gameObject.layer;
            categoryLabel.transform.SetParent(viewport.parent, false);
            categoryLabel.raycastTarget = false;
        }
        categoryLabel.font = label.font; categoryLabel.fontSharedMaterial = label.fontSharedMaterial;
        categoryLabel.fontSize = label.fontSize; categoryLabel.fontStyle = label.fontStyle;
        categoryLabel.color = label.color;
        categoryLabel.enableAutoSizing = false;
        categoryLabel.textWrappingMode = TextWrappingModes.NoWrap;
        categoryLabel.alignment = TextAlignmentOptions.BottomLeft;
        categoryLabel.text = category + (selected ? " /" : "");
        // Reserve the same prefix width when deselected, so the type never shifts with selection.
        float prefixWidth = Mathf.Ceil(categoryLabel.GetPreferredValues(category + " /", Mathf.Infinity, Mathf.Infinity).x);
        const float gap = 6f;
        float nameWidth = Mathf.Max(1f, fullWidth - prefixWidth - gap);
        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, nameWidth);
        var prefix = categoryLabel.rectTransform;
        prefix.anchorMin = prefix.anchorMax = viewport.anchorMax;
        prefix.pivot = new Vector2(1f, viewport.pivot.y);
        prefix.sizeDelta = new Vector2(prefixWidth, viewport.rect.height);
        prefix.localRotation = viewport.localRotation; prefix.localScale = viewport.localScale;
        prefix.anchoredPosition = viewport.anchoredPosition - new Vector2(viewport.pivot.x * nameWidth + gap, 0f);
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
