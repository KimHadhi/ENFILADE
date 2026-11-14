using TMPro;
using UnityEngine;
using UnityEngine.UI;

// AssaultBar: the phase row under the bar, the phase and its countdown, the song as a meter, and the song playing. One part of the class; the rest is in AssaultBar.cs.
public sealed partial class AssaultBar
{
    // The row: its first line's middle this far under the bar's foot, that line's height, the meter's middle and
    // thickness, the song line's middle, and how far down its foot reaches (px).
    private const float RowDrop = 22f, LineHeight = 18f, MeterDrop = 36f, MeterHeight = 6f, NowDrop = 50f, RowBottom = 58f;
    // The meter's segments and the gap between them; the equaliser's bars, their width, gap, lowest and tallest (px).
    private const int Segments = 44, EqBars = 4;
    private const float SegmentGap = 1.5f, EqWidth = 2.5f, EqGap = 2f, EqLow = 2f, EqTall = 11f, TimesWidth = 120f;
    // Each bar's own pace (radians a second): together they read as music, none of them a blink.
    private static readonly float[] EqPaces = { 9.1f, 6.3f, 7.7f, 5.2f };
    // The row fades in this fast (s); a new phase's state grows this much for this long, smoothly.
    private const float RowFade = .3f, StatePunch = .12f, PunchSeconds = .25f;
    private static readonly Color Track = new Color(1f, .96f, .97f, .16f), Played = new Color(1f, .96f, .97f, .62f),
        Window = new Color(1f, .31f, .47f, .38f);

    private RectTransform row, stateRect, timesRect, meter, eqRoot;
    private readonly Image[] cells = new Image[Segments];
    private readonly RectTransform[] eqBars = new RectTransform[EqBars];
    private TextMeshProUGUI state, times, now;
    private CanvasGroup rowGroup, stateGroup;
    private float rowShow, punchClock = -1f, eqClock, meterWidth = -1f;
    private int headCell = -2;
    private float[] meterSpans;

    // A new phase: its state grows a little and settles (never a flash).
    private void Turn(CombatPhase phase)
    {
        shown = phase;
        if (phase != CombatPhase.Quiet) punchClock = 0f;
    }

    private void PoseRow(float width, float dt)
    {
        rowGroup.alpha = Ease.Out(rowShow);
        if (rowShow <= 0f) return;
        // The row reaches as far right as the bar's own parts (its count box and the line's tail, MissionBarLook.Reach).
        float right = look != null ? Mathf.Max(width, look.Reach) : width;
        var phase = shown != CombatPhase.Quiet ? shown : last.Phase;
        if (phase == CombatPhase.Quiet) phase = CombatPhase.Control;
        state.text = StateText(phase);
        stateGroup.alpha = phase == CombatPhase.Anticipation ? pulse : 1f;
        float punch = 0f;
        if (punchClock >= 0f)
        {
            punchClock += dt;
            float t = Mathf.Clamp01(punchClock / PunchSeconds);
            punch = (1f - t) * (1f - t);
            if (t >= 1f) punchClock = -1f;
        }
        stateRect.localScale = Vector3.one * (1f + StatePunch * punch);
        stateRect.sizeDelta = new Vector2(Mathf.Max(80f, right - TimesWidth), LineHeight);
        timesRect.anchoredPosition = new Vector2(right, -RowDrop);

        // The song as a meter: played segments brighter, its assaults in pink, the segment it is at white.
        bool has = last.Length > 0f;
        meter.gameObject.SetActive(has);
        times.gameObject.SetActive(has);
        if (has)
        {
            times.text = DeathCard.Clock(last.Elapsed) + " / " + DeathCard.Clock(last.Length);
            LayMeter(right);
            ColourMeter();
        }

        // The song playing, and an equaliser that moves only while one does.
        now.text = SongLine();
        bool playing = !string.IsNullOrEmpty(last.Title);
        eqClock += dt;
        for (int i = 0; i < EqBars; i++)
        {
            float swell = playing ? .5f + .5f * Mathf.Sin(eqClock * EqPaces[i] + i * 1.7f) : 0f;
            eqBars[i].sizeDelta = new Vector2(EqWidth, Mathf.Lerp(EqLow, EqTall, swell * swell));
        }
    }

    // [ 정비 CONTROL ]   NEXT 1:11 — the phase's word, its English beside it small, and what it counts down to.
    private string StateText(CombatPhase phase)
    {
        string id = phase == CombatPhase.Assault ? "hud.phase.assault" : phase == CombatPhase.Anticipation ? "hud.phase.anticipation" : "hud.phase.control";
        string count = phase == CombatPhase.Assault ? DeathCard.Clock(last.Until)
            : float.IsInfinity(last.Until) ? ""
            : Texts.Format(phase == CombatPhase.Anticipation ? "hud.phase.incoming" : "hud.phase.next", DeathCard.Clock(last.Until));
        string colour = phase == CombatPhase.Control ? "FFF5F7" : "FF4F78";
        return "<color=#FFF5F74D>[</color> <color=#" + colour + ">" + Texts.Get(id) + "</color> <size=68%><color=#FFF5F799>"
            + Texts.Get(id + ".en") + "</color></size> <color=#FFF5F74D>]</color>" + (count.Length > 0 ? "   <color=#FFFFFF>" + count + "</color>" : "");
    }

