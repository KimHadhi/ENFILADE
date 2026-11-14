using TMPro;
using UnityEngine;
using UnityEngine.UI;

// AssaultBar: the emblem over the ! tab while the run is loud, a sticker that fills like a gauge and never spins (the siren's place made anew). One part of the class; the rest is in AssaultBar.cs and the files beside it.
public sealed partial class AssaultBar
{
    // It fades in and out this fast (s); its ink shadow's drop and the white edge round it (px; the edge only in the
    // assault, fading over EdgeFade s); its colours change over this long (s); the light along the gauge's top (px).
    private const float EmblemFade = .3f, EmblemDrop = 4f, EmblemEdge = 3f, EdgeFade = .25f, FaceSeconds = .4f, GaugeLight = 18f,
        GaugeLip = 2f;
    // Its marks (px): the rest's moon, its size, its middle this far down from the top (further when no assault is to
    // come), its shadow; the time to the next assault under it, its size and middle's height; the anticipation's seconds,
    // their size, edge (TMP outline width) and shadow, how much a new one grows and settles over (s); the assault's
    // warning triangle (the user, 2026-11-14: "해골 모양 말고!!!!!!", "삼각형 안에 느낌표 모양으로 해 줘"), its size and shadow.
    private const float MoonSize = 24f, MoonDrop = 18f, MoonAlone = 27f, MarkShadow = 1.5f, NextSize = 12f, NextLift = 14f,
        CountSize = 30f, CountEdge = .25f, CountShadow = 3f, CountPop = .35f, CountPopSeconds = .26f, WarnSize = 36f, WarnShadow = 2f;
    // A new phase turns it, growing this much and leaning this far (degrees) and settling over this long (s); the assault
    // slams it.
    private const float TurnSeconds = .34f, TurnGrow = .18f, TurnTilt = 4f, SlamSeconds = .42f, SlamGrow = .55f, SlamTilt = 10f;
    // Its face under the gauge: the rest's deep blue, the anticipation's ink, the assault's deep pink; the gauge's lit top.
    private static readonly Color RestDeep = new Color(.075f, .157f, .373f), WaitDeep = new Color(.086f, .071f, .102f),
        AssaultDeep = new Color(.478f, .067f, .196f), RestLight = new Color(.494f, .667f, 1f), PinkLight = new Color(1f, .525f, .639f),
        MarkShade = new Color(.055f, .055f, .065f, .55f);

    private RectTransform emblem, emblemBody, gauge, gaugeLight, moon, next, count, warn;
    private CanvasGroup emblemGroup, edgeGroup;
    private Image face, gaugeImage;
    private RawImage gaugeLightImage;
    private TextMeshProUGUI nextText, countText, countShade;
    private float emblemShow, edgeShow, turnClock = -1f, turnSeconds, turnGrow, turnTilt, countClock = -1f, restSpan, faceClock;
    private Color faceFrom, faceTo, gaugeFrom, gaugeTo, lightFrom, lightTo;
    private int countShown = -1, nextShown = -1;

    // A new phase: its colours blend over, it turns (the assault slams it), the gauge starts again.
    private void TurnEmblem(CombatPhase from, CombatPhase phase)
    {
        if (phase == CombatPhase.Quiet) return;
        restSpan = 0f;
        countShown = nextShown = -1;
        faceFrom = face.color;
        gaugeFrom = gaugeImage.color;
        lightFrom = gaugeLightImage.color;
        faceTo = phase == CombatPhase.Control ? RestDeep : phase == CombatPhase.Anticipation ? WaitDeep : AssaultDeep;
        gaugeTo = phase == CombatPhase.Control ? RestBlue : KitStyle.Accent;
        lightTo = phase == CombatPhase.Control ? RestLight : PinkLight;
        // Coming in out of the quiet it shows its colours at once.
        faceClock = from == CombatPhase.Quiet ? FaceSeconds : 0f;
        bool slam = phase == CombatPhase.Assault;
        turnClock = 0f;
        turnSeconds = slam ? SlamSeconds : TurnSeconds;
        turnGrow = slam ? SlamGrow : TurnGrow;
        turnTilt = slam ? SlamTilt : TurnTilt;
    }

