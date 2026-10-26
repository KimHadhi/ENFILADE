using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>Uses the authored UltBar and arrow; never moves the icon or the HUD root.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class UltimateHUD : MonoBehaviour
{
    [SerializeField] private UltimateCharge source;
    [SerializeField] private Image bar;
    [FormerlySerializedAs("minimumMarker")]
    [SerializeField] private RectTransform readyThresholdMarker;
    [Header("Capacity and width")]
    [FormerlySerializedAs("growWithRequiredCharge")]
    [SerializeField] private bool growWithCapacity = true;
    [SerializeField, Min(1)] private float referenceCapacity = 120f;
    [Tooltip("Maximum bar width in Canvas units. Zero means no width limit.")]
    [SerializeField, Min(0)] private float maximumBarWidth = 480f;
    [Header("Presentation")]
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.2f);
    [SerializeField] private Color fillColor = Color.white;
    [SerializeField] private Color readyFillColor = new Color(1f, 0.88f, 0.9f, 1f);

    private Image fill;
    private Image initializedBar;
    private RectTransform initializedMarker;
    private bool initialized;
    private Vector2 originalSize, originalPosition;
    private Vector3 originalMarkerPosition;
    private Color originalBarColor;
    private float referenceWidth;
    private bool originalMarkerActive;

    private void Start() { if (Application.IsPlaying(gameObject)) RefreshNow(); }
    private void LateUpdate() { if (Application.IsPlaying(gameObject)) RefreshNow(); }

    public void RefreshNow()
    {
        if (!EnsureInitialized()) return;
        UltimateCharge charge = UltimateCharge.ResolveHUDSource(this, source);
        RectTransform rect = bar.rectTransform;
        float capacity = charge.MaximumCharge;
        float width = growWithCapacity ? referenceWidth * (capacity / Mathf.Max(1f, referenceCapacity)) : referenceWidth;
        if (maximumBarWidth > 0f) width = Mathf.Min(width, maximumBarWidth);
        if (float.IsNaN(width) || float.IsInfinity(width)) width = Mathf.Max(0f, maximumBarWidth);
        width = Mathf.Max(0f, width);
        // Offset by the pivot so the LEFT edge stays where the user placed it.
        float widthDelta = width - rect.rect.width;
        if (Mathf.Abs(widthDelta) > 0.0001f)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.anchoredPosition += new Vector2(widthDelta * rect.pivot.x, 0f);
        }
        bar.color = emptyColor;
        fill.color = charge.IsReady ? readyFillColor : fillColor;
        fill.rectTransform.anchorMax = new Vector2(charge.FillRatio, 1f);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

        // Existing marker Y, size, rotation and parent remain as authored.
        Vector3 point = rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, charge.ThresholdRatio), 0f, 0f));
        Vector3 local = readyThresholdMarker.parent != null ? readyThresholdMarker.parent.InverseTransformPoint(point) : point;
        Vector3 markerPosition = readyThresholdMarker.localPosition;
        markerPosition.x = local.x;
        readyThresholdMarker.localPosition = markerPosition;
        readyThresholdMarker.gameObject.SetActive(capacity > 0f && charge.RequiredCharge <= capacity);
    }

    private bool EnsureInitialized()
    {
        if (initialized && (bar != initializedBar || readyThresholdMarker != initializedMarker)) RestorePresentation();
        if (initialized) return source != null && bar != null && readyThresholdMarker != null && fill != null;
        if (source == null) source = GetComponent<UltimateCharge>();
        if (source == null || bar == null || readyThresholdMarker == null) return false;
        RectTransform rect = bar.rectTransform;
        initializedBar = bar;
        initializedMarker = readyThresholdMarker;
        originalSize = rect.sizeDelta;
        originalPosition = rect.anchoredPosition;
        referenceWidth = rect.rect.width;
        originalBarColor = bar.color;
        originalMarkerPosition = readyThresholdMarker.localPosition;
        originalMarkerActive = readyThresholdMarker.gameObject.activeSelf;
        var child = new GameObject("Ultimate Fill (Runtime)", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (!Application.isPlaying) child.hideFlags = HideFlags.HideAndDontSave;
        child.layer = bar.gameObject.layer;
        child.transform.SetParent(rect, false);
        fill = child.GetComponent<Image>();
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        initialized = true;
        return true;
    }

    private void OnDisable() { RestorePresentation(); }
    private void OnDestroy() { RestorePresentation(); }

    // The editor preview releases all temporary presentation before saving or entering Play.
    public void StopPreview() { RestorePresentation(); }

    private void RestorePresentation()
    {
        if (!initialized) return;
        if (initializedBar != null)
        {
            initializedBar.rectTransform.sizeDelta = originalSize;
            initializedBar.rectTransform.anchoredPosition = originalPosition;
            initializedBar.color = originalBarColor;
        }
        if (initializedMarker != null)
        {
            initializedMarker.localPosition = originalMarkerPosition;
            initializedMarker.gameObject.SetActive(originalMarkerActive);
        }
        if (fill != null)
        {
            fill.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(fill.gameObject); else DestroyImmediate(fill.gameObject);
        }
        fill = null;
        initializedBar = null;
        initializedMarker = null;
        initialized = false;
    }
}
