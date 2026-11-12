using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// D-3, memo 25, from the third mockup round (the user's note: B overall, with A's hatched rails, and the assault's time
/// shown the way a music player shows a song): while a chapter's assault runs (a hold, or a finale's escape under its
/// song; CampaignRun.TryAssault), the objective bar turns into it. The ! tab flips over into a dark one hatched in pink,
/// a white edge sweeps along, and the strip becomes a pink tape on which 습격 중 runs without end between slashes, held at
/// both ends by dark caps, two thin hatched rails running opposite ways along its top and foot. Under it the time
/// plays like a track: time gone at the left, the line with its pink head walking along, the whole length at the right.
/// When it ends, a white edge cuts the tape away back to the left and the objective shows again. It sits on the bar
/// itself (MissionBarLook adds it), so it leans and follows with the HUD. Real time.
/// The time row is the bar's own row under it: MissionHUD's key cap and 목표·점수판 hint stood in the same place and the
/// time lay over them (user, 2026-11-12: "아니 TAB이랑 목표 점수판 저거랑 겹치는데?"), so the row tells how far under the
/// bar it reaches (Below) and what stands under the bar steps down out of its way while it is there.
/// </summary>
[DisallowMultipleComponent]
public sealed class AssaultBar : MonoBehaviour, IHudPreview
{
    // The ! tab's width as MissionBarLook lays it (Hud.csv Mission badgeSize.x), the caps', the rails', the slant.
    private const float TabWidth = 50f, CapWidth = 14f, RailHeight = 5f, Slant = 6f;
    private const float GrowSeconds = .32f, FlipSeconds = .22f, WipeSeconds = .34f, CutSeconds = .5f, RunSpeed = 86f, RailSpeed = 64f;
    // The time row: its middle this far under the bar's foot, its time labels this tall and wide, where the line starts
    // and the room before the whole length (px).
    private const float RowDrop = 22f, RowHeight = 18f, StampWidth = 48f, LineLeft = 54f, WholeGap = 8f;
    // The band's words (Texts.csv hud.assault.band) and the slashes between repeats: a cell's spaces at its ends are
    // trimmed on reading, so the gap stays here.
    private static string Words => Texts.Get("hud.assault.band") + "   ///   ";
    // What Tools → HUD → 겹침 검사 shows while it holds the bar in its assault (s).
    private const float PreviewElapsed = 37f, PreviewLength = 90f;

    private static Texture2D hatch;
    private RectTransform root, tape, track, head, wipe, cut, tab;
    private RectTransform[] rails;
    private RawImage[] railImages;
    private TextMeshProUGUI run, gone, whole;
    private Image trackFill;
    private float age = -1f, ending = -1f, runOffset;
    private bool on, previewing;

    /// <summary>
    /// How far under the bar's foot the time row reaches now (px), from the moment the tape starts to grow until the row
    /// has faded away with it; 0 while the bar is plain. Whatever stands under the bar keeps clear of this.
    /// </summary>
    public float Below => root != null && root.gameObject.activeSelf ? RowDrop + RowHeight * .5f : 0f;

