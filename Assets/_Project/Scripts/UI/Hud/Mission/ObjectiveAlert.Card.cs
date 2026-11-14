using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ObjectiveAlert: 기본, the card that opens from a white line and flies into the objective bar. One part of the class; the rest is in ObjectiveAlert.cs and the file beside it.
public sealed partial class ObjectiveAlert
{
    // The card: its height, the room over it for the line, its slant, its least width and the room round the words, the
    // words' size and least size, how far in from the card's left they and the chip stand (px).
    private const float CardHeight = 112f, CardTop = 8f, CardSlant = 18f, CardLeast = 440f, CardRoom = 120f, TitleSize = 33f,
        TitleLeast = 20f, CardInset = 30f;
    // Timing (s): the line is drawn and fades, the card opens, the chip slides in, the words land, the line under them
    // runs, a light passes, the card flies into the bar; how long it all lasts, and where the overlap check holds it.
    private const float LineDraw = .12f, LineFade = .5f, OpenAt = .08f, OpenSeconds = .26f, ChipAt = .22f, ChipSeconds = .3f,
        WordsAt = .34f, UnderAt = .6f, UnderSeconds = .4f, SheenAt = 1.05f, SheenSeconds = .9f, FlyAt = 2.35f, FlySeconds = .55f,
        CardLength = 2.95f, CardHeld = 1.6f;
    // How small the card is as it reaches the bar, and the pink shadow under the words (px).
    private const float FlyScale = .24f, ShadeOffset = 3f;
    private static readonly Color ShadeColor = new Color(.83f, .17f, .35f);

    private RectTransform card, content, line, body, chip, chipPlate, chipShadow, sheen, under, halftone;
    private RawImage halftoneImage, grainImage;
    private TextMeshProUGUI title, shade, chipWords, chipKorean;
    private CanvasGroup contentGroup, lineGroup, chipGroup;
    private Vector2 chipHome;
    private float cardWidth, titleWidth;
    private bool landed;

    private void StartCard(string words, float widest)
    {
        if (card == null) BuildCard();
        if (ribbon != null) ribbon.gameObject.SetActive(false);
        card.gameObject.SetActive(true);
        title.text = shade.text = words;
        title.enableAutoSizing = shade.enableAutoSizing = false;
        title.fontSize = shade.fontSize = TitleSize;
        titleWidth = title.GetPreferredValues(words).x;
        cardWidth = Mathf.Clamp(titleWidth + CardRoom, Mathf.Min(CardLeast, widest), widest);
        root.sizeDelta = new Vector2(cardWidth, CardHeight);
        // A long objective shrinks to fit on one line rather than running off the card.
        float room = cardWidth - CardInset * 2f;
        bool shrink = titleWidth > room;
        foreach (var text in new[] { title, shade })
        {
            text.enableAutoSizing = shrink;
            text.fontSizeMin = TitleLeast;
            text.fontSizeMax = TitleSize;
        }
        if (shrink) titleWidth = room;
        chipWords.text = Texts.Get("objective.alert.chip");
        chipKorean.text = Texts.Get("objective.alert.new");
        float plate = chipWords.GetPreferredValues(chipWords.text).x + 16f;
        chipPlate.sizeDelta = chipShadow.sizeDelta = new Vector2(plate, 18f);
        ((RectTransform)chipKorean.rectTransform.parent).anchoredPosition = new Vector2(plate + 10f, -9f);
        line.sizeDelta = new Vector2(cardWidth * 1.24f, 2f);
        line.anchoredPosition = new Vector2(-cardWidth * .12f, -CardTop * .5f);
        under.sizeDelta = new Vector2(titleWidth, 4f);
        landed = false;
        content.localPosition = Vector3.zero;
        content.localScale = Vector3.one;
        contentGroup.alpha = 1f;
        // The patterns tile at the card's new size.
        Canvas.ForceUpdateCanvases();
        Tile(halftoneImage, 16f);
        Tile(grainImage, 128f);
    }

