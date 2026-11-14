using TMPro;
using UnityEngine;
using UnityEngine.UI;

// PerkCardScreen: How grand a card is dressed, and how the ones not picked burn away (Tables/PerkCards). One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    private const string LookTable = "PerkCards";
    private const int MaxMotes = 16, MaxGlints = 4, MaxOutlines = 2;
    // The trims' insets from the card's edge and their thickness, the motes' margin, the flame strip's height and how far
    // under the burn line it reaches, and the foil's tile on the card (px).
    private const float TrimInset = 9f, TrimThick = 2f, Trim2Inset = 15f, Trim2Thick = 1.5f, MoteMargin = 22f;
    private const float EdgeHeight = 46f, EdgeBelow = 16f, FoilTile = 480f;
    private static Texture2D foilTexture, glintTexture, flameTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ForgetTextures() => foilTexture = glintTexture = flameTexture = null;

    /// <summary>
    /// How a card is dressed (the user, 2026-11-14, as the final cards went: "1단계만 있는 카드는 특수로 넣고, 단계 진행된
    /// 카드들은 더 화려하게 … 1단계가 되는 때보다 3단계가 되는 때가 더 멋있게 화려하게"; after the first draft page: "2단계는
    /// 살짝 연한 황금빛으로", "3단계가 황금빛", every grand one "더 화려했음 좋겠어"): by the level picking it brings, its
    /// last the grandest in gold, the middle pale gold, or as a special card in pink when it has one level only. The last
    /// level of a two-level card is Level3 too.
    /// </summary>
    private enum Tier { Level1, Level2, Level3, Special }

    /// <summary>One tier's dressing, read from Tables/PerkCards as the cards are dealt.</summary>
    private sealed class Look
    {
        public float Border, Foil, Spot, Glow, Breath, ShineEvery, Shine, Shake;
        public int Corners, Motes, Glints, Sparks, Outlines;
        public bool Star;
        public Color Frame, Colour, GlowColour, MoteA, MoteB;
        public Color? Trim, Trim2, PatternColour;
        public Texture2D Pattern;
    }

    /// <summary>A card's corner pieces and its crest: an arm along each edge and a diamond tip at every corner, a diamond
    /// on the top edge between two small ones, each backed a little darker so it reads over the frame.</summary>
    private sealed class Ornament
    {
        public RectTransform Root;
        public readonly RectTransform[] Arms = new RectTransform[8], Tips = new RectTransform[4], TipBacks = new RectTransform[4];
        public RectTransform Crest, CrestBack, DotLeft, DotRight;
        public Image[] Lit;
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
            Colour = LookColour(row, "tier", KitStyle.Accent) ?? KitStyle.Accent,
            Trim = LookColour(row, "trim", null),
            Trim2 = LookColour(row, "trim2", null),
            PatternColour = LookColour(row, "patternColor", null),
            Foil = Mathf.Clamp01(LookNumber(row, "foil", 0f)),
            Corners = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "corners", 0f)), 0, 2),
            Spot = Mathf.Clamp01(LookNumber(row, "spot", 0f)),
            Glow = Mathf.Clamp01(LookNumber(row, "glow", 0f)),
            Breath = Mathf.Clamp01(LookNumber(row, "breath", 0f)),
            ShineEvery = Mathf.Max(.6f, LookNumber(row, "shineEvery", ShineEvery)),
            Shine = Mathf.Clamp01(LookNumber(row, "shine", .13f)),
            Motes = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "motes", 0f)), 0, MaxMotes),
            Glints = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "glints", 0f)), 0, MaxGlints),
            Sparks = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "sparks", 10f)), 0, 60),
            Outlines = Mathf.Clamp(Mathf.RoundToInt(LookNumber(row, "outlines", 1f)), 0, MaxOutlines),
            Star = LookNumber(row, "star", 0f) > .5f,
            Shake = Mathf.Clamp(LookNumber(row, "shake", 8f), 0f, 40f),
        };
        look.GlowColour = LookColour(row, "glowColor", look.Colour) ?? look.Colour;
        string pattern = GameTables.Text(LookTable, row, "pattern", "").Trim();
        look.Pattern = pattern == "stripes" ? KitShapes.Stripes : pattern == "halftone" ? KitShapes.Halftone : null;
        // The motes take the tier's own two colours by turns: its trim (or its colour) and its frame.
        look.MoteA = look.Trim ?? look.Colour;
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

    // ---- Textures, drawn once ----

    // Soft bands of light on the slant (115°) a 480 px tile apart: the foil's sheen running round the frame.
    private static Texture2D FoilBands => foilTexture != null ? foilTexture : foilTexture = Drawn("Perk Foil", 256, 256, true, (x, y) =>
    {
        float d = Mathf.Repeat(x + y * .466f, 256f) / 256f;
        float band = Mathf.Clamp01(1f - Mathf.Abs(d - .25f) / .07f);
        return new Color(1f, 1f, 1f, band * band);
    });

    // A four-point glint: two thin spikes and a small hot core.
    private static Texture2D GlintStar => glintTexture != null ? glintTexture : glintTexture = Drawn("Perk Glint", 64, 64, false, (x, y) =>
    {
        float u = (x + .5f) / 32f - 1f, v = (y + .5f) / 32f - 1f, d = Mathf.Sqrt(u * u + v * v);
        float spike = Mathf.Exp(-Mathf.Abs(u) * 26f) * Mathf.Max(0f, 1f - Mathf.Abs(v)) + Mathf.Exp(-Mathf.Abs(v) * 26f) * Mathf.Max(0f, 1f - Mathf.Abs(u));
        return new Color(1f, 1f, 1f, Mathf.Clamp01(spike * .9f + Mathf.Exp(-d * d * 30f)));
    });

    // Tongues of flame along a line, tiling every 256 px: hot at the line, orange up the tongue, red at the tips. In its
    // own colours (the strip is drawn white).
    private static Texture2D Flames => flameTexture != null ? flameTexture : flameTexture = Drawn("Perk Flames", 256, 46, true, (x, y) =>
    {
        float t = x / 256f * Mathf.PI * 2f;
        float top = 16f + 7f * Mathf.Sin(t * 3f + .4f) + 5f * Mathf.Sin(t * 7f + 1.3f) + 3f * Mathf.Sin(t * 13f + 2.1f) + 2f * Mathf.Sin(t * 23f);
        if (y > 30 || y > top) return Color.clear;
        float along = y / Mathf.Max(4f, top);
        float alpha = Mathf.Clamp01(1.15f - along) * (y < 3 ? y / 3f + .2f : 1f);
        float k = Mathf.Clamp01(along);
        return new Color(1f, Mathf.Lerp(236f, 70f, k) / 255f, Mathf.Lerp(170f, 30f, k) / 255f, Mathf.Clamp01(alpha));
    });

    private static Texture2D Drawn(string name, int width, int height, bool tiles, System.Func<int, int, Color> pixel)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
        texture.SetPixels32(pixels);
        texture.wrapMode = tiles ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(false, true);
        return texture;
    }

    // ---- Building ----

    // The dressing every card carries, shown as its look asks (from BuildCard, over the plate and under the words): the
    // foil's sheen over the frame (under the plate, so only the frame shows it), a soft spot of the tier's light behind the
    // icon, a pattern and rising motes inside the plate (under its mask), two trims inside the frame, and the corner pieces.
    private static void BuildDecor(Card card)
    {
        var foil = KitStyle.Stretch("Foil", card.Front);
        foil.SetSiblingIndex(card.Frame.transform.GetSiblingIndex() + 1);
        card.Foil = foil.gameObject.AddComponent<RawImage>();
        card.Foil.texture = FoilBands;
        card.Foil.raycastTarget = false;

        var spot = FeelUI.Rect("Spot", card.PlateRect);
        spot.SetAsFirstSibling();
        spot.anchorMin = spot.anchorMax = new Vector2(.5f, 1f);
        spot.anchoredPosition = new Vector2(0f, -150f);
        spot.sizeDelta = new Vector2(300f, 260f);
        card.Spot = spot.gameObject.AddComponent<RawImage>();
        card.Spot.texture = KitShapes.Glow;
        card.Spot.raycastTarget = false;

        var pattern = KitStyle.Stretch("Pattern", card.PlateRect);
        card.Pattern = pattern.gameObject.AddComponent<RawImage>();
        card.Pattern.raycastTarget = false;
        card.Pattern.enabled = false;

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

        card.Trim = FrameLines(card.Front, "Trim", TrimInset, TrimThick);
        card.Trim2 = FrameLines(card.Front, "Trim 2", Trim2Inset, Trim2Thick);
        card.Ornament = BuildOrnament(card.Front);
    }

    // Four lines <paramref name="inset"/> in from the card's edges, <paramref name="thick"/> thick.
    private static Image[] FrameLines(RectTransform parent, string name, float inset, float thick)
    {
        var holder = KitStyle.Stretch(name, parent);
        return new[]
        {
            Line(holder, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -inset), new Vector2(-inset * 2f, thick)),
            Line(holder, "Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, inset), new Vector2(-inset * 2f, thick)),
            Line(holder, "Left", Vector2.zero, new Vector2(0f, 1f), new Vector2(inset, 0f), new Vector2(thick, -inset * 2f)),
            Line(holder, "Right", new Vector2(1f, 0f), Vector2.one, new Vector2(-inset, 0f), new Vector2(thick, -inset * 2f)),
        };
    }

    private static Image Line(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 at, Vector2 size)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
        return KitStyle.Fill(rect, Color.clear);
    }

    private static Ornament BuildOrnament(RectTransform front)
    {
        var o = new Ornament { Root = KitStyle.Stretch("Ornament", front) };
        var lit = new System.Collections.Generic.List<Image>();
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 at = new Vector2(corner % 2, corner < 2 ? 1f : 0f);
            for (int arm = 0; arm < 2; arm++)
            {
                var rect = FeelUI.Rect(arm == 0 ? "Arm" : "Arm Down", o.Root);
                rect.anchorMin = rect.anchorMax = rect.pivot = at;
                lit.Add(KitStyle.Fill(rect, Color.white));
                o.Arms[corner * 2 + arm] = rect;
            }
            o.TipBacks[corner] = Diamond(o.Root, "Tip Back", at, KitStyle.With(KitStyle.Ink, .9f));
            o.Tips[corner] = Diamond(o.Root, "Tip", at, Color.white);
            lit.Add(o.Tips[corner].GetComponent<Image>());
        }
        var top = new Vector2(.5f, 1f);
        o.CrestBack = Diamond(o.Root, "Crest Back", top, KitStyle.With(KitStyle.Ink, .9f));
        o.Crest = Diamond(o.Root, "Crest", top, Color.white);
        o.DotLeft = Diamond(o.Root, "Crest Left", top, Color.white);
        o.DotRight = Diamond(o.Root, "Crest Right", top, Color.white);
        lit.Add(o.Crest.GetComponent<Image>());
        lit.Add(o.DotLeft.GetComponent<Image>());
        lit.Add(o.DotRight.GetComponent<Image>());
        o.Lit = lit.ToArray();
        return o;
    }

    private static RectTransform Diamond(RectTransform parent, string name, Vector2 anchor, Color colour)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        KitStyle.Fill(rect, colour);
        return rect;
    }

    // The corner pieces' size: 1 small (arms 26, thick 5), 2 large (arms 40, thick 6) with the crest on top.
    private static void ShapeOrnament(Ornament o, int size)
    {
        o.Root.gameObject.SetActive(size > 0);
        if (size <= 0) return;
        float arm = size > 1 ? 40f : 26f, thick = size > 1 ? 6f : 5f, tip = size > 1 ? 13f : 9f, outward = 4f;
        for (int corner = 0; corner < 4; corner++)
        {
            // Inward from this corner along x and along y (up is +y).
            float inX = corner % 2 == 0 ? 1f : -1f, inY = corner < 2 ? -1f : 1f;
            Vector2 origin = new Vector2(-inX * outward, -inY * outward);
            var along = o.Arms[corner * 2];
            along.anchoredPosition = origin;
            along.sizeDelta = new Vector2(arm, thick);
            var down = o.Arms[corner * 2 + 1];
            down.anchoredPosition = origin;
            down.sizeDelta = new Vector2(thick, arm);
            Vector2 tipAt = origin + new Vector2(inX, inY) * (thick * .5f);
            o.Tips[corner].anchoredPosition = o.TipBacks[corner].anchoredPosition = tipAt;
            o.Tips[corner].sizeDelta = new Vector2(tip, tip);
            o.TipBacks[corner].sizeDelta = new Vector2(tip + 4f, tip + 4f);
        }
        bool crest = size > 1;
        o.Crest.gameObject.SetActive(crest);
        o.CrestBack.gameObject.SetActive(crest);
        o.DotLeft.gameObject.SetActive(crest);
        o.DotRight.gameObject.SetActive(crest);
        o.Crest.sizeDelta = new Vector2(22f, 22f);
        o.CrestBack.sizeDelta = new Vector2(28f, 28f);
        o.DotLeft.sizeDelta = o.DotRight.sizeDelta = new Vector2(7f, 7f);
        o.Crest.anchoredPosition = o.CrestBack.anchoredPosition = Vector2.zero;
        o.DotLeft.anchoredPosition = new Vector2(-22f, 0f);
        o.DotRight.anchoredPosition = new Vector2(22f, 0f);
    }

    // Glints on the frame, over everything on the card's face (from BuildCard, last on it).
    private static void BuildGlints(Card card)
    {
        var holder = KitStyle.Stretch("Glints", card.Front);
        card.Glints = new RawImage[MaxGlints];
        for (int i = 0; i < MaxGlints; i++)
        {
            var rect = FeelUI.Rect("Glint", holder);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            var glint = rect.gameObject.AddComponent<RawImage>();
            glint.texture = GlintStar;
            glint.raycastTarget = false;
            rect.gameObject.SetActive(false);
            card.Glints[i] = glint;
        }
    }

    // The burn's own pieces (from BuildCard): the char, darkening the card just above the burn line (under the burn's
    // mask, hung from its bottom so it rides the line), and the strip of flames on the line itself (outside the mask).
    private static void BuildBurn(Card card)
    {
        var charred = FeelUI.Rect("Char", card.Burn);
        charred.anchorMin = Vector2.zero;
        charred.anchorMax = new Vector2(1f, 0f);
        charred.pivot = new Vector2(.5f, 0f);
        charred.anchoredPosition = Vector2.zero;
        charred.sizeDelta = new Vector2(0f, 90f);
        card.CharImage = charred.gameObject.AddComponent<RawImage>();
        card.CharImage.texture = KitShapes.Drop;
        // Drop is clear at its foot: turned over, dark at the line and gone above it.
        card.CharImage.uvRect = new Rect(0f, 1f, 1f, -1f);
        card.CharImage.color = new Color(.11f, .03f, .01f, .95f);
        card.CharImage.raycastTarget = false;
        card.CharImage.enabled = false;

        card.Edge = FeelUI.Rect("Flames", card.Body);
        card.Edge.anchorMin = card.Edge.anchorMax = card.Edge.pivot = new Vector2(.5f, 0f);
        card.Edge.sizeDelta = new Vector2(CardWidth + 28f, EdgeHeight);
        card.EdgeImage = card.Edge.gameObject.AddComponent<RawImage>();
        card.EdgeImage.texture = Flames;
        card.EdgeImage.raycastTarget = false;
        card.Edge.gameObject.SetActive(false);
    }

    // A pick's own flourish round its card (from BuildCard, on the card's root: it stays where the card was while the card
    // flies to its badge): its outline springing out once or twice, and a star flaring at its corner.
    private static void BuildPickFlourish(Card card)
    {
        card.Outlines = new RectTransform[MaxOutlines];
        card.OutlineLines = new Image[MaxOutlines][];
        for (int i = 0; i < MaxOutlines; i++)
        {
            card.Outlines[i] = KitStyle.Stretch("Outline", card.Root);
            card.OutlineLines[i] = FrameLines(card.Outlines[i], "Lines", 0f, 3f);
            card.Outlines[i].gameObject.SetActive(false);
        }
        var star = FeelUI.Rect("Pick Star", card.Root);
        star.anchoredPosition = new Vector2(CardWidth * .5f - 36f, CardHeight * .5f - 34f);
        star.sizeDelta = new Vector2(150f, 150f);
        card.PickStar = star.gameObject.AddComponent<RawImage>();
        card.PickStar.texture = GlintStar;
        card.PickStar.raycastTarget = false;
        star.gameObject.SetActive(false);
    }

    // The stamp a pick slams onto its card (the user, 2026-11-14: "눌렀다는 느낌이 덜해. 고른지 모르겠어"): what the pick
    // made of it, 획득 (a new card), 강화 (a level up) or 최대 강화 (its last), slanted across its middle in the card's colour,
    // ringed dark and then in its colour again so it reads over any card. On the card's body, outside the burn's mask, so it
    // rides the card into its badge.
    private static void BuildStamp(Card card)
    {
        var stamp = card.Stamp = FeelUI.Rect("Stamp", card.Body);
        stamp.anchoredPosition = new Vector2(0f, 17f);
        stamp.sizeDelta = new Vector2(352f, 90f);
        stamp.localRotation = Quaternion.Euler(0f, 0f, 8f);
        card.StampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
        card.StampGroup.blocksRaycasts = card.StampGroup.interactable = false;
        card.StampRing = KitStyle.Fill(stamp, Color.white);
        var ink = FeelUI.Rect("Ink", stamp);
        ink.sizeDelta = new Vector2(346f, 84f);
        KitStyle.Fill(ink, KitStyle.With(KitStyle.Ink, .92f));
        var face = FeelUI.Rect("Face", stamp);
        face.sizeDelta = new Vector2(340f, 78f);
        card.StampFace = KitStyle.Fill(face, Color.white);
        card.StampLabel = KitStyle.Flat(face, "", 40f, TextAlignmentOptions.Center, KitStyle.Ink);
        stamp.gameObject.SetActive(false);
    }

    private static string StampWord(Tier tier)
        => Texts.Get(tier == Tier.Level3 ? "perk.picked.max" : tier == Tier.Level2 ? "perk.picked.up" : "perk.picked.new");

    // The picked card's stamp, <paramref name="after"/> seconds after it is let go: slammed on from large as it pops out,
    // with a small jolt as it lands, and kept on it.
    private void AnimateStamp(Card card, float after)
    {
        bool on = after > .03f;
        if (card.Stamp.gameObject.activeSelf != on) card.Stamp.gameObject.SetActive(on);
        if (!on) return;
        float k = Mathf.Clamp01((after - .03f) / .12f);
        float scale = Mathf.Lerp(1.8f, 1f, KitReveal.EaseOut(k));
        card.Stamp.localScale = new Vector3(scale, scale, 1f);
        card.StampGroup.alpha = k;
        if (k < 1f || card.StampShook) return;
        card.StampShook = true;
        shake = Mathf.Max(shake, 7f);
    }

    // ---- Dressing, as a card is dealt (Fill) ----

    private static void Dress(Card card)
    {
        card.Tier = TierOf(card.Perk);
        var look = card.Look = LookOf(card.Tier);
        card.PlateRect.sizeDelta = new Vector2(-look.Border * 2f, -look.Border * 2f);
        foreach (var line in card.Trim) line.gameObject.SetActive(look.Trim.HasValue);
        foreach (var line in card.Trim2) line.gameObject.SetActive(look.Trim2.HasValue);
        card.Foil.enabled = look.Foil > 0f;
        card.Foil.color = new Color(1f, 1f, 1f, look.Foil);
        card.Spot.enabled = look.Spot > 0f;
        card.Spot.color = KitStyle.With(look.Colour, look.Spot);
        bool patterned = look.Pattern != null && look.PatternColour.HasValue;
        card.Pattern.enabled = patterned;
        if (patterned)
        {
            card.Pattern.texture = look.Pattern;
            card.Pattern.color = look.PatternColour.Value;
            // Tiled at the texture's own size.
            card.Pattern.uvRect = new Rect(0f, 0f, CardWidth / Mathf.Max(1, look.Pattern.width), CardHeight / Mathf.Max(1, look.Pattern.height));
        }
        ShapeOrnament(card.Ornament, look.Corners);
        foreach (var piece in card.Ornament.Lit) piece.color = look.Colour;
        card.ShineImage.color = new Color(1f, 1f, 1f, look.Shine);
        for (int i = 0; i < MaxGlints; i++) card.Glints[i].color = i % 2 == 0 ? look.Colour : Color.white;
        card.PickStar.color = look.Colour;
        card.StampRing.color = card.StampFace.color = look.Colour;
        card.StampLabel.text = StampWord(card.Tier);
        if (card.Tier == Tier.Special) card.Tag.text = Texts.Get("perk.tag.special");
        ResetFlourish(card);
    }

    // Nothing of a pick or a burn left on a card dealt again.
    private static void ResetFlourish(Card card)
    {
        card.BurnFrom = -1f;
        card.EmbersLeft = 0;
        card.Burn.offsetMin = Vector2.zero;
        card.CharImage.enabled = false;
        card.Edge.gameObject.SetActive(false);
        foreach (var outline in card.Outlines) outline.gameObject.SetActive(false);
        card.PickStar.gameObject.SetActive(false);
        card.Stamp.gameObject.SetActive(false);
        card.StampShook = false;
    }

    // ---- Every frame (AnimateCard) ----

    // The trims follow the hover as the rest of the card does, the foil's light runs round the frame, the glow behind a
    // grand card breathes slowly (the user's rule: a smooth swell at most ~1.6 Hz dimming to about half, never a blink),
    // the motes rise in their lanes, and the glints fade in and out somewhere new on the frame each time.
    private static void AnimateLook(Card card, float time)
    {
        var look = card.Look;
        if (look == null) return;
        Color hoverInk = KitStyle.With(KitStyle.Ink, .35f);
        if (look.Trim.HasValue) foreach (var line in card.Trim) line.color = Color.Lerp(look.Trim.Value, hoverInk, card.Hover);
        if (look.Trim2.HasValue) foreach (var line in card.Trim2) line.color = Color.Lerp(look.Trim2.Value, hoverInk, card.Hover);
        if (look.Foil > 0f)
        {
            float speed = LookNumber("Foil", "speed", 90f);
            card.Foil.uvRect = new Rect(-time * speed / FoilTile, 0f, CardWidth / FoilTile, CardHeight / FoilTile);
        }
        float breathHz = Mathf.Clamp(LookNumber("Glow", "breathHz", .6f), 0f, 1.6f);
        float rest = look.Glow * (1f - look.Breath * (.5f - .5f * Mathf.Cos(time * Mathf.PI * 2f * breathHz)));
        card.Glow.color = KitStyle.With(look.GlowColour, Mathf.Max(.5f * card.Hover, rest));

        bool shown = card.Front.gameObject.activeSelf;
        float width = CardWidth - MoteMargin * 2f, height = CardHeight - MoteMargin * 2f;
        for (int i = 0; i < MaxMotes; i++)
        {
            var mote = card.Motes[i];
            bool on = shown && i < look.Motes;
            if (mote.gameObject.activeSelf != on) mote.gameObject.SetActive(on);
            if (!on) continue;
            float seed = Seed(i), period = 3f + 2f * Frac(seed * 7.13f), rise = Frac(time / period + seed);
            float x = (Frac(seed * 3.7f) - .5f) * width + Mathf.Sin((time + seed * 10f) * 1.3f) * 8f;
            mote.rectTransform.anchoredPosition = new Vector2(x, Mathf.Lerp(-height * .5f, height * .5f, rise));
            Color colour = i % 2 == 0 ? look.MoteA : look.MoteB;
            colour.a *= .6f * Mathf.Sin(rise * Mathf.PI);
            mote.color = colour;
        }

        for (int i = 0; i < MaxGlints; i++)
        {
            var glint = card.Glints[i];
            bool on = shown && i < look.Glints;
            float period = 2.4f + i * .7f, shifted = time + i * .9f;
            int cycle = Mathf.FloorToInt(shifted / period);
            float local = shifted - cycle * period, life = 1.3f;
            float alpha = local < life ? Mathf.Sin(local / life * Mathf.PI) : 0f;
            on &= alpha > .01f;
            if (glint.gameObject.activeSelf != on) glint.gameObject.SetActive(on);
            if (!on) continue;
            float h = Hash(cycle * 7 + i * 13 + card.Root.GetSiblingIndex() * 31);
            int side = Mathf.FloorToInt(Hash(cycle * 3 + i) * 4f) % 4;
            Vector2 at = side == 0 ? new Vector2(h * CardWidth, -2f) : side == 1 ? new Vector2(CardWidth - 2f, -h * CardHeight)
                : side == 2 ? new Vector2(h * CardWidth, -CardHeight + 2f) : new Vector2(2f, -h * CardHeight);
            float size = (26f + 14f * Hash(cycle + i * 5)) * (.6f + .4f * alpha);
            var rect = glint.rectTransform;
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, -local * 40f);
            Color colour = glint.color;
            colour.a = .95f * alpha;
            glint.color = colour;
        }
    }

    // The picked card's flourish, <paramref name="end"/> seconds after the pick (below 0: none): its outline springs out
    // and fades, once or twice, and a star flares at its corner.
    private static void AnimatePick(Card card, float end)
    {
        var look = card.Look;
        for (int i = 0; i < MaxOutlines; i++)
        {
            var outline = card.Outlines[i];
            float k = end >= 0f && look != null && i < look.Outlines ? Mathf.Clamp01((end - i * .08f) / .32f) : 1f;
            bool on = k < 1f && end >= i * .08f;
            if (outline.gameObject.activeSelf != on) outline.gameObject.SetActive(on);
            if (!on) continue;
            float grow = 6f + 34f * KitReveal.EaseOut(k);
            outline.offsetMin = new Vector2(-grow, -grow);
            outline.offsetMax = new Vector2(grow, grow);
            foreach (var line in card.OutlineLines[i]) line.color = KitStyle.With(look.Colour, .95f * (1f - k));
        }
        float s = end >= 0f && look != null && look.Star ? Mathf.Clamp01((end - .02f) / .45f) : 1f;
        bool star = s < 1f && end >= .02f;
        if (card.PickStar.gameObject.activeSelf != star) card.PickStar.gameObject.SetActive(star);
        if (!star) return;
        var colour = card.PickStar.color;
        colour.a = Mathf.Sin(s * Mathf.PI);
        card.PickStar.color = colour;
        float scale = .3f + 1.1f * KitReveal.EaseOut(s);
        card.PickStar.rectTransform.localScale = new Vector3(scale, scale, 1f);
        card.PickStar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -60f * s);
    }

    // A card not picked burns away from below (the user, 2026-11-14: "선택받지 못한 카드들은 불타 없어지는 연출로"): the
    // burn's mask rises over it faster and faster, the char rides the line, the flames run along it, and embers and a
    // little smoke rise off it. Nothing flashes: the flames scroll, the embers fade.
    private void AnimateBurn(Card card, float time, float dt)
    {
        if (card.BurnFrom < 0f || time < card.BurnFrom) return;
        float seconds = Mathf.Max(.1f, LookNumber("Burn", "seconds", .6f));
        float k = Mathf.Clamp01((time - card.BurnFrom) / seconds);
        float line = (CardHeight + 20f) * k * k + 6f;
        card.Burn.offsetMin = new Vector2(0f, line);
        bool burning = k < 1f;
        card.CharImage.enabled = burning;
        if (card.Edge.gameObject.activeSelf != burning) card.Edge.gameObject.SetActive(burning);
        card.Glow.color = KitStyle.With(card.Look != null ? card.Look.GlowColour : KitStyle.Accent, Mathf.Max(0f, 1f - k * 2f) * .2f);
        if (!burning) return;
        card.Edge.anchoredPosition = new Vector2(0f, line - EdgeBelow);
        card.EdgeImage.uvRect = new Rect(time * 60f / 256f, 0f, (CardWidth + 28f) / 256f, 1f);
        card.EdgeImage.color = new Color(1f, 1f, 1f, Mathf.Min(1f, Mathf.Min(k * 8f, (1f - k) * 6f + .3f)));
        float embers = Mathf.Max(0f, LookNumber("Burn", "embers", 34f));
        if (card.EmbersLeft > 0)
        {
            int n = Mathf.Min(card.EmbersLeft, Mathf.CeilToInt(embers * dt / seconds * 1.2f));
            Embers(card, n);
            card.EmbersLeft -= n;
        }
        if ((float)random.NextDouble() < dt * Mathf.Max(0f, LookNumber("Burn", "smoke", 4f)) * 1.6f) Smoke(card);
    }

    // A mote's own number between 0 and 1: the golden ratio's turns, spread evenly with no clump.
    private static float Seed(int i) => Frac(i * .618034f + .13f);

    // A steady number between 0 and 1 for any whole one: where a glint shows next.
    private static float Hash(int n) => Frac(Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f);

    private static float Frac(float value) => value - Mathf.Floor(value);
}
