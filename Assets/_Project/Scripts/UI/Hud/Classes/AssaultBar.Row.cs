using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// AssaultBar: the phase row under the bar: the phase on a small slanted plate, the song's time, a line of the song with its assaults in pink, and its whole length. One part of the class; the rest is in AssaultBar.cs and the files beside it.
public sealed partial class AssaultBar
{
    // The row (px): its plate's top this far under the bar's foot, its height, lean, ink-and-pink edge, padding either side
    // and the room between its two words, the gap after it; the played time's width; the line's thickness, its assaults'
    // height, the head's size, ink edge and glow, the gap before the whole length and its width; how far down the row's
    // foot reaches.
    private const float RowDrop = 13f, PlateHeight = 21f, PlateSlant = 5f, PlateEdge = 1.5f, PlatePadLeft = 9f, PlatePadRight = 10f,
        WordGap = 6f, PlateGap = 12f, TimeWidth = 40f, LineThick = 3f, MarkHeight = 7f, HeadSize = 11f, HeadEdge = 2f,
        HeadGlow = 26f, WholeGap = 8f, WholeWidth = 36f, RowBottom = 36f, Middle = -(RowDrop + PlateHeight * .5f);
    // Its words (px): the phase's size, the English's size and spacing, the times' size.
    private const float WordSize = 13f, EnglishSize = 8f, EnglishSpacing = 18f, TimeSize = 13f;
    // The row fades in this fast (s); a new phase's plate pops this much bigger and settles over this long (s).
    private const float RowFade = .3f, TurnPunch = .4f, PunchSeconds = .34f;
    private static readonly Color Track = new Color(1f, .96f, .97f, .18f), Played = new Color(1f, .96f, .97f, .6f),
        Window = new Color(1f, .31f, .47f, .38f);

    private RectTransform row, plate, wordRect, englishRect, timeRect, line, playedRect, marksRoot, head, headGlow, wholeRect;
    private readonly List<RectTransform> marks = new List<RectTransform>(), markFills = new List<RectTransform>();
    private Image plateEdge, plateFace, headImage;
    private CanvasGroup rowGroup, headGlowGroup;
    private TextMeshProUGUI word, english, timeText, wholeText;
    private float rowShow, punchClock = -1f, plateWidth;
    private int timeShown = -1, wholeShown = -1;

    // A new phase: the plate's words and colours (the rest's blue, the anticipation's pink, the assault's ink edged in
    // pink), and a pop.
    private void TurnRow(CombatPhase phase)
    {
        if (phase == CombatPhase.Quiet) return;
        string id = phase == CombatPhase.Assault ? "hud.phase.assault" : phase == CombatPhase.Anticipation ? "hud.phase.anticipation" : "hud.phase.control";
        word.text = Texts.Get(id);
        english.text = Texts.Get(id + ".en");
        float wordWidth = word.GetPreferredValues(word.text).x, englishWidth = english.GetPreferredValues(english.text).x;
        plateWidth = PlatePadLeft + wordWidth + WordGap + englishWidth + PlatePadRight;
        plate.sizeDelta = new Vector2(plateWidth, PlateHeight);
        wordRect.anchoredPosition = new Vector2(PlatePadLeft, 0f);
        wordRect.sizeDelta = new Vector2(wordWidth + 2f, PlateHeight);
        englishRect.anchoredPosition = new Vector2(PlatePadLeft + wordWidth + WordGap, 0f);
        englishRect.sizeDelta = new Vector2(englishWidth + 2f, PlateHeight);
        bool rest = phase == CombatPhase.Control, assault = phase == CombatPhase.Assault;
        var fill = rest ? RestBlue : assault ? KitStyle.Ink : KitStyle.Accent;
        plateFace.color = fill;
        plateEdge.color = assault ? KitStyle.Accent : fill;
        var words = assault ? KitStyle.Accent : Color.white;
        word.color = words;
        english.color = KitStyle.With(words, rest ? .8f : .6f);
        headImage.color = rest ? RestLight : KitStyle.Accent;
        punchClock = 0f;
    }

