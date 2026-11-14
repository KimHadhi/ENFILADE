using TMPro;
using UnityEngine;
using UnityEngine.UI;

// AssaultBar: the anticipation's build-up and the assault's entrance, all of it motion and none of it a blink. One part of the class; the rest is in AssaultBar.cs and the files beside it.
public sealed partial class AssaultBar
{
    // The build-up (the user, 2026-11-14, of the calm panel: "습격 임박이랑 습격 진입이랑 너무 슴슴해졌는데"): the beacon
    // turns faster as the assault nears, from once in TurnAnticipation to once in this (s); a ring pings out of it every
    // PingEvery, every PingNear in the last PingNearAt (s), reaching this many tab widths over its life (s) from this
    // strength; a charge strip fills along the strip's top as the assault nears, this tall (px), drifting (px/s).
    private const float TurnNear = .8f, PingEvery = 1f, PingNear = .66f, PingNearAt = 3f, PingReach = 3.2f, PingSeconds = .9f,
        PingAlpha = .55f, ChargeHeight = 4f, ChargeSpeed = 40f;
    private const int Pings = 3;
    // The entrance (s, px): one soft light; the white slash drawn across and fading; the giant word sweeping past behind
    // the bar (the user's note on the second draft: "A의 거대한 ASSAULT 스치는 건 넣어 줘"), its size, start, travel, time,
    // fill strength and outline; the halftone burst out of the beacon; a short shake; the stripes' rush slowing to their
    // drift; the panel's title landing letter by letter.
    private const float ImpactAlpha = .5f, ImpactSeconds = .35f, SlashDraw = .15f, SlashFade = .42f, SlashLength = 1500f,
        SlashStart = -60f, BigSize = 150f, BigLeft = 250f, BigFrom = 420f, BigTo = -160f, BigSeconds = .95f, BigFillAlpha = .28f,
        BigOutline = .12f, BurstAt = .05f, BurstSeconds = .7f, BurstSize = 240f, ShakeSeconds = .3f, ShakeReach = 5f,
        RushSpeed = 90f, RushSeconds = 1.2f, TitleAt = .12f, TitleStep = .03f, TitleLand = .3f, TitleFrom = 2f;

    private RectTransform impact, slash, big, burst, charge;
    private readonly RectTransform[] pings = new RectTransform[Pings];
    private readonly Image[] pingImages = new Image[Pings];
    private readonly float[] pingAge = { -1f, -1f, -1f };
    private Image impactImage;
    private RawImage burstImage, chargeImage;
    private CanvasGroup slashGroup;
    private TextMeshProUGUI bigOutline, bigFill;
    private float pingClock = PingEvery;
    private int pingNext;
    private static Material outlineOnly;
    private static TMP_FontAsset outlineFont;

    // How far off the assault still is: 1 as the anticipation begins, 0 as it breaks in.
    private float Nearness => Mathf.Clamp01((float.IsInfinity(last.Until) ? PulseSpan : last.Until) / PulseSpan);

    // The anticipation builds: a striped charge fills along the strip's top, and rings ping out of the beacon, closer
    // together in the last seconds.
    private void PoseBuildUp(CombatPhase phase, float width, float dt)
    {
        bool building = phase == CombatPhase.Anticipation;
        charge.gameObject.SetActive(building);
        if (building)
        {
            float full = Mathf.Max(20f, width - TabWidth - PanelGap);
            charge.anchoredPosition = new Vector2(TabWidth + PanelGap, ((RectTransform)transform).rect.height);
            charge.sizeDelta = new Vector2(full * Ease.InOut(1f - Nearness), ChargeHeight);
            var uv = chargeImage.uvRect;
            uv.width = Mathf.Max(.01f, charge.sizeDelta.x / 32f);
            uv.height = ChargeHeight / 32f;
            uv.x = Mathf.Repeat(uv.x - dt * ChargeSpeed / 32f, 1f);
            chargeImage.uvRect = uv;
            pingClock += dt;
            if (pingClock >= (last.Until <= PingNearAt ? PingNear : PingEvery))
            {
                pingClock = 0f;
                pingAge[pingNext] = 0f;
                pingNext = (pingNext + 1) % Pings;
            }
        }
        // The first ping comes as the anticipation begins.
        else pingClock = PingEvery;
        for (int i = 0; i < Pings; i++)
        {
            if (pingAge[i] >= 0f) pingAge[i] += dt;
            float t = pingAge[i] / PingSeconds;
            bool alive = pingAge[i] >= 0f && t < 1f;
            if (!alive) pingAge[i] = -1f;
            pings[i].gameObject.SetActive(alive);
            if (!alive) continue;
            pings[i].localScale = Vector3.one * Mathf.Lerp(.6f, PingReach, Ease.Out(t));
            pingImages[i].color = KitStyle.With(KitStyle.Accent, PingAlpha * (1f - t));
        }
    }