    private void PoseEmblem(float dt)
    {
        emblem.gameObject.SetActive(emblemShow > 0f);
        if (!emblem.gameObject.activeSelf) return;
        // Fading out once the run is over, it keeps the phase it last showed.
        var phase = shown != CombatPhase.Quiet ? shown : last.Phase;
        emblemGroup.alpha = Ease.Out(emblemShow);
        faceClock += dt;
        float blend = Ease.Out(faceClock / FaceSeconds);
        face.color = Color.Lerp(faceFrom, faceTo, blend);
        gaugeImage.color = Color.Lerp(gaugeFrom, gaugeTo, blend);
        gaugeLightImage.color = Color.Lerp(lightFrom, lightTo, blend);
        edgeShow = Mathf.MoveTowards(edgeShow, phase == CombatPhase.Assault ? 1f : 0f, dt / EdgeFade);
        edgeGroup.alpha = edgeShow;

        // The gauge: the rest left (draining toward the anticipation), the assault coming (rising), the assault left
        // (draining). A rest with no assault to come stays full.
        float lead = CampaignPhases.Lead, share = 1f;
        bool known = !float.IsInfinity(last.Until);
        if (phase == CombatPhase.Control && known)
        {
            float left = Mathf.Max(0f, last.Until - lead);
            restSpan = Mathf.Max(restSpan, left);
            share = restSpan > 0f ? left / restSpan : 1f;
        }
        else if (phase == CombatPhase.Anticipation) share = 1f - Mathf.Clamp01((known ? last.Until : lead) / Mathf.Max(.01f, lead));
        else if (phase == CombatPhase.Assault) share = known ? Mathf.Clamp01(last.Until / AssaultSpan()) : 1f;
        gauge.sizeDelta = new Vector2(0f, TabHeight * share);
        gaugeLight.sizeDelta = new Vector2(0f, Mathf.Min(GaugeLight, TabHeight * share));

        // The rest's moon over the time to the next assault, the anticipation's seconds, or the assault's warning.
        bool rest = phase == CombatPhase.Control, waiting = phase == CombatPhase.Anticipation, counted = rest && known;
        moon.gameObject.SetActive(rest);
        moon.anchoredPosition = new Vector2(0f, -(counted ? MoonDrop : MoonAlone));
        next.gameObject.SetActive(counted);
        if (counted)
        {
            int seconds = Mathf.CeilToInt(last.Until);
            if (seconds != nextShown)
            {
                nextShown = seconds;
                nextText.text = DeathCard.Clock(seconds);
            }
        }
        count.gameObject.SetActive(waiting);
        if (waiting)
        {
            int seconds = Mathf.Max(1, Mathf.CeilToInt(known ? last.Until : lead));
            if (seconds != countShown)
            {
                if (countShown >= 0) countClock = 0f;
                countShown = seconds;
                countText.text = countShade.text = seconds.ToString();
            }
        }
        float pop = 0f;
        if (countClock >= 0f)
        {
            countClock += dt;
            float k = Mathf.Clamp01(countClock / CountPopSeconds);
            pop = 1f - Ease.Out(k);
            if (k >= 1f) countClock = -1f;
        }
        count.localScale = Vector3.one * (1f + CountPop * pop);
        warn.gameObject.SetActive(phase == CombatPhase.Assault);

        // A new phase turns it, leaning back as it settles.
        float settle = 0f;
        if (turnClock >= 0f)
        {
            turnClock += dt;
            float k = Mathf.Clamp01(turnClock / turnSeconds);
            settle = 1f - KitMotion.BackOut(k);
            if (k >= 1f) turnClock = -1f;
        }
        emblemBody.localScale = Vector3.one * (1f + turnGrow * settle);
        emblemBody.localRotation = Quaternion.Euler(0f, 0f, turnTilt * settle);
    }

