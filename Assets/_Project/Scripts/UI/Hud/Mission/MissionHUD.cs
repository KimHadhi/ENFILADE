using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The objective always on screen, and the alert when it changes. The full list is on the Tab screen (ObjectiveLogPanel).
/// What the bar says is decided here; how it looks is MissionBarLook's (Hud.csv's Mission row).
/// Under the bar stand the scoreboard's key cap and the 목표·점수판 hint, each in a slot of its own (Hud.csv MissionTab):
/// the cap is as wide as the key's name, the hint starts after it, and both step down out of the way of whatever row the
/// bar draws under itself (an assault's time row lay over them; user, 2026-11-12: "아니 TAB이랑 목표 점수판 저거랑
/// 겹치는데?"). The alert stays narrower than the room right of the bar leaves, so it never shares a column with it.
/// </summary>
public sealed class MissionHUD : MonoBehaviour, ITableBound, IHudPreview
{
    private static string TabHint => Texts.Get("objective.tab_hint");
    // How far the bar slides off as the Tab screen's list takes its place (px at 1080p).
    private const float MakeWay = 24f;
    // The prefab's key cap, beside the hint (KeyBindings names the key in its Label).
    private const string KeyCapName = "TabKey";

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
    [Header("Under the bar (Hud.csv MissionTab)")]
    [Tooltip("Room left and right of the key's name inside its cap (px). A long key name widens the cap; it is never narrower than the prefab drew it.")]
    [SerializeField, Min(0)] private float keyPad = 6f;
    [Tooltip("Room between the key cap and the 목표·점수판 hint (px).")]
    [SerializeField, Min(0)] private float hintGap = 9f;
    [Tooltip("Least room between the bar's lowest row (its progress line, or an assault's time row) and the key cap and hint under it (px).")]
    [SerializeField, Min(0)] private float rowGap = 6f;
    [Tooltip("How fast the key cap and hint step down out of an assault's time row, and back up after it (px/s).")]
    [SerializeField, Min(1)] private float stepSpeed = 220f;
    [Tooltip("Least room between the new-objective alert and the bar's right end (px), lean included. The alert stays narrower than that leaves.")]
    [SerializeField, Min(0)] private float alertGap = 28f;

