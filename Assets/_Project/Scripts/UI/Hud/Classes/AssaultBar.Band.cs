using TMPro;
using UnityEngine;
using UnityEngine.UI;

// AssaultBar: the sticker band over the strip, pink through the assault and blue for a moment after it, the rest's. One part of the class; the rest is in AssaultBar.cs and the files beside it.
public sealed partial class AssaultBar
{
    private enum BandMode { Assault, Rest }

    // Where it sits (px): from this far right of the bar's left edge, this far over and under the strip, to this far past
    // the strip's end (or this far short of it when the count box hangs there), its slant; the white edge round it and
    // the ink shadow's drop; the room round it, and over it for the stamp, that its wipe clips within.
    private const float BandLeft = 46f, BandOver = 9f, BandPast = 6f, BandShort = 4f, BandSlant = 12f, EdgeAcross = 4f,
        EdgeUp = 3f, ShadowDrop = 7f, ClipRoom = 16f, ClipAbove = 44f;
    // Its words (px): the title from this far in, its size, ink edge (TMP outline width) and shadow; the stamp from this
    // far in over the top edge, its size, padding, height, spacing, lean (degrees) and shadow; the ticker along the foot,
    // its lift, height, size and spacing, and its pace (px/s), the room between its runs.
    private const float TitleLeft = 26f, TitleSize = 27f, TitleEdge = .28f, TitleShadow = 4f, StampLeft = 16f, StampSize = 11f,
        StampPad = 15f, StampHeight = 17f, StampSpacing = 26f, StampTilt = 7f, StampShadow = 3f, TickerLift = 4f,
        TickerHeight = 13f, TickerSize = 9.5f, TickerSpacing = 14f, TickerSpeed = 45f;
    private const string TickerGap = "   ";
    // Its face: the streaks' drift (px/s) and strength; the halftone's share of the right end, and its strength.
    private const float StreakSpeed = 37f, StreakAlpha = .2f, DotShare = .6f, DotAlpha = .3f;
    // Timing (s): it bounces in from this far left (px) over this long; one light passes at and for, this wide (px); the
    // title's letters land from, a step apart, each over, from this size; the stamp lands at, over, from this lean and
    // size; the ticker unrolls at, over; it wipes away over; the rest's band comes this long after the assault's began to
    // go, and stays this long. The first assault's band once flashed a sheen every 2.6 s and its streaks raced; both went
    // ("너무 튀는 거 빼면").
    private const float InFrom = 60f, InSeconds = .42f, SheenAt = .45f, SheenSeconds = .8f, SheenWidth = 70f, LettersAt = .26f,
        LetterStep = .07f, LetterSeconds = .34f, LetterFrom = 2.1f, StampAt = .5f, StampSeconds = .36f, StampFromTilt = 38f,
        StampFrom = 2.4f, TickerAt = .55f, TickerSeconds = .3f, WipeSeconds = .4f, RestDelay = .45f, RestHold = 2.6f;
    // The slab's top and foot (its middle is the HUD's pink, or the rest's blue).
    private static readonly Color AssaultTop = new Color(1f, .427f, .569f), AssaultFoot = new Color(.941f, .212f, .373f),
        RestTop = new Color(.525f, .69f, 1f), RestFoot = new Color(.165f, .373f, .839f);
    // The rest's blue: its band, the emblem's gauge, the row's plate.
    private static readonly Color RestBlue = new Color(.29f, .525f, 1f);

    private RectTransform bandClip, body, sheen, tickerPlate, tickerRunRect, stamp;
    private RectMask2D bandMask;
    private CanvasGroup bandGroup, stampGroup;
    private Image slabImage;
    private RawImage lightImage, shadeImage, streakImage, dotImage;
    private TextMeshProUGUI bandTitle, bandShade, tickerRun, stampText;
    private BandMode mode, tickerMode;
    private bool bandOn, tickerSiren, tickerSet;
    private float bandAge, wipe = -1f, restWait = -1f, tickerX, tickerLoop = 1f;
    private string tickerTitle;

    private bool BandShowing => bandOn || wipe >= 0f;

    // The phase turns: an assault opens the pink band (straight over the rest's); anything else wipes the band away, and
    // the rest straight after an assault brings its blue band a moment later.
    private void TurnBand(CombatPhase from, CombatPhase phase)
    {
        restWait = -1f;
        if (phase == CombatPhase.Assault) { OpenBand(BandMode.Assault); return; }
        CloseBand();
        if (from == CombatPhase.Assault && phase == CombatPhase.Control) restWait = RestDelay;
    }

