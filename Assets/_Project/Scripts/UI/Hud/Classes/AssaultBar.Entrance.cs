using TMPro;
using UnityEngine;
using UnityEngine.UI;

// AssaultBar: the assault's entrance, its moments that each happen once: one soft light, a white slash, the giant word sweeping past behind the bar, a halftone burst out of the emblem. One part of the class; the rest is in AssaultBar.cs and the files beside it.
public sealed partial class AssaultBar
{
    // The entrance (s, px): one soft light over the band; the white slash drawn across low over the strip, fading; the
    // giant word sweeping past behind the bar (the user's note on the second draft: "A의 거대한 ASSAULT 스치는 건 넣어
    // 줘"), its size, start, travel, time, fill strength and outline; the halftone burst out of the emblem. The 0.9.26
    // build-up (a charge strip, rings pinging out of a turning beacon) and the shake went with the beacon: the emblem's
    // gauge and its seconds build the anticipation now.
    private const float ImpactAlpha = .4f, ImpactSeconds = .35f, SlashDraw = .15f, SlashFade = .42f, SlashLength = 1500f,
        SlashStart = -60f, SlashHeight = 7.5f, BigSize = 150f, BigLeft = 250f, BigFrom = 420f, BigTo = -160f, BigSeconds = .95f,
        BigFillAlpha = .28f, BigOutline = .12f, BurstAt = .05f, BurstSeconds = .7f, BurstSize = 240f;

    private RectTransform impact, slash, big, burst;
    private Image impactImage;
    private RawImage burstImage;
    private CanvasGroup slashGroup;
    private TextMeshProUGUI bigOutline, bigFill;
    private static Material outlineOnly;
    private static TMP_FontAsset outlineFont;

    // The assault breaks in: each moment once, while its band is new.
    private void PoseEntrance(float width)
    {
        float t = bandOn && mode == BandMode.Assault ? bandAge : float.PositiveInfinity;
        impact.gameObject.SetActive(t < ImpactSeconds);
        if (impact.gameObject.activeSelf)
        {
            impact.sizeDelta = new Vector2(Mathf.Max(80f, width - BandLeft + BandPast), TabHeight);
            impactImage.color = KitStyle.With(Color.white, ImpactAlpha * (1f - Ease.Out(t / ImpactSeconds)));
        }
        slash.gameObject.SetActive(t < SlashFade);
        if (slash.gameObject.activeSelf)
        {
            slash.localScale = new Vector3(Ease.Out(t / SlashDraw), 1f, 1f);
            slashGroup.alpha = 1f - Mathf.Clamp01((t - SlashDraw) / (SlashFade - SlashDraw));
        }
        big.gameObject.SetActive(t < BigSeconds);
        if (big.gameObject.activeSelf)
        {
            float k = t / BigSeconds;
            big.anchoredPosition = new Vector2(BigLeft + Mathf.Lerp(BigFrom, BigTo, Ease.Out(k)), ((RectTransform)transform).rect.height * .5f + 6f);
            float alpha = k < .18f ? k / .18f : k < .7f ? Mathf.Lerp(1f, .9f, (k - .18f) / .52f) : .9f * (1f - (k - .7f) / .3f);
            bigOutline.alpha = alpha;
            bigFill.alpha = alpha * BigFillAlpha;
        }
        float spread = (t - BurstAt) / BurstSeconds;
        burst.gameObject.SetActive(spread >= 0f && spread < 1f);
        if (burst.gameObject.activeSelf)
        {
            burst.localScale = Vector3.one * Mathf.Lerp(.2f, 1.6f, Ease.Out(spread));
            burstImage.color = KitStyle.With(KitStyle.Accent, 1f - spread);
        }
    }

    private void BuildEntrance()
    {
        // The giant word behind everything else of the assault: a white ghost under a pink outline.
        big = Corner("Giant Word", root);
        big.SetAsFirstSibling();
        big.pivot = new Vector2(0f, .5f);
        big.sizeDelta = new Vector2(1000f, BigSize * 1.2f);
        bigFill = Giant("Fill", big, new Vector2(12f, -12f));
        bigOutline = Giant("Outline", big, Vector2.zero);
        bigOutline.fontSharedMaterial = OutlineOnly(bigOutline);
        big.gameObject.SetActive(false);

        // The halftone burst out of the emblem, the soft light over the band and the white slash, over the rest.
        burst = Corner("Burst", root);
        burst.pivot = new Vector2(.5f, .5f);
        burst.anchoredPosition = new Vector2(TabWidth * .5f, TabHeight * .5f);
        burst.sizeDelta = new Vector2(BurstSize, BurstSize);
        burstImage = KitStyle.Raw(burst, KitShapes.Burst, KitStyle.Accent);
        burst.gameObject.SetActive(false);
        impact = Corner("Impact", root);
        impact.anchoredPosition = new Vector2(BandLeft - 2f, -BandOver);
        impactImage = KitStyle.Fill(impact, Color.clear);
        KitSkew.On(impactImage, BandSlant);
        impact.gameObject.SetActive(false);
        slash = Corner("Slash", root);
        slash.pivot = new Vector2(0f, .5f);
        slash.anchoredPosition = new Vector2(SlashStart, SlashHeight);
        slash.sizeDelta = new Vector2(SlashLength, 3f);
        slashGroup = slash.gameObject.AddComponent<CanvasGroup>();
        var glow = Around("Glow", slash, 0f, 7f);
        var glowImage = KitStyle.Fill(glow, KitStyle.With(KitStyle.Accent, .6f));
        glowImage.sprite = CombatUIAssets.SoftGlow;
        glowImage.type = Image.Type.Sliced;
        KitStyle.Fill(Around("Line", slash, 0f, 0f), Color.white);
        slash.gameObject.SetActive(false);
    }

    // One layer of the giant word, leaning, offset this far from its box (Texts.csv hud.assault.big).
    private static TextMeshProUGUI Giant(string label, RectTransform parent, Vector2 offset)
    {
        var rect = FeelUI.Rect(label, parent);
        KitStyle.Stretch(rect);
        rect.offsetMin = rect.offsetMax = offset;
        var text = KitStyle.Flat(rect, Texts.Get("hud.assault.big"), BigSize, TextAlignmentOptions.MidlineLeft, Color.white);
        text.fontStyle |= FontStyles.Bold | FontStyles.Italic;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.characterSpacing = -2f;
        return text;
    }

    // The giant word's outline alone, in pink: a copy of its font's material with an outline and a see-through face.
    private static Material OutlineOnly(TMP_Text text)
    {
        if (outlineOnly != null && outlineFont == text.font) return outlineOnly;
        outlineFont = text.font;
        outlineOnly = new Material(text.fontSharedMaterial) { name = "Assault Giant Outline (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        outlineOnly.EnableKeyword(ShaderUtilities.Keyword_Outline);
        if (outlineOnly.HasProperty(ShaderUtilities.ID_OutlineWidth)) outlineOnly.SetFloat(ShaderUtilities.ID_OutlineWidth, BigOutline);
        if (outlineOnly.HasProperty(ShaderUtilities.ID_OutlineColor)) outlineOnly.SetColor(ShaderUtilities.ID_OutlineColor, KitStyle.Accent);
        if (outlineOnly.HasProperty(ShaderUtilities.ID_FaceColor)) outlineOnly.SetColor(ShaderUtilities.ID_FaceColor, new Color(1f, 1f, 1f, 0f));
        return outlineOnly;
    }
}