    public bool IsAlertPlaying => alertClock >= 0;
    public float AlertDuration => openDuration + emphasisDuration + closeDuration;
    /// <summary>
    /// How far below the screen's top edge the objective block reaches now (px at 1080p): the bar, the rows it draws under
    /// itself and the key cap and hint, stepped down or not. What stands under the block (the chat's history) keeps below it.
    /// </summary>
    public static float Floor { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Floor = 0f;
    private float alertClock = -1, alertWidth;
    private float lastCanvasWidth;
    private MissionBarLook look;

    // Home is where a part stands now that the cap has its width; Authored is where the prefab put it.
    private struct Part { public RectTransform Rect; public CanvasGroup Group; public Vector2 Home, Authored; public Quaternion Turn; public bool Under; }
    // The bar stands on neither side's plane (HudPlane), so it turns with a lean itself (MakeWayForTabScreen).
    private bool turnsWithLean, turned, previewing;
    private readonly List<Part> parts = new List<Part>();
    private RectTransform keyCap;
    private TMP_Text keyLabel;
    // The cap's width as drawn, the least room the prefab left between the bar's foot and what stands under it, and how
    // far those stand stepped down now (px).
    private float keyCapWidth = -1f, room = float.PositiveInfinity, step;
    // How far the Tab screen has taken the bar's place (0-1, eased), which the alert makes way by too.
    private float makeWay;

    private void Awake()
    {
        GameAudio.Bind(audioSource, AudioChannel.UI);
        Look();
        GameTables.Bind(HudCamera.TableName, "MissionTab", this);
    }

    public void OnTableApplied()
    {
        if (parts.Count > 0) LayOutTabRow();
        if (IsAlertPlaying) MeasureAlert();
    }

    /// <summary>
    /// Editor (ENFILADE > HUD): lays the bar out from Hud.csv with its sample texts and the count showing, so the prefab
    /// looks in the editor the way it will in Play.
    /// </summary>
    public void LayOutForEditor()
    {
        if (Look() != null) look.LayOutForEditor(CanvasWidth());
    }

    /// <summary>ENFILADE → HUD → 겹침 검사: holds the new-objective alert open on the bar's objective while it measures.</summary>
    public void Preview(bool show)
    {
        if (show == previewing) return;
        previewing = show;
        if (show) ShowAlert(title != null && !string.IsNullOrEmpty(title.text) ? title.text : Texts.Get("objective.waiting"));
        else HideAlert();
    }

    // The bar's look, found on the bar or added to it, and handed the bar's parts.
    private MissionBarLook Look()
    {
        if (look != null || missionBar == null || title == null || counter == null || counterRoot == null) return look;
        if (!missionBar.TryGetComponent(out look)) look = missionBar.gameObject.AddComponent<MissionBarLook>();
        look.Init(title, counter, counterRoot, progress);
        return look;
    }

    // Everything here but the alert stands where the Tab screen's list will: the bar, its key chip, its hint. Found once,
    // where the prefab put them (after MissionBarLook has set the bar on its foot), so being switched off and on midway
    // through a slide or a step never moves their homes.
    private void FindParts()
    {
        parts.Clear();
        keyCap = transform.Find(KeyCapName) as RectTransform;
        keyLabel = keyCap != null ? keyCap.GetComponentInChildren<TMP_Text>(true) : null;
        if (keyCap != null) keyCapWidth = keyCap.sizeDelta.x;
        float foot = missionBar != null && missionBar.parent == transform ? Bottom(missionBar, missionBar.anchoredPosition) : 0f;
        room = float.PositiveInfinity;
        foreach (Transform child in transform)
        {
            if (child == alert || !(child is RectTransform rect)) continue;
            var group = child.GetComponent<CanvasGroup>();
            if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
            bool under = missionBar != null && rect != missionBar && missionBar.parent == transform;
            if (under) room = Mathf.Min(room, foot - Top(rect, rect.anchoredPosition));
            parts.Add(new Part { Rect = rect, Group = group, Home = rect.anchoredPosition, Authored = rect.anchoredPosition, Turn = rect.localRotation, Under = under });
        }
    }

    private static float Top(RectTransform rect, Vector2 at) => at.y + rect.rect.height * (1f - rect.pivot.y);
    private static float Bottom(RectTransform rect, Vector2 at) => at.y - rect.rect.height * rect.pivot.y;

    private void OnEnable()
    {
        if (parts.Count == 0) FindParts();
        turnsWithLean = GetComponentInParent<HudPlane>() == null;
        if (boardHint != null) boardHint.text = TabHint;
        ShowBoardKey();
        KeyBindings.Changed += ShowBoardKey;
        if (tracker != null) tracker.Changed += Refresh;
        HideAlert();
        Refresh(tracker != null ? tracker.Current : null, false);
    }
    private void OnDisable()
    {
        KeyBindings.Changed -= ShowBoardKey;
        if (tracker != null) tracker.Changed -= Refresh;
        HideAlert();
        if (audioSource != null) audioSource.Stop();
    }

    // HudRig puts the sides on their planes in its Awake; by now every Awake has run.
    private void Start() => turnsWithLean = GetComponentInParent<HudPlane>() == null;

    // The chip beside the hint (the HUD prefab's TabKey/Label) names the scoreboard key as the player set it in the key
    // window (TAB by default).
    private void ShowBoardKey()
    {
        if (keyLabel != null) keyLabel.text = KeyBindings.Name("UI/OpenObjectiveBoard");
        LayOutTabRow();
    }

    // The key cap as wide as its key's name needs (a key set to 뒤 버튼 or NUMPAD ENTER ran out of the 42 px cap into the
    // hint), never narrower than the prefab drew it, its left edge where it was; the hint starts hintGap after it.
    private void LayOutTabRow()
    {
        if (keyCap == null || keyCapWidth < 0f) return;
        float wide = keyCapWidth;
        if (keyLabel != null) wide = Mathf.Max(keyCapWidth, keyLabel.GetPreferredValues(keyLabel.text).x + keyPad * 2f);
        keyCap.sizeDelta = new Vector2(wide, keyCap.sizeDelta.y);
        var hint = boardHint != null ? boardHint.rectTransform : null;
        Vector2 capAt = Vector2.zero;
        bool capFound = false;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Rect != keyCap) continue;
            var part = parts[i];
            // Grown about its left edge.
            part.Home = part.Authored + new Vector2((wide - keyCapWidth) * keyCap.pivot.x, 0f);
            parts[i] = part;
            capAt = part.Home;
            capFound = true;
        }
        if (!capFound || hint == null) return;
        float capRight = capAt.x + wide * (1f - keyCap.pivot.x);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Rect != hint) continue;
            var part = parts[i];
            part.Home = new Vector2(capRight + hintGap + hint.rect.width * hint.pivot.x, part.Authored.y);
            parts[i] = part;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        MakeWayForTabScreen(dt);
        if (alertClock >= 0)
        {
            // Held open while the overlap check measures it.
            if (previewing) alertClock = openDuration;
            else alertClock += dt;
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

    // The bar leaves as the Tab screen's list arrives. What stands under it steps down while the bar draws a row of its
    // own lower than the prefab left room for (an assault's time row), and back up once it has gone.
    private void MakeWayForTabScreen(float dt)
    {
        float away = Mathf.SmoothStep(0f, 1f, ScoreboardHUD.Openness);
        makeWay = away;
        var slide = new Vector2(-MakeWay * away, 0f);
        float wanted = look != null && !float.IsInfinity(room) ? Mathf.Max(0f, look.Below + rowGap - room) : 0f;
        step = Mathf.MoveTowards(step, wanted, stepSpeed * dt);
        var down = new Vector2(0f, -step);
        float lowest = 0f;
        foreach (var part in parts)
        {
            part.Group.alpha = 1f - away;
            var at = part.Home + (part.Under ? down : Vector2.zero);
            part.Rect.anchoredPosition = at + slide;
            // The parts hang from the block's top edge, so the lowest foot is the block's reach down the screen.
            float foot = Bottom(part.Rect, at);
            if (part.Rect == missionBar && look != null) foot -= look.Below;
            lowest = Mathf.Min(lowest, foot);
        }
        Floor = -lowest;
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
        title.text = objective != null ? objective.Title : Texts.Get("objective.waiting");
        if (Look() != null) look.Show(objective, CanvasWidth());
        if (objective == null && !previewing) HideAlert();
        else if (announce && objective != null) ShowAlert(objective.Title);
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
        if (audioSource != null && alertSound != null && !previewing)
        {
            audioSource.Stop(); audioSource.PlayOneShot(alertSound, alertVolume);
        }
    }
    private void MeasureAlert()
    {
        float cap = Mathf.Min(maxAlertWidth, Mathf.Max(260, CanvasWidth() - 100));
        // Centred at the top, the alert keeps right of the bar's right end, so the two never share a column, whatever
        // rows the bar draws under itself and however far a lean turns it (a long objective's bar reached 730 px from
        // the left edge and its widest alert started at 550). A long title wraps instead.
        if (look != null && missionBar != null)
        {
            float right = missionBar.anchoredPosition.x + look.Reach;
            if (missionBar.parent == transform)
                foreach (var part in parts)
                    if (part.Rect == missionBar) right = part.Home.x + look.Reach;
            cap = Mathf.Min(cap, Mathf.Max(280f, 2f * (CanvasWidth() * .5f - right - alertGap)));
        }
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
        // The Tab screen shows the objective in its list: the alert makes way with the bar. It was the one objective piece
        // that did not, and kept blinking at full strength over the Tab screen's titles while Tab was held (user,
        // 2026-11-13: "탭 누르고 있을 때 목표 하이라이트 돼").
        alertGroup.alpha = alpha * (1f - makeWay);
    }
    private float Pulse(float t, int count) => Mathf.Repeat(t * count, 1) < .5f ? 1 : blinkLowAlpha;
    private void HideAlert()
    {
        alertClock = -1;
        if (alertGroup != null) alertGroup.alpha = 0;
        if (alert != null) alert.gameObject.SetActive(false);
    }
}