    private void StepBand(CombatPhase phase, float dt)
    {
        if (bandOn) bandAge += dt;
        if (wipe >= 0f && (wipe += dt) >= WipeSeconds) wipe = -1f;
        if (restWait >= 0f)
        {
            restWait = Mathf.Max(0f, restWait - dt);
            if (restWait <= 0f && wipe < 0f)
            {
                restWait = -1f;
                if (phase == CombatPhase.Control) OpenBand(BandMode.Rest);
            }
        }
        if (bandOn && mode == BandMode.Rest && bandAge >= RestHold) CloseBand();
    }

    private void OpenBand(BandMode with)
    {
        mode = with;
        bandOn = true;
        bandAge = 0f;
        wipe = -1f;
        tickerX = 0f;
        root.SetAsLastSibling();
        bool rest = with == BandMode.Rest;
        slabImage.color = rest ? RestBlue : KitStyle.Accent;
        lightImage.color = rest ? RestTop : AssaultTop;
        shadeImage.color = rest ? RestFoot : AssaultFoot;
        bandTitle.text = bandShade.text = Texts.Get(rest ? "hud.rest.title" : "hud.assault.title");
        stampText.text = Texts.Get(rest ? "hud.rest.stamp" : "hud.assault.stamp");
        stamp.sizeDelta = new Vector2(stampText.GetPreferredValues(stampText.text).x + StampPad, StampHeight);
        stamp.anchoredPosition = new Vector2(StampLeft + stamp.sizeDelta.x * .5f, StampHeight * .5f);
    }

    private void CloseBand()
    {
        if (!bandOn) return;
        bandOn = false;
        wipe = 0f;
    }