    private void PoseRow(float width, float dt)
    {
        rowGroup.alpha = Ease.Out(rowShow);
        if (rowShow <= 0f) return;
        // The anticipation's plate breathes (never a blink); a new phase's pops and settles.
        if (shown == CombatPhase.Anticipation) plateFace.color = plateEdge.color = KitStyle.With(KitStyle.Accent, .45f + .55f * pulse);
        float punch = 0f;
        if (punchClock >= 0f)
        {
            punchClock += dt;
            float k = Mathf.Clamp01(punchClock / PunchSeconds);
            punch = 1f - KitMotion.BackOut(k);
            if (k >= 1f) punchClock = -1f;
        }
        plate.localScale = Vector3.one * (1f + TurnPunch * punch);

        // The song: its time, its line with its assaults and the part played lit, its whole length, reaching as far right
        // as the bar's own parts (its count box and the line's tail, MissionBarLook.Reach).
        bool has = last.Length > 0f && !float.IsInfinity(last.Length);
        timeRect.gameObject.SetActive(has);
        line.gameObject.SetActive(has);
        wholeRect.gameObject.SetActive(has);
        if (!has) return;
        float right = look != null ? Mathf.Max(width, look.Reach) : width;
        float timeLeft = plateWidth + PlateGap, lineLeft = timeLeft + TimeWidth;
        float lineWidth = Mathf.Max(40f, right - lineLeft - WholeGap - WholeWidth);
        timeRect.anchoredPosition = new Vector2(timeLeft, Middle);
        line.anchoredPosition = new Vector2(lineLeft, Middle);
        line.sizeDelta = new Vector2(lineWidth, LineThick);
        wholeRect.anchoredPosition = new Vector2(lineLeft + lineWidth + WholeGap, Middle);
        int seconds = Mathf.FloorToInt(Mathf.Clamp(last.Elapsed, 0f, last.Length));
        if (seconds != timeShown)
        {
            timeShown = seconds;
            timeText.text = DeathCard.Clock(seconds);
        }
        int whole = Mathf.FloorToInt(last.Length);
        if (whole != wholeShown)
        {
            wholeShown = whole;
            wholeText.text = DeathCard.Clock(whole);
        }
        float headX = lineWidth * Mathf.Clamp01(last.Elapsed / last.Length);
        playedRect.sizeDelta = new Vector2(headX, LineThick);
        head.anchoredPosition = headGlow.anchoredPosition = new Vector2(headX, 0f);
        headGlowGroup.alpha = shown == CombatPhase.Anticipation || shown == CombatPhase.Assault ? 1f : 0f;
        LayMarks(lineWidth, headX);
    }

    // The song's assaults along the line, each lit as far as it is played.
    private void LayMarks(float lineWidth, float headX)
    {
        var spans = last.Assaults;
        int windows = spans != null ? spans.Length / 2 : 0;
        while (marks.Count < windows) AddMark();
        for (int i = 0; i < marks.Count; i++)
        {
            bool on = i < windows;
            marks[i].gameObject.SetActive(on);
            if (!on) continue;
            float from = lineWidth * Mathf.Clamp01(spans[i * 2] / last.Length), to = lineWidth * Mathf.Clamp01(spans[i * 2 + 1] / last.Length);
            marks[i].anchoredPosition = new Vector2(from, 0f);
            marks[i].sizeDelta = new Vector2(Mathf.Max(2f, to - from), MarkHeight);
            markFills[i].sizeDelta = new Vector2(Mathf.Clamp(headX - from, 0f, to - from), 0f);
        }
    }

    private void AddMark()
    {
        var mark = FeelUI.Rect("Assault", marksRoot);
        mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(0f, .5f);
        KitStyle.Fill(mark, Window);
        var lit = FeelUI.Rect("Played", mark);
        lit.anchorMin = Vector2.zero;
        lit.anchorMax = Vector2.up;
        lit.pivot = new Vector2(0f, .5f);
        KitStyle.Fill(lit, KitStyle.Accent);
        marks.Add(mark);
        markFills.Add(lit);
    }

