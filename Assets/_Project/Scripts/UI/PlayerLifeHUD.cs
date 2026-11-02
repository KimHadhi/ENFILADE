using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the screen says about staying alive: the wipe, being down, waiting to come back, and getting someone up. While
/// someone is being got up, both of them see a panel with who it is, the seconds left and a filling bar.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLifeHUD : MonoBehaviour
{
    private const float PromptSize = 27f, WipeSize = 96f, PanelWidth = 440f, PanelHeight = 62f, BarHeight = 5f, Inset = 24f, Shear = 9f;
    private static readonly Color Pale = new Color(1f, .94f, .9f), WipeRed = new Color(.86f, .16f, .12f);
    private static readonly Color Plate = new Color(0f, 0f, 0f, .6f), Track = new Color(.34f, .91f, .72f, .18f);
    private PlayerLife life;
    private PlayerRevive revive;
    private PlayerDamageReceiver receiver;
    private TextMeshProUGUI label, who, secondsLeft;
    private RectTransform panel, fill;
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
        ShowRescue();
    }

    private string Message(out float size, out Color color)
    {
        size = PromptSize;
        color = Pale;
        if (life.IsWiped) { size = WipeSize; color = WipeRed; return "전멸"; }
        if (receiver.IsDowned)
            return life.Rescuer != null ? "쓰러졌습니다. 출혈이 멈췄습니다" : $"쓰러졌습니다. 동료를 기다리세요 ({Mathf.CeilToInt(life.BleedLeft)}초)";
        if (!receiver.IsAlive)
            return life.WaitsForWave ? "다음 웨이브에 돌아옵니다" : $"{Mathf.Max(1, Mathf.CeilToInt(life.RespawnLeft))}초 뒤 돌아옵니다";
        return revive != null && revive.Candidate != null ? "[F] 일으키기" : null;
    }

    private void ShowRescue()
    {
        bool beingHelped = !life.IsWiped && life.Rescuer != null;
        bool helping = !life.IsWiped && !beingHelped && revive != null && revive.Candidate != null && revive.Progress > 0f;
        panel.gameObject.SetActive(beingHelped || helping);
        if (!beingHelped && !helping) return;
        float progress = beingHelped ? life.RescueProgress : revive.Progress;
        who.text = beingHelped ? NameOf(life.Rescuer) + " 님이 살리는 중" : NameOf(revive.Candidate) + " 살리는 중";
        secondsLeft.text = ((1f - progress) * revive.Seconds).ToString("0.0") + "초";
        fill.sizeDelta = new Vector2((PanelWidth - Inset * 2f) * progress, BarHeight);
    }

    private static string NameOf(PlayerMovement player)
        => player.TryGetComponent(out PlayerTally tally) ? "<color=#" + ColorUtility.ToHtmlStringRGB(tally.Color) + ">" + tally.DisplayName + "</color>" : player.name;

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
        BuildPanel();
    }

    private void BuildPanel()
    {
        panel = FeelUI.Rect("Rescue", canvas.transform);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.anchoredPosition = new Vector2(0f, -182f);
        panel.gameObject.AddComponent<SlantedHealthGraphic>().Configure(1f, 1f, 1d, false, Shear, 0f, Plate, Color.clear, Color.clear);

        float halfWidth = PanelWidth * .5f, textWidth = PanelWidth - Inset * 2f - 96f;
        who = FeelUI.ScoreLabel("Who", panel, 20f, TextAlignmentOptions.MidlineLeft);
        who.rectTransform.sizeDelta = new Vector2(textWidth, 34f);
        who.rectTransform.anchoredPosition = new Vector2(Inset + textWidth * .5f - halfWidth, 8f);
        secondsLeft = FeelUI.ScoreLabel("Seconds", panel, 22f, TextAlignmentOptions.MidlineRight);
        secondsLeft.rectTransform.sizeDelta = new Vector2(96f, 34f);
        secondsLeft.rectTransform.anchoredPosition = new Vector2(halfWidth - Inset - 48f, 8f);
        secondsLeft.color = ObjectiveLogPanel.Accent;

        var track = FeelUI.Rect("Track", panel);
        track.sizeDelta = new Vector2(PanelWidth - Inset * 2f, BarHeight);
        track.anchoredPosition = new Vector2(0f, -17f);
        var trackFill = track.gameObject.AddComponent<Image>();
        trackFill.color = Track;
        trackFill.raycastTarget = false;
        fill = FeelUI.Rect("Fill", track);
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, .5f);
        var barFill = fill.gameObject.AddComponent<Image>();
        barFill.color = ObjectiveLogPanel.Accent;
        barFill.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas);
    }
}
