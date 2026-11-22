using TMPro;
using UnityEngine;

/// <summary>
/// The objectives on the Tab screen, in the manner of PAYDAY 2's list: what's been done quiet in grey, and what to do now
/// in white with its progress and details. It slides in where the mission bar slides out, so one reads as turning into
/// the other. Its surfaces are the addon HUD's (HudTone): its black box under each row (held dense under what to do now),
/// white ticks and progress, its face. Reads MissionTracker. Its rows are made up front.
/// </summary>
public sealed class ObjectiveLogPanel
{
    private const int DoneRows = 4;
    // TextInset clears the tick at the top of the row, where the slant carries it furthest right.
    private const float Width = 440f, DoneHeight = 30f, CurrentHeight = 38f, RowGap = 5f, Shear = 9f, TextInset = 30f;
    private const float ProgressHeight = 4f, DetailWidth = 410f;
    // Lined up with the mission bar it takes over from; it starts a little to the left.
    private const float Left = 44f, Top = -46f, SlideFrom = 24f;
    private static string Waiting => Texts.Get("objective.waiting");
    private static string WaitingDetail => Texts.Get("objective.waiting.detail");
    private const float TickShare = 7f / Width;
    // A mint that ModeScreen's title and the downed countdown (PlayerLifeHUD) are drawn in; the list itself wears the
    // HUD's tones (HudTone).
    public static readonly Color Accent = new Color(.34f, .91f, .72f);
    // The progress track: white, faint.
    private const float TrackAlpha = .18f;

    private sealed class DoneRow { public RectTransform root; public TextMeshProUGUI label; }

    private readonly DoneRow[] done = new DoneRow[DoneRows];
    private readonly RectTransform root, current, track, fill;
    private readonly TextMeshProUGUI title, counter, detail;

    public ObjectiveLogPanel(Transform parent)
    {
        root = FeelUI.Rect("Objectives", parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        SetReveal(0f);

        for (int i = 0; i < DoneRows; i++)
        {
            var row = new DoneRow { root = FeelUI.Rect("Done", root) };
            Plate(row.root, HudTone.Box);
            Tick(row.root);
            row.label = FeelUI.ScoreLabel("Label", row.root, 18f, TextAlignmentOptions.Left);
            HudTone.Apply(row.label);
            row.label.color = HudTone.Grey;
            Place(row.label.rectTransform, TextInset, 0f, Width - TextInset * 2f, DoneHeight);
            row.label.alignment = TextAlignmentOptions.MidlineLeft;
            row.root.gameObject.SetActive(false);
            done[i] = row;
        }

        current = FeelUI.Rect("Current", root);
        Plate(current, HudTone.BoxHeld);
        Tick(current);
        title = FeelUI.ScoreLabel("Title", current, 21f, TextAlignmentOptions.MidlineLeft);
        HudTone.Apply(title);
        title.color = HudTone.Text;
        counter = FeelUI.ScoreLabel("Counter", current, 19f, TextAlignmentOptions.MidlineRight);
        HudTone.Apply(counter);
        counter.color = HudTone.Text;
        track = FeelUI.Rect("Track", current);
        var trackFill = track.gameObject.AddComponent<UnityEngine.UI.Image>();
        trackFill.color = HudTone.With(HudTone.Text, TrackAlpha);
        trackFill.raycastTarget = false;
        fill = FeelUI.Rect("Fill", track);
        var barFill = fill.gameObject.AddComponent<UnityEngine.UI.Image>();
        barFill.color = HudTone.Text;
        barFill.raycastTarget = false;
        detail = FeelUI.ScoreLabel("Detail", current, 16f, TextAlignmentOptions.TopLeft);
        HudTone.Apply(detail);
        detail.color = HudTone.Grey;
        detail.textWrappingMode = TextWrappingModes.Normal;
        Place(title.rectTransform, TextInset, 0f, Width - 120f, CurrentHeight);
        Place(counter.rectTransform, Width - 108f, 0f, 90f, CurrentHeight);
        Place(track, TextInset, -CurrentHeight - 2f, Width - TextInset * 2f, ProgressHeight);
        Place(fill, 0f, 0f, 0f, ProgressHeight);
        Place(detail.rectTransform, TextInset, -CurrentHeight - 12f, DetailWidth, 90f);
    }

    /// <summary>0 waiting out of place, 1 in place, eased.</summary>
    public void SetReveal(float amount)
        => root.anchoredPosition = new Vector2(Left - SlideFrom * (1f - Mathf.SmoothStep(0f, 1f, amount)), Top);

    public void Refresh(MissionTracker tracker)
    {
        float y = 0f;
        var finished = tracker != null ? tracker.Finished : null;
        int count = finished != null ? finished.Count : 0, shown = Mathf.Min(count, DoneRows);
        for (int i = 0; i < DoneRows; i++)
        {
            bool used = i < shown;
            done[i].root.gameObject.SetActive(used);
            if (!used) continue;
            done[i].label.text = finished[count - shown + i];
            Place(done[i].root, 0f, y, Width, DoneHeight);
            y -= DoneHeight + RowGap;
        }
        Place(current, 0f, y, Width, CurrentHeight);

        var objective = tracker != null ? tracker.Current : null;
        bool numeric = objective != null && objective.Display != MissionProgressDisplay.None;
        title.text = objective != null ? objective.Title : Waiting;
        counter.text = numeric ? objective.ProgressText : "";
        detail.text = objective != null ? objective.Description : WaitingDetail;
        track.gameObject.SetActive(numeric);
        if (numeric) fill.sizeDelta = new Vector2((Width - TextInset * 2f) * objective.Progress, ProgressHeight);
    }

    private static SlantedPlate Plate(RectTransform rect, Color color)
    {
        var plate = rect.gameObject.AddComponent<SlantedPlate>();
        plate.Configure(1f, Shear, color);
        return plate;
    }

    // A band across the plate's left end, drawn at the plate's size so it shares the slant.
    private static void Tick(RectTransform plate)
    {
        var tick = FeelUI.Rect("Tick", plate);
        tick.anchorMin = Vector2.zero; tick.anchorMax = Vector2.one;
        tick.offsetMin = tick.offsetMax = Vector2.zero;
        tick.gameObject.AddComponent<SlantedPlate>().Configure(TickShare, Shear, HudTone.Text);
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
