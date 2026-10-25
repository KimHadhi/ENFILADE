using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PerfectDefenseHUD : MonoBehaviour
{
    private PlayerPerfectDefense source;
    private CanvasGroup group;
    private GameObject root;
    private void Awake()
    {
        source = GetComponent<PlayerPerfectDefense>();
        root = new GameObject("Perfect defense UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
        group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = group.interactable = false;
        var shade = new GameObject("Vignette", typeof(RectTransform), typeof(PerfectDefenseVignette)).GetComponent<PerfectDefenseVignette>();
        shade.transform.SetParent(root.transform, false); shade.raycastTarget = false;
        shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
        shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;
        var label = new GameObject("Counter prompt", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(root.transform, false); label.raycastTarget = false;
        label.font = CombatFeedbackSettings.Instance?.promptFont;
        label.text = "[LMB] 반격"; label.fontSize = 24; label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.8f, .92f, 1f);
        label.rectTransform.sizeDelta = new Vector2(250, 40); label.rectTransform.anchoredPosition = new Vector2(0, -85);
    }
    private void LateUpdate()
    {
        if (group == null) return;
        float desired = source != null && source.CounterAvailable ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, desired, Time.unscaledDeltaTime * (desired > 0f ? 14f : 7f));
    }
    private void OnDisable() { if (group != null) group.alpha = 0f; }
    private void OnDestroy() { if (root != null) Destroy(root); }
}
