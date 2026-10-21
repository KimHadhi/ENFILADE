using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ExecutionHUD : MonoBehaviour
{
    private PlayerExecution execution;
    private CanvasGroup group;
    private RectTransform prompt;
    private MeleeCombatTarget shown;
    private float appearance;
    public float Opacity => group != null ? group.alpha : 0f;
    public float PromptScale => prompt != null ? prompt.localScale.x : 1f;
    private void OnEnable() => execution = GetComponent<PlayerExecution>();
    private void LateUpdate() => Refresh(execution != null ? execution.Candidate : null, Time.unscaledDeltaTime);
    public void Refresh(MeleeCombatTarget target, float dt)
    {
        if (target == null && group == null) return;
        EnsurePanel();
        if (target != null && target != shown) appearance = 0f;
        shown = target;
        appearance += Mathf.Max(0f, dt);
        group.alpha = Mathf.MoveTowards(group.alpha, target != null ? 1f : 0f, Mathf.Max(0f, dt) * 12f);
        float t = Mathf.Clamp01(appearance / .2f);
        prompt.localScale = Vector3.one * Mathf.Lerp(1.32f, 1f, 1f - Mathf.Pow(1f - t, 3f));
        group.gameObject.SetActive(group.alpha > 0f);
    }
    private void EnsurePanel()
    {
        if (group != null) return;
        var root = new GameObject("Deathblow HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.hideFlags = HideFlags.DontSave; root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.interactable = group.blocksRaycasts = false;
        prompt = new GameObject("Execution prompt", typeof(RectTransform), typeof(ExecutionPromptBackdrop)).GetComponent<RectTransform>();
        prompt.SetParent(root.transform, false); prompt.sizeDelta = new Vector2(440f, 90f); prompt.anchoredPosition = new Vector2(0f, -88f);
        prompt.GetComponent<ExecutionPromptBackdrop>().raycastTarget = false;
        var text = new GameObject("[F] 처형하기", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(prompt, false); text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) text.font = settings.promptFont;
        text.text = "[F] 처형하기"; text.fontSize = 27f; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, .94f, .9f); text.raycastTarget = false;
        text.fontStyle = FontStyles.Bold;
    }
    private void OnDisable() { shown = null; if (group != null) group.alpha = 0f; }
    private void OnDestroy()
    {
        if (group == null) return;
        if (Application.isPlaying) Destroy(group.gameObject); else DestroyImmediate(group.gameObject);
    }
}
