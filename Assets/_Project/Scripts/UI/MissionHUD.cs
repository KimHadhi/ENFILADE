using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The objective always on screen, and the alert when it changes. The full list is on the Tab screen (ObjectiveLogPanel).</summary>
public sealed class MissionHUD : MonoBehaviour
{
    private const string TabHint = "목표·점수판";
    // How far the bar slides off as the Tab screen's list takes its place (px at 1080p).
    private const float MakeWay = 24f;

    [Header("Mission")]
    [SerializeField] private MissionTracker tracker;
    [Header("Persistent mission bar")]
    [SerializeField] private RectTransform missionBar;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text counter;
    [SerializeField] private GameObject counterRoot;
    [SerializeField] private Image progress;
    [SerializeField] private TMP_Text boardHint;
    [Header("New objective alert")]
    [SerializeField] private RectTransform alert;
    [SerializeField] private CanvasGroup alertGroup;
    [SerializeField] private RectTransform alertContent;
    [SerializeField] private TMP_Text alertTitle;
    [SerializeField, Min(.05f)] private float openDuration = .45f;
    [SerializeField, Min(.1f)] private float emphasisDuration = 2f;
    [SerializeField, Min(.05f)] private float closeDuration = .30f;
    [SerializeField, Range(0, 1)] private float blinkLowAlpha = .16f;
    [SerializeField, Min(100)] private float maxAlertWidth = 820;
    [Header("Alert sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip alertSound;
    [SerializeField, Range(0, 1)] private float alertVolume = .45f;

    public bool IsAlertPlaying => alertClock >= 0;
    public float AlertDuration => openDuration + emphasisDuration + closeDuration;
    private float alertClock = -1, alertWidth, targetProgress;
    private float lastCanvasWidth;

    private struct Part { public RectTransform Rect; public CanvasGroup Group; public Vector2 Home; }
    private readonly List<Part> parts = new List<Part>();

    private void Awake() => GameAudio.Bind(audioSource, AudioChannel.UI);

    // Everything here but the alert stands where the Tab screen's list will: the bar, its key chip, its hint.
    private void FindParts()
    {
        parts.Clear();
        foreach (Transform child in transform)
        {
            if (child == alert || !(child is RectTransform rect)) continue;
            var group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
            parts.Add(new Part { Rect = rect, Group = group, Home = rect.anchoredPosition });
        }
    }

    private void OnEnable()
    {
        AnchorMissionBar();
        FindParts();
        ApplyProgress(0);
        if (boardHint != null) boardHint.text = TabHint;
        if (tracker != null) tracker.Changed += Refresh;
        HideAlert();
        Refresh(tracker != null ? tracker.Current : null, false);
    }
    private void OnDisable()
    {
        if (tracker != null) tracker.Changed -= Refresh;
        HideAlert();
        if (audioSource != null) audioSource.Stop();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        MakeWayForTabScreen();
        if (progress != null) ApplyProgress(Mathf.MoveTowards(progress.fillAmount, targetProgress, dt * 1.8f));
        if (alertClock >= 0)
        {
            alertClock += dt;
            EvaluateAlert(alertClock);
        }
        float width = CanvasWidth();
        if (Mathf.Abs(width - lastCanvasWidth) > 1)
        {
            lastCanvasWidth = width;
            Refresh(tracker != null ? tracker.Current : null, false);
            if (IsAlertPlaying) MeasureAlert();
        }
    }

    // The bar leaves as the Tab screen's list arrives.
    private void MakeWayForTabScreen()
    {
        float away = Mathf.SmoothStep(0f, 1f, ScoreboardHUD.Openness);
        var slide = new Vector2(-MakeWay * away, 0f);
        foreach (var part in parts)
        {
            part.Group.alpha = 1f - away;
            part.Rect.anchoredPosition = part.Home + slide;
        }
    }

    private float CanvasWidth() => (transform as RectTransform).rect.width;

    private void AnchorMissionBar()
    {
        if (missionBar == null) return;
        // Preserve the authored left edge, then grow only toward the right as titles change.
        float left = missionBar.anchoredPosition.x - missionBar.rect.width * missionBar.pivot.x;
        missionBar.pivot = new Vector2(0, missionBar.pivot.y);
        missionBar.anchoredPosition = new Vector2(left, missionBar.anchoredPosition.y);
    }

    private void ApplyProgress(float value)
    {
        if (progress == null) return;
        progress.fillAmount = Mathf.Clamp01(value);
        // A plain rectangle avoids the transparent/rounded padding of the built-in UI sprite.
        progress.sprite = null;
        progress.type = Image.Type.Simple;
        var rect = progress.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(progress.fillAmount, 1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void Refresh(MissionObjective objective, bool announce)
    {
        if (title == null) return;
        bool numeric = objective != null && objective.Display != MissionProgressDisplay.None;
        title.text = objective != null ? objective.Title : "목표 대기 중";
        counterRoot.SetActive(numeric);
        counter.text = numeric ? objective.ProgressText : "";
        targetProgress = numeric ? objective.Progress : 0;
        progress.transform.parent.gameObject.SetActive(numeric);
        float counterWidth = numeric ? Mathf.Max(100, counter.GetPreferredValues(counter.text).x + 28) : 0;
        var counterRect = (RectTransform)counterRoot.transform;
        counterRect.anchorMin = Vector2.right;
        counterRect.anchorMax = Vector2.one;
        counterRect.offsetMin = new Vector2(-counterWidth, 0);
        counterRect.offsetMax = Vector2.zero;
        float maxWidth = Mathf.Min(690, Mathf.Max(300, CanvasWidth() - 84));
        float width = Mathf.Clamp(title.GetPreferredValues(title.text).x + 84 + counterWidth, 320, maxWidth);
        missionBar.sizeDelta = new Vector2(width, 48);
        title.rectTransform.offsetMax = new Vector2(-counterWidth - 16, -4);
        if (objective == null) HideAlert();
        else if (announce) ShowAlert(objective.Title);
    }

    public void ShowAlert(string missionTitle)
    {
        if (alert == null || alertTitle == null) return;
        alertTitle.text = missionTitle;
        MeasureAlert();
        // New objectives replace stale announcements; progress changes never enter this path.
        alertClock = 0;
        alert.gameObject.SetActive(true);
        EvaluateAlert(0);
        if (audioSource != null && alertSound != null)
        {
            audioSource.Stop(); audioSource.PlayOneShot(alertSound, alertVolume);
        }
    }
    private void MeasureAlert()
    {
        float cap = Mathf.Min(maxAlertWidth, Mathf.Max(260, CanvasWidth() - 100));
        alertWidth = Mathf.Clamp(alertTitle.GetPreferredValues(alertTitle.text).x + 56, 280, cap);
        alertContent.sizeDelta = new Vector2(alertWidth, 0);
        // Keep glyphs at their final size while the centered mask reveals them.
        float height = Mathf.Max(96, alertTitle.GetPreferredValues(alertTitle.text, alertWidth - 56, 0).y + 58);
        alert.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    /// <summary>Absolute time makes pulse counts and fixed-duration expansion independent of text length/frame rate.</summary>
    public void EvaluateAlert(float elapsed)
    {
        if (alert == null || alertGroup == null) return;
        if (elapsed >= AlertDuration) { HideAlert(); return; }
        float fraction, alpha;
        if (elapsed < openDuration)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.05f, openDuration));
            fraction = 1 - Mathf.Pow(1 - t, 3);
            alpha = Pulse(t, 3);
        }
        else if (elapsed < openDuration + emphasisDuration)
        {
            fraction = 1;
            float t = (elapsed - openDuration) / Mathf.Max(.1f, emphasisDuration);
            alpha = Mathf.Lerp(blinkLowAlpha, 1, .5f + .5f * Mathf.Cos(t * Mathf.PI * 4));
        }
        else
        {
            float t = (elapsed - openDuration - emphasisDuration) / Mathf.Max(.05f, closeDuration);
            fraction = 1 - t * t;
            alpha = Pulse(t, 2);
        }
        alert.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(2, alertWidth * fraction));
        alertGroup.alpha = alpha;
    }
    private float Pulse(float t, int count) => Mathf.Repeat(t * count, 1) < .5f ? 1 : blinkLowAlpha;
    private void HideAlert()
    {
        alertClock = -1;
        if (alertGroup != null) alertGroup.alpha = 0;
        if (alert != null) alert.gameObject.SetActive(false);
    }
}
