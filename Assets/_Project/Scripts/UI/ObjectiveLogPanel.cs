using TMPro;
using UnityEngine;

/// <summary>
/// The objectives on the Tab screen, in the manner of PAYDAY 2's list: what's been done in green, and what to do now in
/// white with its progress and details. It slides in where the mission bar slides out, so one reads as turning into the
/// other. Reads MissionTracker. Its rows are made up front.
/// </summary>
public sealed class ObjectiveLogPanel
{
    private const int DoneRows = 4;
    // TextInset clears the tick at the top of the row, where the slant carries it furthest right.
    private const float Width = 440f, DoneHeight = 30f, CurrentHeight = 38f, RowGap = 5f, Shear = 9f, TextInset = 30f;
    private const float ProgressHeight = 4f, DetailWidth = 410f;
    // Lined up with the mission bar it takes over from; it starts a little to the left.
    private const float Left = 44f, Top = -46f, SlideFrom = 24f;
    private const string Waiting = "목표 대기 중", WaitingDetail = "새로운 목표가 등록되면 이곳에 표시됩니다.";
    private const float TickShare = 7f / Width;
    // The mint of the HUD's TAB key, so the list reads as what that key opens and not as the mission bar again.
    public static readonly Color Accent = new Color(.34f, .91f, .72f);
    private static readonly Color DonePlate = new Color(0f, 0f, 0f, .4f), CurrentPlate = new Color(0f, 0f, 0f, .55f);
    private static readonly Color Track = new Color(.34f, .91f, .72f, .18f), Soft = new Color(.82f, .84f, .88f);

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
            Plate(row.root, DonePlate);
            Tick(row.root);
            row.label = Label("Label", row.root, 18f, TextAlignmentOptions.Left);
            row.label.color = Accent;
            Place(row.label.rectTransform, TextInset, 0f, Width - TextInset * 2f, DoneHeight);
            row.label.alignment = TextAlignmentOptions.MidlineLeft;
            row.root.gameObject.SetActive(false);
            done[i] = row;
        }

        current = FeelUI.Rect("Current", root);
        Plate(current, CurrentPlate);
        Tick(current);
        title = Label("Title", current, 21f, TextAlignmentOptions.MidlineLeft);
        counter = Label("Counter", current, 19f, TextAlignmentOptions.MidlineRight);
        counter.color = Accent;
        track = FeelUI.Rect("Track", current);
        var trackFill = track.gameObject.AddComponent<UnityEngine.UI.Image>();
        trackFill.color = Track;
        trackFill.raycastTarget = false;
        fill = FeelUI.Rect("Fill", track);
        var barFill = fill.gameObject.AddComponent<UnityEngine.UI.Image>();
        barFill.color = Accent;
        barFill.raycastTarget = false;
        detail = Label("Detail", current, 16f, TextAlignmentOptions.TopLeft);
        detail.color = Soft;
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

    private static TextMeshProUGUI Label(string name, Transform parent, float size, TextAlignmentOptions alignment)
        => FeelUI.ScoreLabel(name, parent, size, alignment);

    private static SlantedHealthGraphic Plate(RectTransform rect, Color color)
    {
        var plate = rect.gameObject.AddComponent<SlantedHealthGraphic>();
        plate.Configure(1f, 1f, 1d, false, Shear, 0f, color, Color.clear, Color.clear);
        return plate;
    }

    // A band across the plate's left end, drawn at the plate's size so it shares the slant.
    private static void Tick(RectTransform plate)
    {
        var tick = FeelUI.Rect("Tick", plate);
        tick.anchorMin = Vector2.zero; tick.anchorMax = Vector2.one;
        tick.offsetMin = tick.offsetMax = Vector2.zero;
        tick.gameObject.AddComponent<SlantedHealthGraphic>().Configure(TickShare, 1f, 1d, false, Shear, 0f, Accent, Color.clear, Color.clear);
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
