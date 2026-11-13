using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// D-3, memo 25 (third mockup round: B overall, with A's hatched rails, and the time shown the way a music player shows
/// a song), turned into PAYDAY 2's cycle (user, 2026-11-14: "노래가 하이라이트 부분으로 됐을 때 딱 UI도 더 간지나게
/// 바뀌면서"): the objective bar shows the fight's phase (CampaignRun.TryPhase). Under the bar runs the phase row: a small
/// slanted tag with the phase's word (Texts.csv hud.phase.*), then the song like a track in a music player, time gone at
/// the left, the line with the song's assaults marked in pink and a head walking along it, the whole length at the
/// right. In control the tag and head are calm white. In the anticipation the tag blinks pink and the bar's ! tab
/// flickers into the hatched assault tab, faster as the assault nears. As the assault breaks in, on the song's
/// highlight, the tag punches, the tab flips over into the dark one hatched in pink, a white edge sweeps along and the
/// strip becomes a pink tape on which 습격 중 runs without end between slashes, held at both ends by dark caps, two thin
/// hatched rails running opposite ways along its top and foot. When it ends, a white edge cuts the tape back to the left
/// and the objective shows again. A hold without a song is one assault its whole length. It sits on the bar itself
/// (MissionBarLook adds it), so it leans and follows with the HUD. Real time.
/// The row is the bar's own row under it: MissionHUD's key cap and 목표·점수판 hint stood in the same place and the
/// time lay over them (user, 2026-11-12: "아니 TAB이랑 목표 점수판 저거랑 겹치는데?"), so the row tells how far under the
/// bar it reaches (Below) and what stands under the bar steps down out of its way while it is there.
/// </summary>
[DisallowMultipleComponent]
public sealed class AssaultBar : MonoBehaviour, IHudPreview
{
    // The ! tab's width as MissionBarLook lays it (Hud.csv Mission badgeSize.x), the caps', the rails', the slant.
    private const float TabWidth = 50f, CapWidth = 14f, RailHeight = 5f, Slant = 6f;
    private const float GrowSeconds = .32f, FlipSeconds = .22f, WipeSeconds = .34f, CutSeconds = .5f, RunSpeed = 86f, RailSpeed = 64f;
    // The row: its middle this far under the bar's foot, its labels this tall and wide, where the line starts after the
    // time gone and the room before the whole length, the tag's padding, the gap after it and its slant (px).
    private const float RowDrop = 22f, RowHeight = 18f, StampWidth = 48f, LineLeft = 54f, WholeGap = 8f, TagPad = 16f, TagGap = 6f, TagSlant = 4f;
    // The line's and an assault mark's thickness (px); marks drawn at most.
    private const float LineHeight = 3f, MarkHeight = 5f;
    private const int MostMarks = 8;
    // The row fades in this fast (s); a new phase's tag grows this much for this long; the anticipation blinks this many
    // times a second as it begins and as the assault breaks in, over its last this many seconds.
    private const float RowFade = .3f, TagPunch = .3f, PunchSeconds = .3f, BlinkSlow = 1.5f, BlinkFast = 7f, BlinkSpan = 10f;
    // The band's words (Texts.csv hud.assault.band) and the slashes between repeats: a cell's spaces at its ends are
    // trimmed on reading, so the gap stays here.
    private static string Words => Texts.Get("hud.assault.band") + "   ///   ";
    // What ENFILADE → HUD → 겹침 검사 shows while it holds the bar in its assault: a 1:30 song at 0:37, its assault 0:20-1:00.
    private const float PreviewElapsed = 37f, PreviewLength = 90f;
    private static readonly float[] PreviewAssaults = { 20f, 60f };

    private static Texture2D hatch;
    private RectTransform root, band, tape, wipe, cut, tab, row, tagPlate, trackRow, track, played, head, goneBox, wholeBox;
    private RectTransform[] rails;
    private RawImage[] railImages;
    private readonly RectTransform[] marks = new RectTransform[MostMarks], marksPlayed = new RectTransform[MostMarks];
    private TextMeshProUGUI run, gone, whole, tag;
    private Image tagFill, headFill, wipeImage;
    private CanvasGroup rowGroup, tabGroup;
    private PhaseTrack last;
    private CombatPhase shown = CombatPhase.Quiet;
    private float age = -1f, ending = -1f, runOffset, rowShow, punchClock = -1f, blinkClock, tagWidth;
    private bool on, previewing, blinkOn;