    private void BuildRow()
    {
        // Its own name, so the HUD overlap check (HudOverlap.csv) can tell the row from the bar it hangs on.
        row = Corner("Assault Time", root);
        rowGroup = row.gameObject.AddComponent<CanvasGroup>();
        rowGroup.alpha = 0f;
        // The plate: its edge (pink round the assault's ink) and face, leaning, the phase and its English beside it.
        plate = Corner("Phase", row);
        plate.pivot = new Vector2(0f, .5f);
        plate.anchoredPosition = new Vector2(0f, Middle);
        plateEdge = KitStyle.Fill(Around("Edge", plate, 0f, 0f), RestBlue);
        KitSkew.On(plateEdge, PlateSlant);
        plateFace = KitStyle.Fill(Around("Face", plate, -PlateEdge, -PlateEdge), RestBlue);
        KitSkew.On(plateFace, PlateSlant);
        wordRect = PlateWords(plate, "Word");
        word = KitStyle.Flat(wordRect, "", WordSize, TextAlignmentOptions.MidlineLeft, Color.white);
        word.textWrappingMode = TextWrappingModes.NoWrap;
        englishRect = PlateWords(plate, "English");
        english = KitStyle.Flat(englishRect, "", EnglishSize, TextAlignmentOptions.MidlineLeft, Color.white);
        english.characterSpacing = EnglishSpacing;
        english.textWrappingMode = TextWrappingModes.NoWrap;

        // The song's time, the line of it with its assaults and the head where it is, and its whole length.
        timeRect = RowWords("Time", KitStyle.Ice, out timeText);
        wholeRect = RowWords("Whole", KitStyle.Soft, out wholeText);
        line = Corner("Line", row);
        line.pivot = new Vector2(0f, .5f);
        KitStyle.Fill(line, Track);
        playedRect = FeelUI.Rect("Played", line);
        playedRect.anchorMin = playedRect.anchorMax = playedRect.pivot = new Vector2(0f, .5f);
        KitStyle.Fill(playedRect, Played);
        marksRoot = FeelUI.Rect("Assaults", line);
        marksRoot.anchorMin = marksRoot.anchorMax = marksRoot.pivot = new Vector2(0f, .5f);
        marksRoot.sizeDelta = Vector2.zero;
        headGlow = FeelUI.Rect("Glow", line);
        headGlow.anchorMin = headGlow.anchorMax = new Vector2(0f, .5f);
        headGlow.pivot = new Vector2(.5f, .5f);
        headGlow.sizeDelta = new Vector2(HeadGlow, HeadGlow);
        headGlowGroup = headGlow.gameObject.AddComponent<CanvasGroup>();
        KitStyle.Raw(headGlow, KitShapes.Glow, KitStyle.With(KitStyle.Accent, .7f));
        head = FeelUI.Rect("Head", line);
        head.anchorMin = head.anchorMax = new Vector2(0f, .5f);
        head.pivot = new Vector2(.5f, .5f);
        head.sizeDelta = new Vector2(HeadSize, HeadSize);
        head.localRotation = Quaternion.Euler(0f, 0f, 45f);
        KitStyle.Fill(head, KitStyle.Ink);
        headImage = KitStyle.Fill(Around("Fill", head, -HeadEdge, -HeadEdge), Color.white);
    }

    // One of the plate's two words, from its left, its middle on the plate's.
    private static RectTransform PlateWords(RectTransform plate, string label)
    {
        var rect = FeelUI.Rect(label, plate);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, .5f);
        return rect;
    }

    // One of the row's times, from its left, its middle on the row's.
    private RectTransform RowWords(string label, Color color, out TextMeshProUGUI text)
    {
        var rect = Corner(label, row);
        rect.pivot = new Vector2(0f, .5f);
        rect.sizeDelta = new Vector2(TimeWidth + 20f, PlateHeight);
        text = KitStyle.Flat(rect, "", TimeSize, TextAlignmentOptions.MidlineLeft, color);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return rect;
    }
}