    private string SongLine()
    {
        if (!string.IsNullOrEmpty(last.Title)) return Texts.Format("hud.song.now", last.Title, last.Artist);
        return Texts.Get(last.Siren ? "hud.song.siren" : "hud.song.break");
    }

    // The segments spread over the row's width, laid again only when that changes.
    private void LayMeter(float right)
    {
        if (Mathf.Approximately(right, meterWidth)) return;
        meterWidth = right;
        meter.sizeDelta = new Vector2(right, MeterHeight);
        float each = Mathf.Max(1f, (right - SegmentGap * (Segments - 1)) / Segments);
        for (int i = 0; i < Segments; i++)
        {
            var rect = cells[i].rectTransform;
            rect.anchoredPosition = new Vector2(i * (each + SegmentGap), 0f);
            rect.sizeDelta = new Vector2(each, MeterHeight);
        }
    }

    // Coloured again only when the segment the song is at, or the song's assaults, change.
    private void ColourMeter()
    {
        int head = Mathf.Min(Segments - 1, Mathf.FloorToInt(Mathf.Clamp01(last.Elapsed / last.Length) * Segments));
        if (head == headCell && ReferenceEquals(meterSpans, last.Assaults)) return;
        headCell = head;
        meterSpans = last.Assaults;
        for (int i = 0; i < Segments; i++)
        {
            float middle = (i + .5f) / Segments * last.Length;
            bool inWindow = false;
            if (meterSpans != null)
                for (int k = 0; k + 1 < meterSpans.Length; k += 2)
                    if (middle >= meterSpans[k] && middle < meterSpans[k + 1]) { inWindow = true; break; }
            cells[i].color = i == head ? Color.white : i < head ? (inWindow ? KitStyle.Accent : Played) : inWindow ? Window : Track;
        }
    }

    private void BuildRow()
    {
        // Its own name, so the HUD overlap check (HudOverlap.csv) can tell the row from the bar it hangs on.
        row = Corner("Assault Time", root);
        rowGroup = row.gameObject.AddComponent<CanvasGroup>();
        rowGroup.alpha = 0f;
        stateRect = FeelUI.Rect("State", row);
        stateRect.anchorMin = stateRect.anchorMax = Vector2.zero;
        stateRect.pivot = new Vector2(0f, .5f);
        stateRect.anchoredPosition = new Vector2(0f, -RowDrop);
        stateGroup = stateRect.gameObject.AddComponent<CanvasGroup>();
        state = KitStyle.Flat(stateRect, "", 13f, TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        state.textWrappingMode = TextWrappingModes.NoWrap;
        state.richText = true;
        var font = GunFeelSettings.Instance != null ? GunFeelSettings.Instance.scoreFont : null;
        timesRect = FeelUI.Rect("Times", row);
        timesRect.anchorMin = timesRect.anchorMax = Vector2.zero;
        timesRect.pivot = new Vector2(1f, .5f);
        timesRect.sizeDelta = new Vector2(TimesWidth, LineHeight);
        times = KitStyle.Flat(timesRect, "", 12f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        if (font != null) times.font = font;
        times.textWrappingMode = TextWrappingModes.NoWrap;
        meter = FeelUI.Rect("Meter", row);
        meter.anchorMin = meter.anchorMax = Vector2.zero;
        meter.pivot = new Vector2(0f, .5f);
        meter.anchoredPosition = new Vector2(0f, -MeterDrop);
        for (int i = 0; i < Segments; i++)
        {
            var cell = FeelUI.Rect("Segment", meter);
            cell.anchorMin = cell.anchorMax = new Vector2(0f, .5f);
            cell.pivot = new Vector2(0f, .5f);
            cells[i] = Fill(cell, Track);
        }
        var line = FeelUI.Rect("Song", row);
        line.anchorMin = line.anchorMax = Vector2.zero;
        line.pivot = new Vector2(0f, .5f);
        line.anchoredPosition = new Vector2(0f, -NowDrop);
        line.sizeDelta = new Vector2(600f, 14f);
        eqRoot = FeelUI.Rect("Equaliser", line);
        eqRoot.anchorMin = eqRoot.anchorMax = new Vector2(0f, .5f);
        eqRoot.pivot = new Vector2(0f, .5f);
        eqRoot.anchoredPosition = new Vector2(1f, -EqTall * .5f);
        for (int i = 0; i < EqBars; i++)
        {
            var bar = FeelUI.Rect("Bar", eqRoot);
            bar.anchorMin = bar.anchorMax = Vector2.zero;
            bar.pivot = Vector2.zero;
            bar.anchoredPosition = new Vector2(i * (EqWidth + EqGap), 0f);
            bar.sizeDelta = new Vector2(EqWidth, EqLow);
            Fill(bar, KitStyle.Accent);
            eqBars[i] = bar;
        }
        var words = FeelUI.Rect("Title", line);
        words.anchorMin = Vector2.zero;
        words.anchorMax = Vector2.up;
        words.pivot = new Vector2(0f, .5f);
        words.anchoredPosition = new Vector2(EqBars * (EqWidth + EqGap) + 6f, 0f);
        words.sizeDelta = new Vector2(560f, 0f);
        now = KitStyle.Flat(words, "", 11f, TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        now.textWrappingMode = TextWrappingModes.NoWrap;
        now.fontStyle |= FontStyles.UpperCase;
        now.characterSpacing = 8f;
        now.richText = true;
    }
}
