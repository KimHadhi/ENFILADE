using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ObjectiveAlert: 심플, the ribbon the objective's words are typed onto. One part of the class; the rest is in ObjectiveAlert.cs and the file beside it.
public sealed partial class ObjectiveAlert
{
    // The ribbon: its height, slant, the pink head's width and slant, its least width, the room round the words, the
    // words' size and least size (px).
    private const float RibbonHeight = 52f, RibbonSlant = 14f, HeadWidth = 176f, HeadSlant = 16f, RibbonLeast = 440f,
        RibbonRoom = 232f, TypeSize = 25f, TypeLeast = 16f;
    // Timing (s): it opens from the middle, the words are typed a letter at a time, it closes; how long it all lasts and
    // where the overlap check holds it.
    private const float RibbonOpen = .3f, TypeAt = .3f, TypeStep = .04f, CloseAt = 2.5f, CloseSeconds = .32f, RibbonLength = 2.85f,
        RibbonHeld = 1.4f;
    // The head's arrows and the caret breathe: once in this long (s), dimming only to this.
    private const float ArrowBreath = 1.4f, ArrowDim = .6f, CaretBreath = 1.6f, CaretDim = .35f;
    private static readonly Color RibbonColor = new Color(.06f, .05f, .07f, .94f);

    private RectTransform ribbon, band, caret;
    private RawImage ribbonGrain;
    private TextMeshProUGUI typed, headWords;
    private CanvasGroup ribbonGroup, arrows, caretGroup;
    private float[] letterEnds = new float[0];

    private void StartRibbon(string words, float widest)
    {
        if (ribbon == null) BuildRibbon();
        if (card != null) card.gameObject.SetActive(false);
        ribbon.gameObject.SetActive(true);
        typed.text = words;
        typed.enableAutoSizing = false;
        typed.fontSize = TypeSize;
        float wordsWidth = typed.GetPreferredValues(words).x;
        float width = Mathf.Clamp(wordsWidth + RibbonRoom, Mathf.Min(RibbonLeast, widest), widest);
        root.sizeDelta = new Vector2(width, RibbonHeight);
        typed.enableAutoSizing = wordsWidth + RibbonRoom > width;
        typed.fontSizeMin = TypeLeast;
        typed.fontSizeMax = TypeSize;
        headWords.text = Texts.Get("objective.alert.chip");
        typed.maxVisibleCharacters = 99999;
        Canvas.ForceUpdateCanvases();
        Tile(ribbonGrain, 128f);
        // Where each letter ends, for the caret that follows the typing.
        typed.ForceMeshUpdate();
        var info = typed.textInfo;
        letterEnds = new float[info.characterCount];
        for (int i = 0; i < info.characterCount; i++) letterEnds[i] = info.characterInfo[i].topRight.x;
        typed.maxVisibleCharacters = 0;
    }

    private void PoseRibbon(float t)
    {
        float open = KitMotion.BackOut(t / RibbonOpen), close = Mathf.Clamp01((t - CloseAt) / CloseSeconds);
        band.localScale = new Vector3(Mathf.Max(.001f, open * (1f - Ease.Out(close))), 1f, 1f);
        ribbonGroup.alpha = 1f - close;
        int count = Mathf.Clamp(Mathf.FloorToInt((t - TypeAt) / TypeStep) + 1, 0, letterEnds.Length);
        typed.maxVisibleCharacters = count;
        caret.gameObject.SetActive(t >= TypeAt);
        float left = typed.rectTransform.rect.xMin;
        caret.anchoredPosition = new Vector2((count > 0 ? letterEnds[count - 1] : left) + 3f, 0f);
        caretGroup.alpha = Mathf.Lerp(CaretDim, 1f, .5f + .5f * Mathf.Cos(t / CaretBreath * Mathf.PI * 2f));
        arrows.alpha = Mathf.Lerp(ArrowDim, 1f, .5f + .5f * Mathf.Cos(t / ArrowBreath * Mathf.PI * 2f));
    }

    private void BuildRibbon()
    {
        ribbon = Stretched("Ribbon", root);
        ribbonGroup = ribbon.gameObject.AddComponent<CanvasGroup>();
        band = Stretched("Band", ribbon);
        KitSkew.On(Fill(band, RibbonColor), RibbonSlant);
        band.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        ribbonGrain = KitStyle.Raw(Stretched("Grain", band), KitShapes.Grain, KitStyle.With(Color.white, .05f));

        // The pink head: three arrows breathing, and NEW OBJECTIVE.
        var head = FeelUI.Rect("Head", band);
        head.anchorMin = Vector2.zero;
        head.anchorMax = Vector2.up;
        head.pivot = new Vector2(0f, .5f);
        head.sizeDelta = new Vector2(HeadWidth, 0f);
        KitSkew.On(Fill(head, KitStyle.Accent), HeadSlant);
        var arrowRoot = FeelUI.Rect("Arrows", head);
        arrowRoot.anchorMin = arrowRoot.anchorMax = new Vector2(0f, .5f);
        arrowRoot.pivot = new Vector2(0f, .5f);
        arrowRoot.anchoredPosition = new Vector2(22f, 0f);
        arrows = arrowRoot.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < 3; i++) Arrow(arrowRoot, i * 7f);
        var label = FeelUI.Rect("Label", head);
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(48f, 0f);
        label.offsetMax = new Vector2(-14f, 0f);
        headWords = Words(label, 11f, Color.white, TextAlignmentOptions.MidlineLeft);
        headWords.fontStyle |= FontStyles.Bold;
        headWords.characterSpacing = 24f;

        // The words, typed in after a pink caret.
        var words = FeelUI.Rect("Words", band);
        words.anchorMin = Vector2.zero;
        words.anchorMax = Vector2.one;
        words.offsetMin = new Vector2(HeadWidth + 16f, 0f);
        words.offsetMax = new Vector2(-26f, 0f);
        typed = Words(words, TypeSize, Color.white, TextAlignmentOptions.MidlineLeft);
        FontRoles.Apply(typed, FontRoles.Alarm);
        caret = FeelUI.Rect("Caret", typed.rectTransform);
        caret.anchorMin = caret.anchorMax = new Vector2(.5f, .5f);
        caret.pivot = new Vector2(0f, .5f);
        caret.sizeDelta = new Vector2(3f, 26f);
        caretGroup = caret.gameObject.AddComponent<CanvasGroup>();
        Fill(caret, KitStyle.Accent);
        ribbon.gameObject.SetActive(false);
    }

    // One arrow (>) of two thin strokes meeting at its point, this far along the head.
    private static void Arrow(RectTransform parent, float x)
    {
        for (int k = 0; k < 2; k++)
        {
            var stroke = FeelUI.Rect("Arrow", parent);
            stroke.anchorMin = stroke.anchorMax = new Vector2(0f, .5f);
            stroke.pivot = new Vector2(1f, .5f);
            stroke.anchoredPosition = new Vector2(x + 7f, 0f);
            stroke.sizeDelta = new Vector2(8f, 2.5f);
            stroke.localRotation = Quaternion.Euler(0f, 0f, k == 0 ? 40f : -40f);
            Fill(stroke, Color.white);
        }
    }
}
