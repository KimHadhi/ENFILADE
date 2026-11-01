using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What the screen says about staying alive: the wipe, being down, waiting to come back, and getting someone up.</summary>
[DisallowMultipleComponent]
public sealed class PlayerLifeHUD : MonoBehaviour
{
    private const float PromptSize = 27f, WipeSize = 96f, BarWidth = 320f, BarHeight = 6f;
    private static readonly Color Pale = new Color(1f, .94f, .9f), WipeRed = new Color(.86f, .16f, .12f);
    private PlayerLife life;
    private PlayerRevive revive;
    private PlayerDamageReceiver receiver;
    private TextMeshProUGUI label;
    private RectTransform bar;
    private GameObject canvas;

    private void Awake()
    {
        life = GetComponent<PlayerLife>();
        revive = GetComponent<PlayerRevive>();
        receiver = GetComponent<PlayerDamageReceiver>();
    }

    private void LateUpdate()
    {
        string text = Message(out float size, out Color color);
        if (text == null && canvas == null) return;
        if (canvas == null) Build();
        canvas.SetActive(text != null);
        if (text == null) return;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        float progress = revive != null && !life.IsWiped ? revive.Progress : 0f;
        bar.gameObject.SetActive(progress > 0f);
        bar.sizeDelta = new Vector2(BarWidth * progress, BarHeight);
    }

    private string Message(out float size, out Color color)
    {
        size = PromptSize;
        color = Pale;
        if (life.IsWiped) { size = WipeSize; color = WipeRed; return "전멸"; }
        if (receiver.IsDowned) return $"쓰러졌습니다. 동료를 기다리세요 ({Mathf.CeilToInt(life.BleedLeft)}초)";
        if (!receiver.IsAlive)
            return life.WaitsForWave ? "다음 웨이브에 돌아옵니다" : $"{Mathf.Max(1, Mathf.CeilToInt(life.RespawnLeft))}초 뒤 돌아옵니다";
        return revive != null && revive.Candidate != null ? "[F] 일으키기" : null;
    }

    private void Build()
    {
        canvas = new GameObject("Life HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.hideFlags = HideFlags.DontSave;
        canvas.transform.SetParent(transform, false);
        var layer = canvas.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = 80;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        label = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(canvas.transform, false);
        label.rectTransform.sizeDelta = new Vector2(1200f, 160f);
        label.rectTransform.anchoredPosition = new Vector2(0f, -120f);
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) label.font = settings.promptFont;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;

        bar = new GameObject("Progress", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(canvas.transform, false);
        bar.anchoredPosition = new Vector2(0f, -170f);
        var fill = bar.GetComponent<Image>();
        fill.color = Pale;
        fill.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas);
    }
}