    /// <summary>
    /// How far under the bar's foot the phase row reaches now (px), from the moment it starts to show until it has faded
    /// away; 0 while the bar is plain. Whatever stands under the bar keeps clear of this.
    /// </summary>
    public float Below => root != null && root.gameObject.activeSelf && rowShow > 0f ? RowDrop + RowHeight * .5f : 0f;

    /// <summary>ENFILADE → HUD → 겹침 검사: holds the bar in an assault (a 1:30 song at 0:37) while it measures.</summary>
    public void Preview(bool show) => previewing = show;

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        bool wanted = CampaignRun.TryPhase(out var now);
        if (previewing && !wanted)
        {
            wanted = true;
            now = new PhaseTrack { Phase = CombatPhase.Assault, Elapsed = PreviewElapsed, Length = PreviewLength,
                Until = PreviewAssaults[1] - PreviewElapsed, Assaults = PreviewAssaults };
        }
        if (wanted && root == null) Build();
        if (root == null) return;
        var phase = wanted ? now.Phase : CombatPhase.Quiet;
        if (wanted) last = now;
        if (phase != shown) Turn(phase);
        bool assault = phase == CombatPhase.Assault;
        if (assault && !on) Begin();
        else if (!assault && on) End();
        rowShow = Mathf.MoveTowards(rowShow, wanted ? 1f : 0f, dt / (wanted ? RowFade : CutSeconds));
        if (on) age += dt;
        else if (ending >= 0f) ending += dt;
        bool cutting = !on && ending >= 0f && ending < CutSeconds;
        root.gameObject.SetActive(rowShow > 0f || on || cutting);
        if (!root.gameObject.activeSelf) return;
        // The anticipation's blink, faster as the assault nears.
        float nearness = Mathf.Clamp01((float.IsInfinity(last.Until) ? BlinkSpan : last.Until) / BlinkSpan);
        blinkClock = Mathf.Repeat(blinkClock + dt * Mathf.Lerp(BlinkFast, BlinkSlow, nearness), 1f);
        blinkOn = blinkClock < .5f;
        float width = Mathf.Max(TabWidth + 60f, ((RectTransform)transform).rect.width);
        PoseTape(width, cutting, dt);
        PoseRow(width, dt);
    }

    // A new phase: its word on the tag, which punches and flashes white as it comes.
    private void Turn(CombatPhase phase)
    {
        shown = phase;
        if (phase == CombatPhase.Quiet) return;
        tag.text = Texts.Get(TagId(phase));
        punchClock = 0f;
    }

    private static string TagId(CombatPhase phase) => phase == CombatPhase.Assault ? "hud.phase.assault"
        : phase == CombatPhase.Anticipation ? "hud.phase.anticipation" : "hud.phase.control";

    private void Begin()
    {
        on = true;
        age = 0f;
        ending = -1f;
        root.SetAsLastSibling();
    }

    private void End()
    {
        on = false;
        ending = 0f;
    }

    private void PoseTape(float width, bool cutting, float dt)
    {
        float strip = width - TabWidth;
        float grow = on ? Ease.Out(age / GrowSeconds) : 1f;
        float left = on ? 1f : cutting ? 1f - Ease.Out(ending / CutSeconds) : 0f;
        // The tab flips over, edge on, into the hatched one, and goes with the tape; in the anticipation it flickers in.
        float flip = on ? Mathf.Abs(Mathf.Cos(Mathf.Clamp01(age / FlipSeconds) * Mathf.PI)) : 1f;
        tab.localScale = new Vector3(Mathf.Max(.08f, flip), 1f, 1f);
        tabGroup.alpha = on ? 1f : cutting ? left : shown == CombatPhase.Anticipation && blinkOn ? .75f : 0f;
        band.gameObject.SetActive(on || cutting);
        if (!band.gameObject.activeSelf) return;
        float shownWidth = strip * Mathf.Min(grow, left);
        // The tape grows out from the tab and, at the end, is cut back to it.
        tape.anchoredPosition = new Vector2(TabWidth, 0f);
        tape.sizeDelta = new Vector2(shownWidth, tape.sizeDelta.y);
        runOffset = Mathf.Repeat(runOffset + dt * RunSpeed, 10000f);
        run.rectTransform.anchoredPosition = new Vector2(CapWidth + 4f - Mathf.Repeat(runOffset, Lap()), 0f);
        for (int i = 0; i < rails.Length; i++)
        {
            var uv = railImages[i].uvRect;
            uv.width = Mathf.Max(1f, shownWidth / 18f);
            uv.x = (i == 0 ? -1f : 1f) * Time.unscaledTime * RailSpeed / 18f;
            railImages[i].uvRect = uv;
        }
        // A white edge sweeps along as it comes, and stands at the cut as it goes.
        float sweep = on ? Mathf.Clamp01(age / (WipeSeconds * .75f)) : 0f;
        wipe.gameObject.SetActive(on && age < WipeSeconds);
        wipe.anchoredPosition = new Vector2(TabWidth + strip * sweep - 26f, 0f);
        wipeImage.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01((age - WipeSeconds * .2f) / (WipeSeconds * .8f)));
        cut.gameObject.SetActive(!on);
        cut.anchoredPosition = new Vector2(TabWidth + shownWidth, 0f);
    }

    private void PoseRow(float width, float dt)
    {
        rowGroup.alpha = Ease.Out(rowShow);
        if (rowShow <= 0f) return;
        // The tag: calm white in control, blinking pink in the anticipation, solid pink in the assault; white for a
        // moment as a phase begins.
        float punch = 0f;
        if (punchClock >= 0f)
        {
            punchClock += dt;
            float t = Mathf.Clamp01(punchClock / PunchSeconds);
            punch = (1f - t) * (1f - t);
            if (t >= 1f) punchClock = -1f;
        }
        tagPlate.localScale = Vector3.one * (1f + TagPunch * punch);
        if (shown != CombatPhase.Quiet)
        {
            var plate = shown == CombatPhase.Control ? KitStyle.With(KitStyle.Ice, .16f)
                : shown == CombatPhase.Anticipation ? KitStyle.With(KitStyle.Accent, blinkOn ? 1f : .4f) : KitStyle.Accent;
            tagFill.color = Color.Lerp(plate, Color.white, punch);
            tag.color = shown == CombatPhase.Control && punch < .5f ? KitStyle.Ice : KitStyle.Ink;
            headFill.color = shown == CombatPhase.Control ? KitStyle.Ice : KitStyle.Accent;
        }
        // The song like a music player's: time gone, the line with its assaults in pink and the head walking along it,
        // the whole length. No length (between two songs, a song without its file): the tag alone.
        float length = last.Length;
        trackRow.gameObject.SetActive(length > 0f);
        if (length <= 0f) return;
        float goneLeft = tagWidth + TagGap, lineLeft = goneLeft + LineLeft;
        float trackWidth = Mathf.Max(40f, width - lineLeft - WholeGap - StampWidth + 2f);
        float headX = trackWidth * Mathf.Clamp01(last.Elapsed / length);
        track.anchoredPosition = new Vector2(lineLeft, 0f);
        track.sizeDelta = new Vector2(trackWidth, LineHeight);
        played.sizeDelta = new Vector2(headX, LineHeight);
        var spans = last.Assaults ?? System.Array.Empty<float>();
        for (int i = 0; i < MostMarks; i++)
        {
            bool used = i * 2 + 1 < spans.Length && spans[i * 2] < length;
            marks[i].gameObject.SetActive(used);
            if (!used) continue;
            float from = trackWidth * spans[i * 2] / length, to = trackWidth * Mathf.Min(spans[i * 2 + 1], length) / length;
            marks[i].anchoredPosition = new Vector2(from, 0f);
            marks[i].sizeDelta = new Vector2(Mathf.Max(2f, to - from), MarkHeight);
            marksPlayed[i].sizeDelta = new Vector2(Mathf.Clamp(headX - from, 0f, to - from), MarkHeight);
        }
        head.anchoredPosition = new Vector2(headX, 0f);
        gone.text = DeathCard.Clock(last.Elapsed);
        whole.text = DeathCard.Clock(length);
        goneBox.anchoredPosition = new Vector2(goneLeft, 0f);
        wholeBox.anchoredPosition = new Vector2(lineLeft + trackWidth + WholeGap, 0f);
    }

    private float Lap() => Mathf.Max(80f, run.GetPreferredValues(Words).x);

    private void Build()
    {
        var bar = (RectTransform)transform;
        root = FeelUI.Rect("Assault", bar);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.sizeDelta = Vector2.zero;
        float height = bar.rect.height;
        band = FeelUI.Rect("Band", root);
        band.anchorMin = band.anchorMax = band.pivot = Vector2.zero;

        // The pink tape over the strip, clipped so the words run inside it.
        tape = FeelUI.Rect("Tape", band);
        tape.anchorMin = tape.anchorMax = tape.pivot = Vector2.zero;
        tape.sizeDelta = new Vector2(0f, height);
        KitSkew.On(KitStyle.Fill(tape, KitStyle.Accent), Slant);
        tape.gameObject.AddComponent<RectMask2D>();
        var words = FeelUI.Rect("Run", tape);
        words.anchorMin = words.anchorMax = words.pivot = new Vector2(0f, .5f);
        words.sizeDelta = new Vector2(4000f, height);
        run = KitStyle.Flat(words, string.Concat(System.Linq.Enumerable.Repeat(Words, 12)), 21f, TextAlignmentOptions.MidlineLeft, KitStyle.Ink);
        run.textWrappingMode = TextWrappingModes.NoWrap;
        rails = new RectTransform[2];
        railImages = new RawImage[2];
        for (int i = 0; i < 2; i++)
        {
            var rail = FeelUI.Rect(i == 0 ? "Top rail" : "Foot rail", tape);
            rail.anchorMin = new Vector2(0f, i == 0 ? 1f : 0f);
            rail.anchorMax = new Vector2(1f, i == 0 ? 1f : 0f);
            rail.pivot = new Vector2(.5f, i == 0 ? 1f : 0f);
            rail.sizeDelta = new Vector2(0f, RailHeight);
            var image = rail.gameObject.AddComponent<RawImage>();
            image.texture = Hatch();
            image.color = KitStyle.Ink;
            image.raycastTarget = false;
            rails[i] = rail;
            railImages[i] = image;
        }
        Cap("Cap left", tape, new Vector2(0f, 0f));
        Cap("Cap right", tape, new Vector2(1f, 0f));

        // The tab, dark and hatched in pink, flipped over the ! one.
        tab = FeelUI.Rect("Tab", root);
        tab.anchorMin = tab.anchorMax = Vector2.zero;
        tab.pivot = new Vector2(.5f, 0f);
        tab.anchoredPosition = new Vector2(TabWidth * .5f, 0f);
        tab.sizeDelta = new Vector2(TabWidth, 54f);
        tabGroup = tab.gameObject.AddComponent<CanvasGroup>();
        KitStyle.Fill(tab, KitStyle.Ink).raycastTarget = false;
        var stripes = FeelUI.Rect("Hatch", tab);
        stripes.anchorMin = new Vector2(.12f, .14f);
        stripes.anchorMax = new Vector2(.88f, .86f);
        stripes.sizeDelta = Vector2.zero;
        var hatched = stripes.gameObject.AddComponent<RawImage>();
        hatched.texture = Hatch();
        hatched.color = KitStyle.Accent;
        hatched.uvRect = new Rect(0f, 0f, 2f, 5f);
        hatched.raycastTarget = false;

        // The white edge that sweeps in, and the one that stands at the cut going out.
        wipe = FeelUI.Rect("Wipe", band);
        wipe.anchorMin = wipe.anchorMax = wipe.pivot = Vector2.zero;
        wipe.sizeDelta = new Vector2(52f, height);
        wipeImage = KitStyle.Fill(wipe, Color.white);
        wipeImage.raycastTarget = false;
        KitSkew.On(wipeImage, Slant);
        cut = FeelUI.Rect("Cut", band);
        cut.anchorMin = cut.anchorMax = cut.pivot = Vector2.zero;
        cut.sizeDelta = new Vector2(3f, height + 6f);
        KitSkew.On(KitStyle.Fill(cut, Color.white), Slant).GetComponent<Image>().raycastTarget = false;

        // The phase row under the bar. Its own name, so the HUD overlap check (HudOverlap.csv) can tell the row from
        // the bar it hangs on.
        row = FeelUI.Rect("Assault Time", root);
        row.anchorMin = row.anchorMax = row.pivot = Vector2.zero;
        row.anchoredPosition = new Vector2(0f, -RowDrop);
        rowGroup = row.gameObject.AddComponent<CanvasGroup>();
        rowGroup.alpha = 0f;
        tagPlate = FeelUI.Rect("Tag", row);
        tagPlate.anchorMin = tagPlate.anchorMax = tagPlate.pivot = new Vector2(0f, .5f);
        tagFill = KitStyle.Fill(tagPlate, KitStyle.With(KitStyle.Ice, .16f));
        tagFill.raycastTarget = false;
        KitSkew.On(tagFill, TagSlant);
        tag = KitStyle.Flat(tagPlate, "", 13f, TextAlignmentOptions.Center, KitStyle.Ice);
        tag.textWrappingMode = TextWrappingModes.NoWrap;
        // One width for every phase's word, so the line does not jump as the phase turns.
        tagWidth = 0f;
        foreach (var phase in new[] { CombatPhase.Control, CombatPhase.Anticipation, CombatPhase.Assault })
            tagWidth = Mathf.Max(tagWidth, tag.GetPreferredValues(Texts.Get(TagId(phase))).x);
        tagWidth += TagPad;
        tagPlate.sizeDelta = new Vector2(tagWidth, RowHeight);

        trackRow = FeelUI.Rect("Track", row);
        trackRow.anchorMin = trackRow.anchorMax = trackRow.pivot = Vector2.zero;
        var font = GunFeelSettings.Instance != null ? GunFeelSettings.Instance.scoreFont : null;
        gone = Stamp("Gone", trackRow, font, out goneBox);
        track = FeelUI.Rect("Line", trackRow);
        track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, .5f);
        KitStyle.Fill(track, KitStyle.With(KitStyle.Ice, .18f)).raycastTarget = false;
        played = Strip("Played", track, KitStyle.With(KitStyle.Ice, .55f));
        for (int i = 0; i < MostMarks; i++)
        {
            marks[i] = Strip("Assault " + (i + 1), track, KitStyle.With(KitStyle.Accent, .42f));
            marksPlayed[i] = Strip("Played", marks[i], KitStyle.Accent);
        }
        head = FeelUI.Rect("Head", track);
        head.anchorMin = head.anchorMax = new Vector2(0f, .5f);
        head.sizeDelta = new Vector2(9f, 9f);
        head.localRotation = Quaternion.Euler(0f, 0f, 45f);
        headFill = KitStyle.Fill(head, KitStyle.Ice);
        headFill.raycastTarget = false;
        whole = Stamp("Whole", trackRow, font, out wholeBox);
        whole.color = KitStyle.Soft;
        root.gameObject.SetActive(false);
    }

    // A thin bar along the line from its left end, as long as it is posed.
    private static RectTransform Strip(string label, RectTransform parent, Color color)
    {
        var strip = FeelUI.Rect(label, parent);
        strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0f, .5f);
        KitStyle.Fill(strip, color).raycastTarget = false;
        return strip;
    }

    private static void Cap(string label, RectTransform parent, Vector2 anchor)
    {
        var cap = FeelUI.Rect(label, parent);
        cap.anchorMin = new Vector2(anchor.x, 0f);
        cap.anchorMax = new Vector2(anchor.x, 1f);
        cap.pivot = new Vector2(anchor.x, .5f);
        cap.sizeDelta = new Vector2(CapWidth, 0f);
        KitSkew.On(KitStyle.Fill(cap, KitStyle.Ink), Slant).GetComponent<Image>().raycastTarget = false;
    }

    // A time label in its own box: the box is what moves (the kit's label sits stretched in it).
    private static TextMeshProUGUI Stamp(string label, RectTransform parent, TMP_FontAsset font, out RectTransform box)
    {
        box = FeelUI.Rect(label, parent);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, .5f);
        box.sizeDelta = new Vector2(StampWidth, RowHeight);
        var text = KitStyle.Flat(box, "0:00", 14f, TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        if (font != null) text.font = font;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    // Diagonal stripes, one to a tile, repeated along the rails and over the tab.
    private static Texture2D Hatch()
    {
        if (hatch != null) return hatch;
        const int width = 18, height = 6;
        hatch = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Assault Hatch (Runtime)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float along = Mathf.Repeat(x + y, width);
            pixels[y * width + x] = along < width * .5f ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
        hatch.SetPixels32(pixels);
        hatch.Apply(false, true);
        return hatch;
    }
}