    private void PoseCard(float t)
    {
        // The white line is drawn across, then fades as the card opens from it.
        line.localScale = new Vector3(Ease.Out(t / LineDraw), 1f, 1f);
        lineGroup.alpha = 1f - Mathf.Clamp01((t - LineDraw) / (LineFade - LineDraw));
        // Gone once faded, so the overlap check measures the card alone.
        line.gameObject.SetActive(t < LineFade);
        body.localScale = new Vector3(1f, Mathf.Max(.001f, BackOut((t - OpenAt) / OpenSeconds)), 1f);
        float chipIn = Ease.Out(Mathf.Clamp01((t - ChipAt) / ChipSeconds));
        chip.anchoredPosition = chipHome + new Vector2(-26f * (1f - chipIn), 0f);
        chipGroup.alpha = chipIn;
        if (t - WordsAt < (title.text.Length + 1) * LetterStep + LetterSeconds)
        {
            LandLetters(title, t - WordsAt);
            LandLetters(shade, t - WordsAt);
        }
        under.localScale = new Vector3(Ease.Out(Mathf.Clamp01((t - UnderAt) / UnderSeconds)), 1f, 1f);
        float pass = (t - SheenAt) / SheenSeconds;
        sheen.gameObject.SetActive(pass > 0f && pass < 1f);
        sheen.anchoredPosition = new Vector2(Mathf.Lerp(-120f, cardWidth + 60f, Ease.InOut(pass)), 0f);
        // Then it flies off into the objective bar, shrinking, and the bar lights up as it lands.
        float fly = Mathf.Clamp01((t - FlyAt) / FlySeconds);
        if (fly <= 0f) return;
        float eased = fly * fly;
        content.localPosition = Vector3.Lerp(Vector3.zero, BarPoint(), eased);
        content.localScale = Vector3.one * Mathf.Lerp(1f, FlyScale, eased);
        contentGroup.alpha = 1f - eased;
        if (landed || fly < .92f) return;
        landed = true;
        if (landOn != null) landOn.Land();
    }

    // The objective bar's middle in the alert's own space.
    private Vector3 BarPoint()
    {
        if (target == null) return Vector3.zero;
        return root.InverseTransformPoint(target.TransformPoint(target.rect.center));
    }