    /// <summary>Tools → HUD → 겹침 검사: holds the bar in an assault (a 1:30 song at 0:37) while it measures.</summary>
    public void Preview(bool show) => previewing = show;

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        bool wanted = CampaignRun.TryAssault(out float elapsed, out float length);
        if (previewing && !wanted) { wanted = true; elapsed = PreviewElapsed; length = PreviewLength; }
        if (wanted && !on) Begin();
        else if (!wanted && on) End();
        if (root == null || !root.gameObject.activeSelf) return;
        float width = Mathf.Max(TabWidth + 60f, ((RectTransform)transform).rect.width);
        if (on) age += dt;
        else if ((ending += dt) >= CutSeconds) { root.gameObject.SetActive(false); return; }
        Pose(width, elapsed, length, dt);
    }

    private void Begin()
    {
        on = true;
        age = 0f;
        ending = -1f;
        if (root == null) Build();
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
    }

    private void End()
    {
        on = false;
        ending = 0f;
    }

    private void Pose(float width, float elapsed, float length, float dt)
    {
        float strip = width - TabWidth;
        float grow = on ? Ease(age / GrowSeconds) : 1f;
        float left = on ? 1f : 1f - Ease(ending / CutSeconds);
        float shown = strip * Mathf.Min(grow, left);
        // The tape grows out from the tab and, at the end, is cut back to it.
        tape.anchoredPosition = new Vector2(TabWidth, 0f);
        tape.sizeDelta = new Vector2(shown, tape.sizeDelta.y);
        runOffset = Mathf.Repeat(runOffset + dt * RunSpeed, 10000f);
        run.rectTransform.anchoredPosition = new Vector2(CapWidth + 4f - Mathf.Repeat(runOffset, Lap()), 0f);
        for (int i = 0; i < rails.Length; i++)
        {
            var uv = railImages[i].uvRect;
            uv.width = Mathf.Max(1f, shown / 18f);
            uv.x = (i == 0 ? -1f : 1f) * Time.unscaledTime * RailSpeed / 18f;
            railImages[i].uvRect = uv;
        }
        // The tab flips over, edge on, into the hatched one.
        float flip = on ? Mathf.Abs(Mathf.Cos(Mathf.Clamp01(age / FlipSeconds) * Mathf.PI)) : 1f;
        tab.localScale = new Vector3(Mathf.Max(.08f, flip), 1f, 1f);
        tab.gameObject.SetActive(on || left > .02f);
        // A white edge sweeps along as it comes, and stands at the cut as it goes.
        float sweep = on ? Mathf.Clamp01(age / (WipeSeconds * .75f)) : 0f;
        wipe.gameObject.SetActive(on && age < WipeSeconds);
        wipe.anchoredPosition = new Vector2(TabWidth + strip * sweep - 26f, 0f);
        wipe.GetComponent<Image>().color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01((age - WipeSeconds * .2f) / (WipeSeconds * .8f)));
        cut.gameObject.SetActive(!on);
        cut.anchoredPosition = new Vector2(TabWidth + shown, 0f);
        // The song's line: time gone, the head walking along, the whole length.
        float share = length > 0f ? Mathf.Clamp01(elapsed / length) : 0f;
        // The whole length's box ends just past the strip's end, under the count box.
        float trackWidth = Mathf.Max(40f, width - LineLeft - WholeGap - StampWidth + 2f);
        track.sizeDelta = new Vector2(trackWidth, 3f);
        trackFill.rectTransform.sizeDelta = new Vector2(trackWidth * share, 3f);
        head.anchoredPosition = new Vector2(trackWidth * share, 0f);
        gone.text = Clock(elapsed);
        whole.text = Clock(length);
        // The kit's label sits stretched in its own box: the box is what moves.
        ((RectTransform)whole.rectTransform.parent).anchoredPosition = new Vector2(LineLeft + trackWidth + WholeGap, 0f);
        float lineShow = on ? Ease((age - .1f) / GrowSeconds) : left;
        track.parent.GetComponent<CanvasGroup>().alpha = lineShow;
    }

    private float Lap() => Mathf.Max(80f, run.GetPreferredValues(Words).x);

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    private static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }

    private void Build()
    {
        var bar = (RectTransform)transform;
        root = FeelUI.Rect("Assault", bar);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.sizeDelta = Vector2.zero;
        float height = bar.rect.height;

        // The pink tape over the strip, clipped so the words run inside it.
        tape = FeelUI.Rect("Tape", root);
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
        wipe = FeelUI.Rect("Wipe", root);
        wipe.anchorMin = wipe.anchorMax = wipe.pivot = Vector2.zero;
        wipe.sizeDelta = new Vector2(52f, height);
        KitSkew.On(KitStyle.Fill(wipe, Color.white), Slant).GetComponent<Image>().raycastTarget = false;
        cut = FeelUI.Rect("Cut", root);
        cut.anchorMin = cut.anchorMax = cut.pivot = Vector2.zero;
        cut.sizeDelta = new Vector2(3f, height + 6f);
        KitSkew.On(KitStyle.Fill(cut, Color.white), Slant).GetComponent<Image>().raycastTarget = false;

        // The track under the bar, played like a song. Its own name, so the HUD overlap check (HudOverlap.csv) can tell
        // the row from the bar it hangs on.
        var line = FeelUI.Rect("Assault Time", root);
        line.anchorMin = line.anchorMax = line.pivot = Vector2.zero;
        line.anchoredPosition = new Vector2(0f, -RowDrop);
        line.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
        var font = GunFeelSettings.Instance != null ? GunFeelSettings.Instance.scoreFont : null;
        gone = Stamp("Gone", line, new Vector2(0f, 0f), TextAlignmentOptions.MidlineLeft, font);
        track = FeelUI.Rect("Line", line);
        track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, .5f);
        track.anchoredPosition = new Vector2(LineLeft, 0f);
        KitStyle.Fill(track, KitStyle.With(KitStyle.Ice, .22f)).raycastTarget = false;
        var fill = FeelUI.Rect("Played", track);
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, .5f);
        trackFill = KitStyle.Fill(fill, KitStyle.Accent);
        trackFill.raycastTarget = false;
        head = FeelUI.Rect("Head", track);
        head.anchorMin = head.anchorMax = new Vector2(0f, .5f);
        head.sizeDelta = new Vector2(9f, 9f);
        head.localRotation = Quaternion.Euler(0f, 0f, 45f);
        KitStyle.Fill(head, KitStyle.Accent).raycastTarget = false;
        whole = Stamp("Whole", line, new Vector2(LineLeft, 0f), TextAlignmentOptions.MidlineLeft, font);
        whole.color = KitStyle.Soft;
        root.gameObject.SetActive(false);
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

    private static TextMeshProUGUI Stamp(string label, RectTransform parent, Vector2 at, TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        var rect = FeelUI.Rect(label, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(StampWidth, RowHeight);
        var text = KitStyle.Flat(rect, "0:00", 14f, alignment, KitStyle.Ice);
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
