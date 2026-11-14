using UnityEngine;
using UnityEngine.UI;

// PerkCardScreen: How grand a card is dressed (Tables/PerkCards). One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    private const string LookTable = "PerkCards";
    private const int MaxMotes = 16;
    // The trim's inset from the card's edge and its thickness (px); the motes' margin from the plate's edges (px).
    private const float TrimInset = 9f, TrimThick = 2f, MoteMargin = 22f;

    /// <summary>
    /// How a card is dressed (the user, 2026-11-14, as the final cards went: "1단계만 있는 카드는 특수로 넣고, 단계 진행된
    /// 카드들은 더 화려하게 꾸미면 되잖아. 그니까 이 카드를 찍을 때 1단계가 되는 때보다 3단계가 되는 때가 더 멋있게
    /// 화려하게"): by the level picking it brings, its last level the grandest, or as a special card when it has one level
    /// only. The last of a two-level card is Level3 too.
    /// </summary>
    private enum Tier { Level1, Level2, Level3, Special }

    /// <summary>One tier's dressing, read from Tables/PerkCards as the cards are dealt.</summary>
    private sealed class Look
    {
        public float Border, Glow, Breath, ShineEvery, Shine, Shake;
        public int Motes, Shards, Rings;
        public Color Frame, MoteA, MoteB;
        public Color? Trim, PatternColor;
        public Texture2D Pattern;
    }

    private static Tier TierOf(PlayerPerks.Perk perk)
    {
        if (perk == null) return Tier.Level1;
        if (perk.Levels <= 1) return Tier.Special;
        int next = PlayerPerks.Level(perk.Id) + 1;
        return next >= perk.Levels ? Tier.Level3 : next >= 2 ? Tier.Level2 : Tier.Level1;
    }

    // Read at each deal, so a table saved during play shows at the next one (GameTables.Reload).
    private static Look LookOf(Tier tier)
    {
        string row = tier.ToString();
        var look = new Look
        {
            Border = Mathf.Clamp(LookNumber(row, "border", Border), 1f, 20f),
            Frame = LookColour(row, "frame", tier == Tier.Special ? KitStyle.Accent : KitStyle.Ice) ?? KitStyle.Ice,
            Trim = LookColour(row, "trim", null),
            Glow = Mathf.Clamp01(LookNumber(row, "glow", 0f)),
            Breath = Mathf.Clamp01(LookNumber(row, "breath", 0f)),
            ShineEvery = Mathf.Max(.6f, LookNumber(row, "shineEvery", ShineEvery)),
            Shine = Mathf.Clamp01(LookNumber(row, "shine", .13f)),
            Motes = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "motes", 0f)), 0, MaxMotes),
            Shards = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "shards", ShardCount)), 0, 60),
            Rings = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "rings", 0f)), 0, 2),
            Shake = Mathf.Clamp(LookNumber(row, "shake", 14f), 0f, 40f),
            PatternColor = LookColour(row, "patternColor", null),
        };
        string pattern = GameTables.Text(LookTable, row, "pattern", "").Trim();
        look.Pattern = pattern == "stripes" ? KitShapes.Stripes : pattern == "halftone" ? KitShapes.Halftone : null;
        // The motes take the tier's own two colours by turns: its trim (or pink) and its frame.
        look.MoteA = look.Trim ?? KitStyle.Accent;
        look.MoteB = look.Frame;
        return look;
    }

    private static float LookNumber(string row, string key, float fallback) => GameTables.Number(LookTable, row, key, fallback);

    // A colour cell (#RRGGBB or #RRGGBBAA): "-" for none, a missing or unreadable one the fallback.
    private static Color? LookColour(string row, string key, Color? fallback)
    {
        string cell = GameTables.Text(LookTable, row, key, "").Trim();
        if (cell.Length == 0) return fallback;
        if (cell == "-") return null;
        return ColorUtility.TryParseHtmlString(cell, out var colour) ? colour : fallback;
    }

    // The dressing every card carries, shown as its look asks (from BuildCard, over the plate and under the words): a
    // pattern laid over the plate, a thin trim inside the frame, and motes of light rising inside it (under the plate's
    // mask, so they stay on the card).
    private static void BuildDecor(Card card)
    {
        var pattern = KitStyle.Stretch("Pattern", card.PlateRect);
        card.Pattern = pattern.gameObject.AddComponent<RawImage>();
        card.Pattern.raycastTarget = false;
        card.Pattern.enabled = false;

        var trim = KitStyle.Stretch("Trim", card.Front);
        card.Trim = new Image[4];
        card.Trim[0] = TrimLine(trim, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -TrimInset), new Vector2(-TrimInset * 2f, TrimThick));
        card.Trim[1] = TrimLine(trim, "Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, TrimInset), new Vector2(-TrimInset * 2f, TrimThick));
        card.Trim[2] = TrimLine(trim, "Left", Vector2.zero, new Vector2(0f, 1f), new Vector2(TrimInset, 0f), new Vector2(TrimThick, -TrimInset * 2f));
        card.Trim[3] = TrimLine(trim, "Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-TrimInset, 0f), new Vector2(TrimThick, -TrimInset * 2f));

        var motes = KitStyle.Stretch("Motes", card.PlateRect);
        card.Motes = new RawImage[MaxMotes];
        for (int i = 0; i < MaxMotes; i++)
        {
            var rect = FeelUI.Rect("Mote", motes);
            float size = 5f + 5f * Frac(Seed(i) * 5.1f);
            rect.sizeDelta = new Vector2(size, size);
            var mote = rect.gameObject.AddComponent<RawImage>();
            mote.texture = KitShapes.Dot;
            mote.raycastTarget = false;
            rect.gameObject.SetActive(false);
            card.Motes[i] = mote;
        }
    }

    private static Image TrimLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 at, Vector2 size)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
        return KitStyle.Fill(rect, Color.clear);
    }

    // As a card is dealt (Fill): its tier and look, the frame as thick as the look says, the trim and the pattern on or
    // off, the shine's strength, and a special card's tag.
    private static void Dress(Card card)
    {
        card.Tier = TierOf(card.Perk);
        var look = card.Look = LookOf(card.Tier);
        card.PlateRect.sizeDelta = new Vector2(-look.Border * 2f, -look.Border * 2f);
        foreach (var line in card.Trim) line.gameObject.SetActive(look.Trim.HasValue);
        bool patterned = look.Pattern != null && look.PatternColor.HasValue;
        card.Pattern.enabled = patterned;
        if (patterned)
        {
            card.Pattern.texture = look.Pattern;
            card.Pattern.color = look.PatternColor.Value;
            // Tiled at the texture's own size.
            card.Pattern.uvRect = new Rect(0f, 0f, CardWidth / Mathf.Max(1, look.Pattern.width), CardHeight / Mathf.Max(1, look.Pattern.height));
        }
        card.ShineImage.color = new Color(1f, 1f, 1f, look.Shine);
        if (card.Tier == Tier.Special) card.Tag.text = Texts.Get("perk.tag.special");
    }

    // Every frame (AnimateCard): the trim follows the hover as the rest of the card does, the glow behind a grand card
    // breathes slowly (the user's rule: a smooth swell at most ~1.6 Hz dimming to about half, never a blink), and the
    // motes rise, each in its own lane and at its own pace, fading in and out as they go.
    private static void AnimateLook(Card card, float time)
    {
        var look = card.Look;
        if (look == null) return;
        if (look.Trim.HasValue)
        {
            Color trim = Color.Lerp(look.Trim.Value, KitStyle.With(KitStyle.Ink, .35f), card.Hover);
            foreach (var line in card.Trim) line.color = trim;
        }
        float breathHz = Mathf.Clamp(LookNumber("Glow", "breathHz", .6f), 0f, 1.6f);
        float rest = look.Glow * (1f - look.Breath * (.5f - .5f * Mathf.Cos(time * Mathf.PI * 2f * breathHz)));
        card.Glow.color = KitStyle.With(KitStyle.Accent, Mathf.Max(.5f * card.Hover, rest));
        bool shown = card.Front.gameObject.activeSelf;
        float width = CardWidth - MoteMargin * 2f, height = CardHeight - MoteMargin * 2f;
        for (int i = 0; i < MaxMotes; i++)
        {
            var mote = card.Motes[i];
            bool on = shown && i < look.Motes;
            if (mote.gameObject.activeSelf != on) mote.gameObject.SetActive(on);
            if (!on) continue;
            float seed = Seed(i), period = 3f + 2f * Frac(seed * 7.13f);
            float rise = Frac(time / period + seed);
            float x = (Frac(seed * 3.7f) - .5f) * width + Mathf.Sin((time + seed * 10f) * 1.3f) * 8f;
            mote.rectTransform.anchoredPosition = new Vector2(x, Mathf.Lerp(-height * .5f, height * .5f, rise));
            Color colour = i % 2 == 0 ? look.MoteA : look.MoteB;
            colour.a *= .55f * Mathf.Sin(rise * Mathf.PI);
            mote.color = colour;
        }
    }

    // A mote's own number between 0 and 1: the golden ratio's turns, spread evenly with no clump.
    private static float Seed(int i) => Frac(i * .618034f + .13f);

    private static float Frac(float value) => value - Mathf.Floor(value);
}