    private void BuildEmblem()
    {
        emblem = Corner("Emblem", root);
        emblem.sizeDelta = new Vector2(TabWidth, TabHeight);
        emblemGroup = emblem.gameObject.AddComponent<CanvasGroup>();
        // It turns about its middle: its ink shadow, the white edge, the face with the gauge rising from its foot.
        emblemBody = Centred("Body", emblem);
        emblemBody.sizeDelta = new Vector2(TabWidth, TabHeight);
        var drop = Around("Shadow", emblemBody, 0f, 0f);
        drop.offsetMin = drop.offsetMax = new Vector2(EmblemDrop, -EmblemDrop);
        KitStyle.Fill(drop, KitStyle.Ink);
        var edge = Around("Edge", emblemBody, EmblemEdge, EmblemEdge);
        edgeGroup = edge.gameObject.AddComponent<CanvasGroup>();
        edgeGroup.alpha = 0f;
        KitStyle.Fill(edge, Color.white);
        var plate = Around("Face", emblemBody, 0f, 0f);
        face = KitStyle.Fill(plate, RestDeep);
        gauge = FeelUI.Rect("Gauge", plate);
        gauge.anchorMin = Vector2.zero;
        gauge.anchorMax = Vector2.right;
        gauge.pivot = new Vector2(.5f, 0f);
        gauge.sizeDelta = new Vector2(0f, TabHeight);
        gaugeImage = KitStyle.Fill(gauge, RestBlue);
        gaugeLight = FeelUI.Rect("Light", gauge);
        gaugeLight.anchorMin = Vector2.up;
        gaugeLight.anchorMax = Vector2.one;
        gaugeLight.pivot = new Vector2(.5f, 1f);
        gaugeLightImage = KitStyle.Raw(gaugeLight, KitShapes.Drop, RestLight);
        var lip = FeelUI.Rect("Lip", gauge);
        lip.anchorMin = Vector2.up;
        lip.anchorMax = Vector2.one;
        lip.pivot = new Vector2(.5f, 1f);
        lip.sizeDelta = new Vector2(0f, GaugeLip);
        KitStyle.Fill(lip, KitStyle.With(Color.white, .6f));
        faceFrom = faceTo = RestDeep;
        gaugeFrom = gaugeTo = RestBlue;
        lightFrom = lightTo = RestLight;

        // The rest's moon and the time to the next assault under it.
        moon = FeelUI.Rect("Moon", plate);
        moon.anchorMin = moon.anchorMax = new Vector2(.5f, 1f);
        moon.pivot = new Vector2(.5f, .5f);
        moon.sizeDelta = new Vector2(MoonSize, MoonSize);
        Mark(moon, "Shadow", KitShapes.Moon, MarkShade, new Vector2(MarkShadow, -MarkShadow));
        Mark(moon, "Moon", KitShapes.Moon, Color.white, Vector2.zero);
        next = FeelUI.Rect("Next", plate);
        next.anchorMin = next.anchorMax = new Vector2(.5f, 0f);
        next.pivot = new Vector2(.5f, .5f);
        next.anchoredPosition = new Vector2(0f, NextLift);
        next.sizeDelta = new Vector2(TabWidth, NextSize + 4f);
        nextText = KitStyle.Flat(next, "", NextSize, TextAlignmentOptions.Center, Color.white);
        nextText.textWrappingMode = TextWrappingModes.NoWrap;

        // The anticipation's seconds, big, leaning and edged in ink over their shadow.
        count = Centred("Count", plate);
        count.sizeDelta = new Vector2(TabWidth, TabHeight);
        countShade = Count(count, "Shadow", KitStyle.Ink, new Vector2(CountShadow, -CountShadow));
        countText = Count(count, "Seconds", Color.white, Vector2.zero);

        // The assault's warning triangle, its ! in ink, over its shadow.
        warn = Centred("Warning", plate);
        warn.sizeDelta = new Vector2(WarnSize, WarnSize);
        Mark(warn, "Shadow", KitShapes.Warning, KitStyle.Ink, new Vector2(WarnShadow, -WarnShadow));
        Mark(warn, "Triangle", KitShapes.Warning, Color.white, Vector2.zero);
        Mark(warn, "Mark", KitShapes.WarningMark, KitStyle.Ink, Vector2.zero);
        moon.gameObject.SetActive(false);
        next.gameObject.SetActive(false);
        count.gameObject.SetActive(false);
        warn.gameObject.SetActive(false);
        emblem.gameObject.SetActive(false);
    }

    // One of the emblem's drawn marks, filling its rect, offset (a shadow) and tinted.
    private static void Mark(RectTransform parent, string label, Texture texture, Color color, Vector2 offset)
    {
        var rect = Around(label, parent, 0f, 0f);
        rect.offsetMin = rect.offsetMax = offset;
        KitStyle.Raw(rect, texture, color);
    }

    // The anticipation's seconds (or their shadow): big, leaning, edged in ink.
    private static TextMeshProUGUI Count(RectTransform parent, string label, Color color, Vector2 offset)
    {
        var rect = Around(label, parent, 0f, 0f);
        rect.offsetMin = rect.offsetMax = offset;
        var text = KitStyle.Flat(rect, "", CountSize, TextAlignmentOptions.Center, color);
        text.fontStyle |= FontStyles.Bold | FontStyles.Italic;
        var edged = CombatUIAssets.Outlined(text.font, KitStyle.Ink, CountEdge);
        if (edged != null) text.fontSharedMaterial = edged;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }
}
