using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The objective always on screen, and the alert when it changes. The full list is on the Tab screen (ObjectiveLogPanel).
/// What the bar says is decided here; how it looks is MissionBarLook's (Hud.csv's Mission row).
/// </summary>
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
    private float alertClock = -1, alertWidth;
    private float lastCanvasWidth;
    private MissionBarLook look;

    private struct Part { public RectTransform Rect; public CanvasGroup Group; public Vector2 Home; public Quaternion Turn; }
    // The bar stands on neither side's plane (HudPlane), so it turns with a lean itself (MakeWayForTabScreen).
    private bool turnsWithLean, turned;
    private readonly List<Part> parts = new List<Part>();

    private void Awake()
    {
        GameAudio.Bind(audioSource, AudioChannel.UI);
        Look();
    }

    /// <summary>
    /// Editor (Tools > HUD): lays the bar out from Hud.csv with its sample texts and the count showing, so the prefab
    /// looks in the editor the way it will in Play.
    /// </summary>
    public void LayOutForEditor()
    {
        if (Look() != null) look.LayOutForEditor(CanvasWidth());
    }

    // The bar's look, found on the bar or added to it, and handed the bar's parts.
    private MissionBarLook Look()
    {
        if (look != null || missionBar == null || title == null || counter == null || counterRoot == null) return look;
        if (!missionBar.TryGetComponent(out look)) look = missionBar.gameObject.AddComponent<MissionBarLook>();
        look.Init(title, counter, counterRoot, progress);
        return look;
    }

    // Everything here but the alert stands where the Tab screen's list will: the bar, its key chip, its hint.
    private void FindParts()
    {
        parts.Clear();
        foreach (Transform child in transform)
        {
            if (child == alert || !(child is RectTransform rect)) continue;
            var group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
            parts.Add(new Part { Rect = rect, Group = group, Home = rect.anchoredPosition, Turn = rect.localRotation });
        }
        turnsWithLean = GetComponentInParent<HudPlane>() == null;
    }

    private void OnEnable()
    {
        FindParts();
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
        // Leaning with Q/E, the bar turns within the screen as the HUD's sides do (HudCamera.LeanRoll, HudPlane), its
        // parts as one about the bar itself, so the whole HUD tilts together (user, 2026-11-10: the bar staying upright
        // while the rest tilted read badly and made them queasy).
        float roll = turnsWithLean ? HudCamera.LeanRoll : 0f;
        if (Mathf.Abs(roll) < .01f && !turned) return;
        turned = Mathf.Abs(roll) >= .01f;
        var turn = Quaternion.Euler(0f, 0f, turned ? roll : 0f);
        Vector3 about = missionBar != null && missionBar.parent == transform ? missionBar.localPosition
            : parts.Count > 0 ? parts[0].Rect.localPosition : Vector3.zero;
        foreach (var part in parts)
        {
            part.Rect.localPosition = about + turn * (part.Rect.localPosition - about);
            part.Rect.localRotation = turn * part.Turn;
        }
    }

    private float CanvasWidth() => (transform as RectTransform).rect.width;

    private void Refresh(MissionObjective objective, bool announce)
    {
        if (title == null) return;
        title.text = objective != null ? objective.Title : "목표 대기 중";
        if (Look() != null) look.Show(objective, CanvasWidth());
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