    private void PoseBand(float width, float dt)
    {
        bandClip.gameObject.SetActive(BandShowing);
        if (!BandShowing) return;
        // It reaches past the strip's end, or stops short of the count box hanging there. Its lean stays inside that
        // span, the way the drafts drew it.
        float right = look != null && look.CountWidth > 0f ? width - BandShort : width + BandPast;
        float span = Mathf.Max(80f, right - BandLeft);
        bandClip.anchoredPosition = new Vector2(BandLeft - ClipRoom, -BandOver - ClipRoom);
        bandClip.sizeDelta = new Vector2(span + ClipRoom * 2f, TabHeight + ClipRoom + ClipAbove);
        // It bounces in from the left out of a sliver; wiping, it stands as it was while its clip sweeps it away from the
        // left.
        float t = bandOn ? bandAge : float.PositiveInfinity;
        float grow = KitMotion.BackOut(t / InSeconds);
        body.anchoredPosition = new Vector2(ClipRoom + BandSlant * .5f + Mathf.LerpUnclamped(-InFrom, 0f, grow), ClipRoom + TabHeight * .5f);
        body.sizeDelta = new Vector2(span - BandSlant, TabHeight);
        body.localScale = new Vector3(Mathf.LerpUnclamped(.1f, 1f, grow), 1f, 1f);
        bandGroup.alpha = Mathf.Clamp01(t / (InSeconds * .6f));
        bandMask.enabled = wipe >= 0f;
        if (wipe >= 0f)
        {
            float k = Mathf.Clamp01(wipe / WipeSeconds);
            bandMask.padding = new Vector4(Mathf.Lerp(ClipRoom - EdgeAcross - BandSlant, span + ClipRoom * 2f, k * k * k), 0f, 0f, 0f);
        }
        // The title's letters land one by one, their shadow with them.
        if (bandOn)
        {
            float letters = bandAge - LettersAt;
            if (letters < KitMotion.LettersSeconds(bandTitle.text.Length, LetterStep, LetterSeconds) + .05f)
            {
                KitMotion.LandLetters(bandTitle, letters, LetterStep, LetterSeconds, LetterFrom);
                KitMotion.LandLetters(bandShade, letters, LetterStep, LetterSeconds, LetterFrom);
            }
        }
        // One light passes as it lands.
        float pass = (t - SheenAt) / SheenSeconds;
        bool passing = pass > 0f && pass < 1f;
        sheen.gameObject.SetActive(passing);
        if (passing) sheen.anchoredPosition = new Vector2(Mathf.Lerp(-SheenWidth, span + SheenWidth, Ease.InOut(pass)), 0f);
        // The stamp lands, swinging back to its lean.
        float landed = KitMotion.BackOut((t - StampAt) / StampSeconds);
        stamp.localScale = Vector3.one * Mathf.LerpUnclamped(StampFrom, 1f, landed);
        stamp.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(StampFromTilt, StampTilt, landed));
        stampGroup.alpha = Mathf.Clamp01((t - StampAt) / StampSeconds * 3f);
        // The ticker unrolls along the foot and its words run; the face's streaks drift slowly.
        tickerPlate.localScale = new Vector3(Ease.Out((t - TickerAt) / TickerSeconds), 1f, 1f);
        RunTicker(dt);
        var uv = streakImage.uvRect;
        uv.width = span / 128f;
        uv.height = TabHeight / 512f;
        uv.x = Mathf.Repeat(uv.x + dt * StreakSpeed / 128f, 1f);
        streakImage.uvRect = uv;
        dotImage.uvRect = new Rect(0f, 0f, span * DotShare / 16f, TabHeight / 16f);
    }

    // Two runs and more side by side, sliding left and wrapping by one run's width; laid again only when its words change.
    private void RunTicker(float dt)
    {
        bool siren = mode == BandMode.Assault && last.Siren;
        string title = mode == BandMode.Assault ? last.Title : null;
        if (!tickerSet || mode != tickerMode || siren != tickerSiren || !string.Equals(title, tickerTitle))
        {
            tickerSet = true;
            tickerMode = mode;
            tickerSiren = siren;
            tickerTitle = title;
            string one = TickerWords() + TickerGap;
            tickerRun.text = one + one + one;
            tickerLoop = Mathf.Max(1f, tickerRun.GetPreferredValues(one + one).x - tickerRun.GetPreferredValues(one).x);
        }
        tickerX = Mathf.Repeat(tickerX + dt * TickerSpeed, tickerLoop);
        tickerRunRect.anchoredPosition = new Vector2(-tickerX, 0f);
    }

    // The assault's words with the song playing (or a hold's siren), or the rest's (Texts.csv hud.assault.ticker, hud.rest.ticker).
    private string TickerWords()
    {
        if (mode == BandMode.Rest) return Texts.Format("hud.rest.ticker", Texts.Get("hud.rest.title"));
        string title = Texts.Get("hud.assault.title");
        if (!string.IsNullOrEmpty(last.Title))
            return Texts.Format("hud.assault.ticker", title, Texts.Format("hud.assault.song", last.Title, last.Artist));
        return last.Siren ? Texts.Format("hud.assault.ticker", title, Texts.Get("hud.assault.siren")) : Texts.Format("hud.assault.ticker.plain", title);
    }

    private void BuildBand()
    {
        // The room its wipe clips within, the body that bounces in, its ink shadow and white edge.
        bandClip = Corner("Band", root);
        bandMask = bandClip.gameObject.AddComponent<RectMask2D>();
        bandMask.enabled = false;
        body = FeelUI.Rect("Body", bandClip);
        body.anchorMin = body.anchorMax = Vector2.zero;
        body.pivot = new Vector2(0f, .5f);
        bandGroup = body.gameObject.AddComponent<CanvasGroup>();
        var drop = Around("Shadow", body, 0f, 0f);
        drop.offsetMin = drop.offsetMax = new Vector2(ShadowDrop, -ShadowDrop);
        KitSkew.On(KitStyle.Fill(drop, KitStyle.Ink), BandSlant);
        KitSkew.On(KitStyle.Fill(Around("Edge", body, EdgeAcross, EdgeUp), Color.white), BandSlant);

        // The slab clips what is on it to its lean: its light and shade, the halftone fading in toward its right end (over
        // the light and shade, so they run on under it), the streaks, the passing light, the title over its shadow, the ticker.
        var slab = Around("Slab", body, 0f, 0f);
        slabImage = KitStyle.Fill(slab, KitStyle.Accent);
        KitSkew.On(slabImage, BandSlant);
        slab.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        lightImage = KitStyle.Raw(Across(slab, "Light", .45f, 1f), KitShapes.Drop, AssaultTop);
        shadeImage = KitStyle.Raw(Across(slab, "Shade", 0f, .55f), KitShapes.Drop, AssaultFoot);
        shadeImage.uvRect = new Rect(0f, 1f, 1f, -1f);
        var dots = FeelUI.Rect("Halftone", slab);
        dots.anchorMin = new Vector2(1f - DotShare, 0f);
        dots.anchorMax = Vector2.one;
        dots.offsetMin = dots.offsetMax = Vector2.zero;
        dotImage = KitStyle.Raw(dots, KitShapes.Halftone, KitStyle.With(KitStyle.Ink, DotAlpha));
        dots.gameObject.AddComponent<KitFade>();
        streakImage = KitStyle.Raw(Around("Streaks", slab, 0f, 0f), KitShapes.Streaks, KitStyle.With(Color.white, StreakAlpha));
        sheen = FeelUI.Rect("Sheen", slab);
        sheen.anchorMin = Vector2.zero;
        sheen.anchorMax = Vector2.up;
        sheen.pivot = new Vector2(.5f, .5f);
        sheen.sizeDelta = new Vector2(SheenWidth, 0f);
        KitStyle.Raw(sheen, KitShapes.Glow, KitStyle.With(Color.white, .55f));
        sheen.gameObject.SetActive(false);
        bandShade = Title(slab, "Title Shadow", KitStyle.Ink, new Vector2(TitleShadow, -TitleShadow));
        bandTitle = Title(slab, "Title", Color.white, Vector2.zero);
        tickerPlate = FeelUI.Rect("Ticker", slab);
        tickerPlate.anchorMin = Vector2.zero;
        tickerPlate.anchorMax = Vector2.right;
        tickerPlate.pivot = Vector2.zero;
        tickerPlate.offsetMin = new Vector2(0f, TickerLift);
        tickerPlate.offsetMax = new Vector2(0f, TickerLift + TickerHeight);
        KitStyle.Fill(tickerPlate, Color.white);
        tickerRunRect = FeelUI.Rect("Words", tickerPlate);
        tickerRunRect.anchorMin = Vector2.zero;
        tickerRunRect.anchorMax = Vector2.up;
        tickerRunRect.pivot = new Vector2(0f, .5f);
        tickerRunRect.sizeDelta = new Vector2(4000f, 0f);
        tickerRun = KitStyle.Flat(tickerRunRect, "", TickerSize, TextAlignmentOptions.MidlineLeft, KitStyle.Ink);
        tickerRun.textWrappingMode = TextWrappingModes.NoWrap;
        tickerRun.overflowMode = TextOverflowModes.Overflow;
        tickerRun.fontStyle |= FontStyles.UpperCase;
        tickerRun.characterSpacing = TickerSpacing;
        tickerRun.richText = true;

        // The stamp over the band's top left, on its ink shadow (outside the slab, so it is not clipped to it).
        stamp = FeelUI.Rect("Stamp", body);
        stamp.anchorMin = stamp.anchorMax = Vector2.up;
        stamp.pivot = new Vector2(.5f, .5f);
        stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
        var stampDrop = Around("Shadow", stamp, 0f, 0f);
        stampDrop.offsetMin = stampDrop.offsetMax = new Vector2(StampShadow, -StampShadow);
        KitStyle.Fill(stampDrop, KitStyle.Ink);
        KitStyle.Fill(Around("Plate", stamp, 0f, 0f), Color.white);
        stampText = KitStyle.Flat(Around("Words", stamp, 0f, 0f), "", StampSize, TextAlignmentOptions.Center, KitStyle.Ink);
        stampText.textWrappingMode = TextWrappingModes.NoWrap;
        stampText.characterSpacing = StampSpacing;
        bandClip.gameObject.SetActive(false);
    }

    // A strip across the slab between these shares of its height.
    private static RectTransform Across(RectTransform slab, string label, float from, float to)
    {
        var rect = FeelUI.Rect(label, slab);
        rect.anchorMin = new Vector2(0f, from);
        rect.anchorMax = new Vector2(1f, to);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    // 습격 중 (or 휴식 시간) in the alert's own face (Fonts.csv Alarm), leaning and edged in ink, over the ticker.
    private static TextMeshProUGUI Title(RectTransform slab, string label, Color color, Vector2 offset)
    {
        var rect = FeelUI.Rect(label, slab);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.right;
        rect.pivot = new Vector2(0f, .5f);
        rect.offsetMin = new Vector2(TitleLeft, TickerLift + TickerHeight) + offset;
        rect.offsetMax = new Vector2(-8f, TabHeight - 3f) + offset;
        var text = KitStyle.Flat(rect, "", TitleSize, TextAlignmentOptions.MidlineLeft, color);
        FontRoles.Apply(text, FontRoles.Alarm);
        text.fontStyle |= FontStyles.Bold | FontStyles.Italic;
        var edged = CombatUIAssets.Outlined(text.font, KitStyle.Ink, TitleEdge);
        if (edged != null) text.fontSharedMaterial = edged;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }
}
