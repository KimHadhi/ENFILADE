using TMPro;
using UnityEngine;
using UnityEngine.UI;

// PerkCardScreen: Building. One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    private void Build()
    {
        host = new GameObject("Perk Cards", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        FeelUI.SetupOverlay(host, Order);
        var canvas = (RectTransform)host.transform;

        layer = KitStyle.Stretch("Cards", canvas);
        layerGroup = layer.gameObject.AddComponent<CanvasGroup>();
        veil = KitStyle.Fill(KitStyle.Stretch("Veil", layer), KitStyle.With(KitStyle.Night, 0f));
        // Everything that shakes on a landing lives on the stage.
        stage = KitStyle.Stretch("Stage", layer);

        band = FeelUI.Rect("Slash", stage);
        band.pivot = new Vector2(0f, .5f);
        band.anchoredPosition = new Vector2(-1500f, 40f);
        band.sizeDelta = new Vector2(3000f, 300f);
        band.localRotation = Quaternion.Euler(0f, 0f, 10f);
        bandImage = KitStyle.Fill(band, KitStyle.Accent);

        title = KitStyle.TopLeft("Title", stage, new Vector2(72f, 64f), new Vector2(700f, 76f));
        title.pivot = new Vector2(0f, .5f);
        title.anchoredPosition = new Vector2(72f, -102f);
        title.localRotation = Quaternion.Euler(0f, 0f, 3f);
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();
        KitStyle.Flat(title, Texts.Get("perk.title"), 60f, TextAlignmentOptions.MidlineLeft);
        var cost = KitStyle.TopLeft("Cost", title, new Vector2(6f, 80f), new Vector2(220f, 32f));
        cost.localRotation = Quaternion.Euler(0f, 0f, -6f);
        KitStyle.Fill(cost, KitStyle.Accent);
        costLabel = KitStyle.Flat(cost, "", 17f, TextAlignmentOptions.Center, KitStyle.Ink);
        // In the hub: these are practice cards, gone again when it is left (shown only then, Open).
        practiceLabel = KitStyle.Flat(KitStyle.TopLeft("Practice", title, new Vector2(10f, 124f), new Vector2(640f, 28f)),
            Texts.Get("perk.practice"), 18f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        practiceLabel.gameObject.SetActive(false);

        var hint = FeelUI.Rect("Hint", stage);
        hint.anchorMin = hint.anchorMax = hint.pivot = Vector2.one;
        hint.anchoredPosition = new Vector2(-72f, -70f);
        hint.sizeDelta = new Vector2(420f, 64f);
        var hintLabel = KitStyle.Flat(hint, Texts.Get("perk.hint"), 19f, TextAlignmentOptions.TopRight, KitStyle.Soft);
        hintLabel.fontStyle = FontStyles.Normal;
        hintLabel.lineSpacing = 10f;

        for (int i = 0; i < Choices; i++) cards.Add(BuildCard(i));

        // The perks held, as badges along the bottom.
        var trayCaption = FeelUI.Rect("Tray Caption", stage);
        trayCaption.anchorMin = trayCaption.anchorMax = trayCaption.pivot = new Vector2(.5f, 0f);
        trayCaption.anchoredPosition = new Vector2(0f, TrayY + BadgeHeight + 8f);
        trayCaption.sizeDelta = new Vector2(400f, 24f);
        KitStyle.Flat(trayCaption, Texts.Get("perk.tray"), 15f, TextAlignmentOptions.Center, KitStyle.Ice).characterSpacing = 4f;
        tray = FeelUI.Rect("Tray", stage);
        tray.anchorMin = tray.anchorMax = new Vector2(.5f, 0f);
        tray.anchoredPosition = new Vector2(0f, TrayY + BadgeHeight * .5f);
        var empty = FeelUI.Rect("None Yet", stage);
        empty.anchorMin = empty.anchorMax = new Vector2(.5f, 0f);
        empty.anchoredPosition = new Vector2(0f, TrayY + BadgeHeight * .5f);
        empty.sizeDelta = new Vector2(600f, BadgeHeight);
        emptyLabel = KitStyle.Flat(empty, Texts.Get("perk.tray.empty"), 17f, TextAlignmentOptions.Center, KitStyle.Faint);
        emptyLabel.fontStyle = FontStyles.Normal;

        fx = KitStyle.Stretch("Effects", stage);
        layer.gameObject.SetActive(false);
    }

    private Card BuildCard(int index)
    {
        var root = FeelUI.Rect("Card " + (index + 1), stage);
        root.anchoredPosition = new Vector2((index - (Choices - 1) * .5f) * Spread, 30f);
        root.sizeDelta = new Vector2(CardWidth, CardHeight);
        root.localRotation = Quaternion.Euler(0f, 0f, Tilts[index % Tilts.Length]);
        KitStyle.Fill(root, Color.clear).raycastTarget = true;
        var card = new Card { Root = root };

        // A pink glow behind it while it is pointed at (and a soft one at rest behind a grand card: Looks).
        var glow = FeelUI.Rect("Glow", root);
        glow.sizeDelta = new Vector2(CardWidth * 1.7f, CardHeight * 1.45f);
        card.Glow = glow.gameObject.AddComponent<RawImage>();
        card.Glow.texture = KitShapes.Glow;
        card.Glow.color = Color.clear;
        card.Glow.raycastTarget = false;

        card.Lift = KitStyle.Stretch("Lift", root);
        card.Body = KitStyle.Stretch("Body", card.Lift);
        // Everything on the card hangs under one mask whose foot rises as it burns away (Looks: AnimateBurn), each part the
        // card's own size and hung from its top, so the mask's rise only covers it and moves nothing.
        card.Burn = KitStyle.Stretch("Burn", card.Body);
        KitStyle.Fill(card.Burn, Color.white);
        card.Burn.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        card.Front = Hung("Front", card.Burn);
        card.Frame = KitStyle.Fill(KitStyle.Stretch("Frame", card.Front), KitStyle.Ice);
        var plate = card.PlateRect = KitStyle.Stretch("Plate", card.Front);
        plate.sizeDelta = new Vector2(-Border * 2f, -Border * 2f);
        card.Plate = KitStyle.Fill(plate, KitStyle.With(KitStyle.Night, .97f));
        plate.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        card.Shine = FeelUI.Rect("Shine", plate);
        card.Shine.sizeDelta = new Vector2(70f, CardHeight * 1.6f);
        card.Shine.localRotation = Quaternion.Euler(0f, 0f, -22f);
        card.ShineImage = KitStyle.Fill(card.Shine, new Color(1f, 1f, 1f, .13f));
        // Its dressing, worn as its tier asks when it is dealt (PerkCardScreen.Looks), over the plate and under the words.
        BuildDecor(card);

        var tag = KitStyle.TopLeft("Tag", card.Front, new Vector2(24f, 24f), new Vector2(84f, 30f));
        card.TagBack = KitStyle.Fill(tag, KitStyle.With(KitStyle.Ice, .15f));
        card.Tag = KitStyle.Flat(tag, "", 16f, TextAlignmentOptions.Center);
        var owned = KitStyle.TopLeft("Owned", card.Front, new Vector2(CardWidth - 112f, 22f), new Vector2(90f, 32f));
        owned.localRotation = Quaternion.Euler(0f, 0f, -6f);
        card.Owned = KitStyle.Fill(owned, KitStyle.Accent);
        card.OwnedLabel = KitStyle.Flat(owned, Texts.Get("perk.owned"), 17f, TextAlignmentOptions.Center, KitStyle.Ink);
        var icon = FeelUI.Rect("Icon", card.Front);
        icon.anchorMin = icon.anchorMax = new Vector2(.5f, 1f);
        icon.anchoredPosition = new Vector2(0f, -150f);
        icon.sizeDelta = new Vector2(128f, 128f);
        card.Icon = icon.gameObject.AddComponent<RawImage>();
        card.Icon.raycastTarget = false;
        card.Name = KitStyle.Flat(KitStyle.TopLeft("Name", card.Front, new Vector2(20f, 226f), new Vector2(CardWidth - 40f, 54f)), "", 38f,
            TextAlignmentOptions.Center);
        var pips = KitStyle.TopLeft("Pips", card.Front, new Vector2(0f, 286f), new Vector2(CardWidth, 6f));
        card.Pips = new Image[MaxPips];
        for (int i = 0; i < MaxPips; i++) card.Pips[i] = KitStyle.Fill(KitStyle.TopLeft("Pip", pips, Vector2.zero, new Vector2(34f, 6f)), KitStyle.Faint);
        card.Step = KitStyle.Flat(KitStyle.TopLeft("Step", card.Front, new Vector2(20f, 298f), new Vector2(CardWidth - 40f, 28f)), "", 18f,
            TextAlignmentOptions.Center, KitStyle.Accent);
        // Why it works (the user's rule: every effect comes with a reason that makes it believable), small, over the effect.
        card.Why = KitStyle.Flat(KitStyle.TopLeft("Why", card.Front, new Vector2(30f, 332f), new Vector2(CardWidth - 60f, 66f)), "", 16f,
            TextAlignmentOptions.Top, KitStyle.With(KitStyle.Soft, .72f));
        card.Why.textWrappingMode = TextWrappingModes.Normal;
        card.Why.fontStyle = FontStyles.Italic;
        card.Text = KitStyle.Flat(KitStyle.TopLeft("Effect", card.Front, new Vector2(30f, 402f), new Vector2(CardWidth - 60f, 100f)), "", 21f,
            TextAlignmentOptions.Top, KitStyle.Soft);
        card.Text.textWrappingMode = TextWrappingModes.Normal;
        card.Text.fontStyle = FontStyles.Normal;
        var key = FeelUI.Rect("Key", card.Front);
        key.anchorMin = key.anchorMax = new Vector2(.5f, 0f);
        key.sizeDelta = new Vector2(60f, 60f);
        card.KeyBack = KitStyle.Fill(key, KitStyle.Ice);
        card.Key = KitStyle.Flat(key, (index + 1).ToString(), 30f, TextAlignmentOptions.Center, KitStyle.Ink);
        BuildGlints(card);
        BuildBloom(card);

        // The face-down side: pink, a big question mark, the game's name.
        card.Back = Hung("Back", card.Burn);
        KitStyle.Fill(card.Back, KitStyle.Accent);
        var inner = KitStyle.Stretch("Inner", card.Back);
        inner.sizeDelta = new Vector2(-28f, -28f);
        KitStyle.Fill(inner, KitStyle.With(KitStyle.Ink, .12f));
        KitStyle.Flat(KitStyle.Stretch("Mark", card.Back), "?", 190f, TextAlignmentOptions.Center, KitStyle.Ink);
        var name = KitStyle.TopLeft("Name", card.Back, new Vector2(0f, CardHeight - 70f), new Vector2(CardWidth, 40f));
        KitStyle.Flat(name, "ENFILADE", 20f, TextAlignmentOptions.Center, KitStyle.Ink).characterSpacing = 10f;

        card.Lid = KitStyle.Fill(Hung("Flash", card.Burn), Color.clear);
        BuildBurn(card);
        BuildPickFlourish(card);

        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Pick(index));
        return card;
    }

    // A part of the card at the card's own size, hung from the top of <paramref name="parent"/> (the burn's mask, whose foot
    // rises: what hangs from its top stays where it is).
    private static RectTransform Hung(string name, RectTransform parent)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(CardWidth, CardHeight);
        return rect;
    }
}