    private void BuildCard()
    {
        card = Stretched("Card", root);
        content = Stretched("Content", card);
        contentGroup = content.gameObject.AddComponent<CanvasGroup>();

        // The white line the card opens from, with a pink glow under it.
        line = FeelUI.Rect("Line", content);
        line.anchorMin = line.anchorMax = new Vector2(0f, .5f);
        line.pivot = new Vector2(0f, .5f);
        lineGroup = line.gameObject.AddComponent<CanvasGroup>();
        var lineGlow = Stretched("Glow", line);
        lineGlow.offsetMin = new Vector2(0f, -7f);
        lineGlow.offsetMax = new Vector2(0f, 7f);
        var glow = lineGlow.gameObject.AddComponent<Image>();
        glow.sprite = CombatUIAssets.SoftGlow;
        glow.type = Image.Type.Sliced;
        glow.color = KitStyle.With(KitStyle.Accent, .5f);
        glow.raycastTarget = false;
        Fill(Stretched("White", line), Color.white);

        // The card: dark, leaning, edged in pink along its top, halftone and grain on it, clipped to its lean.
        body = FeelUI.Rect("Body", content);
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = Vector2.zero;
        body.offsetMax = new Vector2(0f, -CardTop);
        KitSkew.On(Fill(body, BodyColor), CardSlant);
        body.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        halftone = FeelUI.Rect("Halftone", body);
        halftone.anchorMin = new Vector2(.52f, 0f);
        halftone.anchorMax = Vector2.one;
        halftone.offsetMin = halftone.offsetMax = Vector2.zero;
        halftoneImage = KitStyle.Raw(halftone, KitShapes.Halftone, KitStyle.With(KitStyle.Accent, .42f));
        var fade = FeelUI.Rect("Halftone Fade", halftone);
        fade.anchorMin = Vector2.zero;
        fade.anchorMax = new Vector2(.7f, 1f);
        fade.offsetMin = fade.offsetMax = Vector2.zero;
        KitStyle.Raw(fade, KitShapes.Fade, BodyColor);
        grainImage = KitStyle.Raw(Stretched("Grain", body), KitShapes.Grain, KitStyle.With(Color.white, .05f));
        var top = FeelUI.Rect("Top Edge", body);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = Vector2.one;
        top.pivot = new Vector2(.5f, 1f);
        top.sizeDelta = new Vector2(0f, 4f);
        Fill(top, KitStyle.Accent);
        var notch = FeelUI.Rect("Notch", body);
        notch.anchorMin = notch.anchorMax = notch.pivot = Vector2.right;
        notch.sizeDelta = new Vector2(90f, 6f);
        KitSkew.On(Fill(notch, KitStyle.Accent), 6f);
        sheen = FeelUI.Rect("Sheen", body);
        sheen.anchorMin = Vector2.zero;
        sheen.anchorMax = Vector2.up;
        sheen.pivot = new Vector2(.5f, .5f);
        sheen.sizeDelta = new Vector2(90f, 0f);
        KitSkew.On(Fill(sheen, KitStyle.With(Color.white, .12f)), 30f);

        // NEW OBJECTIVE on a white chip with a pink shadow, tilted a little, and 새 목표 beside it.
        chip = FeelUI.Rect("Chip", body);
        chip.anchorMin = chip.anchorMax = chip.pivot = Vector2.up;
        chipHome = new Vector2(CardInset, -9f);
        chip.localRotation = Quaternion.Euler(0f, 0f, 3f);
        chipGroup = chip.gameObject.AddComponent<CanvasGroup>();
        chipShadow = FeelUI.Rect("Shadow", chip);
        chipShadow.anchorMin = chipShadow.anchorMax = chipShadow.pivot = Vector2.up;
        chipShadow.anchoredPosition = new Vector2(3f, -3f);
        Fill(chipShadow, KitStyle.Accent);
        chipPlate = FeelUI.Rect("Plate", chip);
        chipPlate.anchorMin = chipPlate.anchorMax = chipPlate.pivot = Vector2.up;
        Fill(chipPlate, Color.white);
        var chipText = Stretched("Words", chipPlate);
        chipWords = Words(chipText, 10.5f, KitStyle.Ink, TextAlignmentOptions.Center);
        chipWords.fontStyle |= FontStyles.Bold;
        chipWords.characterSpacing = 26f;
        var korean = FeelUI.Rect("Korean", chip);
        korean.anchorMin = korean.anchorMax = Vector2.up;
        korean.pivot = new Vector2(0f, .5f);
        korean.sizeDelta = new Vector2(160f, 18f);
        chipKorean = Words(korean, 13f, KitStyle.Accent, TextAlignmentOptions.MidlineLeft);
        chipKorean.fontStyle |= FontStyles.Bold;

        // The objective's words over their pink shadow, leaning, and the line that runs under them.
        shade = Title("Shade", ShadeColor, new Vector2(ShadeOffset, -ShadeOffset));
        title = Title("Words", Color.white, Vector2.zero);
        under = FeelUI.Rect("Under", body);
        under.anchorMin = under.anchorMax = new Vector2(0f, .5f);
        under.pivot = new Vector2(0f, .5f);
        under.anchoredPosition = new Vector2(CardInset, -38f);
        Fill(under, KitStyle.Accent);
        card.gameObject.SetActive(false);
    }

    private TextMeshProUGUI Title(string label, Color color, Vector2 offset)
    {
        var rect = FeelUI.Rect(label, body);
        rect.anchorMin = new Vector2(0f, .5f);
        rect.anchorMax = new Vector2(1f, .5f);
        rect.offsetMin = new Vector2(CardInset, -32f) + offset;
        rect.offsetMax = new Vector2(-CardInset, 12f) + offset;
        var text = Words(rect, TitleSize, color, TextAlignmentOptions.MidlineLeft);
        // The objective's words draw in the alert's own role (Fonts.csv Alarm), leaning.
        FontRoles.Apply(text, FontRoles.Alarm);
        text.fontStyle |= FontStyles.Italic;
        return text;
    }
}