    // The assault breaks in: one soft light, a white slash drawn across, the giant word sweeping past behind, a burst of
    // halftone out of the beacon, and a short shake. Each happens once.
    private void PoseEntrance(float width)
    {
        float t = on ? age : float.PositiveInfinity;
        impact.gameObject.SetActive(t < ImpactSeconds);
        if (impact.gameObject.activeSelf)
        {
            impact.sizeDelta = new Vector2(width, TabHeight + PanelOver);
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
        // A short shake of the bar's own parts, dying away (smooth noise, not a jitter).
        if (t < ShakeSeconds)
        {
            float reach = ShakeReach * (1f - t / ShakeSeconds) * (1f - t / ShakeSeconds) * 2f;
            root.anchoredPosition = new Vector2(Mathf.PerlinNoise(t * 40f, 0f) - .5f, Mathf.PerlinNoise(0f, t * 40f) - .5f) * reach;
        }
        else root.anchoredPosition = Vector2.zero;
    }

    private void BuildEntrance()
    {
        float barHeight = ((RectTransform)transform).rect.height;
        // The giant word behind everything else of the assault: a white ghost under a pink outline.
        big = Corner("Giant Word", root);
        big.SetAsFirstSibling();
        big.pivot = new Vector2(0f, .5f);
        big.sizeDelta = new Vector2(1000f, BigSize * 1.2f);
        bigFill = Giant("Fill", big, new Vector2(12f, -12f));
        bigOutline = Giant("Outline", big, Vector2.zero);
        bigOutline.fontSharedMaterial = OutlineOnly(bigOutline);
        big.gameObject.SetActive(false);

        // The rings the beacon pings out in the anticipation, over the tab.
        for (int i = 0; i < Pings; i++)
        {
            var ping = Corner("Ping", root);
            ping.pivot = new Vector2(.5f, .5f);
            ping.anchoredPosition = new Vector2(TabWidth * .5f, TabHeight * .5f);
            ping.sizeDelta = new Vector2(TabWidth, TabWidth);
            pingImages[i] = Fill(ping, KitStyle.Accent);
            pingImages[i].sprite = CombatUIAssets.Ring;
            ping.gameObject.SetActive(false);
            pings[i] = ping;
        }
        charge = Corner("Charge", root);
        chargeImage = KitStyle.Raw(charge, KitShapes.Stripes, KitStyle.With(KitStyle.Accent, .85f));
        charge.gameObject.SetActive(false);

        // The halftone burst out of the beacon, the soft light and the white slash, over the rest.
        burst = Corner("Burst", root);
        burst.pivot = new Vector2(.5f, .5f);
        burst.anchoredPosition = new Vector2(TabWidth * .5f, TabHeight * .5f);
        burst.sizeDelta = new Vector2(BurstSize, BurstSize);
        burstImage = KitStyle.Raw(burst, KitShapes.Burst, KitStyle.Accent);
        burst.gameObject.SetActive(false);
        impact = Corner("Impact", root);
        impact.anchoredPosition = new Vector2(0f, -PanelOver);
        impactImage = Fill(impact, Color.clear);
        impact.gameObject.SetActive(false);
        slash = Corner("Slash", root);
        slash.pivot = new Vector2(0f, .5f);
        slash.anchoredPosition = new Vector2(SlashStart, barHeight * .5f);
        slash.sizeDelta = new Vector2(SlashLength, 3f);
        slashGroup = slash.gameObject.AddComponent<CanvasGroup>();
        var glow = FeelUI.Rect("Glow", slash);
        KitStyle.Stretch(glow);
        glow.offsetMin = new Vector2(0f, -7f);
        glow.offsetMax = new Vector2(0f, 7f);
        var glowImage = Fill(glow, KitStyle.With(KitStyle.Accent, .6f));
        glowImage.sprite = CombatUIAssets.SoftGlow;
        glowImage.type = Image.Type.Sliced;
        var line = FeelUI.Rect("Line", slash);
        KitStyle.Stretch(line);
        Fill(line, Color.white);
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
