using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Backlog 141: "[F] 사다리 내려가기" floating just above a ladder's top while the player stands where Interact would
/// take them down it (MovementFixtures.ClimbDownLadder). Drawn like the execution prompt (ExecutionHUD), but pinned to
/// the ladder in the world. Added to the local player by PlayerMovement.
/// </summary>
[DisallowMultipleComponent]
public sealed class LadderPromptHUD : MonoBehaviour
{
    // Above the top of the rails, a little out over the climbing side (m).
    private const float Above = .35f, Out = .1f;
    private PlayerMovement movement;
    private CanvasGroup group;
    private RectTransform root, prompt;
    private Ladder shown;
    private float appearance;

    private void OnEnable() => movement = GetComponent<PlayerMovement>();

    private void LateUpdate()
    {
        var ladder = movement != null && movement.Abilities != null ? movement.Abilities.Fixtures.ClimbDownLadder : null;
        var view = Camera.main;
        Vector3 screen = Vector3.zero;
        if (ladder != null && view != null)
        {
            Vector3 top = ladder.transform.position + ladder.transform.up * (ladder.Height + Above) + ladder.Outward * Out;
            screen = view.WorldToScreenPoint(top);
            // Behind the camera there is nowhere to draw it.
            if (screen.z <= 0f) ladder = null;
        }
        if (ladder == null && group == null) return;
        EnsurePanel();
        float dt = Time.unscaledDeltaTime;
        if (ladder != null && ladder != shown) appearance = 0f;
        shown = ladder;
        appearance += dt;
        group.alpha = Mathf.MoveTowards(group.alpha, ladder != null ? 1f : 0f, dt * 12f);
        if (ladder != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
            prompt.anchoredPosition = local;
        }
        float t = Mathf.Clamp01(appearance / .2f);
        prompt.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, 1f - Mathf.Pow(1f - t, 3f));
        group.gameObject.SetActive(group.alpha > 0f);
    }

    private static string KeyName()
    {
        // KeyBindings.Name: the first key the player set, not every binding joined by " | " (the empty second slot too).
        string key = KeyBindings.Name("Player/Interact");
        return string.IsNullOrEmpty(key) ? "F" : key;
    }

    private void EnsurePanel()
    {
        if (group != null) return;
        var canvasObject = new GameObject("Ladder prompt HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.hideFlags = HideFlags.DontSave; canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 79;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        group = canvasObject.GetComponent<CanvasGroup>(); group.alpha = 0f; group.interactable = group.blocksRaycasts = false;
        root = canvasObject.GetComponent<RectTransform>();
        prompt = new GameObject("Climb down prompt", typeof(RectTransform), typeof(ExecutionPromptBackdrop)).GetComponent<RectTransform>();
        prompt.SetParent(root, false); prompt.sizeDelta = new Vector2(330f, 64f);
        prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, .5f);
        prompt.GetComponent<ExecutionPromptBackdrop>().raycastTarget = false;
        var text = new GameObject("Climb down text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(prompt, false); text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) text.font = settings.promptFont;
        text.text = Texts.Format("hud.ladder.down", KeyName()); text.fontSize = 22f; text.alignment = TextAlignmentOptions.Center;
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
